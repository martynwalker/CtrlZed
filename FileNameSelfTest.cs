namespace CtrlZed;

internal static class FileNameSelfTest
{
    internal static void Run()
    {
        static void Equal(string expected,string actual,string reason)
        {
            if(actual!=expected)throw new InvalidOperationException($"Filename suggestion {reason}: expected '{expected}', got '{actual}'.");
        }

        Equal("The useful heading",DocumentFileName.Suggest(new[]{("Opening body sentence. A second sentence.",false),("  \r\n",true),("The useful heading",true),("Later heading",true)}),"must prefer the first non-empty heading over opening prose");
        Equal("The first sentence",DocumentFileName.Suggest(new[]{(" \r\n  The first sentence. The second sentence is different.",false)}),"must use only the first sentence without a heading");
        Equal("The opening sentence is complete",DocumentFileName.Suggest(new[]{("The opening",false),("sentence is complete. Next sentence.",false)}),"must retain a sentence continued across paragraphs");
        Equal("Dr. Walker reviewed version 3.2",DocumentFileName.Suggest(new[]{("Dr. Walker reviewed version 3.2. The remainder is separate.",false)}),"must retain abbreviations and decimal numbers");
        Equal("The U.K. report is ready",DocumentFileName.Suggest(new[]{("The U.K. report is ready. More follows.",false)}),"must retain dotted initials");
        Equal("Is this ready",DocumentFileName.Suggest(new[]{("Is this ready? Yes, it is.",false)}),"must recognise question sentence boundaries");
        Equal("1. The numbered sentence is ready",DocumentFileName.Suggest(new[]{("1. The numbered sentence is ready. Next sentence.",false)}),"must not treat a leading list number as the first sentence");
        Equal("A B C D E",DocumentFileName.SanitiseStem(" A:<B>/C\\D|E?*.  "),"must remove Windows filename punctuation");
        Equal("Untitled",DocumentFileName.Suggest(Array.Empty<(string,bool)>()),"must provide an empty-document fallback");
        Equal("Untitled",DocumentFileName.SanitiseStem("  .:*?\uFFFC\r\n"),"must provide a fallback for unusable text");
        Equal("_CON",DocumentFileName.SanitiseStem("CON"),"must avoid reserved Windows device names");
        Equal("_LPT1.txt",DocumentFileName.SanitiseStem("LPT1.txt"),"must avoid reserved names with an extension");
        Equal("_COM¹",DocumentFileName.SanitiseStem("COM¹"),"must avoid superscript device aliases");
        Equal("Untitled.czd",DocumentFileName.WithExtension(DocumentFileName.Fallback),"must retain the normal native file type");
        Equal("Dr. Walker report.czd",DocumentFileName.WithExtension("Dr. Walker report"),"must not mistake a title's internal dot for a file extension");
        Equal("A title.docx",DocumentFileName.WithExtension("A title",".docx"),"must permit the requested save type");
        var longName=DocumentFileName.SanitiseStem(new string('a',119)+"😀"+new string('b',50));
        if(longName.Length>DocumentFileName.MaximumStemLength || char.IsHighSurrogate(longName[^1]))
            throw new InvalidOperationException("The filename suggestion split a Unicode character or exceeded the length cap.");
        if(DocumentFileName.SanitiseStem("CON."+new string('a',200)).Length>DocumentFileName.MaximumStemLength)
            throw new InvalidOperationException("A reserved-device prefix exceeded the filename length cap.");
    }
}

