using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    bool updatingStyleBox;

    void Editor_FollowingStylePreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Handled || e.Key!=Key.Enter || Keyboard.Modifiers!=ModifierKeys.None ||
           Editor.IsReadOnly || !Editor.IsKeyboardFocused)return;

        var paragraph=ParagraphEndingAtSelection(Editor);
        if(paragraph==null)return;

        var role=InferParagraphStyle(paragraph,styleSets.Values)??"Normal";
        var following=StyleSetStore.NormalizeFollowingStyle(ResolveStyle(role)?.FollowingStyle);
        if(string.Equals(role,following,StringComparison.OrdinalIgnoreCase))return;

        var format=ResolveStyle(following);
        if(format==null || !EditingCommands.EnterParagraphBreak.CanExecute(null,Editor))return;

        e.Handled=true;
        if(InsertFollowingStyledParagraph(Editor,paragraph,following,format))
        {
            updatingStyleBox=true;
            try
            {
                StyleBox.SelectedItem=StyleBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item=>string.Equals(item.Content as string,following,StringComparison.Ordinal));
            }
            finally { updatingStyleBox=false; }

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
        paragraph.Tag=ParagraphStyleTagPrefix+following;

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
