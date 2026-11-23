using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CtrlZed;

// Installation is deliberately separate from downloading and previewing. It
// changes only the current user's font registration, after validating the
// complete pinned package again. WPF's process-wide font catalogue may still
// require a restart; installation never claims that it refreshed that cache.
internal static class OnlineFontInstallation
{
    const string FontRegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    const int MaximumFontBytes = 32 * 1024 * 1024;
    const int MaximumReceiptBytes = 256 * 1024;
    static readonly Regex Sha1Pattern = new("^[a-f0-9]{40}$", RegexOptions.CultureInvariant);
    static readonly JsonSerializerOptions ReceiptJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal static Action<string>? StepOverride { get; set; }
    internal static string? ReceiptRootOverride { get; set; }

    sealed class PlannedFile
    {
        public required OnlineFontFile Source { get; init; }
        public required byte[] Bytes { get; init; }
        public required string Path { get; set; }
        public required string RegistryName { get; init; }
        public required string Sha256 { get; init; }
        public bool ExistingRegistration { get; set; }
        public bool CreatedFile { get; set; }
        public bool CreatedRegistry { get; set; }
        public bool AddedSession { get; set; }
    }

    sealed record ReceiptFile(string Path, string SourcePath, string RegistryName, string Sha256, string BlobSha,
        bool CreatedFile, bool CreatedRegistry, bool AddedSession);
    sealed record ReceiptLicence(string Path, string SourcePath, string BlobSha, string Sha256, bool Created);
    sealed record InstallReceipt(int Schema, string Owner, string Commit, string FamilyId,
        string FamilyName, DateTimeOffset InstalledUtc, IReadOnlyList<ReceiptFile> Files,
        IReadOnlyList<ReceiptLicence> Licences);

