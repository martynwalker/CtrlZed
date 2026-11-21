using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Markup;
using HtmlAgilityPack;

namespace CtrlZed;

internal static class HtmlClipboardFormat
{
    internal const int MaximumHtmlBytes=4*1024*1024;
    const int MaximumNodes=50000;
    const int MaximumDepth=160;
    static readonly XNamespace Wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly HashSet<string> Ignore=new(StringComparer.OrdinalIgnoreCase)
    {
        "head","script","style","iframe","object","embed","noscript","template","svg","math","link","meta","base","input","button","select","textarea"
    };
    static readonly HashSet<string> Blocks=new(StringComparer.OrdinalIgnoreCase)
    {
        "html","body","div","section","article","main","header","footer","nav","aside","p","h1","h2","h3","h4","h5","h6",
        "blockquote","pre","address","ul","ol","li","table","thead","tbody","tfoot","tr","td","th","hr","figure","figcaption"
    };

    sealed record Format(
        string Font="Arial",double Size=16,string Weight="Normal",bool Italic=false,
        bool Underline=false,bool Strike=false,string Colour="#000000",string? Background=null,
        string Align="Left",bool Preformatted=false,string? Baseline=null,string? LineHeight=null,double Indent=0);

    internal static bool TryConvert(string clipboardHtml,out string xaml)
    {
        xaml="";
        if(string.IsNullOrWhiteSpace(clipboardHtml) || Encoding.UTF8.GetByteCount(clipboardHtml)>MaximumHtmlBytes)return false;
        try
        {
            var html=ExtractHtml(clipboardHtml);
            var document=new HtmlDocument();
            document.LoadHtml(html);
            var included=SelectedNodes(document.DocumentNode,html.Contains("<!--StartFragment",StringComparison.OrdinalIgnoreCase));
            var section=new XElement(Wpf+"Section",new XAttribute(XNamespace.Xml+"space","preserve"));
            RenderContainer(document.DocumentNode,section,new Format(),included,0);
            if(!section.Descendants(Wpf+"Run").Any(run=>run.Value.Length>0) && !section.Descendants(Wpf+"LineBreak").Any())return false;
            xaml=section.ToString(SaveOptions.DisableFormatting);
            // Validate only our generated, whitelisted markup before handing it
            // to WPF. Unsupported layout values must leave native paste usable.
            if(XamlReader.Parse(xaml) is not Section)return false;
            return true;
        }
        catch(FormatException){return false;}
        catch(ArgumentException){return false;}
        catch(OverflowException){return false;}
        catch(InvalidOperationException){return false;}
        catch(XamlParseException){return false;}
    }

