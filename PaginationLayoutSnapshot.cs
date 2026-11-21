using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CtrlZed;

/// <summary>
/// Immutable, thread-neutral data which RTF does not reliably retain. Capture
/// on the editor thread and apply only to the pagination worker's own document.
/// </summary>
public sealed class PaginationLayoutSnapshot
{
    readonly record struct TextSegment(int CharacterStart,int SourceOffset,int Length);
    sealed record ParagraphLayout(
        Thickness Margin,double TextIndent,double LineHeight,LineStackingStrategy Stacking,
        bool KeepWithNext,bool KeepTogether,bool BreakPageBefore,
        bool Hyphenation,TextAlignment Alignment,FlowDirection Direction,
        int EndOffset,TextSegment[] Segments);

    readonly ParagraphLayout[] paragraphs;
    readonly Visibility[] imageVisibility;

    public int SourceSymbolLength { get; }
    public string CacheDiscriminator { get; }

    PaginationLayoutSnapshot(ParagraphLayout[] paragraphs,Visibility[] imageVisibility,int sourceSymbolLength)
    {
        this.paragraphs=paragraphs;
        this.imageVisibility=imageVisibility;
        SourceSymbolLength=sourceSymbolLength;
        var key=new StringBuilder().Append(sourceSymbolLength).Append('|');
        static string Number(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        foreach(var paragraph in paragraphs)
        {
            key.Append(Number(paragraph.Margin.Left)).Append(',').Append(Number(paragraph.Margin.Top)).Append(',')
                .Append(Number(paragraph.Margin.Right)).Append(',').Append(Number(paragraph.Margin.Bottom)).Append('|')
                .Append(Number(paragraph.TextIndent)).Append('|').Append(Number(paragraph.LineHeight)).Append('|')
                .Append((int)paragraph.Stacking).Append('|').Append(paragraph.KeepWithNext).Append('|')
                .Append(paragraph.KeepTogether).Append('|').Append(paragraph.BreakPageBefore).Append('|')
                .Append(paragraph.Hyphenation).Append('|').Append((int)paragraph.Alignment).Append('|')
                .Append((int)paragraph.Direction).Append('|').Append(paragraph.EndOffset).Append(':');
            foreach(var segment in paragraph.Segments)
                key.Append(segment.CharacterStart).Append(',').Append(segment.SourceOffset).Append(',').Append(segment.Length).Append(';');
            key.Append('\n');
        }
        foreach(var visibility in imageVisibility)key.Append((int)visibility).Append(',');
        CacheDiscriminator=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
    }

    public static PaginationLayoutSnapshot Capture(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Dispatcher.VerifyAccess();
        var start=document.ContentStart;
        var captured=Paragraphs(document.Blocks).Select(paragraph=>new ParagraphLayout(
            paragraph.Margin,paragraph.TextIndent,paragraph.LineHeight,paragraph.LineStackingStrategy,
            paragraph.KeepWithNext,paragraph.KeepTogether,paragraph.BreakPageBefore,
            paragraph.IsHyphenationEnabled,paragraph.TextAlignment,paragraph.FlowDirection,
            start.GetOffsetToPosition(paragraph.ContentEnd),CaptureSegments(start,paragraph))).ToArray();
        var images=Images(document.Blocks).Select(image=>image.Visibility).ToArray();
        return new PaginationLayoutSnapshot(captured,images,start.GetOffsetToPosition(document.ContentEnd));
    }

    internal void Apply(FlowDocument document)
    {
        var target=Paragraphs(document.Blocks).ToArray();
        for(var index=0;index<Math.Min(target.Length,paragraphs.Length);index++)
        {
            var paragraph=target[index];
            var layout=paragraphs[index];
            paragraph.Margin=layout.Margin;
            paragraph.TextIndent=layout.TextIndent;
            paragraph.LineHeight=layout.LineHeight;
            paragraph.LineStackingStrategy=layout.Stacking;
            paragraph.KeepWithNext=layout.KeepWithNext;
            paragraph.KeepTogether=layout.KeepTogether;
            paragraph.BreakPageBefore=layout.BreakPageBefore;
            paragraph.IsHyphenationEnabled=layout.Hyphenation;
            paragraph.TextAlignment=layout.Alignment;
            paragraph.FlowDirection=layout.Direction;
        }
        var images=Images(document.Blocks).ToArray();
        for(var index=0;index<Math.Min(images.Length,imageVisibility.Length);index++)
            images[index].Visibility=imageVisibility[index];
    }

    internal Func<TextPointer,int> CreatePageOffsetMapper(FlowDocument document)
    {
        var target=Paragraphs(document.Blocks).ToArray();
        var indexes=target.Select((paragraph,index)=>(paragraph,index)).ToDictionary(pair=>pair.paragraph,pair=>pair.index);
        return pointer=>
        {
            var paragraph=pointer.Paragraph??pointer.GetInsertionPosition(LogicalDirection.Forward)?.Paragraph;
            if(target.Length!=paragraphs.Length || paragraph==null || !indexes.TryGetValue(paragraph,out var index))
                return Math.Clamp(document.ContentStart.GetOffsetToPosition(pointer),0,SourceSymbolLength);

            var character=CharacterOffset(paragraph,pointer);
            var source=paragraphs[index];
            foreach(var segment in source.Segments)
            {
                // At a formatting boundary prefer the following text run,
                // rather than a position before its intervening Span markers.
                if(character<segment.CharacterStart+segment.Length)
                    return segment.SourceOffset+Math.Clamp(character-segment.CharacterStart,0,segment.Length);
            }
            return source.EndOffset;
        };
    }

    static TextSegment[] CaptureSegments(TextPointer documentStart,Paragraph paragraph)
    {
        var segments=new List<TextSegment>();
        var character=0;
        for(var position=paragraph.ContentStart;position!=null && position.CompareTo(paragraph.ContentEnd)<0;
            position=position.GetNextContextPosition(LogicalDirection.Forward))
        {
            var length=LogicalLength(position);
            if(length==0)continue;
            segments.Add(new TextSegment(character,documentStart.GetOffsetToPosition(position),length));
            character+=length;
        }
        return segments.ToArray();
    }

    static int CharacterOffset(Paragraph paragraph,TextPointer target)
    {
        var count=0;
        for(var position=paragraph.ContentStart;position!=null && position.CompareTo(paragraph.ContentEnd)<0;
            position=position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if(position.CompareTo(target)>=0)break;
            var length=LogicalLength(position);
            if(length==0)continue;
            count+=Math.Min(length,Math.Max(0,position.GetOffsetToPosition(target)));
        }
        return count;
    }

    static int LogicalLength(TextPointer position)
        =>position.GetPointerContext(LogicalDirection.Forward) switch
        {
            TextPointerContext.Text=>position.GetTextRunLength(LogicalDirection.Forward),
            TextPointerContext.EmbeddedElement=>1,
            TextPointerContext.ElementStart when position.GetAdjacentElement(LogicalDirection.Forward) is LineBreak=>1,
            _=>0
        };

    static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
    {
        foreach(var block in blocks)
        {
            if(block is Paragraph paragraph)yield return paragraph;
            else if(block is Section section)
                foreach(var value in Paragraphs(section.Blocks))yield return value;
            else if(block is System.Windows.Documents.List list)
                foreach(var item in list.ListItems)
                    foreach(var value in Paragraphs(item.Blocks))yield return value;
            else if(block is Table table)
                foreach(var group in table.RowGroups)
                    foreach(var row in group.Rows)
                        foreach(var cell in row.Cells)
                            foreach(var value in Paragraphs(cell.Blocks))yield return value;
        }
    }

    static IEnumerable<Image> Images(IEnumerable<Block> blocks)
    {
        static IEnumerable<Image> Inlines(InlineCollection inlines)
        {
            foreach(var inline in inlines)
            {
                if(inline is Span span)
                    foreach(var value in Inlines(span.Inlines))yield return value;
                else if(inline is InlineUIContainer ui)
                {
                    if(ui.Child is Image image)yield return image;
                    else if(ui.Child is Canvas canvas)
                        foreach(var nested in canvas.Children.OfType<Image>())yield return nested;
                }
            }
        }
        foreach(var paragraph in Paragraphs(blocks))
            foreach(var image in Inlines(paragraph.Inlines))yield return image;
    }
}
