using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace CtrlZed;

public partial class MainWindow
{
    bool richTextPasteInitialised;

    void InitializeRichTextPaste()
    {
        if(richTextPasteInitialised)return;
        richTextPasteInitialised=true;
        DataObject.AddPastingHandler(Editor,Editor_RichTextPasting);
    }

    void Editor_RichTextPasting(object sender,DataObjectPastingEventArgs e)
    {
        if(e.CommandCancelled || e.IsDragDrop || !ReferenceEquals(e.OriginalSource,Editor) || Editor.IsReadOnly ||
           (e.FormatToApply!=DataFormats.UnicodeText && e.FormatToApply!=DataFormats.Text))return;
        try
        {
            var source=e.DataObject;
            // WPF already preserves its own fragments, Word/RTF and bitmaps.
            // Enhance only browser HTML that would otherwise become plain text.
            if(source.GetDataPresent(DataFormats.XamlPackage,false) ||
               source.GetDataPresent(DataFormats.Xaml,false) ||
               source.GetDataPresent(DataFormats.Rtf,false) ||
               !source.GetDataPresent(DataFormats.Html,false))return;

            var value=source.GetData(DataFormats.Html,false);
            string? html=value as string;
            if(html==null && value is MemoryStream memory && memory.Length<=HtmlClipboardFormat.MaximumHtmlBytes)
                html=Encoding.UTF8.GetString(memory.ToArray()).TrimEnd('\0');
            else if(html==null && value is byte[] bytes && bytes.Length<=HtmlClipboardFormat.MaximumHtmlBytes)
                html=Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            if(html==null || !HtmlClipboardFormat.TryConvert(html,out var xaml))return;

            // This data object belongs only to this paste event. In particular,
            // do not replace the global clipboard or strip formats for Ctrl+V.
            var converted=new DataObject();
            converted.SetData(DataFormats.Xaml,xaml,false);
            if(source.GetData(DataFormats.UnicodeText,true) is string text)
                converted.SetData(DataFormats.UnicodeText,text,false);
            e.DataObject=converted;
            e.FormatToApply=DataFormats.Xaml;
        }
        catch(ExternalException)
        {
            // Leave WPF's original paste available when the clipboard is busy.
        }
    }
}

