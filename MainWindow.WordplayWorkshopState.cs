using System.Text.Json;

namespace CtrlZed;

public partial class MainWindow
{
    // The word pile, generated results and favourites travel with this document.
    internal WordplayWorkshopState wordplayWorkshop=new();
    internal long wordplayWorkshopRevision;
    internal event Action? WordplayWorkshopChanged;
    internal event Action? WordplayWorkshopDocumentChanged;

    bool HasDocumentWorkshopData=>storyWorkshop.HasContent || wordplayWorkshop.HasContent;

    internal WordplayWorkshopState SnapshotWordplayWorkshop()=>wordplayWorkshop.DeepClone();

    internal void CommitWordplayWorkshop(WordplayWorkshopState next,string reason)
    {
        ArgumentNullException.ThrowIfNull(next);
        if(Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. Its Wordplay Workshop cannot be changed.");
        var copy=next.DeepClone();
        if(copy.Id!=wordplayWorkshop.Id)
            throw new InvalidOperationException("This Wordplay Workshop belongs to another document. Reopen it for the current document.");
        // Only a successful document commit owns the revision, never a form or
        // delayed AI result. The window separately checks its captured revision.
        copy.Revision=wordplayWorkshop.Revision;
        if(JsonSerializer.Serialize(copy)==JsonSerializer.Serialize(wordplayWorkshop))return;
        copy.Revision=checked(wordplayWorkshop.Revision+1);
        copy.Validate();
        wordplayWorkshop=copy;
        wordplayWorkshopRevision++;
        MarkDirty(string.IsNullOrWhiteSpace(reason)?"Wordplay Workshop updated — save the .czd document to keep it.":reason);
        WordplayWorkshopChanged?.Invoke();
    }

    void ReplaceWordplayWorkshopDocument(WordplayWorkshopState? metadata)
    {
        var copy=metadata?.DeepClone()??new WordplayWorkshopState();
        wordplayWorkshop=copy;
        wordplayWorkshopRevision++;
        WordplayWorkshopDocumentChanged?.Invoke();
        WordplayWorkshopChanged?.Invoke();
    }
}
