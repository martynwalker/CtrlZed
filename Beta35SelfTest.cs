using System.Diagnostics;
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
            for(var i=0;i<500;i++)
            {
                var paragraph=new Paragraph(new Run(
                    $"Pagination test paragraph {i+1}. This deliberately contains enough ordinary text to create a representative long document without changing the source while it is measured. The writer can continue editing while the cached page map is calculated from a separate snapshot."))
                {
                    Margin=new Thickness(0,0,0,7),
                    KeepTogether=i%9==0,
                    KeepWithNext=i%13==0
                };
                if(i>0 && i%80==0)paragraph.BreakPageBefore=true;
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

            var stopwatch=Stopwatch.StartNew();
            var backgroundTask=PaginationMapBuilder.BuildAsync(
                rtf,794,1123,margins,key,CancellationToken.None);
            var dispatchMilliseconds=stopwatch.ElapsedMilliseconds;
            if(dispatchMilliseconds>500)
                return Fail(107,$"Starting the background pagination worker took {dispatchMilliseconds} ms.");
            var second=backgroundTask.GetAwaiter().GetResult();
            stopwatch.Stop();
            if(stopwatch.Elapsed>TimeSpan.FromSeconds(20))
                return Fail(108,$"Long-document pagination took {stopwatch.Elapsed.TotalSeconds:0.0} seconds.");

            var after=Convert.ToHexString(SHA256.HashData(rtf));

            if(first.PageCount<15)
                return Fail(100,$"The long pagination fixture produced only {first.PageCount} pages.");
            if(first.PageStartOffsets.Count!=first.PageCount)
                return Fail(101,"The page-start count does not match the page count.");
            if(first.PageStartOffsets[0]!=0)
                return Fail(102,"The first page does not begin at offset zero.");
            if(first.PageStartOffsets.Zip(first.PageStartOffsets.Skip(1),(a,b)=>b>=a).Any(ok=>!ok))
                return Fail(103,"Page boundaries are not monotonic.");
            if(first.PageCount!=second.PageCount ||
               !first.PageStartOffsets.SequenceEqual(second.PageStartOffsets))
                return Fail(104,"Foreground and background pagination produced different maps.");
            if(!string.Equals(before,after,StringComparison.Ordinal))
                return Fail(105,"Pagination mutated the RTF snapshot.");
            if(!string.Equals(first.CacheKey,key,StringComparison.Ordinal))
                return Fail(106,"The completed map did not retain its cache key.");

            var changedLayoutKey=PaginationMapBuilder.CreateCacheKey(
                rtf,816,1056,new Thickness(60));
            if(string.Equals(changedLayoutKey,key,StringComparison.Ordinal))
                return Fail(109,"Changing the page layout did not invalidate the cache key.");

            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    PaginationMapBuilder.BuildCore(
                        rtf,794,1123,margins,key,cancelled.Token);
                    return Fail(110,"A cancelled pagination request still produced a map.");
                }
                catch(OperationCanceledException){ }
            }

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
            return 119;
        }
    }

    static int Fail(int code,string message)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),
                $"Beta 35 pagination test {code}: {message}");
        }
        catch{ }
        return code;
    }
}
