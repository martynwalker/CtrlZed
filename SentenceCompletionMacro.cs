using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

/// <summary>Opens the writing assistant for review; it never inserts AI output automatically.</summary>
public static class SentenceCompletionMacro
{
    public static void Show(RichTextBox editor,string? inflection=null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if(!editor.Dispatcher.CheckAccess())
        {
            editor.Dispatcher.Invoke(()=>Show(editor,inflection));
            return;
        }
        var window=Window.GetWindow(editor) as MainWindow ??
            Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault(item=>ReferenceEquals(item.Editor,editor));
        if(window==null || !ReferenceEquals(window.Editor,editor))
            throw new InvalidOperationException("The macro editor is not attached to a CtrlZed window.");
        window.OpenSentenceCompletionFromMacro(inflection);
    }
}

public partial class MainWindow
{
    long sentenceCompletionMacroGeneration;

    internal void OpenSentenceCompletionFromMacro(string? inflection)
    {
        inflection=(inflection??"").Trim();
        if(inflection.Length>=2 && inflection[0]=='"' && inflection[^1]=='"')inflection=inflection[1..^1].Trim();
        if(inflection.Length>0 && !inflection.Equals("Custom",StringComparison.OrdinalIgnoreCase))
        {
            var choices=SentenceCompletionService.BuiltIns.Concat(SentenceCompletionPreferencesStore.Load().CustomInflections);
            var selected=choices.FirstOrDefault(item=>item.Name.Equals(inflection,StringComparison.OrdinalIgnoreCase));
            if(selected==null)throw new InvalidOperationException($"No built-in or saved custom inflection is named ‘{inflection}’.");
            inflection=selected.Name;
        }
        sentenceCompletionMacroGeneration++;
        ShowSentenceCompletion(inflection.Length==0?null:inflection);
    }

    // A preview-only compiled macro must not dirty the document. Source/size
    // edits to embedded pictures need tracking in addition to TextChanged.
    IReadOnlyList<(Image Image,ImageSource? Source,double Width,double Height,Visibility Visibility)> CaptureCompletionMacroPictures()
        =>EnumerateImageContainers(Editor.Document).Select(item=>(item.Image,item.Image.Source,
            item.Image.Width,item.Image.Height,item.Image.Visibility)).ToList();

    (double Width,double Height,Thickness Padding,double ColumnWidth,double ColumnGap,FlowDirection Direction) CaptureCompletionMacroLayout()
        =>(Editor.Document.PageWidth,Editor.Document.PageHeight,Editor.Document.PagePadding,
            Editor.Document.ColumnWidth,Editor.Document.ColumnGap,Editor.Document.FlowDirection);
}
