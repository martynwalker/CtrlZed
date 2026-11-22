using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    const string ProfileSignatureReference="@profile:signature";

    bool UsesStoredProfileSignature=>string.Equals(signatureImagePath,ProfileSignatureReference,StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(signatureImagePath) && ProfileImageStore.Contains("signature");

    bool HasVectorSignatureConfiguration()
        =>UsesStoredProfileSignature?ProfileImageStore.Contains("signature"):
            !string.IsNullOrWhiteSpace(signatureImagePath) && File.Exists(signatureImagePath);

    string SignatureSetupDescription()
    {
        try
        {
            if(UsesStoredProfileSignature)
            {
                if(!ProfileImageStore.Contains("signature"))return "The stored signature is missing. Configure a replacement.";
                return ProfileImageStore.LoadAsset("signature").Svg!=null
                    ?"Configured SVG signature, stored in the CtrlZed profile."
                    :"Configured image signature, stored in the CtrlZed profile.";
            }
            return string.IsNullOrWhiteSpace(signatureImagePath)?"No signature image configured.":
                File.Exists(signatureImagePath)?"Configured: "+Path.GetFileName(signatureImagePath):
                "The configured signature file is missing. Configure a replacement.";
        }
        catch(Exception){return "The configured signature is unavailable. Configure a replacement.";}
    }

    void ConfigureVectorSignature(Window owner)
    {
        var dialog=new OpenFileDialog
        {
            Title="Choose Signature",
            Filter="Signature images|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif|SVG vector image (*.svg)|*.svg|All files|*.*",
            CheckFileExists=true
        };
        if(dialog.ShowDialog(owner)!=true)return;
        try
        {
            var bytes=SignatureImportWindow.ReadFile(dialog.FileName);
            var selected=SignatureImportWindow.Choose(owner,bytes,"Review Signature",true);
            if(selected==null)return;
            ProfileImageStore.Save("signature",selected);
            signatureImagePath=ProfileSignatureReference;
            SaveAppSettings();
            Status.Text=ProfileImageStore.LoadAsset("signature").Svg!=null?"Vector signature configured.":"Signature image configured.";
        }
        catch(Exception ex){MessageBox.Show(owner,ex.Message,"Signature setup failed",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    void InsertVectorSignature()
    {
        if(Editor.IsReadOnly)
        {
            MessageBox.Show(this,"The document is read-only.","Insert Signature",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        try
        {
            if(!HasVectorSignatureConfiguration())
            {
                var choice=MessageBox.Show(this,"No signature is configured. Configure one now?","Insert Signature",
                    MessageBoxButton.YesNo,MessageBoxImage.Question);
                if(choice!=MessageBoxResult.Yes)return;
                ConfigureVectorSignature(this);
                if(!HasVectorSignatureConfiguration())return;
            }
            if(UsesStoredProfileSignature)
            {
                InsertProfileImage("signature",40,PicturePlacementMode.Inline);
                Status.Text="Signature inserted.";
                return;
            }

            // Existing configured image files remain usable until the writer
            // chooses to replace or trace them into the named profile store.
            var asset=SignatureImages.Load(SignatureImportWindow.ReadFile(signatureImagePath));
            var width=40*96.0/25.4;
            var image=new Image
            {
                Source=asset.Bitmap,Width=width,Height=width*asset.Height/asset.Width,
                Stretch=Stretch.Uniform,IsHitTestVisible=false,Focusable=false,Margin=new Thickness(4)
            };
            VectorImageSupport.SetAsset(image,asset);
            var insertion=Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward)??Editor.Document.ContentEnd;
            var container=new InlineUIContainer(image,insertion){BaselineAlignment=BaselineAlignment.Baseline};
            WirePictureContainer(container,image);
            Editor.CaretPosition=container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward)??container.ElementEnd;
            MarkDirty("Signature inserted.");
            UpdateDocumentStats();Editor.Focus();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Insert signature failed",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
}