    static string ExtractHtml(string payload)
    {
        var bytes=Encoding.UTF8.GetBytes(payload);
        int Offset(string name)
        {
            var match=Regex.Match(payload[..Math.Min(payload.Length,4096)],@"(?im)^"+name+@":\s*(-?\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:-1;
        }
        var start=Offset("StartHTML");
        var end=Offset("EndHTML");
        if(start<0 || start>bytes.Length)start=0;
        if(end<=start || end>bytes.Length)end=bytes.Length;
        var fragmentStart=Offset("StartFragment");
        var fragmentEnd=Offset("EndFragment");
        if(fragmentStart>=start && fragmentEnd>=fragmentStart && fragmentEnd<=end)
        {
            // Offsets in CF_HTML count UTF-8 bytes, not .NET UTF-16 characters.
            // Keep surrounding elements so their inherited inline styles apply.
            return Encoding.UTF8.GetString(bytes,start,fragmentStart-start)+"<!--StartFragment-->"+
                   Encoding.UTF8.GetString(bytes,fragmentStart,fragmentEnd-fragmentStart)+"<!--EndFragment-->"+
                   Encoding.UTF8.GetString(bytes,fragmentEnd,end-fragmentEnd);
        }
        var html=Encoding.UTF8.GetString(bytes,start,end-start).TrimEnd('\0');
        var first=html.IndexOf('<');
        return first>0?html[first..]:html;
    }

    static HashSet<HtmlNode> SelectedNodes(HtmlNode root,bool hasMarkers)
    {
        var selected=new HashSet<HtmlNode>();
        var active=!hasMarkers;
        var count=0;
        bool Visit(HtmlNode node,int depth)
        {
            if(++count>MaximumNodes || depth>MaximumDepth)throw new FormatException("HTML fragment is too complex.");
            if(node.NodeType==HtmlNodeType.Comment)
            {
                var value=node.OuterHtml;
                if(value.Contains("StartFragment",StringComparison.OrdinalIgnoreCase))active=true;
                else if(value.Contains("EndFragment",StringComparison.OrdinalIgnoreCase))active=false;
                return false;
            }
            var include=active && (node.NodeType is HtmlNodeType.Text or HtmlNodeType.Element);
            foreach(var child in node.ChildNodes)include=Visit(child,depth+1)||include;
            if(include)selected.Add(node);
            return include;
        }
        Visit(root,0);
        return selected;
    }

    static void RenderContainer(HtmlNode container,XElement blocks,Format inherited,HashSet<HtmlNode> selected,int depth)
    {
        if(depth>MaximumDepth)return;
        XElement? paragraph=null;
        foreach(var node in container.ChildNodes)
        {
            if(!selected.Contains(node) || Ignore.Contains(node.Name))continue;
            if(node.NodeType==HtmlNodeType.Element && Blocks.Contains(node.Name))
            {
                paragraph=null;
                RenderBlock(node,blocks,inherited,selected,depth+1);
                continue;
            }
            if(node.NodeType==HtmlNodeType.Text && paragraph==null && string.IsNullOrWhiteSpace(Decode(node.InnerText)))continue;
            var inline=RenderInline(node,inherited,selected,depth+1);
            if(inline==null)continue;
            if(paragraph==null){paragraph=Paragraph(inherited);blocks.Add(paragraph);}
            paragraph.Add(inline);
        }
    }

    static void RenderBlock(HtmlNode node,XElement blocks,Format inherited,HashSet<HtmlNode> selected,int depth)
    {
        if(depth>MaximumDepth || Ignore.Contains(node.Name) || Hidden(node))return;
        var format=ReadFormat(node,inherited);
        var name=node.Name.ToLowerInvariant();
        if(name is "ul" or "ol")
        {
            var list=new XElement(Wpf+"List",new XAttribute("MarkerStyle",name=="ol"?"Decimal":"Disc"));
            foreach(var item in node.ChildNodes.Where(item=>item.Name.Equals("li",StringComparison.OrdinalIgnoreCase) && selected.Contains(item)))
            {
                var entry=new XElement(Wpf+"ListItem");
                RenderContainer(item,entry,ReadFormat(item,format),selected,depth+1);
                if(!entry.HasElements)entry.Add(Paragraph(format));
                list.Add(entry);
            }
            if(list.HasElements)blocks.Add(list);
            return;
        }
        if(name=="table")
        {
            var table=new XElement(Wpf+"Table",new XAttribute("CellSpacing","0"));
            var group=new XElement(Wpf+"TableRowGroup");
            foreach(var row in node.Descendants("tr").Where(row=>selected.Contains(row) && ReferenceEquals(row.Ancestors("table").FirstOrDefault(),node)))
            {
                var targetRow=new XElement(Wpf+"TableRow");
                foreach(var cell in row.ChildNodes.Where(cell=>(cell.Name is "td" or "th") && selected.Contains(cell)))
                {
                    var targetCell=new XElement(Wpf+"TableCell",new XAttribute("Padding","4"),new XAttribute("BorderBrush","#D0D0D0"),new XAttribute("BorderThickness","0.5"));
                    if(int.TryParse(cell.GetAttributeValue("colspan","1"),out var span) && span is >1 and <=100)targetCell.SetAttributeValue("ColumnSpan",span);
                    var cellFormat=ReadFormat(cell,format);
                    if(cell.Name=="th")cellFormat=cellFormat with {Weight="Bold"};
                    RenderContainer(cell,targetCell,cellFormat,selected,depth+1);
                    if(!targetCell.HasElements)targetCell.Add(Paragraph(cellFormat));
                    targetRow.Add(targetCell);
                }
                if(targetRow.HasElements)group.Add(targetRow);
            }
            if(group.HasElements){table.Add(group);blocks.Add(table);}
            return;
        }
        if(name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre" or "blockquote" or "address" or "li" or "td" or "th" or "figcaption")
        {
            var paragraph=Paragraph(format);
            var css=Css(node);
            if(css.TryGetValue("margin",out var margin) && Margin(margin,format.Size) is string thickness)paragraph.SetAttributeValue("Margin",thickness);
            else if(css.TryGetValue("margin-top",out var top) || css.TryGetValue("margin-bottom",out _))
            {
                var before=Math.Max(0,Length(top??"0",format.Size)??0);
                var after=Math.Max(0,Length(css.GetValueOrDefault("margin-bottom","0"),format.Size)??0);
                paragraph.SetAttributeValue("Margin",$"0,{Number(before)},0,{Number(after)}");
            }
            foreach(var child in node.ChildNodes)
                if(RenderInline(child,format,selected,depth+1) is { } inline)paragraph.Add(inline);
            blocks.Add(paragraph);
            return;
        }
        if(name=="hr")
        {
            blocks.Add(new XElement(Wpf+"Paragraph",new XElement(Wpf+"Run","────────")));
            return;
        }
        RenderContainer(node,blocks,format,selected,depth+1);
    }

    static XElement? RenderInline(HtmlNode node,Format inherited,HashSet<HtmlNode> selected,int depth)
    {
        if(depth>MaximumDepth || !selected.Contains(node) || Ignore.Contains(node.Name))return null;
        if(node.NodeType==HtmlNodeType.Text)
        {
            var text=Decode(node.InnerText);
            if(!inherited.Preformatted)text=Regex.Replace(text,@"[\t\r\n\f ]+"," ");
            if(text.Length==0)return null;
            var run=new XElement(Wpf+"Run",text);ApplyCharacters(run,inherited);return run;
        }
        if(node.NodeType!=HtmlNodeType.Element || Hidden(node))return null;
        var name=node.Name.ToLowerInvariant();
        if(name is "br" or "wbr")return new XElement(Wpf+"LineBreak");
        var format=ReadFormat(node,inherited);
        if(name=="img")
        {
            var alt=Decode(node.GetAttributeValue("alt",""));
            if(alt.Length==0)return null;
            var run=new XElement(Wpf+"Run",alt);ApplyCharacters(run,format);return run;
        }
        var span=new XElement(Wpf+"Span");
        if(name=="a" && Uri.TryCreate(Decode(node.GetAttributeValue("href","")),UriKind.Absolute,out var uri) &&
           uri.Scheme is "http" or "https" or "mailto")
        {
            span.Name=Wpf+"Hyperlink";
            span.SetAttributeValue("NavigateUri",uri.AbsoluteUri);
        }
        ApplyCharacters(span,format);
        foreach(var child in node.ChildNodes)
            if(RenderInline(child,format,selected,depth+1) is { } inline)span.Add(inline);
        return span.HasElements?span:null;
    }

    static XElement Paragraph(Format format)
    {
        var paragraph=new XElement(Wpf+"Paragraph",new XAttribute("Margin","0,0,0,8"),new XAttribute("TextAlignment",format.Align));
        ApplyCharacters(paragraph,format);
        if(format.Indent!=0)paragraph.SetAttributeValue("TextIndent",Number(format.Indent));
        if(format.LineHeight is string height)
        {
            double? value=double.TryParse(height,NumberStyles.Float,CultureInfo.InvariantCulture,out var ratio)
                ?ratio*format.Size:Length(height,format.Size);
            if(value is >=1 and <=2000)paragraph.SetAttributeValue("LineHeight",Number(value.Value));
        }
        return paragraph;
    }

    static void ApplyCharacters(XElement element,Format format)
    {
        element.SetAttributeValue("FontFamily",format.Font);
        element.SetAttributeValue("FontSize",Number(format.Size));
        element.SetAttributeValue("FontWeight",format.Weight);
        element.SetAttributeValue("FontStyle",format.Italic?"Italic":"Normal");
        element.SetAttributeValue("Foreground",format.Colour);
        if(format.Background!=null)element.SetAttributeValue("Background",format.Background);
        if(element.Name.LocalName is "Run" or "Span" or "Hyperlink")
        {
            element.SetAttributeValue("TextDecorations",string.Join(",",new[]{format.Underline?"Underline":null,format.Strike?"Strikethrough":null}.Where(value=>value!=null)));
            if(format.Baseline!=null)element.SetAttributeValue("BaselineAlignment",format.Baseline);
        }
    }

    static Format ReadFormat(HtmlNode node,Format parent)
    {
        var name=node.Name.ToLowerInvariant();
        var format=parent;
        if(name is "strong" or "b" or "th")format=format with {Weight="Bold"};
        if(name is "em" or "i")format=format with {Italic=true};
        if(name=="u")format=format with {Underline=true};
        if(name is "s" or "strike" or "del")format=format with {Strike=true};
        if(name=="pre")format=format with {Preformatted=true,Font="Consolas"};
        if(name=="code")format=format with {Font="Consolas"};
        if(name is "sub" or "sup")format=format with {Baseline=name=="sub"?"Subscript":"Superscript"};
        if(name.Length==2 && name[0]=='h' && name[1] is >= '1' and <= '6')
            format=format with {Weight="Bold",Size=new[]{32d,24,18.72,16,13.28,10.72}[name[1]-'1']};
        var css=Css(node);
        if(css.TryGetValue("font-family",out var family) || (name=="font" && (family=node.GetAttributeValue("face",""))!=""))
        {
            var first=family.Split(',')[0].Trim().Trim('"','\'');
            first=first.ToLowerInvariant() switch {"serif"=>"Times New Roman","sans-serif" or "system-ui"=>"Arial","monospace"=>"Consolas",_=>first};
            if(Regex.IsMatch(first,@"^[\p{L}\p{N} _'\-]{1,100}$"))format=format with {Font=first};
        }
        if(css.TryGetValue("font-size",out var size) && FontSize(size,parent.Size) is double pixels)format=format with {Size=pixels};
        if(css.TryGetValue("font-weight",out var weight))
            format=format with {Weight=weight.Trim().ToLowerInvariant() switch
            {
                "bold" or "bolder"=>"Bold","normal"=>"Normal","lighter"=>"Light","100"=>"Thin","200"=>"ExtraLight","300"=>"Light",
                "400"=>"Normal","500"=>"Medium","600"=>"SemiBold","700"=>"Bold","800"=>"ExtraBold","900"=>"Black",_=>format.Weight
            }};
        if(css.TryGetValue("font-style",out var italic))format=format with {Italic=italic.Contains("italic",StringComparison.OrdinalIgnoreCase)||italic.Contains("oblique",StringComparison.OrdinalIgnoreCase)};
        if(css.TryGetValue("text-decoration",out var decoration) || css.TryGetValue("text-decoration-line",out decoration))
            format=format with {Underline=decoration.Contains("underline",StringComparison.OrdinalIgnoreCase),Strike=decoration.Contains("line-through",StringComparison.OrdinalIgnoreCase)};
        var colour=css.GetValueOrDefault("color",name=="font"?node.GetAttributeValue("color",""):"");
        if(Colour(colour) is string foreground)format=format with {Colour=foreground};
        if(Colour(css.GetValueOrDefault("background-color","")) is string background)format=format with {Background=background};
        var align=css.GetValueOrDefault("text-align",node.GetAttributeValue("align",""));
        if(align.ToLowerInvariant() is "left" or "right" or "center" or "justify")format=format with {Align=align.ToLowerInvariant() switch {"center"=>"Center","right"=>"Right","justify"=>"Justify",_=>"Left"}};
        if(css.TryGetValue("white-space",out var white))format=format with {Preformatted=white.StartsWith("pre",StringComparison.OrdinalIgnoreCase)};
        if(css.TryGetValue("line-height",out var line) && line!="normal")format=format with {LineHeight=line};
        if(css.TryGetValue("text-indent",out var indent) && Length(indent,format.Size) is double indentation)format=format with {Indent=indentation};
        return format;
    }

    static Dictionary<string,string> Css(HtmlNode node)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var declaration in Decode(node.GetAttributeValue("style","")).Split(';'))
        {
            var colon=declaration.IndexOf(':');if(colon<1)continue;
            var key=declaration[..colon].Trim();
            var value=Regex.Replace(declaration[(colon+1)..].Trim(),@"\s*!important\s*$","",RegexOptions.IgnoreCase);
            if(key.Length>0 && value.Length<2000)values[key]=value;
        }
        return values;
    }

