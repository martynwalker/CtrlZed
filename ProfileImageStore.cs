using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace CtrlZed;

/// <summary>Stores reusable macro images in the current Windows user's CtrlZed profile.</summary>
public static class ProfileImageStore
{
    const int MaximumImageBytes = 10 * 1024 * 1024;
    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    sealed class ImageIndex
    {
        public Dictionary<string, string> Images { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    static string Root
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CtrlZed", "ProfileImages");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    static string IndexPath => Path.Combine(Root, "images.json");

    public static IReadOnlyList<string> Names()
    {
        lock (Gate)
            return LoadIndex().Images.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Contains(string name)
    {
        lock (Gate)
            return LoadIndex().Images.ContainsKey(NormaliseName(name));
    }

    public static void Save(string name, byte[] bytes)
    {
        var key = NormaliseName(name);
        ValidateImage(bytes);
        lock (Gate)
        {
            var index = LoadIndex();
            var extension = DetectExtension(bytes);
            var fileName = Guid.NewGuid().ToString("N") + extension;
            var target = Path.Combine(Root, fileName);
            WriteAtomic(target, bytes);
            if (index.Images.TryGetValue(key, out var prior))
                TryDelete(Path.Combine(Root, Path.GetFileName(prior)));
            index.Images[key] = fileName;
            SaveIndex(index);
        }
    }

    public static void Delete(string name)
    {
        var key = NormaliseName(name);
        lock (Gate)
        {
            var index = LoadIndex();
            if (!index.Images.Remove(key, out var fileName)) return;
            TryDelete(Path.Combine(Root, Path.GetFileName(fileName)));
            SaveIndex(index);
        }
    }

    public static byte[] ReadBytes(string name)
    {
        var key = NormaliseName(name);
        lock (Gate)
        {
            var index = LoadIndex();
            if (!index.Images.TryGetValue(key, out var fileName))
                throw new FileNotFoundException($"No profile image named '{key}' is stored.");
            var path = Path.Combine(Root, Path.GetFileName(fileName));
            if (!File.Exists(path)) throw new FileNotFoundException($"The stored image for '{key}' is missing.", path);
            var bytes = File.ReadAllBytes(path);
            ValidateImage(bytes);
            return bytes;
        }
    }

    public static BitmapImage LoadBitmap(string name)
    {
        var bytes = ReadBytes(name);
        using var stream = new MemoryStream(bytes, false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            throw new InvalidDataException("The profile image has invalid dimensions.");
        bitmap.Freeze();
        return bitmap;
    }

    internal static IReadOnlyList<(string Name, byte[] Bytes)> ExportImages()
    {
        lock (Gate)
        {
            var index = LoadIndex();
            return index.Images.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(name => (name, ReadBytes(name))).ToList();
        }
    }

    internal static bool ImportImage(string name, byte[] bytes, bool replaceExisting)
    {
        var key = NormaliseName(name);
        ValidateImage(bytes);
        lock (Gate)
        {
            var index = LoadIndex();
            if (!replaceExisting && index.Images.ContainsKey(key)) return false;
            Save(key, bytes);
            return true;
        }
    }

    static string NormaliseName(string name)
    {
        var key = (name ?? "").Trim();
        if (key.StartsWith("{", StringComparison.Ordinal) && key.EndsWith("}", StringComparison.Ordinal) && key.Length > 2)
            key = key[1..^1].Trim();
        if (key.Length is < 1 or > 48 || !Regex.IsMatch(key, @"^[\p{L}\p{N}_ -]+$"))
            throw new InvalidDataException("Image names must be 1–48 letters, numbers, spaces, underscores or hyphens.");
        return key;
    }

    static void ValidateImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumImageBytes)
            throw new InvalidDataException("Choose an image file smaller than 10 MB.");
        _ = DetectExtension(bytes);
    }

    static string DetectExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return ".bmp";
        if (bytes.Length >= 6 && (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")) return ".gif";
        throw new InvalidDataException("The selected file is not a supported PNG, JPEG, BMP or GIF image.");
    }

    static ImageIndex LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath)) return new ImageIndex();
            var loaded = JsonSerializer.Deserialize<ImageIndex>(File.ReadAllText(IndexPath)) ?? new ImageIndex();
            return new ImageIndex
            {
                Images = new Dictionary<string, string>(loaded.Images, StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (JsonException) { return new ImageIndex(); }
    }

    static void SaveIndex(ImageIndex index)
    {
        var temporary = IndexPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(index, JsonOptions));
        File.Move(temporary, IndexPath, true);
    }

    static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>Convenience API available to trusted compiled VB.NET and C# macros.</summary>
public static class ProfileImageMacro
{
    public static void Insert(
        System.Windows.Controls.RichTextBox editor,
        string imageName,
        double widthMillimetres,
        PicturePlacementMode placement = PicturePlacementMode.Inline)
    {
        if (editor is null) throw new ArgumentNullException(nameof(editor));
        if (System.Windows.Window.GetWindow(editor) is not MainWindow window)
            throw new InvalidOperationException("The macro editor is not attached to a CtrlZed window.");
        window.InsertProfileImage(imageName, widthMillimetres, placement);
    }
}
