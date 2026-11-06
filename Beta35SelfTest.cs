using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace CtrlZed;

public static class Beta35SelfTest
{
    public static int Run()
    {
        try
        {
            var document=new FlowDocument();
            for(var i=0;i<180;i++)
            {
                var paragraph=new Paragraph(new Run(
                    $"Pagination test paragraph {i+1}. This deliberately contains enough ordinary text to create a representative multi-page document without changing the source while it is measured."))
                {
                    Margin=new Thickness(0,0,0,7),
                    KeepTogether=i%9==0,
                    KeepWithNext=i%13==0
                };
                if(i==70 || i==135)paragraph.BreakPageBefore=true;
                document.Blocks.Add(paragraph);
            }

            byte[] rtf;
            using(var stream=new MemoryStream())
            {
                new TextRange(document.ContentStart,document.ContentEnd)
                    .Save(stream,DataFormats.Rtf);
                rtf=stream.ToArray();
            }

            var before=Convert.ToHexString(SHA256.HashData(rtf));
            var margins=new Thickness(72);
            var key=PaginationMapBuilder.CreateCacheKey(rtf,794,1123,margins);
            var first=PaginationMapBuilder.BuildCore(
                rtf,794,1123,margins,key,CancellationToken.None);
            var second=PaginationMapBuilder.BuildCore(
                rtf,794,1123,margins,key,CancellationToken.None);
            var after=Convert.ToHexString(SHA256.HashData(rtf));

            if(first.PageCount<2)return 100;
            if(first.PageStartOffsets.Count!=first.PageCount)return 101;
            if(first.PageStartOffsets[0]!=0)return 102;
            if(first.PageStartOffsets.Zip(first.PageStartOffsets.Skip(1),(a,b)=>b>=a).Any(ok=>!ok))return 103;
            if(first.PageCount!=second.PageCount ||
               !first.PageStartOffsets.SequenceEqual(second.PageStartOffsets))return 104;
            if(!string.Equals(before,after,StringComparison.Ordinal))return 105;
            if(!string.Equals(first.CacheKey,key,StringComparison.Ordinal))return 106;

            return 0;
        }
        catch(Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),
                    ex.ToString());
            }
            catch{ }
            return 109;
        }
    }
}
