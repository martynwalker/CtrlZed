using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

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

            paragraph.Tag=ParagraphStyleTagPrefix+following;
            ApplyStyleToRange(editor.Selection,format);
            return true;
        }
        finally { editor.EndChange(); }
    }
}
