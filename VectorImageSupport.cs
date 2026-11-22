using System.Runtime.CompilerServices;
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
    sealed record Master(SignatureAsset Asset,ImageSource? Fallback);
    static readonly ConditionalWeakTable<Image,Master> Masters=new();

    public static void SetAsset(Image image,SignatureAsset asset)
    {
        image.Source=asset.Bitmap;
        Attach(image,asset);
    }

    static void Attach(Image image,SignatureAsset asset)
    {
        Masters.Remove(image);
        if(asset.Vector!=null && !string.IsNullOrEmpty(asset.Svg))
            Masters.Add(image,new Master(asset,image.Source));
    }

    static Master? Find(Image image)
    {
        if(!Masters.TryGetValue(image,out var master))return null;
        // A macro can replace a picture's source. Its old SVG must not then
        // replace that new picture during saving or printing.
        if(ReferenceEquals(image.Source,master.Fallback))return master;
        Masters.Remove(image);
        return null;
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
            Attach(copies[index],master.Asset);
        }
    }
}
