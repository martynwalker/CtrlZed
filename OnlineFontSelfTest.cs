using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CtrlZed;

public partial class MainWindow
{
    sealed record OnlineFontFixture(OnlineFontCatalogue Catalogue, OnlineFontFamily Family,
        OnlineFontFace Face, byte[] Bytes);

    // Only this isolated Windows regression uses injected transports and fonts.
    // The normal application never runs it while opening a document or menu.
    internal static int RunOnlineFontSelfTest()
    {
        var report = new StringBuilder("Beta 50 online font discovery, preview and installation regression\r\n");
        var failures = new List<Exception>();
        var appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CtrlZed");
        var testRoot = Path.Combine(Path.GetTempPath(), "CtrlZed50OnlineFonts-" + Guid.NewGuid().ToString("N"));
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var originalDirectories = Directory.Exists(appRoot)
            ? Directory.GetDirectories(appRoot, "*", SearchOption.AllDirectories).Prepend(appRoot).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var download = OnlineFontService.DownloadOverride;
        var generator = OnlineFontService.GeneratorOverride;
        var cacheRoot = OnlineFontService.CacheRootOverride;
        var receiptRoot = OnlineFontInstallation.ReceiptRootOverride;
        var step = OnlineFontInstallation.StepOverride;
        var shutdown = Application.Current.ShutdownMode;
        IDataObject? clipboard = null;
        var clipboardCaptured = false;
        var snapshotCaptured = false;
        MainWindow? window = null;
        OnlineFontFixture? fixture = null;
        var result = 128;
        void Phase(string name, Action work)
        {
            try { work(); report.AppendLine("PASS: " + name + "."); }
            catch (Exception error) { failures.Add(error); report.AppendLine("FAIL: " + name + ": " + error); }
        }
        try
        {
            if (Directory.Exists(appRoot))
                foreach (var path in Directory.GetFiles(appRoot, "*", SearchOption.AllDirectories))
                    originals[path] = File.ReadAllBytes(path);
            snapshotCaptured = true;
            clipboard = Clipboard.GetDataObject(); clipboardCaptured = true;
            Directory.CreateDirectory(appRoot); Directory.CreateDirectory(testRoot);
            foreach (var path in Directory.GetFiles(appRoot, "Recovery*")) File.Delete(path);
            OnlineFontService.CacheRootOverride = Path.Combine(testRoot, "Downloads");
            OnlineFontInstallation.ReceiptRootOverride = Path.Combine(testRoot, "Installed receipts");
            OnlineFontInstallation.StepOverride = null;
            OnlineFontService.GeneratorOverride = (_, _) => throw new InvalidOperationException("Unexpected live AI call in the font regression.");
            OnlineFontService.DownloadOverride = (_, _) => throw new InvalidOperationException("Unexpected network call outside the explicit pinned-download phase.");
            Phase("pinned catalogue and authentic CI font fixture", () => fixture = LoadOnlineFontFixture(report));
            if (fixture != null)
            {
                var data = fixture;
                Phase("bounded catalogue, source, licence and path validation", () => VerifyOnlineFontCatalogue(data, report));
                Phase("local search and strict private deterministic AI recommendations", () => VerifyOnlineFontSearch(data, report));
                Phase("TrueType validation and tamper rejection", () => VerifyOnlineFontBinary(data, report));
                Phase("one real pinned official download and private-file WPF preview", () => VerifyOnlineFontLiveDownload(data, report));
                Phase("deterministic download, cache verification and cancellation", () => VerifyOnlineFontDownloads(data, report));
                Phase("real editor initialisation", () =>
                {
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    window = new MainWindow { WindowState = WindowState.Maximized };
                    window.Show(); window.Activate();
                    SetForegroundWindow(new WindowInteropHelper(window).Handle); PumpInput();
                    window.recoveryTimer.Stop(); window.uiRefreshTimer.Stop(); window.grammarReviewTimer?.Stop();
                    window.languageSettings.SpellingEnabled = false;
                    window.languageSettings.ContextualSpellingSuggestions = false;
                    window.languageSettings.GrammarAndStyleSuggestions = false;
                    window.languageSettings.PoetryRhymeSuggestions = false;
                    window.languageSettings.AllowAiLanguageServices = false;
                    SpellCheck.SetIsEnabled(window.Editor, false);
                });
                if (window is { IsLoaded: true })
                {
                    var editor = window;
                    Phase("real online search, licence review and explicit private preview", () => editor.VerifyOnlineFontWindow(data, report));
                    Phase("native result clicks, cached repeat and keyboard preview activation", () => editor.VerifyOnlineFontResultActivation(data, report));
                    Phase("native result change supersedes an unfinished valid download", () => editor.VerifyOnlineFontResultReplacement(data, report));
                    Phase("explicit AI permission and document-sample privacy", () => editor.VerifyOnlineFontAiWindow(data, report));
                    foreach (var kind in new[] { "query", "mode", "close" })
                    {
                        var change = kind;
                        Phase("obsolete AI recommendations after " + change, () => editor.VerifyOnlineFontLateAi(data, change, report));
                    }
                    foreach (var kind in new[] { "query", "close" })
                    {
                        var change = kind;
                        Phase("obsolete downloaded previews after " + change, () => editor.VerifyOnlineFontLatePreview(data, change, report));
                    }
                    Phase("obsolete downloaded preview after selecting another real face", () => editor.VerifyOnlineFontLateFace(report));
                    Phase("actual synthetic online font window screenshot", () => editor.CaptureOnlineFontScreenshot(data, report));
                    Phase("per-user install conflict, rollback, persistence and owned cleanup", () => VerifyOnlineFontInstallation(data, report));
                    Phase("real explicit Install action and restart guidance", () => editor.VerifyOnlineFontInstallWindow(data, report));
                }
            }
            if (failures.Count == 0) result = 0;
        }
        catch (Exception error) { failures.Add(error); report.AppendLine("FAIL: setup: " + error); }
        finally
        {
            if (window != null) Phase("test window cleanup", () =>
            {
                window.onlineFontWindow?.Close(); window.fontFinderWindow?.Close(); window.sentenceCompletionWindow?.Close();
                window.closingApproved = true;
                window.recoveryTimer.Stop(); window.uiRefreshTimer.Stop(); window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel(); window.beta35PaginationTimer?.Stop(); window.beta35PaginationCancellation?.Cancel();
                window.Close();
            });
            OnlineFontService.DownloadOverride = download; OnlineFontService.GeneratorOverride = generator;
            OnlineFontService.CacheRootOverride = cacheRoot;
            OnlineFontInstallation.StepOverride = step; OnlineFontInstallation.ReceiptRootOverride = receiptRoot;
            Application.Current.ShutdownMode = shutdown;
            if (snapshotCaptured) Phase("original application-data restoration", () =>
            {
                if (Directory.Exists(appRoot))
                    foreach (var path in Directory.GetFiles(appRoot, "*", SearchOption.AllDirectories))
                        if (!originals.ContainsKey(path)) File.Delete(path);
                foreach (var pair in originals)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!); File.WriteAllBytes(pair.Key, pair.Value);
                }
                if (Directory.Exists(appRoot))
                    foreach (var path in Directory.GetDirectories(appRoot, "*", SearchOption.AllDirectories).Append(appRoot).OrderByDescending(path => path.Length))
                        if (!originalDirectories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
                foreach (var pair in originals)
                    OnlineRequire(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value), "A user-data file did not restore exactly: " + Path.GetFileName(pair.Key));
            });
            if (clipboardCaptured) Phase("original clipboard restoration", () =>
            {
                if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear();
            });
            // DirectWrite may retain a private preview file until process exit.
            // Its isolated temporary cache is never the user's download store.
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
            catch (IOException) { report.AppendLine("Isolated private preview cache retained until Windows releases its font handle: " + testRoot); }
            catch (UnauthorizedAccessException) { report.AppendLine("Isolated private preview cache retained until Windows releases its font handle: " + testRoot); }
            if (failures.Count > 0) result = 128;
            report.AppendLine("AI: deterministic in-process provider only. One pinned official font download is a live network integration check; no AI key is used.");
            report.AppendLine("Exit code: " + result);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "CtrlZed50OnlineFonts.txt"), report.ToString());
        }
        return result;
    }

    static OnlineFontFixture LoadOnlineFontFixture(StringBuilder report)
    {
        var catalogue = OnlineFontService.LoadCatalogue();
        OnlineRequire(catalogue.Commit == OnlineFontService.PinnedCommit && catalogue.Families.Count > 50,
            "The bundled supported-font catalogue is missing, unpinned or a placeholder.");
        var family = catalogue.Families.Single(font => font.Name == "Aclonica");
        var face = family.Faces.Single(font => !font.Italic && font.Weight == 400);
        var root = Path.Combine(Path.GetTempPath(), "CtrlZed50FontFixture");
        var bytes = File.ReadAllBytes(Path.Combine(root, Path.GetFileName(face.Path)));
        OnlineRequire(OnlineGitBlobHash(bytes) == face.BlobSha, "The CI font fixture does not match its official pinned Git blob.");
        using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "fixture.json")));
        OnlineRequire(record.RootElement.GetProperty("id").GetString() == family.Id,
            "The CI fixture record does not identify its actual catalogue family.");
        report.AppendLine($"Catalogue: {catalogue.Families.Count} supported static families from {catalogue.MetadataFamiliesScanned} metadata families; commit {catalogue.Commit}. Fixture: {family.Id}, {face.Path}, {bytes.Length} bytes; licence {family.Licence}.");
        return new OnlineFontFixture(catalogue, family, face, bytes);
    }

    static string OnlineFixtureManifest(OnlineFontFixture fixture)
    {
        var family = fixture.Family;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = new
            {
                repository = "google/fonts", commit = fixture.Catalogue.Commit,
                generatedUtc = fixture.Catalogue.GeneratedUtc.ToString("O"), metadataFamiliesScanned = 1,
                supportedFamilies = 1, excludedVariableOrUnsupported = 0, excludedMissingLicence = 0
            },
            scope = fixture.Catalogue.Scope,
            families = new[] { new
            {
                id = family.Id, name = family.Name, designer = family.Designer, category = family.Category,
                subsets = family.Subsets, tags = family.Tags, licence = family.Licence,
                metadataPath = family.MetadataPath, metadataBlobSha = family.MetadataBlobSha,
                faces = family.Faces.Select(face => new
                {
                    path = face.Path, blobSha = face.BlobSha, name = face.Name, fullName = face.FullName,
                    postScriptName = face.PostScriptName, style = face.Style, weight = face.Weight
                }),
                licenceFiles = family.LicenceFiles.Select(licence => new { path = licence.Path, blobSha = licence.BlobSha, text = licence.Text })
            } }
        });
    }

    static void VerifyOnlineFontCatalogue(OnlineFontFixture fixture, StringBuilder report)
    {
        var catalogue = fixture.Catalogue;
        OnlineRequire(catalogue.Families.Select(family => family.Id).Distinct(StringComparer.Ordinal).Count() == catalogue.Families.Count,
            "The catalogue contains duplicate opaque identifiers.");
        foreach (var family in catalogue.Families)
        {
            OnlineRequire(family.Faces.Count is > 0 and <= 4 && family.Faces.All(face => face.Weight is 400 or 700 && face.Style is "normal" or "italic" &&
                face.Path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && !face.Path.Contains('[')),
                "The advertised supported subset contains an unrecognised or variable face: " + family.Id);
            OnlineRequire(family.LicenceFiles.Count > 0 && family.LicenceFiles.All(licence =>
                OnlineGitBlobHash(Encoding.UTF8.GetBytes(licence.Text)) == licence.BlobSha),
                "Bundled licence text is missing or fails its pinned blob hash: " + family.Id);
            OnlineRequire(OnlineFontService.SourcePageUrl(catalogue, family).Host == "github.com", "A catalogue source page leaves the official repository.");
            foreach (var face in family.Faces)
            {
                var uri = OnlineFontService.FontFileUri(catalogue, face);
                OnlineRequire(uri.Scheme == "https" && uri.Host == "raw.githubusercontent.com" && uri.AbsolutePath.Contains("/google/fonts/" + catalogue.Commit + "/", StringComparison.Ordinal),
                    "A font URL is not tied to the exact official source snapshot.");
            }
        }
        var valid = OnlineFixtureManifest(fixture);
        OnlineRequire(OnlineFontService.ParseCatalogue(valid).Families.Single().Id == fixture.Family.Id, "A valid minimal pinned catalogue failed to round-trip.");
        void Invalid(Action<JsonObject> mutate, string description)
        {
            var root = JsonNode.Parse(valid)!.AsObject(); mutate(root);
            OnlineReject(() => OnlineFontService.ParseCatalogue(root.ToJsonString()), description);
        }
        Invalid(root => root["schemaVersion"] = 99, "Unknown catalogue schema was accepted.");
        Invalid(root => root["source"]!["repository"] = "untrusted/fonts", "A foreign repository was accepted.");
        Invalid(root => root["source"]!["commit"] = new string('a', 40), "An unpinned repository revision was accepted.");
        Invalid(root => root["families"]![0]!["id"] = "https://example.invalid/font", "A URL was accepted as a catalogue identifier.");
        Invalid(root => root["families"]![0]!["faces"]![0]!["path"] = "../outside.ttf", "A font traversal path was accepted.");
        Invalid(root => root["families"]![0]!["faces"]![0]!["path"] = fixture.Family.Id + "/%2e%2e/outside.ttf", "An encoded traversal path was accepted.");
        Invalid(root => root["families"]![0]!["faces"]![0]!["weight"] = 500, "An unsupported advertised font weight was accepted.");
        Invalid(root => root["families"]![0]!["faces"]![0]!["style"] = "invented", "An unsupported font style was accepted.");
        Invalid(root => root["families"]![0]!["licence"] = "UNKNOWN", "An unknown licence declaration was accepted.");
        Invalid(root => root["families"]![0]!["licenceFiles"] = new JsonArray(), "A family without its licence text was accepted.");
        Invalid(root => root["families"]![0]!["licenceFiles"]![0]!["text"] = "Changed licence", "A corrupted licence passed its pinned blob hash.");
        Invalid(root =>
        {
            root["families"]!.AsArray().Add(root["families"]![0]!.DeepClone());
            root["source"]!["supportedFamilies"] = 2; root["source"]!["metadataFamiliesScanned"] = 2;
        }, "Duplicate catalogue identifiers were accepted.");
        OnlineReject(() => OnlineFontService.ParseCatalogue(new string(' ', 21 * 1024 * 1024)), "An oversized catalogue was accepted.");
        report.AppendLine("The entire bundled subset has real static faces, source URLs and hash-verified licence text. Invalid schemas, revisions, IDs, paths, styles, weights, licences and duplicates were rejected.");
    }

    static string OnlineResponse(params (string Id, bool? Bold, bool? Italic, string Reason)[] items)
        => JsonSerializer.Serialize(new { matches = items.Select(item => new { source = item.Id, bold = item.Bold, italic = item.Italic, reason = item.Reason }) });

    static void VerifyOnlineFontSearch(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.GeneratorOverride;
        var mode = WritingModeProfiles.Get(WritingMode.Technical);
        try
        {
            var exact = OnlineFontService.Search(fixture.Catalogue, fixture.Family.Name + " Bold", mode);
            OnlineRequire(exact.Count > 0 && exact[0].Source == fixture.Family.Id && exact[0].Bold == true,
                "A real family plus explicit emphasis did not resolve to its opaque catalogue identifier.");
            var browse = OnlineFontService.Search(fixture.Catalogue, "", mode);
            OnlineRequire(browse.Count is > 0 and <= 8 && browse.All(match => fixture.Catalogue.Families.Any(family => family.Id == match.Source)),
                "Local browsing invented a source or exceeded the result bound.");
            var prompt = OnlineFontService.BuildPrompt(fixture.Catalogue, "a readable technical font", mode);
            OnlineRequire(prompt.Length < 300000 && prompt.Contains(fixture.Catalogue.Commit, StringComparison.Ordinal),
                "The bounded recommendation prompt lost its exact source snapshot.");
            string? captured = null; var calls = 0;
            OnlineFontService.GeneratorOverride = (value, token) =>
            {
                token.ThrowIfCancellationRequested(); captured = value; calls++;
                return Task.FromResult(OnlineResponse((fixture.Family.Id, false, false, "A real catalogue family.")));
            };
            var matches = OnlineAwait(OnlineFontService.SuggestAsync(fixture.Catalogue, fixture.Family.Name, mode));
            OnlineRequire(calls == 1 && captured != null && matches.Single().Source == fixture.Family.Id &&
                matches[0].Bold == null && matches[0].Italic == null, "An unspecific AI request changed existing emphasis or returned an invented source.");
            foreach (var invalid in new[]
            {
                OnlineResponse(("https://example.invalid/download.ttf", null, null, "Untrusted URL.")),
                OnlineResponse((fixture.Family.Id, null, null, "One."), (fixture.Family.Id, null, null, "Duplicate.")),
                "{\"matches\":[{\"source\":\"" + fixture.Family.Id + "\",\"bold\":\"yes\",\"italic\":null,\"reason\":\"Invalid flag.\"}]}",
                "```json\n" + OnlineResponse((fixture.Family.Id, null, null, "Fenced.")) + "\n```",
                "{\"matches\":[],\"downloadUrl\":\"https://example.invalid/\"}"
            })
            {
                OnlineFontService.GeneratorOverride = (_, _) => Task.FromResult(invalid);
                OnlineReject(() => OnlineAwait(OnlineFontService.SuggestAsync(fixture.Catalogue, "readable font", mode)), "An invalid AI response reached recommendations.");
            }
            var cancelledCalls = 0;
            OnlineFontService.GeneratorOverride = (_, _) => { cancelledCalls++; return Task.FromResult(OnlineResponse()); };
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            OnlineReject(() => OnlineAwait(OnlineFontService.SuggestAsync(fixture.Catalogue, "font", mode, cancelled.Token)), "A cancelled recommendation request completed.");
            OnlineRequire(cancelledCalls == 0, "A pre-cancelled request still called the provider.");
            report.AppendLine("Local searches require no network or AI. Deterministic AI uses opaque catalogue IDs, preserves implicit emphasis and rejects URLs, duplicates, malformed flags, fences and extra fields. Pre-cancellation prevented transport.");
        }
        finally { OnlineFontService.GeneratorOverride = original; }
    }

    static void VerifyOnlineFontBinary(OnlineFontFixture fixture, StringBuilder report)
    {
        OnlineFontService.ValidateFontBytes(fixture.Bytes);
        OnlineReject(() => OnlineFontService.ValidateFontBytes(Encoding.UTF8.GetBytes("<!doctype html><html>Not a font</html>")), "HTML was accepted as a TrueType font.");
        OnlineReject(() => OnlineFontService.ValidateFontBytes(fixture.Bytes[..Math.Min(40, fixture.Bytes.Length)]), "A truncated TrueType font was accepted.");
        var badTable = fixture.Bytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(badTable.AsSpan(20, 4), uint.MaxValue);
        OnlineReject(() => OnlineFontService.ValidateFontBytes(badTable), "An out-of-bounds font table was accepted.");
        var variable = fixture.Bytes.ToArray(); Encoding.ASCII.GetBytes("fvar").CopyTo(variable, 12);
        OnlineReject(() => OnlineFontService.ValidateFontBytes(variable), "An unsupported variable-font table was accepted.");
        var copied = Path.Combine(OnlineFontService.CacheRootOverride!, "changed-after-review.ttf");
        Directory.CreateDirectory(Path.GetDirectoryName(copied)!); File.WriteAllBytes(copied, fixture.Bytes);
        var reviewed = new OnlineFontFile(fixture.Face, copied, OnlineSha256(fixture.Bytes));
        OnlineFontService.ValidateFile(fixture.Catalogue, fixture.Family, reviewed);
        var changed = fixture.Bytes.ToArray(); changed[^1] ^= 1; File.WriteAllBytes(copied, changed);
        OnlineReject(() => OnlineFontService.ValidateFile(fixture.Catalogue, fixture.Family, reviewed), "A font changed after preview was accepted for installation.");
        File.Delete(copied);
        report.AppendLine("The authentic static fixture passed SFNT checks; HTML, truncation, out-of-bounds tables and an fvar-bearing font were rejected before native font rendering.");
    }

    static void VerifyOnlineFontLiveDownload(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride;
        try
        {
            OnlineFontService.DownloadOverride = null;
            var preview = OnlineAwait(OnlineFontService.PreviewAsync(fixture.Catalogue, fixture.Family.Id, fixture.Face));
            OnlineRequire(File.ReadAllBytes(preview.File.FilePath).AsSpan().SequenceEqual(fixture.Bytes), "The real pinned HTTP download differs from the independently acquired CI font fixture.");
            VerifyOnlinePreviewIdentity(preview);
            report.AppendLine("Live source: " + OnlineFontService.FontFileUri(fixture.Catalogue, fixture.Face) + "; SHA-256 " + preview.File.Sha256 + ". The document sample was not part of the URL.");
        }
        finally { OnlineFontService.DownloadOverride = original; }
    }

    static void VerifyOnlineFontDownloads(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride;
        var originalRoot = OnlineFontService.CacheRootOverride;
        try
        {
            OnlineFontService.CacheRootOverride = Path.Combine(originalRoot!, "Tampered cache");
            var cache = OnlineFontService.CachedFilePath(fixture.Catalogue, fixture.Family, fixture.Face);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!); File.WriteAllText(cache, "Not the pinned font");
            var calls = 0;
            OnlineFontService.DownloadOverride = (uri, token) =>
            {
                token.ThrowIfCancellationRequested(); calls++;
                OnlineRequire(uri == OnlineFontService.FontFileUri(fixture.Catalogue, fixture.Face), "A preview fetched an unrequested or unpinned URL.");
                return Task.FromResult(fixture.Bytes.ToArray());
            };
            var preview = OnlineAwait(OnlineFontService.PreviewAsync(fixture.Catalogue, fixture.Family.Id, fixture.Face));
            OnlineRequire(calls == 1 && File.ReadAllBytes(cache).AsSpan().SequenceEqual(fixture.Bytes), "A corrupt cache was reused or not replaced by a verified download.");
            VerifyOnlinePreviewIdentity(preview);
            OnlineAwait(OnlineFontService.PreviewAsync(fixture.Catalogue, fixture.Family.Id, fixture.Face));
            OnlineRequire(calls == 1, "A valid verified cache caused an unnecessary download.");
            OnlineFontService.ValidateFile(fixture.Catalogue, fixture.Family, preview.File);
            foreach (var invalid in new[] { Encoding.UTF8.GetBytes("<!doctype html>"), fixture.Bytes[..40], new byte[16 * 1024 * 1024 + 1] })
            {
                OnlineFontService.CacheRootOverride = Path.Combine(originalRoot!, "Invalid-" + Guid.NewGuid().ToString("N"));
                OnlineFontService.DownloadOverride = (_, _) => Task.FromResult(invalid);
                OnlineReject(() => OnlineAwait(OnlineFontService.PreviewAsync(fixture.Catalogue, fixture.Family.Id, fixture.Face)), "Invalid or oversized downloaded bytes reached a preview.");
                OnlineRequire(!Directory.Exists(OnlineFontService.CacheRootOverride) || !Directory.GetFiles(OnlineFontService.CacheRootOverride, "*.ttf", SearchOption.AllDirectories).Any(),
                    "An invalid download was retained as a verified cache font.");
            }
            OnlineFontService.CacheRootOverride = Path.Combine(originalRoot!, "Cancelled");
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = false;
            OnlineFontService.DownloadOverride = (_, _) => { started = true; return completion.Task; };
            using var cancellation = new CancellationTokenSource();
            var pending = OnlineFontService.PreviewAsync(fixture.Catalogue, fixture.Family.Id, fixture.Face, cancellation.Token);
            OnlineWait(() => started, "The cancellation download never started."); cancellation.Cancel(); completion.SetResult(fixture.Bytes.ToArray());
            OnlineReject(() => OnlineAwait(pending), "A non-cooperative late download escaped cancellation.");
            OnlineRequire(!Directory.Exists(OnlineFontService.CacheRootOverride) || !Directory.GetFiles(OnlineFontService.CacheRootOverride, "*.ttf", SearchOption.AllDirectories).Any(),
                "A cancelled preview installed a cache font after cancellation.");
            report.AppendLine("A corrupt cache was detected and replaced; a valid cache was reused. Wrong hashes, truncated/HTML/oversized bytes and a late non-cooperative cancelled download never produced a usable font.");
        }
        finally { OnlineFontService.DownloadOverride = original; OnlineFontService.CacheRootOverride = originalRoot; }
    }

    static void VerifyOnlinePreviewIdentity(OnlineFontPreview preview)
    {
        OnlineRequire(preview.PreviewFamily.GetTypefaces().Any(typeface => typeface.TryGetGlyphTypeface(out var glyph) &&
            glyph.FontUri.IsFile && string.Equals(Path.GetFullPath(glyph.FontUri.LocalPath), Path.GetFullPath(preview.File.FilePath), StringComparison.OrdinalIgnoreCase)),
            "The apparent preview is a system-font fallback rather than the exact downloaded font file.");
        OnlineRequire(OnlineSha256(File.ReadAllBytes(preview.File.FilePath)) == preview.File.Sha256,
            "The preview file's SHA-256 receipt does not describe its actual bytes.");
    }

    Window OpenOnlineFixtureWindow(OnlineFontFixture fixture, string source = "Synthetic online font preview source.")
    {
        onlineFontWindow?.Close();
        var paragraph = new Paragraph(new Run(source)
        {
            FontFamily = new FontFamily("Arial"), FontSize = 23, Foreground = Brushes.Navy,
            FontStyle = FontStyles.Italic, TextDecorations = TextDecorations.Underline
        }) { Margin = new Thickness(9, 7, 5, 3), TextIndent = 6 };
        SentenceSetDocument(new FlowDocument(paragraph), EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd); dirty = false;
        ShowOnlineFontFinder();
        var dialog = onlineFontWindow ?? throw new InvalidOperationException("The Google Fonts window did not open.");
        OnlineWait(() => OnlineControl<Button>(dialog, "OnlineFontSearch").IsEnabled, "The bundled online catalogue did not load.");
        OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = fixture.Family.Name;
        OnlineClick(dialog, "OnlineFontSearch");
        var results = OnlineControl<ListBox>(dialog, "OnlineFontResults");
        OnlineWait(() => results.Items.OfType<ListBoxItem>().Any(item => (item.Tag as FontFinderMatch)?.Source == fixture.Family.Id),
            "The online catalogue search did not return its exact real fixture family.");
        results.SelectedItem = results.Items.OfType<ListBoxItem>().First(item => (item.Tag as FontFinderMatch)?.Source == fixture.Family.Id);
        OnlineWait(() => OnlineControl<ComboBox>(dialog, "OnlineFontFaces").SelectedItem is OnlineFontFace,
            "The selected family did not expose its actual faces.");
        var faces = OnlineControl<ComboBox>(dialog, "OnlineFontFaces");
        faces.SelectedItem = faces.Items.OfType<OnlineFontFace>().Single(face => face.Path == fixture.Face.Path);
        PumpInput(); return dialog;
    }

    string OnlineDocumentState()
        => PagedDocumentText() + "\n" + FontFinderCharacterState(new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)) +
           "\n" + string.Join(";", EnumerateDocumentParagraphs().Select(FontFinderParagraphState));

    void VerifyOnlineFontWindow(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride;
        var calls = 0;
        try
        {
            OnlineFontService.DownloadOverride = (_, token) => { token.ThrowIfCancellationRequested(); calls++; return Task.FromResult(fixture.Bytes.ToArray()); };
            var registry = OnlineRegistrySnapshot();
            var dialog = OpenOnlineFixtureWindow(fixture);
            var source = OnlineDocumentState();
            OnlineRequire(dialog.Owner == this && calls == 0, "Opening or searching the online catalogue downloaded or installed a font.");
            var licenceText = OnlineNormalise(OnlineControl<TextBox>(dialog, "OnlineFontLicence").Text);
            OnlineRequire(fixture.Family.LicenceFiles.All(licence => licenceText.Contains(OnlineNormalise(licence.Text).Trim(), StringComparison.Ordinal)),
                "The real licence and notice text was not available before downloading the font.");
            OnlineRequire(OnlineControl<TextBlock>(dialog, "OnlineFontSource").Text.Contains(fixture.Catalogue.Commit, StringComparison.Ordinal),
                "The selected font's visible source is not tied to the pinned source revision.");
            var install = OnlineControl<Button>(dialog, "OnlineFontInstall");
            var accept = OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence");
            OnlineRequire(!install.IsEnabled && accept.IsChecked != true &&
                !OnlineControl<Button>(dialog, "OnlineFontAskAi").IsEnabled, "Install or AI bypassed its explicit permission gate.");
            accept.IsChecked = true; PumpInput();
            OnlineRequire(!install.IsEnabled, "Acknowledging a licence allowed installation before a verified preview.");
            accept.IsChecked = false;
            OnlineClick(dialog, "OnlineFontPreview");
            OnlineWait(() => OnlineControl<TextBlock>(dialog, "OnlineFontPreviewInformation").Text.Contains("Preview ready", StringComparison.Ordinal),
                "The explicit preview action did not render the downloaded font.");
            var sample = OnlineControl<TextBox>(dialog, "OnlineFontSample");
            var cached = OnlineFontService.CachedFilePath(fixture.Catalogue, fixture.Family, fixture.Face);
            OnlineRequire(sample.Visibility == Visibility.Visible && OnlineFamilyUsesFile(sample.FontFamily, cached),
                "The real preview sample is not rendered from the verified private font file.");
            sample.Text = "Editable local sample — British English 0123456789";
            OnlineControl<Slider>(dialog, "OnlineFontPreviewSize").Value = 38; PumpInput();
            OnlineRequire(OnlineDocumentState() == source && !dirty && OnlineRegistryEqual(registry, OnlineRegistrySnapshot()),
                "Previewing changed the document, dirty state or Windows font registry.");
            OnlineRequire(!install.IsEnabled, "A verified preview bypassed the unticked licence acknowledgement.");
            accept.IsChecked = true; PumpInput(); OnlineRequire(install.IsEnabled, "The explicit licence and verified-preview prerequisites did not enable Install.");
            accept.IsChecked = false; dialog.Close(); PumpInput();
            OnlineRequire(onlineFontWindow == null && OnlineDocumentState() == source && !dirty && OnlineRegistryEqual(registry, OnlineRegistrySnapshot()),
                "Closing an uninstalled preview altered the source or registered a font.");
            report.AppendLine("The real window searched its bundled catalogue without network access, showed complete source/licence details first, rendered an explicitly requested private font and kept document/registry unchanged. Both licence acknowledgement and a current verified preview were required for Install.");
        }
        finally { onlineFontWindow?.Close(); OnlineFontService.DownloadOverride = original; }
    }

    void VerifyOnlineFontResultActivation(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride;
        var originalRoot = OnlineFontService.CacheRootOverride;
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        try
        {
            OnlineFontService.CacheRootOverride = Path.Combine(originalRoot!, "Result activation");
            OnlineFontService.DownloadOverride = (_, _) => { calls++; return completion.Task; };
            var registry = OnlineRegistrySnapshot();
            var dialog = OpenOnlineFixtureWindow(fixture);
            var document = OnlineDocumentState();
            var results = OnlineControl<ListBox>(dialog, "OnlineFontResults");
            var row = results.Items.OfType<ListBoxItem>().Single(item => (item.Tag as FontFinderMatch)?.Source == fixture.Family.Id);
            var tabs = OnlineControl<TabControl>(dialog, "OnlineFontTabs");
            var sample = OnlineControl<TextBox>(dialog, "OnlineFontSample");
            var accept = OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence");
            var install = OnlineControl<Button>(dialog, "OnlineFontInstall");
            OnlineRequire(calls == 0 && tabs.SelectedIndex == 1 && sample.Visibility != Visibility.Visible,
                "Programmatic first-result selection downloaded a font or bypassed the initial licence view.");

            OnlineNativeClick(dialog, row);
            OnlineWait(() => calls == 1, "A physical click on the selected font row did not start its preview download.");
            OnlineRequire(tabs.SelectedIndex == 0 && sample.Visibility != Visibility.Visible && !completion.Task.IsCompleted && !install.IsEnabled,
                "The result click did not open Preview immediately while its controlled download remained unfinished.");
            completion.SetResult(fixture.Bytes.ToArray());
            OnlineWait(() => sample.Visibility == Visibility.Visible, "The clicked result's valid private-font preview did not finish.");
            var cached = OnlineFontService.CachedFilePath(fixture.Catalogue, fixture.Family, fixture.Face);
            OnlineRequire(OnlineFamilyUsesFile(sample.FontFamily, cached) && calls == 1,
                "The clicked result preview did not use its verified downloaded file.");

            // Clicking another physical target resets the native double-click
            // sequence before exercising an ordinary click on the same row.
            accept.IsChecked = false; PumpInput();
            OnlineNativeClick(dialog, (TabItem)tabs.Items[1]);
            OnlineRequire(tabs.SelectedIndex == 1, "The licence tab did not receive the physical repeat-preview setup click.");
            OnlineNativeClick(dialog, row);
            OnlineWait(() => tabs.SelectedIndex == 0 && sample.Visibility == Visibility.Visible,
                "Clicking the already-selected row did not return from Licence to its cached Preview.");
            OnlineRequire(calls == 1 && accept.IsChecked != true && !install.IsEnabled,
                "Repeating a result click fetched an already-verified font again or retained installation consent.");

            foreach (var key in new ushort[] { 0x0D, 0x20 })
            {
                tabs.SelectedIndex = 1;
                dialog.Activate(); SetForegroundWindow(new WindowInteropHelper(dialog).Handle);
                row.Focus(); PumpInput();
                SendKeys(new[] { KeyInput(key), KeyInput(key, true) });
                OnlineWait(() => tabs.SelectedIndex == 0 && sample.Visibility == Visibility.Visible,
                    "The selected result did not activate its preview using " + (key == 0x0D ? "Enter" : "Space") + ".");
                OnlineRequire(calls == 1 && accept.IsChecked != true && !install.IsEnabled,
                    "Keyboard preview activation bypassed the verified cache or licence acknowledgement.");
            }
            OnlineRequire(OnlineDocumentState() == document && !dirty && OnlineRegistryEqual(registry, OnlineRegistrySnapshot()),
                "Result-click or keyboard preview activation edited the source or installed a font.");
            dialog.Close();
            report.AppendLine("Actual native mouse clicks downloaded a result and opened Preview before the pending transfer finished. Re-clicking the same row reopened its verified cached preview; native Enter and Space also activated it. Programmatic first-result selection stayed download-free, and every preview retained explicit installation consent and unchanged document/registry state.");
        }
        finally
        {
            completion.TrySetResult(fixture.Bytes.ToArray()); onlineFontWindow?.Close();
            OnlineFontService.DownloadOverride = original; OnlineFontService.CacheRootOverride = originalRoot;
        }
    }

    void VerifyOnlineFontResultReplacement(OnlineFontFixture fixture, StringBuilder report)
    {
        var originalDownload = OnlineFontService.DownloadOverride;
        var originalGenerator = OnlineFontService.GeneratorOverride;
        var originalRoot = OnlineFontService.CacheRootOverride;
        var originalPermission = languageSettings.AllowAiLanguageServices;
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0; var secondCalls = 0; var firstReturned = false;
        var nextFamily = fixture.Catalogue.Families.Single(family => family.Name == "Lato");
        var nextFace = nextFamily.Faces.Single(face => !face.Italic && face.Weight == 400);
        var nextBytes = File.ReadAllBytes(Path.Combine(Path.GetTempPath(), "CtrlZed50FontFixture", Path.GetFileName(nextFace.Path)));
        OnlineRequire(OnlineGitBlobHash(nextBytes) == nextFace.BlobSha,
            "The second clickable catalogue result does not have its authentic static font fixture.");
        try
        {
            OnlineFontService.CacheRootOverride = Path.Combine(originalRoot!, "Result replacement");
            OnlineFontService.DownloadOverride = async (uri, _) =>
            {
                if (uri.AbsolutePath.EndsWith("/" + fixture.Face.Path, StringComparison.Ordinal))
                {
                    firstCalls++; var value = await completion.Task; firstReturned = true; return value;
                }
                if (uri.AbsolutePath.EndsWith("/" + nextFace.Path, StringComparison.Ordinal))
                { secondCalls++; return nextBytes.ToArray(); }
                throw new InvalidOperationException("The result-click regression requested an unexpected face.");
            };
            OnlineFontService.GeneratorOverride = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(OnlineResponse((fixture.Family.Id, null, null, "First real clickable family."),
                    (nextFamily.Id, null, null, "Second real clickable family.")));
            };
            languageSettings.AllowAiLanguageServices = true;
            var registry = OnlineRegistrySnapshot();
            var dialog = OpenOnlineFixtureWindow(fixture);
            var document = OnlineDocumentState();
            OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = fixture.Family.Name + " and " + nextFamily.Name + " for clicking";
            OnlineWait(() => OnlineControl<Button>(dialog, "OnlineFontAskAi").IsEnabled,
                "The deterministic two-family search did not become available.");
            OnlineClick(dialog, "OnlineFontAskAi");
            var results = OnlineControl<ListBox>(dialog, "OnlineFontResults");
            OnlineWait(() => results.Items.Count == 2, "The deterministic provider did not expose both real clickable results.");
            var first = results.Items.OfType<ListBoxItem>().Single(item => (item.Tag as FontFinderMatch)?.Source == fixture.Family.Id);
            var second = results.Items.OfType<ListBoxItem>().Single(item => (item.Tag as FontFinderMatch)?.Source == nextFamily.Id);
            OnlineRequire(firstCalls == 0 && secondCalls == 0,
                "Populating two provider results automatically downloaded a font.");
            OnlineNativeClick(dialog, first);
            OnlineWait(() => firstCalls == 1, "The first physical result click did not reach its controlled download.");
            OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence").IsChecked = true;
            OnlineNativeClick(dialog, second);
            OnlineRequire(ReferenceEquals(results.SelectedItem, second) &&
                OnlineControl<TabControl>(dialog, "OnlineFontTabs").SelectedIndex == 0 &&
                OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence").IsChecked != true &&
                !OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled,
                "Clicking a later result kept the old selection/consent or failed to open its Preview tab.");
            completion.SetResult(fixture.Bytes.ToArray());
            var sample = OnlineControl<TextBox>(dialog, "OnlineFontSample");
            var nextPath = OnlineFontService.CachedFilePath(fixture.Catalogue, nextFamily, nextFace);
            OnlineWait(() => firstReturned && secondCalls == 1 && sample.Visibility == Visibility.Visible &&
                OnlineFamilyUsesFile(sample.FontFamily, nextPath),
                "The later clicked result did not replace the valid but obsolete unfinished download.");
            OnlineRequire(firstCalls == 1 && OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence").IsChecked != true &&
                !OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled &&
                OnlineDocumentState() == document && !dirty && OnlineRegistryEqual(registry, OnlineRegistrySnapshot()),
                "The old valid result escaped cancellation, retained consent or edited/installed while the newer preview was selected.");
            dialog.Close();
            report.AppendLine("Two actual supplied families were returned by the deterministic provider without automatic downloads. Native clicks switched from an unfinished authentic Aclonica transfer to Lato Regular; the old transfer ignored cancellation, but only the later clicked family's verified preview appeared and neither font was installed.");
        }
        finally
        {
            completion.TrySetResult(fixture.Bytes.ToArray()); onlineFontWindow?.Close();
            OnlineFontService.DownloadOverride = originalDownload; OnlineFontService.GeneratorOverride = originalGenerator;
            OnlineFontService.CacheRootOverride = originalRoot; languageSettings.AllowAiLanguageServices = originalPermission;
        }
    }

    static void OnlineNativeClick(Window dialog, FrameworkElement target)
    {
        dialog.Activate(); SetForegroundWindow(new WindowInteropHelper(dialog).Handle);
        target.BringIntoView(); dialog.UpdateLayout(); PumpInput();
        OnlineRequire(target.IsVisible && target.ActualWidth > 0 && target.ActualHeight > 0,
            "The physical online-font click target has no visible extent.");
        var screen = target.PointToScreen(new Point(Math.Min(40, target.ActualWidth / 2), Math.Min(15, target.ActualHeight / 2)));
        SetPagedCursor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        SendKeys(new[]
        {
            new NativeInput { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 2 } } },
            new NativeInput { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 4 } } }
        });
        PumpInput();
    }

    void VerifyOnlineFontAiWindow(OnlineFontFixture fixture, StringBuilder report)
    {
        const string secret = "PRIVATE_DOCUMENT_SAMPLE_50_DO_NOT_SEND";
        var original = OnlineFontService.GeneratorOverride; var permission = languageSettings.AllowAiLanguageServices;
        var calls = 0; string? captured = null;
        try
        {
            OnlineFontService.GeneratorOverride = (prompt, token) =>
            {
                token.ThrowIfCancellationRequested(); captured = prompt; calls++;
                return Task.FromResult(OnlineResponse((fixture.Family.Id, null, null, "DETERMINISTIC_PRIVATE_ONLINE_50")));
            };
            languageSettings.AllowAiLanguageServices = false;
            var dialog = OpenOnlineFixtureWindow(fixture, secret);
            OnlineRequire(OnlineControl<TextBox>(dialog, "OnlineFontSample").Text.Contains(secret, StringComparison.Ordinal), "The private document fixture never reached the local preview sample.");
            OnlineRequire(!OnlineControl<Button>(dialog, "OnlineFontAskAi").IsEnabled, "Ask AI was enabled without explicit language-service permission.");
            OnlineClick(dialog, "OnlineFontAskAi"); PumpInput(); OnlineRequire(calls == 0, "The AI handler ignored disabled language permission.");
            dialog.Close(); languageSettings.AllowAiLanguageServices = true;
            dialog = OpenOnlineFixtureWindow(fixture, secret);
            OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = fixture.Family.Name + " for a readable technical report";
            var source = OnlineDocumentState();
            OnlineWait(() => OnlineControl<Button>(dialog, "OnlineFontAskAi").IsEnabled, "The permitted deterministic AI action did not become available.");
            OnlineClick(dialog, "OnlineFontAskAi");
            var results = OnlineControl<ListBox>(dialog, "OnlineFontResults");
            OnlineWait(() => results.Items.OfType<ListBoxItem>().Any(item => (item.Tag as FontFinderMatch)?.Reason == "DETERMINISTIC_PRIVATE_ONLINE_50"),
                "The actual AI action did not display its deterministic catalogue result.");
            OnlineRequire(calls == 1 && captured != null && !captured.Contains(secret, StringComparison.Ordinal) &&
                OnlineDocumentState() == source && !dirty, "The recommendation sent document prose or changed its source.");
            dialog.Close(); report.AppendLine("Ask AI remained gated even with an injected provider; the permitted request used the query/profile/catalogue and omitted the actual private sample shown in the preview.");
        }
        finally { onlineFontWindow?.Close(); OnlineFontService.GeneratorOverride = original; languageSettings.AllowAiLanguageServices = permission; }
    }

    void VerifyOnlineFontLateAi(OnlineFontFixture fixture, string change, StringBuilder report)
    {
        var original = OnlineFontService.GeneratorOverride;
        var permission = languageSettings.AllowAiLanguageServices; var mode = languageSettings.WritingMode;
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false; var returned = false;
        try
        {
            languageSettings.AllowAiLanguageServices = true;
            OnlineFontService.GeneratorOverride = async (_, _) =>
            {
                started = true; var response = await completion.Task; returned = true; return response; // Deliberately ignores cancellation.
            };
            var dialog = OpenOnlineFixtureWindow(fixture);
            var source = OnlineDocumentState();
            OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = fixture.Family.Name + " readable " + change;
            OnlineWait(() => OnlineControl<Button>(dialog, "OnlineFontAskAi").IsEnabled, "The delayed AI action did not become ready.");
            OnlineClick(dialog, "OnlineFontAskAi"); OnlineWait(() => started, "The delayed provider never started.");
            if (change == "query") OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = "Changed request";
            else if (change == "mode") languageSettings.WritingMode = mode == WritingMode.Fiction ? WritingMode.Technical : WritingMode.Fiction;
            else dialog.Close();
            completion.SetResult(OnlineResponse((fixture.Family.Id, null, null, "LATE_ONLINE_" + change)));
            OnlineWait(() => returned, "The non-cooperative provider never returned."); PumpInput(); PumpInput();
            OnlineRequire(!OnlineControl<ListBox>(dialog, "OnlineFontResults").Items.OfType<ListBoxItem>().Any(item =>
                (item.Tag as FontFinderMatch)?.Reason == "LATE_ONLINE_" + change) && OnlineDocumentState() == source && !dirty,
                "An obsolete AI result escaped its " + change + " guard.");
            if (change != "close") dialog.Close();
            report.AppendLine("An intentionally non-cooperative AI result returned after " + change + " and was discarded without document changes.");
        }
        finally
        {
            if (!completion.Task.IsCompleted) completion.TrySetResult(OnlineResponse());
            onlineFontWindow?.Close(); OnlineFontService.GeneratorOverride = original;
            languageSettings.AllowAiLanguageServices = permission; languageSettings.WritingMode = mode;
        }
    }

    void VerifyOnlineFontLatePreview(OnlineFontFixture fixture, string change, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride; var root = OnlineFontService.CacheRootOverride;
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false; var returned = false;
        try
        {
            OnlineFontService.CacheRootOverride = Path.Combine(root!, "Late-preview-" + change);
            OnlineFontService.DownloadOverride = async (_, _) => { started = true; var bytes = await completion.Task; returned = true; return bytes; };
            var registry = OnlineRegistrySnapshot();
            var dialog = OpenOnlineFixtureWindow(fixture); var source = OnlineDocumentState();
            OnlineClick(dialog, "OnlineFontPreview"); OnlineWait(() => started, "The delayed preview download never started.");
            if (change == "query") OnlineControl<TextBox>(dialog, "OnlineFontQuery").Text = "A different family"; else dialog.Close();
            completion.SetResult(fixture.Bytes.ToArray()); OnlineWait(() => returned, "The delayed preview transport never returned."); PumpInput(); PumpInput();
            OnlineRequire(OnlineControl<TextBox>(dialog, "OnlineFontSample").Visibility != Visibility.Visible &&
                !OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled && OnlineDocumentState() == source && !dirty && OnlineRegistryEqual(registry, OnlineRegistrySnapshot()),
                "A late preview became installable or changed the source after " + change + ".");
            if (change != "close") dialog.Close();
            report.AppendLine("A valid font download returned after " + change + "; its obsolete preview could neither appear nor enable installation.");
        }
        finally
        {
            completion.TrySetResult(fixture.Bytes.ToArray()); onlineFontWindow?.Close();
            OnlineFontService.DownloadOverride = original; OnlineFontService.CacheRootOverride = root;
        }
    }

    void VerifyOnlineFontLateFace(StringBuilder report)
    {
        var catalogue = OnlineFontService.LoadCatalogue();
        var family = catalogue.Families.Single(font => font.Name == "Lato");
        var face = family.Faces.Single(font => !font.Italic && font.Weight == 400);
        var other = family.Faces.First(font => font.Path != face.Path);
        var bytes = File.ReadAllBytes(Path.Combine(Path.GetTempPath(), "CtrlZed50FontFixture", Path.GetFileName(face.Path)));
        OnlineRequire(OnlineGitBlobHash(bytes) == face.BlobSha, "The secondary real-face fixture does not match its pinned font.");
        var fixture = new OnlineFontFixture(catalogue, family, face, bytes);
        var original = OnlineFontService.DownloadOverride; var root = OnlineFontService.CacheRootOverride;
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false; var returned = false;
        try
        {
            OnlineFontService.CacheRootOverride = Path.Combine(root!, "Late actual face");
            OnlineFontService.DownloadOverride = async (_, _) => { started = true; var data = await completion.Task; returned = true; return data; };
            var dialog = OpenOnlineFixtureWindow(fixture); var source = OnlineDocumentState();
            OnlineClick(dialog, "OnlineFontPreview"); OnlineWait(() => started, "The delayed real-face preview never started.");
            var faces = OnlineControl<ComboBox>(dialog, "OnlineFontFaces");
            faces.SelectedItem = faces.Items.OfType<OnlineFontFace>().Single(item => item.Path == other.Path);
            completion.SetResult(bytes); OnlineWait(() => returned, "The old real-face preview never returned."); PumpInput(); PumpInput();
            OnlineRequire((faces.SelectedItem as OnlineFontFace)?.Path == other.Path &&
                OnlineControl<TextBox>(dialog, "OnlineFontSample").Visibility != Visibility.Visible &&
                !OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled && OnlineDocumentState() == source && !dirty,
                "A downloaded face replaced the user's later real face choice or became installable.");
            dialog.Close(); report.AppendLine("A valid Lato Regular download returned after the user selected another actual supplied face; the old preview and installation permission were discarded.");
        }
        finally
        {
            completion.TrySetResult(bytes); onlineFontWindow?.Close();
            OnlineFontService.DownloadOverride = original; OnlineFontService.CacheRootOverride = root;
        }
    }

    void CaptureOnlineFontScreenshot(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride;
        try
        {
            OnlineFontService.DownloadOverride = (_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(fixture.Bytes.ToArray()); };
            var dialog = OpenOnlineFixtureWindow(fixture, "Synthetic preview: carefully chosen type for a technical report.");
            OnlineClick(dialog, "OnlineFontPreview");
            OnlineWait(() => OnlineControl<TextBox>(dialog, "OnlineFontSample").Visibility == Visibility.Visible, "The screenshot's genuine font preview did not finish.");
            OnlineControl<TextBox>(dialog, "OnlineFontSample").Text = "A genuine downloaded font\r\nThe quick brown fox jumps over the lazy dog.\r\n0123456789 — British English";
            OnlineControl<TabControl>(dialog, "OnlineFontTabs").SelectedIndex = 0;
            dialog.UpdateLayout(); PumpInput();
            OnlineRequire(dialog.ActualWidth > 0 && dialog.ActualHeight > 0, "The actual online font window has no visible extent.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(dialog); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(Path.GetTempPath(), "CtrlZed50OnlineFonts.png"); using (var stream = File.Create(path)) encoder.Save(stream);
            OnlineRequire(new FileInfo(path).Length > 1000, "The actual online font window screenshot is empty.");
            dialog.Close(); report.AppendLine("Actual WPF Google Fonts window captured with a verified private font and synthetic text to " + path + ".");
        }
        finally { onlineFontWindow?.Close(); OnlineFontService.DownloadOverride = original; }
    }

    const string OnlineTestFontRegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    static Dictionary<string, string> OnlineRegistrySnapshot()
    {
        using var fonts = Registry.CurrentUser.OpenSubKey(OnlineTestFontRegistryPath, false);
        return fonts?.GetValueNames().ToDictionary(name => name, name => fonts.GetValueKind(name) + "|" +
            JsonSerializer.Serialize(fonts.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    static bool OnlineRegistryEqual(Dictionary<string, string> before, Dictionary<string, string> after)
        => before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value);

    static string OnlineOwnedFontPath(OnlineFontFace face)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts", "CtrlZed-" + face.BlobSha + ".ttf");

    static void VerifyOnlineFontInstallation(OnlineFontFixture fixture, StringBuilder report)
    {
        var originalDownload = OnlineFontService.DownloadOverride; var originalStep = OnlineFontInstallation.StepOverride;
        var baseline = OnlineRegistrySnapshot();
        OnlineFontInstallResult? installed = null;
        var expectedName = fixture.Face.FullName + " (TrueType)";
        var expectedPath = OnlineOwnedFontPath(fixture.Face);
        try
        {
            OnlineRequire(!baseline.ContainsKey(expectedName) && !File.Exists(expectedPath),
                "The unique integration font is already present; the regression will not replace a user's existing font.");
            OnlineFontService.DownloadOverride = (_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(fixture.Bytes.ToArray()); };
            var package = OnlineAwait(OnlineFontService.DownloadFamilyAsync(fixture.Catalogue, fixture.Family.Id));
            var marker = Path.Combine(Path.GetTempPath(), "CtrlZed50-external-font-" + Guid.NewGuid().ToString("N") + ".ttf");
            File.WriteAllText(marker, "External data that the installer must leave alone.");
            try
            {
                using (var fonts = Registry.CurrentUser.CreateSubKey(OnlineTestFontRegistryPath, true))
                    fonts!.SetValue(expectedName, marker, RegistryValueKind.String);
                OnlineReject(() => OnlineAwait(OnlineFontInstallation.InstallAsync(package)), "The installer overwrote a conflicting external registration.");
                using var fontsAfter = Registry.CurrentUser.OpenSubKey(OnlineTestFontRegistryPath, false);
                OnlineRequire((fontsAfter?.GetValue(expectedName) as string) == marker &&
                    File.ReadAllText(marker) == "External data that the installer must leave alone." && !File.Exists(expectedPath),
                    "A conflicting external registration or file was changed.");
            }
            finally
            {
                using var fonts = Registry.CurrentUser.OpenSubKey(OnlineTestFontRegistryPath, true);
                if ((fonts?.GetValue(expectedName) as string) == marker) fonts!.DeleteValue(expectedName, false);
                File.Delete(marker);
            }
            foreach (var stage in new[] { "after-file", "after-registry", "after-session", "before-receipt" })
            {
                var reached = false;
                OnlineFontInstallation.StepOverride = step => { if (step == stage) { reached = true; throw new IOException("Injected installation failure at " + stage); } };
                OnlineReject(() => OnlineAwait(OnlineFontInstallation.InstallAsync(package)), "An injected installation failure was reported as success.");
                OnlineFontInstallation.StepOverride = null;
                OnlineRequire(reached && !File.Exists(expectedPath) && OnlineRegistryEqual(baseline, OnlineRegistrySnapshot()),
                    "Rollback left a font or changed registry data after " + stage + ".");
                var receipts = OnlineFontInstallation.ReceiptRootOverride!;
                OnlineRequire(!Directory.Exists(receipts) || !Directory.GetFiles(receipts, "*", SearchOption.AllDirectories).Any(),
                    "Rollback left a receipt or licence file after " + stage + ".");
            }
            OnlineFontInstallation.StepOverride = null;
            installed = OnlineAwait(OnlineFontInstallation.InstallAsync(package));
            OnlineRequire(installed.Installed && !installed.AlreadyInstalled && installed.RestartRequired &&
                installed.Files.Count == package.Files.Count && installed.ReceiptPath != null && File.Exists(installed.ReceiptPath),
                "The per-user install did not return an owned persistent receipt and honest restart requirement.");
            VerifyOnlineInstalledReceipt(fixture, installed);
            var receiptBytes = File.ReadAllBytes(installed.ReceiptPath!); var registered = OnlineRegistrySnapshot();
            var duplicate = OnlineAwait(OnlineFontInstallation.InstallAsync(package));
            OnlineRequire(duplicate.Installed && duplicate.AlreadyInstalled && duplicate.RestartRequired &&
                OnlineRegistryEqual(registered, OnlineRegistrySnapshot()) && File.ReadAllBytes(installed.ReceiptPath!).AsSpan().SequenceEqual(receiptBytes),
                "Installing identical pinned data again changed the existing installation or receipt.");
            OnlineFontInstallation.CleanupForTest(duplicate);
            OnlineRequire(installed.Files.All(file => File.Exists(file.Path)), "A duplicate result was allowed to remove an older installation.");
            OnlineFontInstallation.CleanupForTest(installed); installed = null;
            OnlineRequire(!File.Exists(expectedPath) && OnlineRegistryEqual(baseline, OnlineRegistrySnapshot()), "Owned cleanup did not restore the original font registration state.");
            report.AppendLine("The real per-user font transaction preserved a conflicting external entry, rolled back after file/registry/session/receipt failures, registered verified bytes and full licence records, required restart, remained idempotent and removed only its own newly created state. No machine-wide font key was written.");
        }
        finally
        {
            OnlineFontInstallation.StepOverride = null;
            if (installed != null) OnlineFontInstallation.CleanupForTest(installed);
            OnlineFontInstallation.StepOverride = originalStep; OnlineFontService.DownloadOverride = originalDownload;
        }
    }

    static void VerifyOnlineInstalledReceipt(OnlineFontFixture fixture, OnlineFontInstallResult result)
    {
        using var fonts = Registry.CurrentUser.OpenSubKey(OnlineTestFontRegistryPath, false);
        foreach (var file in result.Files)
        {
            OnlineRequire(File.Exists(file.Path) && OnlineSha256(File.ReadAllBytes(file.Path)) == file.Sha256 &&
                string.Equals(fonts?.GetValue(file.RegistryName) as string, file.Path, StringComparison.OrdinalIgnoreCase),
                "The installation receipt does not describe actual per-user registered font bytes.");
            OnlineRequire(Path.GetFullPath(file.Path).StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "The installation wrote outside the current user's font directory.");
        }
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(result.ReceiptPath!));
        var root = receipt.RootElement;
        OnlineRequire(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("owner").GetString() == "CtrlZed" &&
            root.GetProperty("commit").GetString() == fixture.Catalogue.Commit && root.GetProperty("familyId").GetString() == fixture.Family.Id,
            "The installation receipt lost its source, family or owner identity.");
        foreach (var licence in root.GetProperty("licences").EnumerateArray())
        {
            var path = licence.GetProperty("path").GetString()!;
            var sourcePath = licence.GetProperty("sourcePath").GetString();
            var source = fixture.Family.LicenceFiles.Single(item => item.Path == sourcePath);
            OnlineRequire(File.ReadAllBytes(path).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(source.Text)),
                "The installed licence/notice was not retained verbatim.");
        }
    }

    void VerifyOnlineFontInstallWindow(OnlineFontFixture fixture, StringBuilder report)
    {
        var original = OnlineFontService.DownloadOverride; var baseline = OnlineRegistrySnapshot();
        OnlineFontInstallResult? installed = null;
        try
        {
            OnlineFontService.DownloadOverride = (_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(fixture.Bytes.ToArray()); };
            onlineFontLastInstallResult = null;
            var dialog = OpenOnlineFixtureWindow(fixture); var source = OnlineDocumentState();
            OnlineRequire(!OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled, "Install was enabled before a verified preview and acknowledgement.");
            OnlineClick(dialog, "OnlineFontPreview");
            OnlineWait(() => OnlineControl<TextBox>(dialog, "OnlineFontSample").Visibility == Visibility.Visible, "The install workflow's verified preview did not finish.");
            OnlineRequire(!OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled, "Previewing automatically accepted licence terms.");
            OnlineControl<CheckBox>(dialog, "OnlineFontAcceptLicence").IsChecked = true;
            OnlineWait(() => OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled, "The explicit install prerequisites did not enable Install.");
            OnlineClick(dialog, "OnlineFontInstall");
            OnlineWait(() => onlineFontLastInstallResult != null || OnlineControl<TextBlock>(dialog, "OnlineFontStatus").Text.Contains("failed", StringComparison.OrdinalIgnoreCase),
                "The explicit Install action did not complete.");
            installed = onlineFontLastInstallResult;
            OnlineRequire(installed is { Installed: true, AlreadyInstalled: false, RestartRequired: true },
                "The actual install action did not produce a new per-user installation: " + OnlineControl<TextBlock>(dialog, "OnlineFontStatus").Text);
            VerifyOnlineInstalledReceipt(fixture, installed!);
            OnlineRequire(OnlineControl<TextBlock>(dialog, "OnlineFontStatus").Text.Contains("restart", StringComparison.OrdinalIgnoreCase) &&
                OnlineDocumentState() == source && !dirty && !OnlineControl<Button>(dialog, "OnlineFontInstall").IsEnabled,
                "Installation claimed immediate availability, altered the source or stayed repeatable without a new choice.");
            dialog.Close(); OnlineFontInstallation.CleanupForTest(installed!); installed = null;
            OnlineRequire(OnlineRegistryEqual(baseline, OnlineRegistrySnapshot()), "The UI integration cleanup changed the original font registry.");
            report.AppendLine("The real Install button required a current authentic preview and explicit licence acknowledgement, installed for the current Windows user, gave restart guidance and left the document untouched. Its owned state was then removed.");
        }
        finally
        {
            onlineFontWindow?.Close();
            if (installed != null) OnlineFontInstallation.CleanupForTest(installed);
            OnlineFontService.DownloadOverride = original;
        }
    }

    static bool OnlineFamilyUsesFile(FontFamily family, string path)
        => family.GetTypefaces().Any(typeface => typeface.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.IsFile &&
            string.Equals(Path.GetFullPath(glyph.FontUri.LocalPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    static string OnlineGitBlobHash(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0")); hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static string OnlineSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string OnlineNormalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    static T OnlineControl<T>(Window window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidOperationException("The online font window is missing " + name + ".");
    static void OnlineClick(Window window, string name)
        => OnlineControl<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void OnlineRequire(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void OnlineReject(Action work, string message)
    {
        try { work(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or IOException or
            JsonException or OperationCanceledException or AggregateException or FormatException or NotSupportedException) { return; }
        throw new InvalidOperationException(message);
    }
    static T OnlineAwait<T>(Task<T> task)
    {
        OnlineWait(() => task.IsCompleted, "An online font operation timed out."); return task.GetAwaiter().GetResult();
    }
    static void OnlineWait(Func<bool> ready, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(45)) throw new InvalidOperationException(message);
            PumpInput();
        }
    }
}
