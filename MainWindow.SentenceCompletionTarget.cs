using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace CtrlZed;

// A completion belongs to this precise passage, even while a model request is
// pending. Live TextPointers alone are insufficient: WPF moves them after edits.
internal sealed class SentenceCompletionTarget
{
    internal FlowDocument Document { get; }
    internal Paragraph Paragraph { get; }
    internal object? ParagraphParent { get; }
    internal Block? PreviousBlock { get; }
    internal Block? NextBlock { get; }
    internal TextPointer Start { get; }
    internal TextPointer End { get; }
    internal string SelectedText { get; }
    internal string OriginalSelectionText { get; }
    internal string Prefix { get; }
    internal string Suffix { get; }
    internal string ParagraphContext { get; }
    internal bool IsReplacement { get; }
    internal int StartTextOffset { get; }
    internal int EndTextOffset { get; }
    internal int StartSymbolOffset { get; }
    internal int EndSymbolOffset { get; }
    internal string LeadingWhitespace { get; }
    internal string TrailingWhitespace { get; }
    internal IReadOnlyList<SentenceCompletionInlineState> InlineStates { get; }
    internal IReadOnlyList<(DependencyProperty Property,object Value)> Formatting { get; }
    internal bool Applied { get; set; }

    internal SentenceCompletionTarget(FlowDocument document,Paragraph paragraph,TextPointer start,TextPointer end,
        string originalSelectionText,string prefix,string suffix,string paragraphContext,int startTextOffset,
        int endTextOffset,IReadOnlyList<SentenceCompletionInlineState> inlineStates,
        IReadOnlyList<(DependencyProperty Property,object Value)> formatting,bool replacement)
    {
        Document=document;Paragraph=paragraph;Start=start;End=end;
        ParagraphParent=paragraph.Parent;PreviousBlock=paragraph.PreviousBlock;NextBlock=paragraph.NextBlock;
        OriginalSelectionText=originalSelectionText;
        SelectedText=originalSelectionText.Trim();
        Prefix=prefix;Suffix=suffix;ParagraphContext=paragraphContext;
        StartTextOffset=startTextOffset;EndTextOffset=endTextOffset;
        StartSymbolOffset=paragraph.ContentStart.GetOffsetToPosition(start);
        EndSymbolOffset=paragraph.ContentStart.GetOffsetToPosition(end);
        IsReplacement=replacement;
        var leading=originalSelectionText.Length-originalSelectionText.TrimStart().Length;
        var trailing=originalSelectionText.Length-originalSelectionText.TrimEnd().Length;
        LeadingWhitespace=originalSelectionText[..leading];
        TrailingWhitespace=trailing==0?"":originalSelectionText[^trailing..];
        InlineStates=inlineStates;Formatting=formatting;
    }
}

internal sealed record SentenceCompletionInlineState(Inline Inline,object? Parent,string? RunText,object? Child,object? Tag);

