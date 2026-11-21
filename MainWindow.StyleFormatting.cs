using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    internal const string ParagraphStyleTagPrefix="CtrlZed:ParagraphStyle:";

    StyleSetStore.StyleSet ActiveParagraphStyleSet
    {
        get
        {
            if(styleSets.TryGetValue(activeStyleSet,out var set))return set;
            set=StyleSetStore.Defaults()["Default"];
            styleSets["Default"]=set;
            activeStyleSet="Default";
            return set;
        }
    }

    void StyleMenu_Click(object sender,RoutedEventArgs e)
    {
        if(sender is MenuItem {Tag:string style})ApplyParagraphStyle(style);
    }

    void StyleBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updatingStyleBox || !IsLoaded)return;
        var style=StyleBox.SelectedItem is ComboBoxItem item?item.Content as string:StyleBox.SelectedItem as string;
        if(style!=null)ApplyParagraphStyle(style);
    }

    internal void RefreshParagraphStyleBox()
    {
        if(StyleBox==null)return;
        var paragraph=Editor?.CaretPosition.Paragraph;
        var selected=paragraph==null?null:InferParagraphStyle(paragraph,styleSets.Values);
        selected??=(StyleBox.SelectedItem as ComboBoxItem)?.Content as string??StyleBox.SelectedItem as string??"Normal";
        updatingStyleBox=true;
        try
        {
            StyleBox.Items.Clear();
            foreach(var name in StyleSetStore.StyleNames(ActiveParagraphStyleSet))
                StyleBox.Items.Add(new ComboBoxItem{Content=name});
            StyleBox.SelectedItem=StyleBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item=>string.Equals(item.Content as string,selected,StringComparison.OrdinalIgnoreCase))
                ??StyleBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally { updatingStyleBox=false; }
    }

    static TextAlignment StyleAlignment(string? alignment)
        =>alignment?.ToLowerInvariant() switch
        {
            "center" or "centre"=>TextAlignment.Center,
            "right"=>TextAlignment.Right,
            "justify"=>TextAlignment.Justify,
            _=>TextAlignment.Left
        };

    internal static string ParagraphStyleTag(string name,StyleSetStore.TextStyle style)
        =>ParagraphStyleTagPrefix+(StyleSetStore.ParagraphStyleNames.Contains(name,StringComparer.OrdinalIgnoreCase)
            ?StyleSetStore.ParagraphStyleNames.First(value=>string.Equals(value,name,StringComparison.OrdinalIgnoreCase))
            :"#"+(string.IsNullOrWhiteSpace(style.Id)?name:style.Id));

    static void ApplyStyleToRange(TextRange range,StyleSetStore.TextStyle spec,Paragraph? paragraphDefaults=null)
    {
        // WPF paragraph properties clear pending typing properties. Layout
        // must precede all character formatting, especially at an empty caret.
        range.ApplyPropertyValue(Paragraph.TextAlignmentProperty,StyleAlignment(spec.Alignment));
        range.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(spec.MarginLeft,spec.MarginTop,spec.MarginRight,spec.MarginBottom));
        range.ApplyPropertyValue(Paragraph.TextIndentProperty,spec.FirstLineIndent);
        range.ApplyPropertyValue(Paragraph.LineHeightProperty,spec.LineHeight>0?spec.LineHeight:double.NaN);
        range.ApplyPropertyValue(Paragraph.KeepWithNextProperty,spec.KeepWithNext);
        range.ApplyPropertyValue(Paragraph.KeepTogetherProperty,spec.KeepTogether);
        // TextRange's supported paragraph-property list omits these two WPF
        // properties, so apply them directly to each affected paragraph.
        var layoutParagraphs=new HashSet<Paragraph>();
        for(var position=range.Start;position!=null;position=position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if(position.Paragraph is Paragraph value)layoutParagraphs.Add(value);
            if(position.CompareTo(range.End)>=0)break;
            var next=position.GetNextContextPosition(LogicalDirection.Forward);
            if(next==null || next.CompareTo(range.End)>=0)break;
        }
        if(paragraphDefaults!=null)layoutParagraphs.Add(paragraphDefaults);
        foreach(var paragraph in layoutParagraphs)
        {
            paragraph.LineStackingStrategy=spec.LineHeight>0?LineStackingStrategy.BlockLineHeight:LineStackingStrategy.MaxHeight;
            paragraph.BreakPageBefore=spec.BreakPageBefore;
        }
        if(paragraphDefaults!=null)
        {
            paragraphDefaults.TextAlignment=StyleAlignment(spec.Alignment);
            paragraphDefaults.Margin=new Thickness(spec.MarginLeft,spec.MarginTop,spec.MarginRight,spec.MarginBottom);
            paragraphDefaults.TextIndent=spec.FirstLineIndent;
            paragraphDefaults.LineHeight=spec.LineHeight>0?spec.LineHeight:double.NaN;
            paragraphDefaults.LineStackingStrategy=spec.LineHeight>0?LineStackingStrategy.BlockLineHeight:LineStackingStrategy.MaxHeight;
            paragraphDefaults.KeepWithNext=spec.KeepWithNext;
            paragraphDefaults.KeepTogether=spec.KeepTogether;
            paragraphDefaults.BreakPageBefore=spec.BreakPageBefore;
        }
        void Character(DependencyProperty property,object value)
        {
            paragraphDefaults?.SetValue(property,value);
            range.ApplyPropertyValue(property,value);
        }
        Character(TextElement.FontFamilyProperty,new FontFamily(spec.FontFamily));
        Character(TextElement.FontSizeProperty,spec.SizePt*96/72);
        Character(TextElement.FontStyleProperty,spec.Italic?FontStyles.Italic:FontStyles.Normal);
        Character(TextElement.FontWeightProperty,spec.Bold?FontWeights.Bold:FontWeights.Normal);
        Character(TextElement.ForegroundProperty,BrushFromHex(spec.Colour));
        range.ApplyPropertyValue(Inline.TextDecorationsProperty,spec.Underline?TextDecorations.Underline:null);
    }

    string? ResolveActiveStyleName(string style)
    {
        if(StyleSetStore.ResolveStyleName(ActiveParagraphStyleSet,style) is string direct)return direct;
        foreach(var set in styleSets.Values)
            if(StyleSetStore.ResolveStyle(set,style) is { } previous &&
               StyleSetStore.ResolveStyleName(ActiveParagraphStyleSet,previous.Id) is string current)return current;
        return null;
    }

    StyleSetStore.TextStyle? ResolveStyle(string style)
        =>StyleSetStore.ResolveStyle(ActiveParagraphStyleSet,ResolveActiveStyleName(style)??style)
          ??StyleSetStore.ResolveStyle(DocxInterop.GetDocumentStyles(Editor.Document)??ActiveParagraphStyleSet,style)
          ??StyleSetStore.ResolveStyle(StyleSetStore.Defaults()["Default"],style);

    void RememberParagraphStyle(TextRange range,string style)
    {
        var name=ResolveActiveStyleName(style)??style;
        var spec=ResolveStyle(style);
        if(spec==null)return;
        foreach(var paragraph in ParagraphsInRange(range))paragraph.Tag=ParagraphStyleTag(name,spec);
    }

    IEnumerable<Paragraph> ParagraphsInRange(TextRange range)
    {
        foreach(var paragraph in EnumerateDocumentParagraphs())
            if(paragraph==range.Start.Paragraph ||
               (paragraph.ContentEnd.CompareTo(range.Start)>0 && paragraph.ContentStart.CompareTo(range.End)<0))
                yield return paragraph;
    }

    internal bool IsParagraphStyleInUse(string id,string name)
        =>EnumerateDocumentParagraphs().Any(paragraph=>
            Equals(paragraph.Tag,ParagraphStyleTagPrefix+"#"+id) ||
            string.Equals(InferParagraphStyle(paragraph,styleSets.Values),name,StringComparison.OrdinalIgnoreCase));

    void ApplyParagraphStyle(string style)
    {
        if(Editor==null)return;
        var name=ResolveActiveStyleName(style)??style;
        var spec=ResolveStyle(style);
        if(spec==null)return;
        Editor.Focus();
        Editor.BeginChange();
        try
        {
            foreach(var paragraph in ParagraphsInRange(Editor.Selection).ToList())
            {
                paragraph.Tag=ParagraphStyleTag(name,spec);
                ApplyStyleToRange(new TextRange(paragraph.ContentStart,paragraph.ContentEnd),spec,paragraph);
            }
            // Restore the pending format after paragraph edits and focus changes.
            if(Editor.Selection.IsEmpty && Editor.CaretPosition.Paragraph is Paragraph current)
                ApplyFollowingStyle(Editor,current,name,spec);
        }
        finally { Editor.EndChange(); }
        PrepareDocumentStylesForInterop();
        RefreshParagraphStyleBox();
        RefreshFontBox();
        MarkDirty($"Style: {name} ({activeStyleSet})");
    }

    string? InferParagraphStyle(Paragraph paragraph,IEnumerable<StyleSetStore.StyleSet> sourceSets)
    {
        var sets=new List<StyleSetStore.StyleSet>{ActiveParagraphStyleSet};
        if(DocxInterop.GetDocumentStyles(Editor.Document) is { } documentStyles)sets.Add(documentStyles);
        sets.AddRange(sourceSets.Where(set=>!ReferenceEquals(set,ActiveParagraphStyleSet)));
        if(paragraph.Tag is string tag && tag.StartsWith(ParagraphStyleTagPrefix,StringComparison.Ordinal))
        {
            var identity=tag[ParagraphStyleTagPrefix.Length..];
            if(identity.StartsWith('#'))identity=identity[1..];
            foreach(var set in sets)
                if(StyleSetStore.ResolveStyleName(set,identity) is string name)return name;
            if(!tag[ParagraphStyleTagPrefix.Length..].StartsWith('#'))return identity;
        }
        if(DocxInterop.GetImportedParagraphRole(paragraph) is string importedRole)return importedRole;

        var probe=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        var family=probe.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
        var size=probe.GetPropertyValue(TextElement.FontSizeProperty) is double d?d*72/96:0;
        var bold=probe.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight fw && fw>=FontWeights.SemiBold;
        var italic=probe.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle fs && fs==FontStyles.Italic;
        var margin=paragraph.Margin;
        var matches=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var (name,spec) in sets.SelectMany(set=>set.Styles))
        {
            if(!string.Equals(family?.Source,spec.FontFamily,StringComparison.OrdinalIgnoreCase) ||
               Math.Abs(size-spec.SizePt)>0.15 || bold!=spec.Bold || italic!=spec.Italic ||
               paragraph.TextAlignment!=StyleAlignment(spec.Alignment) ||
               Math.Abs(margin.Left-spec.MarginLeft)>0.5 || Math.Abs(margin.Top-spec.MarginTop)>0.5 ||
               Math.Abs(margin.Right-spec.MarginRight)>0.5 || Math.Abs(margin.Bottom-spec.MarginBottom)>0.5 ||
               Math.Abs(paragraph.TextIndent-spec.FirstLineIndent)>0.5)continue;
            matches.Add(name);
        }
        return matches.Count==1?matches.Single():null;
    }
}

