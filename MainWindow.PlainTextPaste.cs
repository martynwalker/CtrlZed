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
            if(value==DependencyProperty.UnsetValue)
                value=new TextRange(editor.Selection.Start,editor.Selection.Start).GetPropertyValue(property);
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

        static void CopyProperties(Inline source,Inline destination)
        {
            var values=source.GetLocalValueEnumerator();
            while(values.MoveNext())
            {
                var entry=values.Current;
                if(!entry.Property.ReadOnly && entry.Property!=FrameworkContentElement.NameProperty &&
                   entry.Property!=Run.TextProperty)
                    destination.SetValue(entry.Property,source.GetValue(entry.Property));
            }
        }

        static void MoveChildren(IEnumerable<Inline> children,InlineCollection target)
        {
            foreach(var child in children)
            {
                if(child.Parent is Span oldSpan)oldSpan.Inlines.Remove(child);
                else if(child.Parent is Paragraph oldParagraph)oldParagraph.Inlines.Remove(child);
                target.Add(child);
            }
        }

        static Span CopySpan(Span source)
        {
            Span copy=source switch { Bold=>new Bold(),Italic=>new Italic(),Underline=>new Underline(),_=>new Span() };
            CopyProperties(source,copy);
            return copy;
        }

        // Span(start,end) itself tries to split ancestor Hyperlink elements,
        // which WPF forbids. Partition explicitly instead. All boundaries are
        // read before moving any existing child, so the caret cannot migrate
        // into a detached text container during this traversal.
        var origin=link.ContentStart;
        var at=origin.GetOffsetToPosition(editor.Selection.Start);
        var populate=new System.Collections.Generic.List<Action>();
        (System.Collections.Generic.List<Inline> Before,System.Collections.Generic.List<Inline> After) Partition(InlineCollection children)
        {
            var left=new System.Collections.Generic.List<Inline>();
            var right=new System.Collections.Generic.List<Inline>();
            foreach(var child in children.ToList())
            {
                var first=origin.GetOffsetToPosition(child.ContentStart);
                var last=origin.GetOffsetToPosition(child.ContentEnd);
                if(at<=first)right.Add(child);
                else if(at>=last)left.Add(child);
                else if(child is Run run)
                {
                    var offset=Math.Clamp(at-first,0,run.Text.Length);
                    if(offset>0)
                    {
                        var beforeRun=new Run(run.Text[..offset]);
                        CopyProperties(run,beforeRun);
                        left.Add(beforeRun);
                    }
                    if(offset<run.Text.Length)
                    {
                        var afterRun=new Run(run.Text[offset..]);
                        CopyProperties(run,afterRun);
                        right.Add(afterRun);
                    }
                }
                else if(child is Span span)
                {
                    var pieces=Partition(span.Inlines);
                    if(pieces.Before.Count>0)
                    {
                        var beforeSpan=CopySpan(span);
                        populate.Add(()=>MoveChildren(pieces.Before,beforeSpan.Inlines));
                        left.Add(beforeSpan);
                    }
                    if(pieces.After.Count>0)
                    {
                        var afterSpan=CopySpan(span);
                        populate.Add(()=>MoveChildren(pieces.After,afterSpan.Inlines));
                        right.Add(afterSpan);
                    }
                }
                else
                {
                    // Insertion positions cannot divide an embedded inline.
                    // Keep the existing object intact on the corresponding side.
                    if(at-first<last-at)right.Add(child);else left.Add(child);
                }
            }
            return (left,right);
        }

        var parts=Partition(link.Inlines);
        var before=new Hyperlink();
        var after=new Hyperlink();
        CopyProperties(link,before);
        CopyProperties(link,after);
        foreach(var action in populate)action();
        MoveChildren(parts.Before,before.Inlines);
        MoveChildren(parts.After,after.Inlines);

        if(before.Inlines.Count>0)collection.InsertBefore(link,before);
        // A nonempty temporary run gives the replacement a precise range.
        // WPF may normalise an empty run into either neighbouring hyperlink.
        // The marker is replaced inside the same undo block and never remains
        // in the document or reaches the clipboard.
        var insertion=new Run("\u200B");
        foreach(var (property,value) in formatting)insertion.SetValue(property,value);
        collection.InsertBefore(link,insertion);
        if(after.Inlines.Count>0)collection.InsertBefore(link,after);
        collection.Remove(link);
        editor.Selection.Select(insertion.ContentStart,insertion.ContentEnd);
    }
}

