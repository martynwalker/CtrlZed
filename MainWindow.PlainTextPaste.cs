using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CtrlZed;

public partial class MainWindow
{
    void PasteUnformattedText_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        PasteUnformattedText();
    }

    internal bool PasteUnformattedText()
    {
        if(Editor.IsReadOnly || !Editor.IsEnabled)return false;

        string? text;
        try
        {
            var data=Clipboard.GetDataObject();
            text=data?.GetData(DataFormats.UnicodeText,true) as string
                ??data?.GetData(DataFormats.Text,true) as string;
        }
        catch(ExternalException)
        {
            Status.Text="The clipboard is busy. Try pasting again.";
            return false;
        }
        if(string.IsNullOrEmpty(text))return false;
        text=text.Replace("\0","").Replace("\r\n","\n").Replace('\r','\n');
        if(text.Length==0)return false;

        var formatting=CapturePlainPasteFormatting(Editor);
        var role=Editor.Selection.Start.Paragraph?.Tag;
        Editor.BeginChange();
        try
        {
            Editor.Selection.Text="";
            // WPF deliberately turns newlines into spaces inside Hyperlink.
            // Split only the destination link around the replacement range,
            // retaining its unaffected text and target on either side.
            PreparePlainPasteHyperlinkBoundary(Editor,formatting);
            // Set only the captured string. The rich clipboard object is never
            // handed to the editor, and the system clipboard stays untouched.
            // WPF's text-range setter creates paragraphs for the line breaks.
            Editor.Selection.Text=text;
            var inserted=new TextRange(Editor.Selection.Start,Editor.Selection.End);
            foreach(var (property,value) in formatting)inserted.ApplyPropertyValue(property,value);
            if(role is string tag && tag.StartsWith(ParagraphStyleTagPrefix,StringComparison.Ordinal))
            {
                foreach(var paragraph in EnumerateDocumentParagraphs())
                    if(paragraph.ContentStart.CompareTo(inserted.End)<0 && paragraph.ContentEnd.CompareTo(inserted.Start)>0)
                        paragraph.Tag=tag;
            }
            Editor.CaretPosition=inserted.End.GetInsertionPosition(LogicalDirection.Backward);
            foreach(var (property,value) in formatting)Editor.Selection.ApplyPropertyValue(property,value);
        }
        finally
        {
            Editor.EndChange();
        }
        MarkDirty("Pasted unformatted text.");
        Editor.Focus();
        RefreshFontBox();
        return true;
    }

    static IReadOnlyList<(DependencyProperty Property,object Value)> CapturePlainPasteFormatting(RichTextBox editor)
    {
        var result=new System.Collections.Generic.List<(DependencyProperty,object)>();
        foreach(var property in new[]
        {
            TextElement.FontFamilyProperty,TextElement.FontSizeProperty,TextElement.FontStyleProperty,
            TextElement.FontWeightProperty,TextElement.FontStretchProperty,TextElement.ForegroundProperty,
            TextElement.BackgroundProperty,Inline.TextDecorationsProperty,Inline.BaselineAlignmentProperty
        })
        {
            var value=editor.Selection.GetPropertyValue(property);
            if(value==DependencyProperty.UnsetValue)value=editor.Selection.Start.GetPropertyValue(property);
            if(value!=DependencyProperty.UnsetValue)result.Add((property,value));
        }
        return result;
    }

    static void PreparePlainPasteHyperlinkBoundary(
        RichTextBox editor,IReadOnlyList<(DependencyProperty Property,object Value)> formatting)
    {
        static Hyperlink? LinkAt(TextPointer pointer)
        {
            for(var element=pointer.Parent as TextElement;element!=null;element=element.Parent as TextElement)
                if(element is Hyperlink link)return link;
            return null;
        }

        var link=LinkAt(editor.Selection.Start);
        if(link==null || !ReferenceEquals(link,LinkAt(editor.Selection.End)))return;
        var collection=link.Parent switch
        {
            Paragraph paragraph=>paragraph.Inlines,
            Span span=>span.Inlines,
            _=>null
        };
        if(collection==null)return;

        var start=editor.Selection.Start.GetPositionAtOffset(0,LogicalDirection.Backward)!;
        var end=editor.Selection.End.GetPositionAtOffset(0,LogicalDirection.Forward)!;
        var before=new Span(link.ContentStart,start);
        var after=new Span(end,link.ContentEnd);
        link.Inlines.Remove(before);
        link.Inlines.Remove(after);

        Hyperlink CopyLink(Span content)
        {
            var copy=new Hyperlink(content)
            {
                NavigateUri=link.NavigateUri,TargetName=link.TargetName,
                Command=link.Command,CommandParameter=link.CommandParameter,CommandTarget=link.CommandTarget
            };
            var values=link.GetLocalValueEnumerator();
            while(values.MoveNext())
            {
                var entry=values.Current;
                if(!entry.Property.ReadOnly && entry.Property!=FrameworkContentElement.NameProperty)
                    copy.SetValue(entry.Property,entry.Value);
            }
            return copy;
        }

        if(!string.IsNullOrEmpty(new TextRange(before.ContentStart,before.ContentEnd).Text))
            collection.InsertBefore(link,CopyLink(before));
        var insertion=new Run();
        foreach(var (property,value) in formatting)insertion.SetValue(property,value);
        collection.InsertBefore(link,insertion);
        if(!string.IsNullOrEmpty(new TextRange(after.ContentStart,after.ContentEnd).Text))
            collection.InsertBefore(link,CopyLink(after));
        collection.Remove(link);
        var position=insertion.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        editor.Selection.Select(position,position);
    }
}
