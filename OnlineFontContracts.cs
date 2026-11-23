using System.Windows.Media;

namespace CtrlZed;

internal sealed record OnlineFontFace(string Path, string BlobSha, string Name,
    string FullName, string PostScriptName, string Style, int Weight)
{
    public bool Italic => Style == "italic";
    public string DisplayName => Weight == 700 ? (Italic ? "Bold Italic" : "Bold") : (Italic ? "Italic" : "Regular");
}

internal sealed record OnlineFontLicence(string Path, string BlobSha, string Text);

internal sealed record OnlineFontFamily(string Id, string Name, string Designer, string Category,
    IReadOnlyList<string> Subsets, IReadOnlyList<string> Tags, string Licence,
    string MetadataPath, string MetadataBlobSha,
    IReadOnlyList<OnlineFontFace> Faces, IReadOnlyList<OnlineFontLicence> LicenceFiles);

internal sealed record OnlineFontCatalogue(string Commit, DateTimeOffset GeneratedUtc,
    int MetadataFamiliesScanned, int ExcludedVariableOrUnsupported, int ExcludedMissingLicence,
    string Scope, IReadOnlyList<OnlineFontFamily> Families);

internal sealed record OnlineFontFile(OnlineFontFace Face, string FilePath, string Sha256);

internal sealed record OnlineFontPreview(OnlineFontFamily Family, OnlineFontFile File,
    FontFamily PreviewFamily, string ActualFamilyName);

internal sealed record OnlineFontPackage(OnlineFontCatalogue Catalogue, OnlineFontFamily Family,
    IReadOnlyList<OnlineFontFile> Files, string DirectoryPath);

internal sealed record OnlineFontInstalledFile(string Path, string RegistryName, string Sha256);

internal sealed record OnlineFontInstallResult(bool Installed, bool AlreadyInstalled,
    bool RestartRequired, string Message, IReadOnlyList<OnlineFontInstalledFile> Files,
    string? ReceiptPath);
