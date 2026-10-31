using System.IO;
using System.IO.Compression;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public static class SelfTest
{
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"CtrlZedSelfTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc=new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("CtrlZed self test")){FontSize=24,FontWeight=System.Windows.FontWeights.Bold});
            doc.Blocks.Add(new Paragraph(new Run("Find across ordinary text.")));

            var pixels=new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255};
            var bitmap=new WriteableBitmap(2,2,96,96,PixelFormats.Bgra32,null);
            bitmap.WritePixels(new System.Windows.Int32Rect(0,0,2,2),pixels,8,0);
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(new Image{Source=bitmap,Width=32,Height=32})));

            var table=new Table();
            table.Columns.Add(new TableColumn());table.Columns.Add(new TableColumn());
            var group=new TableRowGroup();table.RowGroups.Add(group);
            var row=new TableRow();group.Rows.Add(row);
            row.Cells.Add(new TableCell(new Paragraph(new Run("A"))));
            row.Cells.Add(new TableCell(new Paragraph(new Run("B"))));
            doc.Blocks.Add(table);

            var rtf=Path.Combine(root,"test.rtf");
            using(var fs=File.Create(rtf))new TextRange(doc.ContentStart,doc.ContentEnd).Save(fs,System.Windows.DataFormats.Rtf);
            var rtfDoc=new FlowDocument();
            using(var fs=File.OpenRead(rtf))new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Load(fs,System.Windows.DataFormats.Rtf);
            if(!new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Text.Contains("CtrlZed self test"))return 11;

            var docx=Path.Combine(root,"test.docx");
            DocxInterop.Save(docx,doc);
            if(!File.Exists(docx)||new FileInfo(docx).Length<500)return 12;
            using(var zip=ZipFile.OpenRead(docx))
                if(zip.GetEntry("word/document.xml")==null)return 13;

            var imported=DocxInterop.Load(docx);
            var text=new TextRange(imported.ContentStart,imported.ContentEnd).Text;
            if(!text.Contains("CtrlZed self test")||!text.Contains("A")||!text.Contains("B"))return 14;
            if(!imported.Blocks.OfType<Table>().Any())return 15;
            var importedImage=imported.Blocks.OfType<Paragraph>()
                .SelectMany(p=>p.Inlines.OfType<InlineUIContainer>())
                .Any(ui=>ui.Child is Image);
            if(!importedImage)return 16;

            return 0;
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());}catch{}
            return 99;
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{}
        }
    }
}
