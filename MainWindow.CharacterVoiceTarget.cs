using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;

namespace CtrlZed;

// The preview owns one exact destination. Live pointers alone would silently
// follow intervening edits, so retain the document revision and rich snapshot.
internal sealed class CharacterVoiceTarget
{
    internal FlowDocument Document { get; }
    internal TextPointer Start { get; }
    internal TextPointer End { get; }
    internal string OriginalText { get; }
    internal bool IsReplacement { get; }
    internal bool Applied { get; set; }
    internal IReadOnlyList<(DependencyProperty Property, object Value)> Formatting { get; }
    internal long Version { get; }
    internal int StartSymbolOffset { get; }
    internal int EndSymbolOffset { get; }
    internal string RichSnapshot { get; }
    internal IReadOnlyList<CharacterVoiceParagraphState> Paragraphs { get; }
    internal bool RepeatStylePageBreak { get; }

    internal CharacterVoiceTarget(FlowDocument document, TextPointer start, TextPointer end,
        string originalText, bool replacement, long version, string richSnapshot,
        IReadOnlyList<(DependencyProperty Property, object Value)> formatting,
        IReadOnlyList<CharacterVoiceParagraphState> paragraphs, bool repeatStylePageBreak)
    {
        Document = document; Start = start; End = end; OriginalText = originalText;
        IsReplacement = replacement; Version = version; RichSnapshot = richSnapshot;
        Formatting = formatting; Paragraphs = paragraphs; RepeatStylePageBreak = repeatStylePageBreak;
        StartSymbolOffset = document.ContentStart.GetOffsetToPosition(start);
        EndSymbolOffset = document.ContentStart.GetOffsetToPosition(end);
    }
}

internal sealed record CharacterVoiceParagraphState(Paragraph Paragraph, object? Tag,
    IReadOnlyList<(DependencyProperty Property, object Value)> Formatting);

public partial class MainWindow
{
    internal const int CharacterVoiceMaximumSelection = CharacterVoiceService.MaximumSourceLength;
    internal const int CharacterVoiceMaximumResult = CharacterVoiceService.MaximumRewriteLength;
    static readonly DependencyProperty[] CharacterVoiceParagraphProperties =
    {
        Block.MarginProperty, Block.PaddingProperty, Block.BorderBrushProperty, Block.BorderThicknessProperty,
        Block.TextAlignmentProperty, Block.LineHeightProperty, Block.LineStackingStrategyProperty,
        Block.BreakPageBeforeProperty, Block.BreakColumnBeforeProperty, Block.ClearFloatersProperty,
        Paragraph.TextIndentProperty, Paragraph.KeepTogetherProperty, Paragraph.KeepWithNextProperty,
        Block.FlowDirectionProperty
    };

    internal CharacterVoiceTarget CaptureCharacterVoiceTarget()
    {
        if (Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. Character Voice cannot change it.");
        InstallFontFinderTargetTracking();
        var replacement = !Editor.Selection.IsEmpty;
        var start = Editor.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward)!;
        var end = Editor.Selection.End.GetPositionAtOffset(0, LogicalDirection.Forward)!;
        // Bound symbol traversal as well as the plain prose: thousands of empty
        // nested spans are not an ordinary passage for the writing assistant.
        if (start.GetOffsetToPosition(end) > 160000)
            throw new InvalidOperationException("Choose a smaller ordinary-text passage for Character Voice.");
        var original = new TextRange(start, end).Text;
        if (replacement && (string.IsNullOrWhiteSpace(original) || original.Length > CharacterVoiceMaximumSelection))
            throw new InvalidOperationException($"Select ordinary text of up to {CharacterVoiceMaximumSelection:N0} characters, or paste text in Character Voice.");
        if (original.IndexOfAny(new[] { '\0', '\uFFFC' }) >= 0)
            throw new InvalidOperationException("Choose ordinary text without embedded objects or field codes.");

        var paragraphs = CharacterVoiceParagraphs(start, end).ToArray();
        if (paragraphs.Length == 0)
            throw new InvalidOperationException("Place the caret in ordinary text, or select the prose to rewrite.");
        ValidateCharacterVoicePassage(start, end, paragraphs, replacement);
        if (!replacement)
        {
            var paragraph = paragraphs[0];
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
            var offset = new TextRange(paragraph.ContentStart, start).Text.Length;
            if (IsSentenceCompletionInsideWord(text, offset))
                throw new InvalidOperationException("Place the caret between words before inserting a rewritten passage.");
        }

        var role = InferParagraphStyle(paragraphs[0], styleSets.Values) ?? "Normal";
        return new CharacterVoiceTarget(Editor.Document, start, end, original, replacement,
            fontFinderDocumentVersion, CharacterVoiceRichSnapshot(start, end), CaptureFontFinderFormatting(),
            paragraphs.Select(CaptureCharacterVoiceParagraph).ToArray(), ResolveStyle(role)?.BreakPageBefore == true);
    }