public partial class MainWindow
{
    const int SentenceCompletionMaximumSelection=600;
    static readonly Regex SentenceCompletionFieldToken=new(@"\{(?:(?:field|system|user)[:.])?[A-Za-z0-9_-]+\}",RegexOptions.CultureInvariant);
    static readonly Regex SentenceCompletionWords=new(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]{2,}",RegexOptions.CultureInvariant);
    static readonly HashSet<string> SentenceCompletionAbbreviations=new(StringComparer.OrdinalIgnoreCase)
    {
        "mr","mrs","ms","dr","prof","sr","jr","st","mt","rev","hon","gen","capt","cmdr","lt","col","sgt",
        "vs","e.g","i.e","fig","figs","eq","no","nos","vol","pp","p","ch","sec","approx","dept","inc","ltd","co",
        "jan","feb","mar","apr","jun","jul","aug","sep","sept","oct","nov","dec","a.m","p.m"
    };
    static readonly HashSet<string> SentenceCompletionStopWords=new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","that","this","with","from","they","their","there","these","those","have","has","had","were",
        "was","are","will","would","could","should","into","about","than","then","when","where","which","what",
        "for","not","but","our","your","you","its","can","may","also","been","being","each","some","such"
    };

    internal SentenceCompletionTarget CaptureSentenceCompletionTarget()
    {
        if(Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. Sentence completion cannot change it.");

        var replacement=!Editor.Selection.IsEmpty;
        var originalStart=replacement?Editor.Selection.Start:Editor.CaretPosition;
        var originalEnd=replacement?Editor.Selection.End:originalStart;
        var paragraph=FindContainingParagraph(originalStart);
        if(paragraph==null || originalStart.CompareTo(paragraph.ContentStart)<0 ||
           originalEnd.CompareTo(paragraph.ContentEnd)>0 ||
           !ReferenceEquals(paragraph,FindContainingParagraph(originalEnd)))
            throw new InvalidOperationException("Place the cursor in a paragraph, or select a short phrase within one paragraph.");

        var start=originalStart.GetPositionAtOffset(0,LogicalDirection.Backward)!;
        var end=originalEnd.GetPositionAtOffset(0,LogicalDirection.Forward)!;
        var paragraphText=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text;
        var selected=new TextRange(start,end).Text;
        var startOffset=new TextRange(paragraph.ContentStart,start).Text.Length;
        var endOffset=new TextRange(paragraph.ContentStart,end).Text.Length;
        if(startOffset>paragraphText.Length || endOffset>paragraphText.Length)
            throw new InvalidOperationException("The cursor is not in an editable text passage.");

        if(replacement && (string.IsNullOrWhiteSpace(selected) || selected.Length>SentenceCompletionMaximumSelection ||
            selected.IndexOfAny(new[]{'\r','\n','\u2028','\u2029','\uFFFC'})>=0))
            throw new InvalidOperationException($"Select a phrase of up to {SentenceCompletionMaximumSelection} characters within one paragraph, without pictures or line breaks.");
        if(!replacement && IsSentenceCompletionInsideWord(paragraphText,startOffset))
            throw new InvalidOperationException("Place the cursor between words, or at the end of the unfinished sentence.");
        if(replacement)
        {
            var coreStart=startOffset+selected.Length-selected.TrimStart().Length;
            var coreEnd=endOffset-(selected.Length-selected.TrimEnd().Length);
            if(IsSentenceCompletionInsideWord(paragraphText,coreStart) || IsSentenceCompletionInsideWord(paragraphText,coreEnd))
                throw new InvalidOperationException("Select complete words for phrase replacement.");
        }

        var states=CaptureSentenceCompletionInlineStates(paragraph);
        if(IsUnsafeSentenceCompletionPassage(paragraphText,startOffset,endOffset,start,end,states,replacement))
            throw new InvalidOperationException("Choose ordinary text outside field codes, displayed fields and embedded objects.");

        var (sentenceStart,sentenceEnd)=SentenceCompletionSentenceBounds(paragraphText,startOffset,endOffset);
        return new SentenceCompletionTarget(Editor.Document,paragraph,start,end,selected,
            paragraphText[sentenceStart..startOffset],paragraphText[endOffset..sentenceEnd],paragraphText,
            startOffset,endOffset,states,CaptureSentenceCompletionFormatting(originalStart,originalEnd,replacement),replacement);
    }

    IReadOnlyList<(DependencyProperty Property,object Value)> CaptureSentenceCompletionFormatting(
        TextPointer start,TextPointer end,bool replacement)
    {
        // An empty TextSelection at Run.ContentEnd can report the following
        // run's formatting. Read the actual destination run instead: completing
        // a bold/navy lead must not borrow an italic right-hand continuation.
        Run? RunInDirection(TextPointer pointer,LogicalDirection direction)
        {
            var paragraph=FindContainingParagraph(pointer);
            var probe=pointer;
            while(probe!=null && ReferenceEquals(FindContainingParagraph(probe),paragraph))
            {
                if(probe.Parent is Run run && !string.IsNullOrEmpty(run.Text) &&
                   (direction==LogicalDirection.Forward?probe.CompareTo(run.ContentEnd)<0:
                       probe.CompareTo(run.ContentStart)>0))return run;
                if(probe.GetPointerContext(direction)==TextPointerContext.EmbeddedElement)break;
                probe=probe.GetNextContextPosition(direction);
            }
            return null;
        }

        Run? source;
        if(replacement)source=RunInDirection(start,LogicalDirection.Forward);
        else if(start.Parent is Run direct && !string.IsNullOrEmpty(direct.Text) &&
            (start.CompareTo(direct.ContentStart)>0 || start.LogicalDirection==LogicalDirection.Forward))source=direct;
        else source=RunInDirection(start,start.LogicalDirection)
            ??RunInDirection(start,start.LogicalDirection==LogicalDirection.Forward?LogicalDirection.Backward:LogicalDirection.Forward);

        var original=new TextRange(start,end);
        var result=new System.Collections.Generic.List<(DependencyProperty,object)>();
        foreach(var property in new[]
        {
            TextElement.FontFamilyProperty,TextElement.FontSizeProperty,TextElement.FontStyleProperty,
            TextElement.FontWeightProperty,TextElement.FontStretchProperty,TextElement.ForegroundProperty,
            TextElement.BackgroundProperty,Inline.TextDecorationsProperty,Inline.BaselineAlignmentProperty
        })
        {
            var value=replacement?original.GetPropertyValue(property):
                source!=null?source.GetValue(property):original.GetPropertyValue(property);
            if(value==DependencyProperty.UnsetValue && source!=null)value=source.GetValue(property);
            if(value!=DependencyProperty.UnsetValue)result.Add((property,value));
        }
        return result;
    }

    internal bool IsSentenceCompletionTargetCurrent(SentenceCompletionTarget target)
    {
        if(target==null || target.Applied || !ReferenceEquals(Editor.Document,target.Document))return false;
        try
        {
            if(!SentenceCompletionParagraphBelongsToDocument(target.Paragraph,target.Document) ||
               !ReferenceEquals(target.Paragraph.Parent,target.ParagraphParent) ||
               !ReferenceEquals(target.Paragraph.PreviousBlock,target.PreviousBlock) ||
               !ReferenceEquals(target.Paragraph.NextBlock,target.NextBlock) ||
               !ReferenceEquals(FindContainingParagraph(target.Start),target.Paragraph) ||
               !ReferenceEquals(FindContainingParagraph(target.End),target.Paragraph) ||
               target.Start.CompareTo(target.Paragraph.ContentStart)<0 ||
               target.End.CompareTo(target.Paragraph.ContentEnd)>0 || target.Start.CompareTo(target.End)>0 ||
               target.Paragraph.ContentStart.GetOffsetToPosition(target.Start)!=target.StartSymbolOffset ||
               target.Paragraph.ContentStart.GetOffsetToPosition(target.End)!=target.EndSymbolOffset ||
               !string.Equals(new TextRange(target.Paragraph.ContentStart,target.Paragraph.ContentEnd).Text,
                   target.ParagraphContext,StringComparison.Ordinal) ||
               !string.Equals(new TextRange(target.Start,target.End).Text,target.OriginalSelectionText,StringComparison.Ordinal))
                return false;
            var current=CaptureSentenceCompletionInlineStates(target.Paragraph);
            return current.Count==target.InlineStates.Count && current.Zip(target.InlineStates).All(pair=>
                ReferenceEquals(pair.First.Inline,pair.Second.Inline) && ReferenceEquals(pair.First.Parent,pair.Second.Parent) &&
                string.Equals(pair.First.RunText,pair.Second.RunText,StringComparison.Ordinal) &&
                ReferenceEquals(pair.First.Child,pair.Second.Child) && Equals(pair.First.Tag,pair.Second.Tag));
        }
        catch(ArgumentException){return false;}
        catch(InvalidOperationException){return false;}
    }

    static bool SentenceCompletionParagraphBelongsToDocument(Paragraph paragraph,FlowDocument document)
    {
        object? parent=paragraph;
        while(parent is FrameworkContentElement content)
        {
            if(ReferenceEquals(parent,document))return true;
            parent=content.Parent;
        }
        return false;
    }

    internal SentenceCompletionRequest CreateSentenceCompletionRequest(SentenceCompletionTarget target,
        CompletionContextScope scope,string inflection,string instruction,CompletionStrength strength,string hint)
    {
        if(!IsSentenceCompletionTargetCurrent(target))
            throw new InvalidOperationException("The original sentence has changed. Close sentence completion and start again at the intended passage.");
        var (context,description)=CreateSentenceCompletionContext(target,scope);
        return new SentenceCompletionRequest(target.Prefix,target.Suffix,target.SelectedText,target.ParagraphContext,
            context,description,WritingModeProfiles.Get(languageSettings.WritingMode),inflection,instruction,strength,hint);
    }

    internal (string Before,string Inserted,string After) SentenceCompletionPreviewParts(SentenceCompletionTarget target,string text)
        =>(target.Prefix,NormaliseSentenceCompletionInsertion(target,text),target.Suffix);

    internal void ApplySentenceCompletionSuggestion(SentenceCompletionTarget target,string text)
    {
        if(Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. The suggestion cannot be applied.");
        if(!IsSentenceCompletionTargetCurrent(target))
            throw new InvalidOperationException("The original sentence has changed or another document is open. Close sentence completion and start again at the intended passage.");
        var insertion=NormaliseSentenceCompletionInsertion(target,text);
        Editor.BeginChange();
        try
        {
            Editor.Selection.Select(target.Start,target.End);
            Editor.Selection.Text=insertion;
            var inserted=new TextRange(Editor.Selection.Start,Editor.Selection.End);
            foreach(var (property,value) in target.Formatting)inserted.ApplyPropertyValue(property,value);
            Editor.CaretPosition=inserted.End.GetInsertionPosition(LogicalDirection.Backward);
            foreach(var (property,value) in target.Formatting)Editor.Selection.ApplyPropertyValue(property,value);
            target.Applied=true;
        }
        finally{Editor.EndChange();}
        MarkDirty(target.IsReplacement?"Phrase replaced. Ctrl+Z to undo.":"Sentence completion inserted. Ctrl+Z to undo.");
        Editor.Focus();
        RefreshFontBox();
    }

    static string NormaliseSentenceCompletionInsertion(SentenceCompletionTarget target,string text)
    {
        if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException("Choose a non-empty suggestion to insert.");
        if(text.IndexOfAny(new[]{'\r','\n','\u2028','\u2029','\0','\uFFFC'})>=0 || text.Any(ch=>char.IsControl(ch)))
            throw new InvalidOperationException("A sentence completion must be a single inline passage without line breaks or embedded objects.");
        var fragment=text.Trim();
        var before=target.StartTextOffset==0?'\0':target.ParagraphContext[target.StartTextOffset-1];
        var after=target.EndTextOffset>=target.ParagraphContext.Length?'\0':target.ParagraphContext[target.EndTextOffset];
        // The existing punctuation belongs to the document. Do not duplicate it
        // when a provider includes the same punctuation in its fragment.
        if(target.LeadingWhitespace.Length==0 && ",.;:!?".Contains(before) && fragment.Length>0 && fragment[0]==before)
            fragment=fragment[1..].TrimStart();
        if(target.TrailingWhitespace.Length==0 && ",.;:!?".Contains(after) && fragment.Length>0 && fragment[^1]==after)
            fragment=fragment[..^1].TrimEnd();
        if(fragment.Length==0)throw new InvalidOperationException("The suggestion contains no new text to insert.");
        var insertion=target.LeadingWhitespace+fragment+target.TrailingWhitespace;
        if(SentenceCompletionNeedsSpace(before,insertion[0]))insertion=" "+insertion;
        if(SentenceCompletionNeedsSpace(insertion[^1],after))insertion+=" ";
        return insertion;
    }

    static bool SentenceCompletionNeedsSpace(char left,char right)
    {
        if(left=='\0' || right=='\0' || char.IsWhiteSpace(left) || char.IsWhiteSpace(right))return false;
        if("([{“‘".Contains(left) || ",.;:!?%)]}”’'".Contains(right))return false;
        var leftWord=IsSentenceCompletionWordCharacter(left) || ".,;:!?%)]}”\"'".Contains(left);
        var rightWord=IsSentenceCompletionWordCharacter(right) || "([{“‘\"".Contains(right);
        return leftWord && rightWord;
    }

    static bool IsSentenceCompletionWordCharacter(char ch)
        =>char.IsLetterOrDigit(ch) || ch is '\'' or '’' or '-' ||
          char.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    static bool IsSentenceCompletionInsideWord(string text,int offset)
        =>offset>0 && offset<text.Length && IsSentenceCompletionWordCharacter(text[offset-1]) &&
          IsSentenceCompletionWordCharacter(text[offset]);

    static IReadOnlyList<SentenceCompletionInlineState> CaptureSentenceCompletionInlineStates(Paragraph paragraph)
    {
        var states=new System.Collections.Generic.List<SentenceCompletionInlineState>();
        void Visit(InlineCollection collection)
        {
            foreach(var inline in collection)
            {
                states.Add(new SentenceCompletionInlineState(inline,inline.Parent,(inline as Run)?.Text,
                    (inline as InlineUIContainer)?.Child,inline.Tag));
                if(inline is Span span)Visit(span.Inlines);
            }
        }
        Visit(paragraph.Inlines);
        return states;
    }

    static bool IsUnsafeSentenceCompletionPassage(string paragraphText,int startOffset,int endOffset,
        TextPointer start,TextPointer end,IReadOnlyList<SentenceCompletionInlineState> states,bool replacement)
    {
        foreach(Match match in SentenceCompletionFieldToken.Matches(paragraphText))
        {
            if(replacement?startOffset<match.Index+match.Length && endOffset>match.Index:
                startOffset>match.Index && startOffset<match.Index+match.Length)return true;
        }
        foreach(var state in states)
        {
            var inline=state.Inline;
            var field=state.Tag is string tag && tag.StartsWith(FieldTagPrefix,StringComparison.Ordinal);
            var embedded=inline is not Run and not Span and not LineBreak;
            if(!field && !embedded)continue;
            if(replacement?start.CompareTo(inline.ElementEnd)<0 && end.CompareTo(inline.ElementStart)>0:
                ReferenceEquals(start.Parent,inline) || start.CompareTo(inline.ContentStart)>0 && start.CompareTo(inline.ContentEnd)<0)return true;
        }
        return false;
    }

    static (int Start,int End) SentenceCompletionSentenceBounds(string text,int start,int end)
    {
        var sentenceStart=0;
        var sentenceEnd=text.Length;
        for(var i=0;i<text.Length;i++)
        {
            if(!IsSentenceCompletionSentenceEnd(text,i))continue;
            var boundary=i+1;
            while(boundary<text.Length && "\"'”’)]}".Contains(text[boundary]))boundary++;
            if(boundary<=start)
            {
                sentenceStart=boundary;
                while(sentenceStart<start && char.IsWhiteSpace(text[sentenceStart]))sentenceStart++;
            }
            else if(i>=end)
            {
                sentenceEnd=boundary;
                break;
            }
        }
        return (Math.Min(sentenceStart,start),Math.Max(sentenceEnd,end));
    }

    static bool IsSentenceCompletionSentenceEnd(string text,int index)
    {
        var ch=text[index];
        if(ch is not '.' and not '!' and not '?')return false;
        if(index+1<text.Length && ".!?".Contains(text[index+1]))return false;
        if(ch!='.')return true;
        if(index>0 && index+1<text.Length && char.IsDigit(text[index-1]) && char.IsDigit(text[index+1]))return false;
        if(index+1<text.Length && char.IsLetterOrDigit(text[index+1]))return false;
        var tokenStart=index;
        while(tokenStart>0 && (char.IsLetter(text[tokenStart-1]) || text[tokenStart-1]=='.'))tokenStart--;
        var token=text[tokenStart..index].Trim('.');
        if(SentenceCompletionAbbreviations.Contains(token))return false;
        if(token.Length==1 && char.IsUpper(token[0]))return false;
        var initials=token.Split('.');
        if(initials.Length>1 && initials.All(part=>part.Length==1 && char.IsLetter(part[0])))return false;
        return true;
    }

    (string Context,string Description) CreateSentenceCompletionContext(SentenceCompletionTarget target,CompletionContextScope scope)
    {
        // Paragraph scope must not read other prose. Whole-document scope takes
        // one complete plain-text snapshot, without an extra per-paragraph copy.
        if(scope==CompletionContextScope.Paragraph)
            return ("[CURRENT PARAGRAPH — source data]\n"+target.ParagraphContext,
                $"Paragraph only: {target.ParagraphContext.Length:N0} source characters from the current paragraph; no other paragraphs supplied.");
        if(scope==CompletionContextScope.WholeDocument)
        {
            var source=new TextRange(target.Document.ContentStart,target.Document.ContentEnd).Text;
            return ("[WHOLE DOCUMENT — complete plain-text source data]\n"+source,
                $"Whole document: all {source.Length:N0} plain-text characters supplied; no source text omitted.");
        }
        var paragraphs=EnumerateDocumentParagraphs().ToList();
        var current=paragraphs.FindIndex(p=>ReferenceEquals(p,target.Paragraph));
        if(current<0)throw new InvalidOperationException("The original paragraph is no longer in the document.");
        var headings=paragraphs.Select(SentenceCompletionHeadingLevel).ToArray();
        if(scope==CompletionContextScope.Section)
        {
            var first=current;
            while(first>0 && headings[first]==0)first--;
            var level=headings[first];
            var last=current+1;
            while(last<paragraphs.Count && (headings[last]==0 || level>0 && headings[last]>level))last++;
            var selected=paragraphs.Skip(first).Take(last-first)
                .Select(p=>new TextRange(p.ContentStart,p.ContentEnd).Text).ToArray();
            var source=string.Join("\n\n",selected);
            var basis=level>0?$"heading level {level}":"document start (no preceding heading)";
            return ($"[CURRENT SECTION — source data; paragraphs {first+1}–{last}, beginning at {basis}]\n"+source,
                $"Section: paragraphs {first+1}–{last}, from {basis} to the next heading of the same or higher level, or document end; {selected.Sum(t=>t.Length):N0} source characters, with paragraph separators added. No section text omitted.");
        }
        // Automatic context needs only the bounded nearby/relevance window and
        // the selected outline entries. Leave every other paragraph unread.
        var texts=new string[paragraphs.Count];
        Array.Fill(texts,string.Empty);
        var windowStart=Math.Max(0,current-98);
        var windowEnd=Math.Min(paragraphs.Count-1,current+1);
        for(var index=windowStart;index<=windowEnd;index++)
            texts[index]=index==current?target.ParagraphContext:
                new TextRange(paragraphs[index].ContentStart,paragraphs[index].ContentEnd).Text;
        foreach(var index in Enumerable.Range(0,paragraphs.Count).Where(i=>headings[i]>0)
            .OrderBy(i=>Math.Abs(i-current)).Take(12))
        {
            if(index>=windowStart && index<=windowEnd)continue;
            texts[index]=new TextRange(paragraphs[index].ContentStart,paragraphs[index].ContentEnd).Text;
        }
        return CreateAutomaticSentenceCompletionContext(texts,headings,current,target);
    }

    int SentenceCompletionHeadingLevel(Paragraph paragraph)
    {
        var role=InferParagraphStyle(paragraph,styleSets.Values);
        if(role!=null)
        {
            var known=ResolveStyle(role);
            if(known!=null)return Math.Clamp(known.OutlineLevel,0,9);
            var heading=Regex.Match(role,@"^Heading\s*([1-9])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(heading.Success)return int.Parse(heading.Groups[1].Value,CultureInfo.InvariantCulture);
        }
        return 0;
    }

    static (string Context,string Description) CreateAutomaticSentenceCompletionContext(string[] texts,int[] headings,
        int current,SentenceCompletionTarget target)
    {
        var result=new StringBuilder();
        var included=new HashSet<int>();
        var sourceCharacters=0;
        var excerpts=0;
        var nearby=0;
        void Add(int index,string label,int offset,int length)
        {
            var excerpt=offset>0 || length<texts[index].Length;
            result.Append('[').Append(label).Append(" — paragraph ").Append(index+1);
            if(excerpt)result.Append("; excerpt, characters ").Append(offset+1).Append('–').Append(offset+length)
                .Append(" of ").Append(texts[index].Length);
            else result.Append("; complete");
            result.AppendLine("; source data]");
            result.AppendLine(texts[index].Substring(offset,length)).AppendLine();
            sourceCharacters+=length;
            if(excerpt)excerpts++;
            included.Add(index);
        }
        Add(current,"CURRENT PARAGRAPH",0,texts[current].Length);
        for(var i=Math.Max(0,current-2);i<=Math.Min(texts.Length-1,current+1);i++)
        {
            if(i==current)continue;
            var length=Math.Min(texts[i].Length,1400);
            var offset=i<current?texts[i].Length-length:0;
            Add(i,i<current?"PRECEDING CONTEXT":"FOLLOWING CONTEXT",offset,length);
            nearby++;
        }

        var outline=Enumerable.Range(0,texts.Length).Where(i=>headings[i]>0)
            .OrderBy(i=>Math.Abs(i-current)).Take(12).OrderBy(i=>i).ToArray();
        if(outline.Length>0)
        {
            result.AppendLine("[SELECTED HEADING OUTLINE — source data; nearest headings, in document order]");
            foreach(var index in outline)
            {
                var length=Math.Min(texts[index].Length,120);
                result.Append("Paragraph ").Append(index+1).Append(", level ").Append(headings[index]).Append(": ")
                    .Append(texts[index].Substring(0,length));
                if(length<texts[index].Length){result.Append(" [heading excerpt]");excerpts++;}
                result.AppendLine();sourceCharacters+=length;included.Add(index);
            }
            result.AppendLine();
        }

        var keywords=SentenceCompletionWords.Matches(target.Prefix+" "+target.SelectedText+" "+target.Suffix+" "+target.ParagraphContext)
            .Cast<Match>().Select(m=>m.Value.ToLowerInvariant()).Where(w=>!SentenceCompletionStopWords.Contains(w)).ToHashSet();
        // Bound the relevance pass on the UI thread. The nearest earlier prose
        // is searched; this scope makes no claim to have reviewed the whole file.
        var relevanceStart=Math.Max(0,current-98);
        var relevanceCandidates=Enumerable.Range(relevanceStart,Math.Max(0,current-2-relevanceStart))
            .Where(i=>!included.Contains(i) && headings[i]==0).ToArray();
        var relevant=relevanceCandidates
            .Select(i=>(Index:i,Score:SentenceCompletionWords.Matches(SentenceCompletionRelevanceSample(texts[i])).Cast<Match>()
                .Select(m=>m.Value.ToLowerInvariant()).Distinct().Count(keywords.Contains)))
            .Where(item=>item.Score>=2).OrderByDescending(item=>item.Score).ThenByDescending(item=>item.Index).Take(3).ToArray();
        foreach(var item in relevant.OrderBy(item=>item.Index))
        {
            var source=texts[item.Index];
            var length=Math.Min(source.Length,900);
            var firstMatch=SentenceCompletionWords.Matches(source).Cast<Match>()
                .FirstOrDefault(m=>keywords.Contains(m.Value.ToLowerInvariant()));
            var offset=Math.Clamp((firstMatch?.Index??0)-180,0,source.Length-length);
            Add(item.Index,"EARLIER RELEVANT CONTEXT",offset,length);
        }
        return (result.ToString(),$"Automatic: current paragraph, {nearby} nearby paragraphs, {outline.Length} heading entries and {relevant.Length} earlier relevant passages; {sourceCharacters:N0} supplied source characters (including repeated heading text) from {included.Count} paragraphs. {excerpts} labelled excerpts. Relevance checked in at most 4,000 characters from each of {relevanceCandidates.Length} nearest earlier paragraphs; the remaining document is not supplied or represented as a summary.");
    }

    static string SentenceCompletionRelevanceSample(string text)
        =>text.Length<=4000?text:text[..2000]+" "+text[^2000..];
}