    static string FontDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
    static string ReceiptRoot => Path.GetFullPath(ReceiptRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CtrlZed", "Fonts", "Installed"));

    internal static Task<OnlineFontInstallResult> InstallAsync(OnlineFontPackage package, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Font installation requires Windows.");
        return Task.Run(() => WithInstallLock(() => InstallCore(package, ct), ct), ct);
    }

    static T WithInstallLock<T>(Func<T> operation, CancellationToken ct)
    {
        using var mutex = new Mutex(false, @"Local\CtrlZed.FontInstallation");
        var held = false;
        try
        {
            try
            {
                var result = WaitHandle.WaitAny(new WaitHandle[] { mutex, ct.WaitHandle }, TimeSpan.FromSeconds(30));
                if (result == 1) throw new OperationCanceledException(ct);
                if (result == WaitHandle.WaitTimeout) throw new InvalidOperationException("Another font installation is still finishing. Try again shortly.");
                held = true;
            }
            catch (AbandonedMutexException) { held = true; }
            ct.ThrowIfCancellationRequested();
            return operation();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    static OnlineFontInstallResult InstallCore(OnlineFontPackage package, CancellationToken ct)
    {
        OnlineFontService.ValidateFamily(package.Catalogue, package.Family);
        if (!Sha1Pattern.IsMatch(package.Catalogue.Commit)) throw new InvalidDataException("The catalogue does not identify a pinned source commit.");
        if (package.Files.Count == 0 || package.Files.Count != package.Family.Faces.Count ||
            package.Files.Select(file => file.Face.Path).Distinct(StringComparer.Ordinal).Count() != package.Files.Count ||
            package.Family.Faces.Any(face => !package.Files.Any(file => file.Face.Path == face.Path)))
            throw new InvalidDataException("Download every supported face in this family before installing it.");

        // Read and verify all bytes before any font or registry mutation. A
        // second digest over these exact in-memory bytes closes the source-file
        // replacement gap between validation and copying.
        var plans = new List<PlannedFile>();
        foreach (var file in package.Files)
        {
            ct.ThrowIfCancellationRequested();
            OnlineFontService.ValidateFile(package.Catalogue, package.Family, file);
            var bytes = ReadBoundedFile(file.FilePath, MaximumFontBytes, ct);
            var digest = Sha256(bytes);
            if (!digest.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !GitBlobSha(bytes).Equals(file.Face.BlobSha, StringComparison.Ordinal))
                throw new InvalidDataException("A downloaded font changed after validation. Download it again.");
            var registryName = file.Face.FullName + " (TrueType)";
            if (string.IsNullOrWhiteSpace(file.Face.FullName) || registryName.Length > 255 || registryName.Any(char.IsControl))
                throw new InvalidDataException("The font's registered name is invalid.");
            plans.Add(new PlannedFile
            {
                Source = file, Bytes = bytes, Sha256 = digest, RegistryName = registryName,
                Path = Path.Combine(FontDirectory, "CtrlZed-" + file.Face.BlobSha + ".ttf")
            });
        }
        if (plans.Select(plan => plan.RegistryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
            throw new InvalidDataException("Two downloaded faces have the same registered name.");

        using (var userFonts = Registry.CurrentUser.OpenSubKey(FontRegistryPath, false))
        using (var machineFonts = Registry.LocalMachine.OpenSubKey(FontRegistryPath, false))
        {
            foreach (var plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                var userValue = ReadRegistration(userFonts, plan.RegistryName);
                var machineValue = ReadRegistration(machineFonts, plan.RegistryName);
                if (userValue != null || machineValue != null)
                {
                    var registeredPaths = new[] { userValue, machineValue }.Where(value => value != null)
                        .Select(value => ResolveRegistrationPath(value!)).ToArray();
                    if (registeredPaths.Any(path => path == null || !FileHasHash(path, plan.Sha256)))
                        throw new InvalidOperationException($"‘{plan.Source.Face.FullName}’ is already registered with different font data. The existing font has been left unchanged.");
                    plan.Path = registeredPaths[0]!;
                    plan.ExistingRegistration = true;
                }
                else if (File.Exists(plan.Path) && !FileHasHash(plan.Path, plan.Sha256))
                    throw new InvalidOperationException("A different file already occupies the intended font filename. It has been left unchanged.");
            }
        }

        var receiptDirectory = Path.Combine(ReceiptRoot, SafeId(package.Family.Id), package.Catalogue.Commit);
        var receiptPath = Path.Combine(receiptDirectory, "receipt.json");
        var allAlready = plans.All(plan => plan.ExistingRegistration);
        if (allAlready)
            return Result(plans, true, File.Exists(receiptPath) ? receiptPath : null);
        if (File.Exists(receiptPath))
            throw new InvalidOperationException("An installation receipt already exists for this family, but its Windows registration differs. The existing files and registration have been left unchanged.");

        var licencePlans = new List<(string Path, string SourcePath, byte[] Bytes, string Sha256, string BlobSha)>();
        foreach (var licence in package.Family.LicenceFiles)
        {
            var filename = Path.GetFileName(licence.Path);
            if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                filename.Equals("receipt.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The licence filename is invalid.");
            var bytes = Encoding.UTF8.GetBytes(licence.Text);
            if (!GitBlobSha(bytes).Equals(licence.BlobSha, StringComparison.Ordinal))
                throw new InvalidDataException("A licence does not match its pinned source.");
            var destination = Path.Combine(receiptDirectory, filename);
            var digest = Sha256(bytes);
            if (File.Exists(destination) && !FileHasHash(destination, digest))
                throw new InvalidOperationException("A different licence file already occupies the installation record. It has been left unchanged.");
            licencePlans.Add((destination, licence.Path, bytes, digest, licence.BlobSha));
        }
        if (licencePlans.Count == 0 || licencePlans.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != licencePlans.Count)
            throw new InvalidDataException("The complete, distinct licence files are required before installation.");

        var createdLicences = new List<(string Path, string Sha256)>();
        var receiptCreated = false;
        var receiptSha256 = "";
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(FontDirectory);
            Directory.CreateDirectory(receiptDirectory);
            foreach (var item in licencePlans)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(item.Path))
                {
                    AtomicCreate(item.Path, item.Bytes);
                    createdLicences.Add((item.Path, item.Sha256));
                }
            }
            using var userFonts = Registry.CurrentUser.CreateSubKey(FontRegistryPath, true)
                ?? throw new IOException("The current user's font registry could not be opened.");
            foreach (var plan in plans.Where(item => !item.ExistingRegistration))
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(plan.Path))
                {
                    AtomicCreate(plan.Path, plan.Bytes);
                    plan.CreatedFile = true;
                }
                if (!FileHasHash(plan.Path, plan.Sha256)) throw new IOException("The copied font failed its integrity check.");
                StepOverride?.Invoke("after-file");
                ct.ThrowIfCancellationRequested();
                if (ReadRegistration(userFonts, plan.RegistryName) != null)
                    throw new InvalidOperationException("Another installation registered this font while CtrlZed was preparing it. No existing registration was replaced.");
                userFonts.SetValue(plan.RegistryName, plan.Path, RegistryValueKind.String);
                plan.CreatedRegistry = true;
                userFonts.Flush();
                StepOverride?.Invoke("after-registry");
                ct.ThrowIfCancellationRequested();
                if (AddFontResourceExW(plan.Path, 0, IntPtr.Zero) == 0)
                    throw new IOException("Windows did not accept the downloaded font. The new installation will be rolled back.");
                plan.AddedSession = true;
                StepOverride?.Invoke("after-session");
            }
            ct.ThrowIfCancellationRequested();
            StepOverride?.Invoke("before-receipt");
            ct.ThrowIfCancellationRequested();
            var receipt = new InstallReceipt(1, "CtrlZed", package.Catalogue.Commit, package.Family.Id,
                package.Family.Name, DateTimeOffset.UtcNow,
                plans.Select(plan => new ReceiptFile(plan.Path, plan.Source.Face.Path, plan.RegistryName, plan.Sha256, plan.Source.Face.BlobSha,
                    plan.CreatedFile, plan.CreatedRegistry, plan.AddedSession)).ToArray(),
                licencePlans.Select(item => new ReceiptLicence(item.Path, item.SourcePath, item.BlobSha, item.Sha256,
                    createdLicences.Any(created => created.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))).ToArray());
            var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, ReceiptJson);
            receiptSha256 = Sha256(receiptBytes);
            AtomicCreate(receiptPath, receiptBytes);
            receiptCreated = true;
            // The durable record is the commit point. Late cancellation must
            // not turn a completed installation into an ambiguous failure.
            NotifyFontChange();
            return Result(plans, false, receiptPath);
        }
        catch (Exception failure)
        {
            var cleanupErrors = new List<Exception>();
            TryCleanup(() => RollBack(plans, cleanupErrors), cleanupErrors);
            if (receiptCreated) TryCleanup(() => DeleteOwnedHashFile(receiptPath, receiptSha256, ReceiptRoot), cleanupErrors);
            foreach (var item in createdLicences.AsEnumerable().Reverse())
                TryCleanup(() => DeleteOwnedHashFile(item.Path, item.Sha256, ReceiptRoot), cleanupErrors);
            TryCleanup(() => RemoveEmptyOwnedDirectories(receiptDirectory), cleanupErrors);
            if (cleanupErrors.Count != 0)
                throw new AggregateException("Font installation failed and Windows could not remove every newly created file. Existing fonts were left unchanged.", new[] { failure }.Concat(cleanupErrors));
            throw;
        }
    }

    static OnlineFontInstallResult Result(IEnumerable<PlannedFile> plans, bool already, string? receiptPath)
        => new(true, already, true,
            already ? "This exact font data is already registered for Windows. Restart CtrlZed before applying it if it is absent from the installed list."
                : "Installed for your Windows account. Restart CtrlZed to refresh its font list; your document has not been changed.",
            plans.Select(plan => new OnlineFontInstalledFile(plan.Path, plan.RegistryName, plan.Sha256)).ToArray(), receiptPath);

    // Test cleanup deliberately requires this transaction's receipt, exact
    // identities and hashes. A duplicate result cannot remove an older install.
    internal static void CleanupForTest(OnlineFontInstallResult result)
    {
        if (result.AlreadyInstalled || !result.Installed || result.ReceiptPath == null) return;
        WithInstallLock(() =>
        {
            var receiptPath = Path.GetFullPath(result.ReceiptPath);
            if (!IsInside(receiptPath, ReceiptRoot) || !File.Exists(receiptPath))
                throw new InvalidOperationException("Test cleanup requires an owned installation receipt.");
            var bytes = ReadBoundedFile(receiptPath, MaximumReceiptBytes, CancellationToken.None);
            var receipt = JsonSerializer.Deserialize<InstallReceipt>(bytes, ReceiptJson)
                ?? throw new InvalidDataException("The installation receipt is invalid.");
            if (receipt.Schema != 1 || receipt.Owner != "CtrlZed" || !Sha1Pattern.IsMatch(receipt.Commit) ||
                !Path.GetFullPath(Path.Combine(ReceiptRoot, SafeId(receipt.FamilyId), receipt.Commit, "receipt.json")).Equals(receiptPath, StringComparison.OrdinalIgnoreCase) ||
                receipt.Files.Count != result.Files.Count || receipt.Files.Any(file => !result.Files.Any(expected =>
                    expected.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase) && expected.RegistryName == file.RegistryName && expected.Sha256 == file.Sha256)))
                throw new InvalidDataException("The test receipt does not identify this installation result.");
            foreach (var file in receipt.Files.Where(file => file.CreatedFile || file.CreatedRegistry || file.AddedSession))
                if (!IsOwnedFontPath(file.Path, file.BlobSha) || !FileHasHash(file.Path, file.Sha256))
                    throw new InvalidOperationException("A test font changed or is outside CtrlZed's owned font paths. It has been left unchanged.");
            foreach (var licence in receipt.Licences)
                if (!IsInside(licence.Path, Path.GetDirectoryName(receiptPath)!) || !FileHasHash(licence.Path, licence.Sha256))
                    throw new InvalidOperationException("A test licence record changed. It has been left unchanged.");

            var plans = receipt.Files.Select(file => new PlannedFile
            {
                Source = new OnlineFontFile(new OnlineFontFace("", file.BlobSha, "", "", "", "normal", 400), file.Path, file.Sha256),
                Bytes = Array.Empty<byte>(), Path = file.Path, RegistryName = file.RegistryName, Sha256 = file.Sha256,
                CreatedFile = file.CreatedFile, CreatedRegistry = file.CreatedRegistry, AddedSession = file.AddedSession
            }).ToArray();
            var errors = new List<Exception>();
            RollBack(plans, errors);
            if (errors.Count != 0) throw new AggregateException("Test font cleanup was incomplete.", errors);
            DeleteOwnedHashFile(receiptPath, Sha256(bytes), ReceiptRoot);
            foreach (var licence in receipt.Licences.Where(licence => licence.Created)) DeleteOwnedHashFile(licence.Path, licence.Sha256, ReceiptRoot);
            RemoveEmptyOwnedDirectories(Path.GetDirectoryName(receiptPath)!);
            NotifyFontChange();
            return true;
        }, CancellationToken.None);
    }

    static void RollBack(IEnumerable<PlannedFile> plans, List<Exception> errors)
    {
        using var userFonts = Registry.CurrentUser.OpenSubKey(FontRegistryPath, true);
        foreach (var plan in plans.Reverse())
        {
            if (!plan.CreatedFile && !plan.CreatedRegistry && !plan.AddedSession) continue;
            if (!IsOwnedFontPath(plan.Path, plan.Source.Face.BlobSha) || !FileHasHash(plan.Path, plan.Sha256))
            {
                errors.Add(new IOException("An owned installation file changed during cleanup and was left untouched."));
                continue;
            }
            var unloaded = true;
            if (plan.AddedSession)
            {
                unloaded = RemoveFontResourceExW(plan.Path, 0, IntPtr.Zero);
                if (!unloaded) errors.Add(new IOException("Windows could not unload a font added by this installation."));
            }
            if (plan.CreatedRegistry)
            {
                TryCleanup(() =>
                {
                    if (ReadRegistration(userFonts, plan.RegistryName)?.Equals(plan.Path, StringComparison.OrdinalIgnoreCase) == true)
                        userFonts!.DeleteValue(plan.RegistryName, false);
                }, errors);
            }
            if (plan.CreatedFile && unloaded)
                TryCleanup(() => DeleteOwnedHashFile(plan.Path, plan.Sha256, FontDirectory), errors);
        }
        userFonts?.Flush();
    }

    static void TryCleanup(Action action, List<Exception> errors)
    { try { action(); } catch (Exception error) { errors.Add(error); } }

    static string? ReadRegistration(RegistryKey? key, string name)
    {
        if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        if (key.GetValueKind(name) is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new InvalidOperationException("An existing font registration uses an unexpected value type and was left unchanged.");
        return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? throw new InvalidOperationException("An existing font registration is invalid and was left unchanged.");
    }

    static string? ResolveRegistrationPath(string value)
    {
        value = Environment.ExpandEnvironmentVariables(value);
        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.Length > 32700) return null;
        try
        {
            return Path.GetFullPath(Path.IsPathFullyQualified(value) ? value :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), value));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    static bool IsOwnedFontPath(string path, string blobSha)
        => Sha1Pattern.IsMatch(blobSha) && Path.GetFullPath(path).Equals(
            Path.GetFullPath(Path.Combine(FontDirectory, "CtrlZed-" + blobSha + ".ttf")), StringComparison.OrdinalIgnoreCase);

    static bool IsInside(string path, string directory)
        => Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    static string SafeId(string value)
    {
        if (!Regex.IsMatch(value, "^(ofl|apache|ufl)/[a-z0-9][a-z0-9_-]{0,119}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The font family identifier is invalid.");
        return value.Replace('/', '-');
    }

    static byte[] ReadBoundedFile(string path, int maximum, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > maximum) throw new InvalidDataException("The font or receipt file has an invalid size.");
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        for (var count = stream.Read(buffer, 0, buffer.Length); count != 0; count = stream.Read(buffer, 0, buffer.Length))
        {
            ct.ThrowIfCancellationRequested();
            if (output.Length + count > maximum) throw new InvalidDataException("The file exceeded the allowed size while reading it.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    static bool FileHasHash(string path, string hash)
    {
        try { return Sha256(ReadBoundedFile(path, MaximumFontBytes, CancellationToken.None)).Equals(hash, StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    static string GitBlobSha(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0"));
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    static void AtomicCreate(string destination, byte[] bytes)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".CtrlZed-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static void DeleteOwnedHashFile(string path, string hash, string root)
    {
        if (!IsInside(path, root)) throw new InvalidOperationException("Cleanup refused a file outside its owned directory.");
        if (!File.Exists(path)) return;
        if (!FileHasHash(path, hash)) throw new IOException("Cleanup refused a file whose contents changed.");
        File.Delete(path);
    }

    static void RemoveEmptyOwnedDirectories(string path)
    {
        for (var current = Path.GetFullPath(path); IsInside(current, ReceiptRoot); current = Path.GetDirectoryName(current)!)
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) return;
            Directory.Delete(current);
        }
    }

    static void NotifyFontChange()
    {
        // Installation already committed. An unresponsive unrelated window
        // must not turn success into rollback or hang the UI thread.
        _ = SendMessageTimeoutW(new IntPtr(0xffff), 0x001d, IntPtr.Zero, IntPtr.Zero,
            0x0002, 100, out _);
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int AddFontResourceExW(string name, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RemoveFontResourceExW(string name, uint flags, IntPtr reserved);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
}
