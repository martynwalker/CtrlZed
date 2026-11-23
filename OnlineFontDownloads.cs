using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;

namespace CtrlZed;

internal static partial class OnlineFontService
{
    internal const int MaximumFontBytes = 16 * 1024 * 1024;
    static readonly HttpClient FontClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };
    static readonly SemaphoreSlim DownloadLock = new(1, 1);
    internal static Func<Uri, CancellationToken, Task<byte[]>>? DownloadOverride { get; set; }
    internal static string? CacheRootOverride { get; set; }

    internal static async Task<OnlineFontPreview> PreviewAsync(OnlineFontCatalogue catalogue, string familyId,
        OnlineFontFace face, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var family = FindFamily(catalogue, familyId);
        var canonical = CanonicalFace(family, face);
        var file = await DownloadFaceAsync(catalogue, family, canonical, ct);
        ct.ThrowIfCancellationRequested();
        // Font objects are constructed only after bounded sfnt/hash validation.
        // Nothing is registered in Windows or inserted into the document.
        var preview = CreatePreview(family, file);
        ct.ThrowIfCancellationRequested();
        return preview;
    }

    internal static async Task<OnlineFontPackage> DownloadFamilyAsync(OnlineFontCatalogue catalogue,
        string familyId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var family = FindFamily(catalogue, familyId);
        var files = new List<OnlineFontFile>(family.Faces.Count);
        long total = 0;
        foreach (var face in family.Faces)
        {
            ct.ThrowIfCancellationRequested();
            var file = await DownloadFaceAsync(catalogue, family, face, ct);
            total += new FileInfo(file.FilePath).Length;
            if (total > 64L * 1024 * 1024) throw new InvalidDataException("The font family is larger than the supported 64 MB download size.");
            files.Add(file);
        }
        ct.ThrowIfCancellationRequested();
        return new OnlineFontPackage(catalogue, family, files.AsReadOnly(), Path.GetDirectoryName(files[0].FilePath)!);
    }

    internal static void ValidateFile(OnlineFontCatalogue catalogue, OnlineFontFamily family, OnlineFontFile file)
    {
        var current = ValidateFamily(catalogue, family);
        ArgumentNullException.ThrowIfNull(file);
        var face = CanonicalFace(current, file.Face);
        if (string.IsNullOrWhiteSpace(file.FilePath) || !Path.IsPathFullyQualified(file.FilePath) ||
            file.Sha256.Length != 64 || file.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("The downloaded font file record is invalid.");
        var bytes = ReadFontFile(file.FilePath);
        VerifyFont(bytes, face);
        if (!string.Equals(Sha256(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded font changed after it was reviewed. Download the preview again.");
    }

    internal static string CachedFilePath(OnlineFontCatalogue catalogue, OnlineFontFamily family, OnlineFontFace face)
    {
        var current = ValidateFamily(catalogue, family);
        var canonical = CanonicalFace(current, face);
        var root = CacheRootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CtrlZed", "Fonts", "Downloads");
        root = Path.GetFullPath(root);
        return Path.Combine(root, PinnedCommit, current.Id.Replace('/', Path.DirectorySeparatorChar), canonical.BlobSha + ".ttf");
    }

    static OnlineFontFamily FindFamily(OnlineFontCatalogue catalogue, string id)
    {
        ValidateCatalogue(catalogue);
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id)) throw new InvalidDataException("Choose a family from the supplied Google Fonts catalogue.");
        var family = catalogue.Families.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidDataException("The selected family is not in this release's catalogue.");
        return ValidateFamily(catalogue, family);
    }

    static async Task<OnlineFontFile> DownloadFaceAsync(OnlineFontCatalogue catalogue, OnlineFontFamily family,
        OnlineFontFace face, CancellationToken ct)
    {
        var path = CachedFilePath(catalogue, family, face);
        await DownloadLock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureCacheDirectory(path);
            if (File.Exists(path))
            {
                try
                {
                    var bytes = ReadFontFile(path); VerifyFont(bytes, face);
                    ct.ThrowIfCancellationRequested();
                    return new OnlineFontFile(face, path, Sha256(bytes));
                }
                catch (InvalidDataException) { File.Delete(path); }
            }
            var uri = DownloadUri(face.Path);
            var data = DownloadOverride != null ? await DownloadOverride(uri, ct) : await DownloadBytesAsync(uri, ct);
            ct.ThrowIfCancellationRequested();
            VerifyFont(data, face);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, data, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new OnlineFontFile(face, path, Sha256(data));
        }
        finally { DownloadLock.Release(); }
    }

    static void EnsureCacheDirectory(string path)
    {
        var root = Path.GetFullPath(CacheRootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CtrlZed", "Fonts", "Downloads"));
        var directory = Path.GetDirectoryName(path)!;
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The font cache path is outside its configured directory.");
        Directory.CreateDirectory(root);
        RejectReparsePoint(root);
        var current = root;
        foreach (var part in Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            Directory.CreateDirectory(current); RejectReparsePoint(current);
        }
        if (File.Exists(path)) RejectReparsePoint(path);
    }

    static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The font cache contains a link or redirected directory. Choose a normal local cache directory.");
    }

    static async Task<byte[]> DownloadBytesAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "raw.githubusercontent.com" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/google/fonts/" + PinnedCommit + "/", StringComparison.Ordinal))
            throw new InvalidDataException("The requested font address is not an approved upstream file.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CtrlZed/0.50 FontFinder");
            using var response = await FontClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"The font download returned HTTP {(int)response.StatusCode}. No font was installed.");
            if (response.Content.Headers.ContentLength is > MaximumFontBytes or < 1)
                throw new InvalidDataException("The font download is empty or exceeds the supported 16 MB size.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (count == 0) break;
                if (output.Length + count > MaximumFontBytes) throw new InvalidDataException("The font download exceeds the supported 16 MB size.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("The font download took too long. Try the preview again when the connection is available.", error); }
    }

    static byte[] ReadFontFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 12 or > MaximumFontBytes)
            throw new InvalidDataException("The cached font is missing or has an unsupported size.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 12 or > MaximumFontBytes)
            throw new InvalidDataException("The font changed size while it was being checked.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("The font changed while it was being checked.");
        return bytes;
    }

    static void VerifyFont(byte[] bytes, OnlineFontFace face)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 12 or > MaximumFontBytes) throw new InvalidDataException("The font is empty or exceeds the supported 16 MB size.");
        if (!string.Equals(BlobHash(bytes), face.BlobSha, StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded font does not match the recorded official source. No font was installed.");
        ValidateFontBytes(bytes);
    }

    internal static void ValidateFontBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 12 or > MaximumFontBytes || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4)) != 0x00010000)
            throw new InvalidDataException("Only bounded static TrueType fonts are supported in this release.");
        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        var directoryEnd = 12L + count * 16L;
        if (count is < 1 or > 256 || directoryEnd > bytes.Length) throw new InvalidDataException("The TrueType table directory is invalid.");
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(12 + index * 16, 16);
            var tag = System.Text.Encoding.ASCII.GetString(entry[..4]);
            if (entry[..4].ToArray().Any(value => value is < 32 or > 126) || !tables.TryAdd(tag, default))
                throw new InvalidDataException("The TrueType table names are invalid or repeated.");
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8, 4));
            var length = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(12, 4));
            if (offset < directoryEnd || offset > bytes.Length || length > bytes.Length || (long)offset + length > bytes.Length || offset % 4 != 0)
                throw new InvalidDataException("A TrueType table extends outside the font file.");
            tables[tag] = ((int)offset, (int)length);
        }
        if (tables.ContainsKey("fvar")) throw new InvalidDataException("This font contains variable axes. Choose a supported static font instead.");
        var required = new Dictionary<string, int>(StringComparer.Ordinal)
        { ["head"] = 54, ["hhea"] = 36, ["maxp"] = 6, ["hmtx"] = 4, ["name"] = 6, ["cmap"] = 4, ["loca"] = 2, ["glyf"] = 1, ["OS/2"] = 68, ["post"] = 32 };
        foreach (var item in required)
            if (!tables.TryGetValue(item.Key, out var table) || table.Length < item.Value)
                throw new InvalidDataException("The static TrueType font is missing a required table.");
        var occupied = tables.Values.Where(table => table.Length > 0).OrderBy(table => table.Offset).ToArray();
        for (var index = 1; index < occupied.Length; index++)
            if ((long)occupied[index - 1].Offset + occupied[index - 1].Length > occupied[index].Offset)
                throw new InvalidDataException("TrueType tables overlap.");
        var head = tables["head"].Offset;
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(head + 12, 4)) != 0x5f0f3cf5)
            throw new InvalidDataException("The TrueType header is invalid.");
        var em = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(head + 18, 2));
        var glyphs = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tables["maxp"].Offset + 4, 2));
        var metrics = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tables["hhea"].Offset + 34, 2));
        var locationFormat = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(head + 50, 2));
        if (em is < 16 or > 16384 || glyphs == 0 || metrics == 0 || metrics > glyphs || locationFormat is not (0 or 1) ||
            tables["loca"].Length < (glyphs + 1) * (locationFormat == 0 ? 2 : 4) ||
            tables["hmtx"].Length < metrics * 4 + (glyphs - metrics) * 2)
            throw new InvalidDataException("The TrueType glyph metrics are invalid.");
        var previous = 0u;
        for (var index = 0; index <= glyphs; index++)
        {
            var position = tables["loca"].Offset + index * (locationFormat == 0 ? 2 : 4);
            var value = locationFormat == 0 ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position, 2)) * 2u :
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4));
            if (value < previous || value > tables["glyf"].Length) throw new InvalidDataException("The TrueType glyph locations are invalid.");
            previous = value;
        }
        var names = tables["name"];
        var nameCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(names.Offset + 2, 2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(names.Offset + 4, 2));
        if (6L + nameCount * 12L > names.Length || stringOffset > names.Length)
            throw new InvalidDataException("The TrueType name table is invalid.");
        for (var index = 0; index < nameCount; index++)
        {
            var entry = names.Offset + 6 + index * 12;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry + 8, 2));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry + 10, 2));
            if ((long)stringOffset + offset + length > names.Length) throw new InvalidDataException("A TrueType name extends outside its table.");
        }
    }

    static OnlineFontPreview CreatePreview(OnlineFontFamily family, OnlineFontFile file)
    {
        var expected = Path.GetFullPath(file.FilePath);
        try
        {
            foreach (var previewFamily in Fonts.GetFontFamilies(new Uri(expected, UriKind.Absolute)))
            {
                var typeface = new Typeface(previewFamily, file.Face.Italic ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight.FromOpenTypeWeight(file.Face.Weight), FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.IsFile ||
                    !string.Equals(Path.GetFullPath(glyph.FontUri.LocalPath), expected, StringComparison.OrdinalIgnoreCase) ||
                    glyph.Weight.ToOpenTypeWeight() != file.Face.Weight ||
                    (glyph.Style != FontStyles.Normal) != file.Face.Italic)
                    continue;
                var names = glyph.FamilyNames.Values.Concat(glyph.Win32FamilyNames.Values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!names.Any(name => SameName(name, family.Name) || SameName(name, file.Face.Name))) continue;
                var actual = glyph.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var english) ? english : names.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(actual)) continue;
                return new OnlineFontPreview(family, file, previewFamily, actual);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        { throw new InvalidDataException("Windows could not render this recorded static font face. No font was installed.", error); }
        throw new InvalidDataException("The font preview did not resolve to the actual downloaded face. Choose another supported face.");
    }

    static bool SameName(string left, string right)
    {
        static string Key(string value) => new(value.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return Key(left) == Key(right);
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
