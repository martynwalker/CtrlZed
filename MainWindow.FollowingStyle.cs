using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    bool updatingStyleBox;
    bool enteringFollowingParagraph;

    void Editor_FollowingStylePreviewExecuted(object sender,ExecutedRoutedEventArgs e)
    {
        if(enteringFollowingParagraph || e.Command!=EditingCommands.EnterParagraphBreak || Editor.IsReadOnly ||
           ParagraphEndingAtSelection(Editor) is not Paragraph paragraph)return;
        var role=InferParagraphStyle(paragraph,styleSets.Values)??"Normal";
        var following=StyleSetStore.NormalizeFollowingStyle(ResolveStyle(role)?.FollowingStyle,ActiveParagraphStyleSet);
        if(string.Equals(role,following,StringComparison.OrdinalIgnoreCase))return;
        e.Handled=true;
        InsertParagraphWithFollowingStyle();
    }

    void Editor_FollowingStylePreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Handled || e.Key!=Key.Enter || Keyboard.Modifiers!=ModifierKeys.None ||
           Editor.IsReadOnly || !Editor.IsKeyboardFocused)return;

        if(ParagraphEndingAtSelection(Editor)==null ||
           !EditingCommands.EnterParagraphBreak.CanExecute(null,Editor))return;
        var role=InferParagraphStyle(Editor.CaretPosition.Paragraph!,styleSets.Values)??"Normal";
        var following=StyleSetStore.NormalizeFollowingStyle(ResolveStyle(role)?.FollowingStyle,ActiveParagraphStyleSet);
        if(string.Equals(role,"Normal",StringComparison.OrdinalIgnoreCase) &&
           string.Equals(following,"Normal",StringComparison.OrdinalIgnoreCase))return;
        e.Handled=true;
        InsertParagraphWithFollowingStyle();
    }

    internal void InsertParagraphWithFollowingStyle()
    {
        if(Editor.IsReadOnly || !EditingCommands.EnterParagraphBreak.CanExecute(null,Editor))return;
        Editor.Focus();
        var paragraph=ParagraphEndingAtSelection(Editor);
        if(paragraph==null)
        {
            // Splitting existing text retains its current paragraph style.
            var tag=Editor.CaretPosition.Paragraph?.Tag;
            enteringFollowingParagraph=true;
            try { EditingCommands.EnterParagraphBreak.Execute(null,Editor); }
            finally { enteringFollowingParagraph=false; }
            if(Editor.CaretPosition.Paragraph is Paragraph split && tag is string identity)
                split.Tag=identity;
            return;
        }
        var role=InferParagraphStyle(paragraph,styleSets.Values)??"Normal";
        var following=StyleSetStore.NormalizeFollowingStyle(ResolveStyle(role)?.FollowingStyle,ActiveParagraphStyleSet);
        if(string.Equals(role,"Normal",StringComparison.OrdinalIgnoreCase) &&
           string.Equals(following,"Normal",StringComparison.OrdinalIgnoreCase))
        {
            enteringFollowingParagraph=true;
            try { EditingCommands.EnterParagraphBreak.Execute(null,Editor); }
            finally { enteringFollowingParagraph=false; }
            if(Editor.CaretPosition.Paragraph is Paragraph body)body.Tag=ParagraphStyleTagPrefix+"Normal";
            return;
        }
        var format=ResolveStyle(following);
        var inserted=false;
        enteringFollowingParagraph=true;
        try
        {
            if(format!=null)inserted=InsertFollowingStyledParagraph(Editor,paragraph,following,format);
        }
        finally { enteringFollowingParagraph=false; }
        if(inserted)
        {
            PrepareDocumentStylesForInterop();
            RefreshParagraphStyleBox();
            RefreshFontBox();
        }
    }

    internal static Paragraph? ParagraphEndingAtSelection(RichTextBox editor)
    {
        var end=editor.Selection.End;
        var paragraph=end.Paragraph;
        if(paragraph==null || editor.Selection.Start.Paragraph!=paragraph)return null;

        var next=end.GetNextInsertionPosition(LogicalDirection.Forward);
        return next?.Paragraph==paragraph?null:paragraph;
    }

    internal static bool InsertFollowingStyledParagraph(
        RichTextBox editor,Paragraph original,string following,StyleSetStore.TextStyle format)
    {
        editor.BeginChange();
        try
        {
            EditingCommands.EnterParagraphBreak.Execute(null,editor);
            var paragraph=editor.CaretPosition.Paragraph;
            if(paragraph==null || paragraph==original)return false;

            ApplyFollowingStyle(editor,paragraph,following,format);
            return true;
        }
        finally { editor.EndChange(); }
    }

    internal static void ApplyFollowingStyle(
        RichTextBox editor,Paragraph paragraph,string following,StyleSetStore.TextStyle format)
    {
        paragraph.Tag=ParagraphStyleTag(following,format);

        // Paragraph formatting clears WPF's pending typing format. Complete it
        // before positioning the caret and setting the character properties.
        ApplyStyleToRange(new TextRange(paragraph.ContentStart,paragraph.ContentEnd),format,paragraph);

        if(paragraph.Inlines.All(IsEmptyFormattingInline))
        {
            // Return can carry an empty, directly formatted heading Run/Span
            // into the new paragraph. Paragraph defaults cannot override it.
            // Replace only empty formatting containers, retaining any content,
            // hyperlink, field marker or embedded element if one is present.
            paragraph.Inlines.Clear();
            var run=new Run
            {
                FontFamily=new FontFamily(format.FontFamily),
                FontSize=format.SizePt*96/72,
                FontStyle=format.Italic?FontStyles.Italic:FontStyles.Normal,
                FontWeight=format.Bold?FontWeights.Bold:FontWeights.Normal,
                Foreground=BrushFromHex(format.Colour),
                TextDecorations=format.Underline?TextDecorations.Underline:null
            };
            paragraph.Inlines.Add(run);
            editor.CaretPosition=run.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        }

        // Keep these last: later paragraph formatting or caret movement would
        // discard the character properties used for the next text input.
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily(format.FontFamily));
        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,format.SizePt*96/72);
        editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,format.Italic?FontStyles.Italic:FontStyles.Normal);
        editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,format.Bold?FontWeights.Bold:FontWeights.Normal);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,BrushFromHex(format.Colour));
        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,format.Underline?TextDecorations.Underline:null);
    }

    static bool IsEmptyFormattingInline(Inline inline)
    {
        if(inline.Tag!=null || !string.IsNullOrEmpty(inline.Name))return false;
        if(inline is Run run)return run.Text.Length==0;
        if(inline.GetType()!=typeof(Span) && inline is not Bold and not Italic and not Underline)return false;
        return ((Span)inline).Inlines.All(IsEmptyFormattingInline);
    }
}