    internal bool IsCharacterVoiceTargetCurrent(CharacterVoiceTarget target)
    {
        if (target == null || target.Applied || Editor.IsReadOnly || !Editor.IsEnabled ||
            !ReferenceEquals(Editor.Document, target.Document) || target.Version != fontFinderDocumentVersion)
            return false;
        try
        {
            if (target.Start.CompareTo(target.Document.ContentStart) < 0 ||
                target.End.CompareTo(target.Document.ContentEnd) > 0 || target.Start.CompareTo(target.End) > 0 ||
                target.Document.ContentStart.GetOffsetToPosition(target.Start) != target.StartSymbolOffset ||
                target.Document.ContentStart.GetOffsetToPosition(target.End) != target.EndSymbolOffset ||
                !string.Equals(new TextRange(target.Start, target.End).Text, target.OriginalText, StringComparison.Ordinal) ||
                !string.Equals(CharacterVoiceRichSnapshot(target.Start, target.End), target.RichSnapshot, StringComparison.Ordinal))
                return false;
            var currentParagraphs = CharacterVoiceParagraphs(target.Start, target.End).ToArray();
            if (currentParagraphs.Length != target.Paragraphs.Count) return false;
            for (var i = 0; i < currentParagraphs.Length; i++)
            {
                var original = target.Paragraphs[i];
                if (!ReferenceEquals(currentParagraphs[i], original.Paragraph) ||
                    !Equals(currentParagraphs[i].Tag, original.Tag) || original.Formatting.Any(value =>
                        !Equals(currentParagraphs[i].GetValue(value.Property), value.Value)))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        { return false; }
    }

    internal void ApplyCharacterVoiceRewrite(CharacterVoiceTarget target, string text)
    {
        if (Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only. The rewrite cannot be applied.");
        if (!IsCharacterVoiceTargetCurrent(target))
            throw new InvalidOperationException("The original passage or document has changed. Use the intended selection or caret again before inserting.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > CharacterVoiceMaximumResult ||
            text.Any(c => (char.IsControl(c) && c is not ('\r' or '\n' or '\t')) || c == '\uFFFC'))
            throw new InvalidOperationException($"Use non-empty ordinary text of up to {CharacterVoiceMaximumResult:N0} characters, without embedded objects.");
        var insertion = text;
        if (target.IsReplacement)
        {
            // AI responses and the editable preview can omit the selection's
            // outer whitespace. That whitespace belongs to the document: a
            // selected space separates neighbouring words, and a final CRLF
            // can be the boundary preserving the following paragraph.
            var original = target.OriginalText;
            var leading = 0;
            while (leading < original.Length && char.IsWhiteSpace(original[leading])) leading++;
            var trailing = original.Length;
            while (trailing > leading && char.IsWhiteSpace(original[trailing - 1])) trailing--;
            insertion = original[..leading] + text.Trim() + original[trailing..];
        }
        // Pasted-source insertion at an empty caret remains exact apart from
        // WPF line-ending normalisation; no implicit boundary spaces are added.
        insertion = insertion.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n');

        Editor.BeginChange();
        try
        {
            Editor.Selection.Select(target.Start, target.End);
            Editor.Selection.Text = "";
            // The text setter turns newlines into spaces inside a Hyperlink.
            // Existing plain-paste splitting preserves both untouched sides.
            PreparePlainPasteHyperlinkBoundary(Editor, target.Formatting);
            Editor.Selection.Text = insertion;
            var inserted = new TextRange(Editor.Selection.Start, Editor.Selection.End);
            var destinationParagraphs = CharacterVoiceParagraphs(inserted.Start, inserted.End).ToArray();
            for (var i = 0; i < destinationParagraphs.Length; i++)
            {
                var paragraph = destinationParagraphs[i];
                var previous = target.Paragraphs.FirstOrDefault(value => ReferenceEquals(value.Paragraph, paragraph));
                var state = previous ?? target.Paragraphs[0];
                foreach (var (property, value) in state.Formatting)
                {
                    // A manual break is one boundary. Copying it into every
                    // rewritten paragraph would create unintended blank pages.
                    if (property == Block.BreakPageBeforeProperty && previous == null && i > 0 && !target.RepeatStylePageBreak)
                        paragraph.SetValue(property, false);
                    else paragraph.SetValue(property, value);
                }
                paragraph.Tag = state.Tag;
            }
            // Paragraph layout can clear the pending character format, so it
            // precedes foreground/font/emphasis application to inserted text.
            foreach (var (property, value) in target.Formatting) inserted.ApplyPropertyValue(property, value);
            Editor.CaretPosition = inserted.End.GetInsertionPosition(LogicalDirection.Backward);
            foreach (var (property, value) in target.Formatting) Editor.Selection.ApplyPropertyValue(property, value);
            target.Applied = true;
        }
        finally { Editor.EndChange(); }
        MarkDirty(target.IsReplacement ? "Character voice replaced the passage. Ctrl+Z to undo." :
            "Character voice inserted the passage. Ctrl+Z to undo.");
        UpdateDocumentStats(); Editor.Focus(); RefreshFontBox();
    }

    IEnumerable<Paragraph> CharacterVoiceParagraphs(TextPointer start, TextPointer end)
    {
        if (start.CompareTo(end) == 0)
        {
            if (FindContainingParagraph(start) is { } paragraph) yield return paragraph;
            yield break;
        }
        foreach (var paragraph in EnumerateDocumentParagraphs())
            if (paragraph.ContentEnd.CompareTo(start) > 0 && paragraph.ContentStart.CompareTo(end) < 0)
                yield return paragraph;
    }

    static CharacterVoiceParagraphState CaptureCharacterVoiceParagraph(Paragraph paragraph)
        => new(paragraph, paragraph.Tag, CharacterVoiceParagraphProperties.Select(property =>
            (property, paragraph.GetValue(property))).ToArray());

    static string CharacterVoiceRichSnapshot(TextPointer start, TextPointer end)
    {
        using var memory = new MemoryStream();
        new TextRange(start, end).Save(memory, DataFormats.Xaml);
        return Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))));
    }

    static void ValidateCharacterVoicePassage(TextPointer start, TextPointer end,
        IReadOnlyList<Paragraph> paragraphs, bool replacement)
    {
        static T? Ancestor<T>(TextPointer pointer) where T : FrameworkContentElement
        {
            for (var current = pointer.Parent as FrameworkContentElement; current != null;
                 current = current.Parent as FrameworkContentElement)
                if (current is T match) return match;
            return null;
        }
        var cell = Ancestor<TableCell>(start);
        if (!ReferenceEquals(cell, Ancestor<TableCell>(end)))
            throw new InvalidOperationException("Choose prose within a single table cell, or outside the table.");
        for (var position = start; position != null && position.CompareTo(end) <= 0;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (!ReferenceEquals(cell, Ancestor<TableCell>(position)))
                throw new InvalidOperationException("Select prose without crossing a table or table-cell boundary.");
            for (var parent = position.Parent as FrameworkContentElement; parent != null;
                 parent = parent.Parent as FrameworkContentElement)
            {
                if (parent is InlineUIContainer or BlockUIContainer or Figure or Floater ||
                    parent.Tag is string tag && tag.StartsWith(FieldTagPrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("Choose ordinary text outside field codes and embedded objects.");
            }
            if (position.CompareTo(end) == 0) break;
            if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement ||
                position.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer or BlockUIContainer or Figure or Floater or Table)
                throw new InvalidOperationException("Choose ordinary text without pictures, tables or embedded objects.");
        }
        foreach (var paragraph in paragraphs)
        {
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
            var from = start.CompareTo(paragraph.ContentStart) <= 0 ? 0 : new TextRange(paragraph.ContentStart, start).Text.Length;
            var to = end.CompareTo(paragraph.ContentEnd) >= 0 ? text.Length : new TextRange(paragraph.ContentStart, end).Text.Length;
            foreach (System.Text.RegularExpressions.Match match in SentenceCompletionFieldToken.Matches(text))
                if (replacement ? from < match.Index + match.Length && to > match.Index :
                    from > match.Index && from < match.Index + match.Length)
                    throw new InvalidOperationException("Choose ordinary text outside field codes and displayed fields.");
        }
    }
}
