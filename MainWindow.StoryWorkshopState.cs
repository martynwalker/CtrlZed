using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace CtrlZed;

public partial class MainWindow
{
    // This state belongs to the open document, never to the application profile.
    internal StoryWorkshopState storyWorkshop=new();
    internal long storyWorkshopRevision;
    internal event Action? StoryWorkshopChanged;
    internal event Action? StoryWorkshopDocumentChanged;
    internal Func<string?>? storyWorkshopNativePathOverride;
    internal Func<string,bool>? storyWorkshopExportConfirmationOverride;

    internal StoryWorkshopState SnapshotStoryWorkshop()=>storyWorkshop.DeepClone();

    internal void CommitStoryWorkshop(StoryWorkshopState next,string reason)
    {
        ArgumentNullException.ThrowIfNull(next);
        if(Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. Its Story Workshop cannot be changed.");
        var copy=next.DeepClone();
        if(copy.Id!=storyWorkshop.Id)
            throw new InvalidOperationException("This Story Workshop belongs to another document. Reopen the workshop for the current document.");
        // Revision is owned by the document, not by an AI response or form copy.
        copy.Revision=storyWorkshop.Revision;
        if(JsonSerializer.Serialize(copy)==JsonSerializer.Serialize(storyWorkshop))return;
        copy.Revision=checked(storyWorkshop.Revision+1);
        copy.Validate();
        storyWorkshop=copy;
        storyWorkshopRevision++;
        MarkDirty(string.IsNullOrWhiteSpace(reason)?"Story Workshop updated — save the .czd document to keep it.":reason);
        StoryWorkshopChanged?.Invoke();
    }

    void ReplaceStoryWorkshopDocument(StoryWorkshopState? metadata)
    {
        var copy=metadata?.DeepClone()??new StoryWorkshopState();
        storyWorkshop=copy;
        storyWorkshopRevision++;
        StoryWorkshopDocumentChanged?.Invoke();
        StoryWorkshopChanged?.Invoke();
    }

    bool EnsureStoryWorkshopNativeSavePath()
    {
        if(!HasDocumentWorkshopData || currentFile!=null &&
            string.Equals(Path.GetExtension(currentFile),".czd",StringComparison.OrdinalIgnoreCase))return true;
        string? selected;
        if(storyWorkshopNativePathOverride!=null)selected=storyWorkshopNativePathOverride();
        else
        {
            var dialog=new SaveFileDialog
            {
                Title="Save Document and Workshops",Filter=NativeDocumentFilter,
                DefaultExt=".czd",AddExtension=true,
                FileName=currentFile==null?SuggestedFirstSaveFileName():Path.GetFileNameWithoutExtension(currentFile)+".czd"
            };
            if(dialog.ShowDialog(this)!=true)return false;
            selected=dialog.FileName;
        }
        if(string.IsNullOrWhiteSpace(selected))return false;
        if(!string.Equals(Path.GetExtension(selected),".czd",StringComparison.OrdinalIgnoreCase))
        {
            if(storyWorkshopNativePathOverride==null)MessageBox.Show(this,
                "Use a .czd filename to save the document with its workshop notes. Use Save As to export the body separately.",
                "Save Document and Workshops",MessageBoxButton.OK,MessageBoxImage.Information);
            return false;
        }
        currentFile=selected;
        return true;
    }

    // Save As to an interchange format is an explicit body export. The working
    // document, its dirty state and its recovery snapshot continue to own the plan.
    bool ExportStoryWorkshopBody(string path)
    {
        if(documentPassword!=null)
        {
            if(storyWorkshopExportConfirmationOverride==null)MessageBox.Show(this,
                "This document is password protected. Save it as .czd to keep its protection.",
                "Export Document Body",MessageBoxButton.OK,MessageBoxImage.Information);
            return false;
        }
        var accepted=storyWorkshopExportConfirmationOverride?.Invoke(path)??(MessageBox.Show(this,
            "This format exports the document body only. Story and wordplay workshop notes, results, favourites and conversations are stored in .czd files and will not be included.\n\nExport the body and keep the current document open?",
            "Export Document Body",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes);
        if(!accepted)return false;
        var previousFile=currentFile;var wasDirty=dirty;var restoreFields=fieldValuesVisible;
        if(restoreFields)SetFieldValuesVisible(false);
        try
        {
            var extension=Path.GetExtension(path).ToLowerInvariant();
            if(extension==".docx")
            {
                PrepareDocumentStylesForInterop();
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    DocxInterop.Save(temporary,Editor.Document,headerText,footerText,pageWidth,pageHeight,pageMargins,
                        documentTitle,documentAuthor,documentSubject,documentKeywords);
                    File.Move(temporary,path,true);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            else if(extension==".txt")
            {
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    File.WriteAllText(temporary,new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text);
                    File.Move(temporary,path,true);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            else SaveRtf(path);
            AddRecentFile(path);
            Status.Text="Exported document body to "+path+". Workshop notes remain in the current document; save it as .czd.";
            return true;
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Export failed",MessageBoxButton.OK,MessageBoxImage.Error);
            return false;
        }
        finally
        {
            currentFile=previousFile;dirty=wasDirty;UpdateTitle();
            if(restoreFields)SetFieldValuesVisible(true);
        }
    }
}
