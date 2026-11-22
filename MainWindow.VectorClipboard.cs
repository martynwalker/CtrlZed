using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    bool vectorClipboardInstalled;
    bool copyingVectorClipboard;

    internal void InstallVectorClipboard()
    {
        if(vectorClipboardInstalled)return;
        vectorClipboardInstalled=true;
        Editor.AddHandler(CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler(VectorClipboardPreviewExecuted));
    }

    void VectorClipboardPreviewExecuted(object sender,ExecutedRoutedEventArgs e)
    {
        if(e.Handled || copyingVectorClipboard ||
           e.Command!=ApplicationCommands.Copy && e.Command!=ApplicationCommands.Cut ||
           Editor.Selection.IsEmpty)return;
        var selectedVectors=SelectedVectorImages();
        if(selectedVectors.Count==0)return;
        var cut=e.Command==ApplicationCommands.Cut;
        e.Handled=true;
        if(cut && Editor.IsReadOnly)return;

        var document=Editor.Document;
        var start=Editor.Selection.Start.GetPositionAtOffset(0,LogicalDirection.Forward)!;
        var end=Editor.Selection.End.GetPositionAtOffset(0,LogicalDirection.Backward)!;
        DataObjectCopyingEventArgs? copied=null;
        DataObjectCopyingEventHandler observeCopy=(_,args)=>
        {
            if(!args.IsDragDrop)copied=args;
        };
        var written=false;
        try
        {
            copyingVectorClipboard=true;
            Editor.AddHandler(DataObject.CopyingEvent,observeCopy,true);
            try
            {
                // The standard clipboard formats retain the readable bitmap.
                // Restore the live SVG source before deleting anything for Cut
                // so its native Undo record still contains the vector master.
                VectorImageSupport.WithBitmapFallback(document,()=>
                {
                    var visibility=selectedVectors.Select(image=>(Image:image,Value:image.Visibility)).ToArray();
                    try
                    {
                        // A floating image's original is collapsed while its
                        // overlay is shown. The clipboard fallback must be a
                        // visible inline picture in the receiving document.
                        foreach(var item in visibility)item.Image.Visibility=Visibility.Visible;
                        var before=VectorClipboardSequenceNumber();
                        Editor.Copy();
                        var after=VectorClipboardSequenceNumber();
                        // WPF may silently return after a failed clipboard write.
                        // Copying can also be cancelled by another routed handler.
                        written=copied!=null && !copied.CommandCancelled &&
                            after!=0 && before!=after;
                    }
                    finally
                    {
                        foreach(var item in visibility)item.Image.Visibility=item.Value;
                    }
                });
            }
            finally
            {
                Editor.RemoveHandler(DataObject.CopyingEvent,observeCopy);
                copyingVectorClipboard=false;
            }

            if(!written)
            {
                Status.Text=cut?"The selection could not be copied, so it was kept in the document.":
                    "The selection could not be copied to the clipboard.";
                return;
            }
            if(!cut){Status.Text="Copied selection.";return;}
            if(Editor.IsReadOnly || !ReferenceEquals(document,Editor.Document) ||
               Editor.Selection.Start.CompareTo(start)!=0 || Editor.Selection.End.CompareTo(end)!=0)
            {
                Status.Text="The document or selection changed while copying, so it was kept in the document.";
                return;
            }
            Editor.BeginChange();
            try {Editor.Selection.Text="";}
            finally {Editor.EndChange();}
            MarkDirty("Cut selection.");
            Editor.Focus();
        }
        catch(Exception ex)
        {
            Status.Text=cut?"Cut could not be completed: "+ex.Message:
                "Copy failed: "+ex.Message;
        }
    }

    List<Image> SelectedVectorImages()
    {
        var start=Editor.Selection.Start;
        var end=Editor.Selection.End;
        return EnumerateImageContainers(Editor.Document).Where(item=>
            item.Container.ContentStart.CompareTo(end)<0 && item.Container.ContentEnd.CompareTo(start)>0 &&
            VectorImageSupport.TryGetSvg(item.Image,out _)).Select(item=>item.Image).Distinct().ToList();
    }

    [DllImport("user32.dll",EntryPoint="GetClipboardSequenceNumber",ExactSpelling=true)]
    static extern uint VectorClipboardSequenceNumber();
}
