using System.Collections.Concurrent;
using System.IO;
using System.IO.Packaging;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

/// <summary>
/// Gives the compatibility bitmap a stable pack URI for WPF's XAML-based
/// Undo/Redo cloning. The package and its seekable part streams live entirely
/// in memory; protected document pictures never require a plaintext disk cache.
/// </summary>
public static class VectorBitmapCache
{
    static readonly object Gate=new();
    static readonly Uri PackageAddress=new("memorystore://ctrlzed-vector47/",UriKind.Absolute);
    static readonly MemoryImagePackage Images=new();
    static readonly Dictionary<string,WeakReference<BitmapImage>> Bitmaps=new(StringComparer.Ordinal);

    static VectorBitmapCache()
    {
        PackageStore.AddPackage(PackageAddress,Images);
    }

    public static Uri UriForSvg(string svg)
    {
        ArgumentException.ThrowIfNullOrEmpty(svg);
        return PackUriHelper.Create(PackageAddress,PartAddress(Key(svg)));
    }

    public static bool IsBitmapForSvg(ImageSource? source,string svg)
    {
        if(source==null || string.IsNullOrEmpty(svg))return false;
        var expected=UriForSvg(svg);
        if(source is BitmapImage {UriSource:{ } direct})return direct.Equals(expected);
        // ImageSourceConverter can reload a URI as BitmapFrameDecode rather
        // than BitmapImage when the restrictive Undo XAML reader clones it.
        // Its public string representation still carries the source URI.
        try
        {
            return Uri.TryCreate(source.ToString(),UriKind.Absolute,out var uri) && uri.Equals(expected);
        }
        catch(Exception error) when(error is NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return false;
        }
    }

    public static BitmapImage GetBitmap(SignatureAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if(string.IsNullOrEmpty(asset.Svg) || asset.Vector==null)return asset.Bitmap;
        var key=Key(asset.Svg);
        lock(Gate)
        {
            var partUri=PartAddress(key);
            if(Bitmaps.TryGetValue(key,out var reference) && reference.TryGetTarget(out var cached))return cached;
            if(!Images.Contains(partUri))
            {
                var encoder=new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(asset.Bitmap));
                using var data=new MemoryStream();
                encoder.Save(data);
                // Keep compressed bytes for Undo, without retaining decoded
                // bitmap buffers when no displayed image still uses them.
                // Each part supplies an independent seekable read stream.
                lock(Images)Images.Add(partUri,data.ToArray());
            }
            var bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.UriSource=PackUriHelper.Create(PackageAddress,partUri);
            bitmap.EndInit();
            if(bitmap.IsDownloading)
                throw new InvalidDataException("The signature compatibility image did not load completely.");
            bitmap.Freeze();
            Bitmaps[key]=new WeakReference<BitmapImage>(bitmap);
            return bitmap;
        }
    }

    static string Key(string svg)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg))).ToLowerInvariant();
    static Uri PartAddress(string key)=>PackUriHelper.CreatePartUri(new Uri("/signatures/"+key+".png",UriKind.Relative));

    sealed class MemoryImagePackage : Package
    {
        readonly ConcurrentDictionary<string,MemoryImagePart> parts=new(StringComparer.OrdinalIgnoreCase);

        public MemoryImagePackage():base(FileAccess.Read) { }

        public bool Contains(Uri partUri)=>parts.ContainsKey(partUri.ToString());

        public void Add(Uri partUri,byte[] bytes)
            =>parts.TryAdd(partUri.ToString(),new MemoryImagePart(this,partUri,bytes));

        protected override PackagePart GetPartCore(Uri partUri)
            =>parts.TryGetValue(partUri.ToString(),out var part)?part:null!;

        protected override PackagePart[] GetPartsCore()=>parts.Values.Cast<PackagePart>().ToArray();
        protected override PackagePart CreatePartCore(Uri partUri,string contentType,CompressionOption compressionOption)
            =>throw new NotSupportedException("The signature image package is read-only.");
        protected override void DeletePartCore(Uri partUri)
            =>throw new NotSupportedException("The signature image package is read-only.");
        protected override void FlushCore() { }
    }

    sealed class MemoryImagePart : PackagePart
    {
        readonly byte[] bytes;

        public MemoryImagePart(Package package,Uri uri,byte[] bytes)
            :base(package,uri,"image/png",CompressionOption.NotCompressed)
            =>this.bytes=bytes;

        protected override Stream GetStreamCore(FileMode mode,FileAccess access)
        {
            if(access!=FileAccess.Read || mode is not FileMode.Open and not FileMode.OpenOrCreate)
                throw new NotSupportedException("Signature image parts support reading only.");
            return new MemoryStream(bytes,writable:false);
        }
    }
}
