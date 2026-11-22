using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

/// <summary>The native document keeps an SVG master alongside its RTF-compatible picture.</summary>
public sealed class VectorImageData
{
    public int ImageIndex { get; set; }
    public string Svg { get; set; }="";
}

public static class VectorImageSupport
{
    const string MarkerPrefix="CtrlZedVector47:";
    sealed record Master(SignatureAsset Asset,ImageSource? Fallback,string? Placement);
    sealed class Marker
    {
        public Marker() { }
        public string Svg {get;set;}="";
        public string? Placement {get;set;}
    }
    static readonly ConditionalWeakTable<Image,Master> Masters=new();
    static readonly ConcurrentDictionary<string,WeakReference<SignatureAsset>> Assets=new();

    public static void SetAsset(Image image,SignatureAsset asset)
    {
        image.Source=asset.Vector!=null?VectorBitmapCache.GetBitmap(asset):asset.Bitmap;
        Attach(image,asset);
    }

    static string Key(string svg)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg)));

    static void Attach(Image image,SignatureAsset asset,string? placement=null)
    {
        Masters.Remove(image);
        if(asset.Vector!=null && !string.IsNullOrEmpty(asset.Svg))
        {
            Assets[Key(asset.Svg)]=new WeakReference<SignatureAsset>(asset);
            Masters.Add(image,new Master(asset,image.Source,placement));
            WriteMarker(image,asset.Svg,placement);
        }
        else ClearMarker(image);
    }

    static Master? Find(Image image)
    {
        if(Masters.TryGetValue(image,out var master))
        {
            // A macro can replace a picture's source. Its old SVG must not
            // replace that new picture during saving or printing.
            if(ReferenceEquals(image.Source,master.Fallback))return master;
            Masters.Remove(image);ClearMarker(image);return null;
        }
        if(image.Tag is not string tag || !tag.StartsWith(MarkerPrefix,StringComparison.Ordinal))return null;
        try
        {
            var marker=JsonSerializer.Deserialize<Marker>(tag[MarkerPrefix.Length..]);
            if(marker!=null && !string.IsNullOrEmpty(marker.Svg) &&
               VectorBitmapCache.IsBitmapForSvg(image.Source,marker.Svg))
            {
                var key=Key(marker.Svg);
                if(!Assets.TryGetValue(key,out var weak) || !weak.TryGetTarget(out var asset))
                    asset=SignatureImages.FromSvg(marker.Svg);
                Attach(image,asset,marker.Placement);
                return Masters.TryGetValue(image,out master)?master:null;
            }
        }
        catch(Exception ex) when(ex is JsonException or System.IO.InvalidDataException or System.Xml.XmlException or FormatException) { }
        ClearMarker(image);
        return null;
    }

    static void WriteMarker(Image image,string svg,string? placement)
        =>image.Tag=MarkerPrefix+JsonSerializer.Serialize(new Marker{Svg=svg,Placement=placement});

    static void ClearMarker(Image image)
    {
        if(image.Tag is string tag && tag.StartsWith(MarkerPrefix,StringComparison.Ordinal))image.Tag=null;
    }

    public static bool TryGetPlacementData(Image image,out string placement)
    {
        placement=Find(image)?.Placement??"";
        return placement.Length>0;
    }

    public static void SetPlacementData(Image image,string? placement)
    {
        if(Find(image) is not { } master || master.Placement==placement)return;
        Attach(image,master.Asset,placement);
    }

    public static bool TryGetSvg(Image image,out string svg)
    {
        svg=Find(image)?.Asset.Svg??"";
        return svg.Length>0;
    }

    public static ImageSource? GetDisplaySource(Image image)
        =>Find(image)?.Asset.Vector??image.Source;

    public static double? GetAspectRatio(Image image)
        =>Find(image) is { } master?master.Asset.Width/master.Asset.Height:null;

    public static List<VectorImageData> Capture(FlowDocument document)
    {
        var result=new List<VectorImageData>();
        var index=0;
        foreach(var item in MainWindow.EnumerateImageContainers(document))
        {
            if(TryGetSvg(item.Image,out var svg))result.Add(new VectorImageData{ImageIndex=index,Svg=svg});
            index++;
        }
        return result;
    }

    public static void Restore(FlowDocument document,IEnumerable<VectorImageData>? values)
    {
        if(values==null)return;
        var images=MainWindow.EnumerateImageContainers(document).Select(item=>item.Image).ToArray();
        var assets=new Dictionary<string,SignatureAsset>(StringComparer.Ordinal);
        foreach(var value in values)
        {
            if(value.ImageIndex<0 || value.ImageIndex>=images.Length || string.IsNullOrEmpty(value.Svg))continue;
            if(!assets.TryGetValue(value.Svg,out var asset))
                assets[value.Svg]=asset=SignatureImages.FromSvg(value.Svg);
            SetAsset(images[value.ImageIndex],asset);
        }
    }

    public static void Copy(FlowDocument source,FlowDocument target,bool forPrint=false)
    {
        var originals=MainWindow.EnumerateImageContainers(source).Select(item=>item.Image).ToArray();
        var copies=MainWindow.EnumerateImageContainers(target).Select(item=>item.Image).ToArray();
        for(var index=0;index<Math.Min(originals.Length,copies.Length);index++)
        {
            if(Find(originals[index]) is not { } master)continue;
            if(forPrint)copies[index].Source=master.Asset.Vector;
            Attach(copies[index],master.Asset,master.Placement);
        }
    }
}
