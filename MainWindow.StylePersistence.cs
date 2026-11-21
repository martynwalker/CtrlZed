using System.Text.Json;
using System.Windows.Documents;

namespace CtrlZed;

public partial class MainWindow
{
    public sealed class StyleDocumentState
    {
        public StyleSetStore.StyleSet Definitions { get; set; }=new();
        public List<string?> ParagraphTags { get; set; }=new();
        public List<ParagraphStyleLayout> ParagraphLayouts { get; set; }=new();
    }

    public sealed class ParagraphStyleLayout
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double? Right { get; set; }
        public double? Bottom { get; set; }
        public double FirstLineIndent { get; set; }
        public double LineHeight { get; set; }
        public bool ExactLineHeight { get; set; }
        public bool KeepWithNext { get; set; }
        public bool KeepTogether { get; set; }
        public bool BreakPageBefore { get; set; }
    }

    internal StyleDocumentState CaptureStyleDocumentState()
    {
        PrepareDocumentStylesForInterop();
        return new StyleDocumentState
        {
            Definitions=StyleSetStore.Clone(DocxInterop.GetDocumentStyles(Editor.Document)??ActiveParagraphStyleSet),
            ParagraphTags=EnumerateDocumentParagraphs().Select(paragraph=>
            {
                if(paragraph.Tag is string tag && tag.StartsWith(ParagraphStyleTagPrefix,StringComparison.Ordinal))return tag;
                var name=InferParagraphStyle(paragraph,styleSets.Values);
                var spec=name==null?null:ResolveStyle(name);
                return spec==null?null:ParagraphStyleTag(name!,spec);
            }).ToList(),
            ParagraphLayouts=EnumerateDocumentParagraphs().Select(paragraph=>new ParagraphStyleLayout
            {
                Left=double.IsFinite(paragraph.Margin.Left)?paragraph.Margin.Left:null,
                Top=double.IsFinite(paragraph.Margin.Top)?paragraph.Margin.Top:null,
                Right=double.IsFinite(paragraph.Margin.Right)?paragraph.Margin.Right:null,
                Bottom=double.IsFinite(paragraph.Margin.Bottom)?paragraph.Margin.Bottom:null,
                FirstLineIndent=paragraph.TextIndent,
                LineHeight=double.IsFinite(paragraph.LineHeight)?paragraph.LineHeight:0,
                ExactLineHeight=paragraph.LineStackingStrategy==System.Windows.LineStackingStrategy.BlockLineHeight,
                KeepWithNext=paragraph.KeepWithNext,KeepTogether=paragraph.KeepTogether,
                BreakPageBefore=paragraph.BreakPageBefore
            }).ToList()
        };
    }

    internal void RestoreStyleDocumentState(StyleDocumentState? state)
    {
        if(state?.Definitions?.Styles==null || state.Definitions.Styles.Count==0)return;
        RestoreDocumentStyleSet(state.Definitions);
        var paragraphs=EnumerateDocumentParagraphs().ToList();
        for(var index=0;index<Math.Min(paragraphs.Count,state.ParagraphTags?.Count??0);index++)
            if(state.ParagraphTags![index] is string tag && tag.StartsWith(ParagraphStyleTagPrefix,StringComparison.Ordinal))
                paragraphs[index].Tag=tag;
        for(var index=0;index<Math.Min(paragraphs.Count,state.ParagraphLayouts?.Count??0);index++)
        {
            var paragraph=paragraphs[index];
            var layout=state.ParagraphLayouts![index];
            var current=paragraph.Margin;
            paragraph.Margin=new System.Windows.Thickness(layout.Left??current.Left,layout.Top??current.Top,
                layout.Right??current.Right,layout.Bottom??current.Bottom);
            paragraph.TextIndent=layout.FirstLineIndent;
            paragraph.LineHeight=layout.LineHeight>0?layout.LineHeight:double.NaN;
            paragraph.LineStackingStrategy=layout.ExactLineHeight?System.Windows.LineStackingStrategy.BlockLineHeight:System.Windows.LineStackingStrategy.MaxHeight;
            paragraph.KeepWithNext=layout.KeepWithNext;
            paragraph.KeepTogether=layout.KeepTogether;
            paragraph.BreakPageBefore=layout.BreakPageBefore;
        }
        RefreshParagraphStyleBox();
    }

    internal void AdoptImportedDocumentStyles()
    {
        if(DocxInterop.GetDocumentStyles(Editor.Document) is { } definitions)
            RestoreDocumentStyleSet(definitions);
    }

    void RestoreDocumentStyleSet(StyleSetStore.StyleSet incoming)
    {
        var set=StyleSetStore.NormalizeSet(incoming);
        var name=string.IsNullOrWhiteSpace(set.Name)?"Document styles":set.Name;
        if(styleSets.TryGetValue(name,out var existing) &&
           JsonSerializer.Serialize(existing)!=JsonSerializer.Serialize(set))
        {
            var basis="Document — "+name;
            name=basis;
            var suffix=2;
            while(styleSets.TryGetValue(name,out var candidate) &&
                  JsonSerializer.Serialize(candidate.Styles)!=JsonSerializer.Serialize(set.Styles))
                name=basis+" ("+(suffix++)+")";
        }
        set.Name=name;
        styleSets[name]=set;
        activeStyleSet=name;
        DocxInterop.SetDocumentStyles(Editor.Document,set);
        RefreshStyleSetBox();
        RefreshParagraphStyleBox();
    }

    internal void PrepareDocumentStylesForInterop()
    {
        var set=StyleSetStore.Clone(ActiveParagraphStyleSet);
        // Retain a definition used by an existing paragraph even when a
        // profile is changed. The editor's current formatting is untouched.
        if(DocxInterop.GetDocumentStyles(Editor.Document) is { } previous)
        {
            var used=EnumerateDocumentParagraphs()
                .Select(paragraph=>paragraph.Tag as string)
                .Where(tag=>tag?.StartsWith(ParagraphStyleTagPrefix+"#",StringComparison.Ordinal)==true)
                .Select(tag=>tag![(ParagraphStyleTagPrefix.Length+1)..])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            // A used lead style can refer to a following body style which has
            // not been typed yet. Preserve that dependency transitively.
            var pending=new Queue<string>(used);
            while(pending.Count>0)
            {
                var dependency=StyleSetStore.ResolveStyle(previous,pending.Dequeue());
                var next=dependency==null?null:StyleSetStore.ResolveStyle(previous,dependency.FollowingStyle);
                if(next!=null && used.Add(next.Id))pending.Enqueue(next.Id);
            }
            var retained=new List<StyleSetStore.TextStyle>();
            foreach(var (name,spec) in previous.Styles)
                if(used.Contains(spec.Id) && !set.Styles.Values.Any(value=>string.Equals(value.Id,spec.Id,StringComparison.OrdinalIgnoreCase)))
                {
                    var unique=name;
                    var suffix=2;
                    while(set.Styles.ContainsKey(unique))unique=name+" ("+(suffix++)+")";
                    var copy=StyleSetStore.Clone(spec);
                    set.Styles[unique]=copy;
                    retained.Add(copy);
                }
            foreach(var spec in retained)
                if(StyleSetStore.ResolveStyle(previous,spec.FollowingStyle) is { } target)
                    spec.FollowingStyle=StyleSetStore.ResolveStyleName(set,target.Id)??"Normal";
            foreach(var (alias,id) in previous.Aliases)
                if(used.Contains(id) && !set.Styles.ContainsKey(alias) && !set.Aliases.ContainsKey(alias))
                    set.Aliases[alias]=id;
        }
        DocxInterop.SetDocumentStyles(Editor.Document,set);
    }
}