    static bool Hidden(HtmlNode node)
    {
        var css=Css(node);
        return css.GetValueOrDefault("display","").Equals("none",StringComparison.OrdinalIgnoreCase) ||
               css.GetValueOrDefault("visibility","").Equals("hidden",StringComparison.OrdinalIgnoreCase);
    }

    static string? Colour(string value)
    {
        value=value.Trim();if(value.Length==0 || value.Length>80 || value.Contains('{') || value.Contains('(') && !value.StartsWith("rgb",StringComparison.OrdinalIgnoreCase))return null;
        var rgb=Regex.Match(value,@"^rgba?\(\s*([\d.]+%?)\s*,\s*([\d.]+%?)\s*,\s*([\d.]+%?)(?:\s*,\s*([\d.]+))?\s*\)$",RegexOptions.IgnoreCase);
        if(rgb.Success)
        {
            byte Channel(string text)=>byte.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var channel)?channel:
                (byte)Math.Clamp(Math.Round(double.Parse(text.TrimEnd('%'),CultureInfo.InvariantCulture)*(text.EndsWith('%')?2.55:1)),0,255);
            var alpha=rgb.Groups[4].Success?(byte)Math.Clamp(Math.Round(double.Parse(rgb.Groups[4].Value,CultureInfo.InvariantCulture)*255),0,255):(byte)255;
            return $"#{alpha:X2}{Channel(rgb.Groups[1].Value):X2}{Channel(rgb.Groups[2].Value):X2}{Channel(rgb.Groups[3].Value):X2}";
        }
        // WPF also recognises non-CSS ContextColor values that can load an
        // external colour profile. Accept only CSS hex or named-colour tokens.
        if(Regex.IsMatch(value,@"^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$",RegexOptions.IgnoreCase))
        {
            var hex=value[1..].ToUpperInvariant();
            if(hex.Length is 3 or 4)hex=string.Concat(hex.Select(character=>new string(character,2)));
            return hex.Length==6?"#FF"+hex:"#"+hex[6..8]+hex[..6];
        }
        if(!Regex.IsMatch(value,@"^[a-z]{1,40}$",RegexOptions.IgnoreCase))return null;
        try{return ColorConverter.ConvertFromString(value) is Color colour?$"#{colour.A:X2}{colour.R:X2}{colour.G:X2}{colour.B:X2}":null;}
        catch(FormatException){return null;}
        catch(NotSupportedException){return null;}
    }

    static double? FontSize(string value,double parent)
    {
        var pixels=value.Trim().ToLowerInvariant() switch {"xx-small"=>9d,"x-small"=>10d,"small"=>13.33,"medium"=>16d,"large"=>18d,"x-large"=>24d,"xx-large"=>32d,"smaller"=>parent*0.8,"larger"=>parent*1.2,_=>Length(value,parent)};
        return pixels is >=1 and <=1000?pixels:null;
    }

    static double? Length(string value,double em)
    {
        var match=Regex.Match(value.Trim(),@"^(-?[\d.]+)(px|pt|pc|in|cm|mm|em|rem|%)?$",RegexOptions.IgnoreCase);
        if(!match.Success || !double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) || !double.IsFinite(number))return null;
        var result=number*(match.Groups[2].Value.ToLowerInvariant() switch {"pt"=>96d/72,"pc"=>16,"in"=>96,"cm"=>96d/2.54,"mm"=>96d/25.4,"em"=>em,"rem"=>16,"%"=>em/100,_=>1});
        return result is >=-10000 and <=10000?result:null;
    }

    static string? Margin(string value,double em)
    {
        var parts=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(part=>Length(part,em)).ToArray();
        if(parts.Length is <1 or >4 || parts.Any(part=>!part.HasValue || part.Value<0))return null;
        var top=parts[0]!.Value;var right=parts.Length>1?parts[1]!.Value:top;
        var bottom=parts.Length>2?parts[2]!.Value:top;var left=parts.Length>3?parts[3]!.Value:right;
        return $"{Number(left)},{Number(top)},{Number(right)},{Number(bottom)}";
    }

    static string Decode(string value)=>HtmlEntity.DeEntitize(value).Replace("\0","");
    static string Number(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
}

