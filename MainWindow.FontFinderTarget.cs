using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CtrlZed;

// Live pointers retain the original destination while the finder owns focus.
// The revision and symbol offsets also matter: WPF moves live pointers after
// edits, so their continued existence alone does not establish validity.
internal sealed class FontFinderTarget
{
    internal FlowDocument Document { get; }
    internal TextPointer Start { get; }
    internal TextPointer End { get; }
    internal bool IsCaret { get; }
    internal string SampleText { get; }
    internal string FamilySource { get; }
    internal bool Bold { get; }
    internal bool Italic { get; }
    internal long Version { get; }
    internal long FormatVersion => Version;
    internal string SetName { get; }
    internal StyleSetStore.StyleSet StyleSet { get; }
    internal string Context => SampleText;
    internal string ContextDisplay => IsCaret ? "Caret — future typing only" : "Original selection";
    internal int StartSymbolOffset { get; }
    internal int EndSymbolOffset { get; }
    internal TextPointer SampleStart { get; }
    internal TextPointer SampleEnd { get; }
    internal IReadOnlyList<(DependencyProperty Property, object Value)> Formatting { get; }
    internal bool Applied { get; set; }
    internal bool Invalidated { get; set; }

    internal FontFinderTarget(FlowDocument document, TextPointer start, TextPointer end,
        bool isCaret, string sampleText, TextPointer sampleStart, TextPointer sampleEnd,
        string familySource, bool bold, bool italic, long version, string setName,
        StyleSetStore.StyleSet styleSet,
        IReadOnlyList<(DependencyProperty Property, object Value)> formatting)
    {
        Document = document; Start = start; End = end; IsCaret = isCaret;
        SampleText = sampleText; SampleStart = sampleStart; SampleEnd = sampleEnd;
        FamilySource = familySource; Bold = bold; Italic = italic; Version = version;
        SetName = setName; StyleSet = styleSet; Formatting = formatting;
        StartSymbolOffset = document.ContentStart.GetOffsetToPosition(start);
        EndSymbolOffset = document.ContentStart.GetOffsetToPosition(end);
    }
}

public partial class MainWindow
{
    const int FontFinderMaximumSample = 1000;
    bool fontFinderTargetTrackingInstalled;
    long fontFinderDocumentVersion;
    FontFinderCaretChoice? fontFinderCaretChoice;
    FlowDocument? fontFinderTypingBridgeDocument;
    FontFinderInsertionProof? fontFinderInsertionProof;
    bool fontFinderApplyingInsertionFormat;

    sealed class FontFinderCaretChoice
    {
        internal FontFinderTarget Target { get; }
        internal long Version { get; }
        internal FontFamily Family { get; }
        internal FontFinderMatch Match { get; }
        internal IReadOnlyList<(DependencyProperty Property, object Value)> ObservedFormatting { get; }
        internal FontFinderCaretChoice(FontFinderTarget target, long version, FontFamily family,
            FontFinderMatch match, IReadOnlyList<(DependencyProperty Property, object Value)> observedFormatting)
        { Target = target; Version = version; Family = family; Match = match; ObservedFormatting = observedFormatting; }
    }

    sealed class FontFinderInsertionProof
    {
        internal FlowDocument Document { get; }
        internal TextPointer Start { get; }
        internal TextPointer End { get; }
        internal string Text { get; }
        internal long Version { get; }
        internal IReadOnlyList<(DependencyProperty Property, object Value)> Formatting { get; }
        internal FontFinderInsertionProof(FlowDocument document, TextPointer start, TextPointer end,
            string text, long version, IReadOnlyList<(DependencyProperty Property, object Value)> formatting)
        { Document = document; Start = start; End = end; Text = text; Version = version; Formatting = formatting; }
    }

    internal void InstallFontFinderTargetTracking()
    {
        if (fontFinderTargetTrackingInstalled) return;
        fontFinderTargetTrackingInstalled = true;
        Editor.TextChanged += FontFinderTrackedTextChanged;
        Editor.PreviewTextInput += FontFinderPreviewTextInput;
        Editor.SelectionChanged += (_, _) =>
        {
            if (fontFinderCaretChoice is { } choice && !FontFinderCaretChoiceCurrent(choice))
                fontFinderCaretChoice = null;
        };
        // Author navigation or formatting commands discard the old font
        // choice. A later insertion proof always reads the author's live
        // typing state at its actual destination, rather than restoring it.
        PreviewMouseDown += (_, _) => fontFinderCaretChoice = null;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 ||
                e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or
                    Key.PageDown or Key.Back or Key.Delete or Key.Enter or Key.Tab or Key.Escape)
            {
                fontFinderCaretChoice = null;
                fontFinderInsertionProof = null;
            }
        };
    }

    internal FontFinderTarget CaptureFontFinderTarget()
    {
        EnsureFontFinderEditable();
        InstallFontFinderTargetTracking();
        fontFinderCaretChoice = null;
        fontFinderTypingBridgeDocument = null;
        fontFinderInsertionProof = null;
        var selection = Editor.Selection;
        var isCaret = selection.IsEmpty;
        var start = selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward)!;
        var end = selection.End.GetPositionAtOffset(0, LogicalDirection.Forward)!;
        var formatting = CaptureFontFinderFormatting();
        object? Value(DependencyProperty property)
            => formatting.FirstOrDefault(value => value.Property == property).Value;
        var family = Value(TextElement.FontFamilyProperty) as FontFamily;
        var bold = Value(TextElement.FontWeightProperty) is FontWeight weight && weight >= FontWeights.SemiBold;
        var italic = Value(TextElement.FontStyleProperty) is FontStyle style && style != FontStyles.Normal;

        // The local sample is bounded in WPF symbols before reading its text.
        // Even selecting the entire manuscript does not copy it for a preview.
        var sampleStart = start;
        var sampleLimit = end;
        if (isCaret && FindContainingParagraph(start) is Paragraph paragraph)
        {
            var before = Math.Min(200, paragraph.ContentStart.GetOffsetToPosition(start));
            sampleStart = start.GetPositionAtOffset(-before, LogicalDirection.Backward)!;
            sampleLimit = paragraph.ContentEnd;
        }
        var symbols = Math.Min(FontFinderMaximumSample, sampleStart.GetOffsetToPosition(sampleLimit));
        var sampleEnd = sampleStart.GetPositionAtOffset(symbols, LogicalDirection.Forward)!;
        var sample = new TextRange(sampleStart, sampleEnd).Text;
        if (sample.Length > FontFinderMaximumSample) sample = sample[..FontFinderMaximumSample];
        var set = ActiveParagraphStyleSet;
        return new FontFinderTarget(Editor.Document, start, end, isCaret, sample, sampleStart, sampleEnd,
            family?.Source ?? Editor.FontFamily.Source, bold, italic, fontFinderDocumentVersion,
            activeStyleSet, set, formatting);
    }

    IReadOnlyList<(DependencyProperty Property, object Value)> CaptureFontFinderFormatting()
    {
        var selection = Editor.Selection;
        Run? firstRun = null;
        if (!selection.IsEmpty)
        {
            for (var position = selection.Start; position != null && position.CompareTo(selection.End) < 0;
                position = position.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text &&
                    position.Parent is Run run)
                {
                    firstRun = run;
                    break;
                }
            }
        }
        var values = new List<(DependencyProperty, object)>();
        foreach (var property in new[]
        {
            TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontStyleProperty,
            TextElement.FontWeightProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty,
            TextElement.BackgroundProperty, Inline.TextDecorationsProperty, Inline.BaselineAlignmentProperty
        })
        {
            // Selection.GetPropertyValue includes WPF's pending typing format.
            // A neighbouring run must never override that value at a caret.
            var value = selection.GetPropertyValue(property);
            if (value == DependencyProperty.UnsetValue && firstRun != null) value = firstRun.GetValue(property);
            if (value == DependencyProperty.UnsetValue && selection.IsEmpty)
                value = new TextRange(selection.Start, selection.Start).GetPropertyValue(property);
            if (value != DependencyProperty.UnsetValue) values.Add((property, value));
        }
        return values.AsReadOnly();
    }

    internal bool IsFontFinderTargetCurrent(FontFinderTarget target)
    {
        if (target == null || target.Applied || target.Invalidated) return false;
        bool Invalidate()
        {
            target.Invalidated = true;
            return false;
        }
        if (Editor.IsReadOnly || !Editor.IsEnabled || !ReferenceEquals(Editor.Document, target.Document) ||
            target.Version != fontFinderDocumentVersion)
            return Invalidate();
        try
        {
            if (target.Start.CompareTo(target.Document.ContentStart) < 0 ||
                target.End.CompareTo(target.Document.ContentEnd) > 0 || target.Start.CompareTo(target.End) > 0 ||
                target.Document.ContentStart.GetOffsetToPosition(target.Start) != target.StartSymbolOffset ||
                target.Document.ContentStart.GetOffsetToPosition(target.End) != target.EndSymbolOffset)
                return Invalidate();
            var sample = new TextRange(target.SampleStart, target.SampleEnd).Text;
            if (sample.Length > FontFinderMaximumSample) sample = sample[..FontFinderMaximumSample];
            if (!string.Equals(sample, target.SampleText, StringComparison.Ordinal)) return Invalidate();
            // A pending caret format can change without changing any document
            // text. Check it while the editor owns keyboard focus. The finder
            // owns a snapshot while its query/preview has focus: WPF can reset
            // pending properties during that ordinary focus transfer, and the
            // apply path restores the captured format before changing the font.
            if (target.IsCaret && Editor.IsKeyboardFocusWithin && Editor.Selection.IsEmpty &&
                Editor.Selection.Start.CompareTo(target.Start) == 0)
            {
                foreach (var (property, value) in target.Formatting)
                    if (!Equals(Editor.Selection.GetPropertyValue(property), value)) return Invalidate();
            }
            return true;
        }
        catch (ArgumentException) { return Invalidate(); }
        catch (InvalidOperationException) { return Invalidate(); }
    }

    internal void ApplyFontFinderSelection(FontFinderTarget target, FontFinderMatch match,
        IReadOnlyList<FontFinderFamily> catalog)
    {
        EnsureFontFinderEditable();
        if (!IsFontFinderTargetCurrent(target))
            throw new InvalidOperationException("The original selection or caret has changed, or another document is open. Close FontFinder and open it again at the intended text.");
        var family = FontFinderInstalledFamily(match, catalog);
        fontFinderCaretChoice = null;
        fontFinderTypingBridgeDocument = null;
        fontFinderInsertionProof = null;
        Activate();
        Editor.Focus();
        Editor.BeginChange();
        try
        {
            if (Editor.Selection.Start.CompareTo(target.Start) != 0 ||
                Editor.Selection.End.CompareTo(target.End) != 0)
                Editor.Selection.Select(target.Start, target.End);
            if (!target.IsCaret)
                ApplyFontFinderFont(Editor.Selection, family, match);
            else if (FontFinderCaretHasWordBoundary(target.Start))
            {
                // WPF's empty-selection setter formats the existing word at
                // an interior caret. Use its pending format only where this
                // conservative boundary check rules that behaviour out.
                foreach (var (property, value) in target.Formatting)
                    Editor.Selection.ApplyPropertyValue(property, value);
                ApplyFontFinderFont(Editor.Selection, family, match);
            }
            target.Applied = true;
        }
        finally { Editor.EndChange(); }
        if (target.IsCaret)
        {
            fontFinderTypingBridgeDocument = Editor.Document;
            fontFinderCaretChoice = new FontFinderCaretChoice(target, fontFinderDocumentVersion, family,
                match, CaptureFontFinderFormatting());
        }
        RefreshFontBox();
        RefreshParagraphStyleBox();
        MarkDirty(target.IsCaret ? $"FontFinder: {family.Source} selected for future typing at the caret."
            : $"FontFinder: {family.Source} applied to the selection. Ctrl+Z to undo.");
    }

    static bool FontFinderCaretHasWordBoundary(TextPointer position)
    {
        var before = position.GetNextInsertionPosition(LogicalDirection.Backward);
        var after = position.GetNextInsertionPosition(LogicalDirection.Forward);
        if (before == null || after == null || !ReferenceEquals(before.Paragraph, position.Paragraph) ||
            !ReferenceEquals(after.Paragraph, position.Paragraph)) return true;
        var left = new TextRange(before, position).Text;
        var right = new TextRange(position, after).Text;
        return left.Length == 0 || right.Length == 0 ||
            left.Any(char.IsWhiteSpace) || right.Any(char.IsWhiteSpace);
    }

    bool FontFinderCaretChoiceCurrent(FontFinderCaretChoice choice)
    {
        if (Editor.IsReadOnly || !Editor.IsEnabled || !Editor.Selection.IsEmpty ||
            !ReferenceEquals(Editor.Document, choice.Target.Document) || choice.Version != fontFinderDocumentVersion)
            return false;
        try
        {
            return choice.Target.Document.ContentStart.GetOffsetToPosition(choice.Target.Start) == choice.Target.StartSymbolOffset &&
                Editor.Selection.Start.CompareTo(choice.Target.Start) == 0;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    bool FontFinderTypingFormatMatches(IReadOnlyList<(DependencyProperty Property, object Value)> formatting)
        => formatting.All(item => Equals(Editor.Selection.GetPropertyValue(item.Property), item.Value));

    void FontFinderPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Native rich-text input can defer several text events. Keep the first
        // proof until its actual insertion arrives; later characters inherit
        // the first character's real formatting after the bridge is consumed.
        if (fontFinderInsertionProof != null || fontFinderApplyingInsertionFormat ||
            !ReferenceEquals(fontFinderTypingBridgeDocument, Editor.Document) ||
            Editor.IsReadOnly || !Editor.IsEnabled || !Editor.IsKeyboardFocusWithin ||
            !Editor.Selection.IsEmpty || e.Handled || string.IsNullOrEmpty(e.Text) ||
            e.Text.Length > 4096 || e.Text.Any(char.IsControl))
            return;
        var formatting = CaptureFontFinderFormatting().ToDictionary(item => item.Property, item => item.Value);
        var choice = fontFinderCaretChoice;
        if (choice != null && FontFinderCaretChoiceCurrent(choice) &&
            FontFinderTypingFormatMatches(choice.ObservedFormatting))
        {
            formatting[TextElement.FontFamilyProperty] = choice.Family;
            if (choice.Match.Bold is bool bold)
                formatting[TextElement.FontWeightProperty] = bold ? FontWeights.Bold : FontWeights.Normal;
            if (choice.Match.Italic is bool italic)
                formatting[TextElement.FontStyleProperty] = italic ? FontStyles.Italic : FontStyles.Normal;
        }
        fontFinderCaretChoice = null;
        // Keep outward pointers separate. Constructing an empty TextRange now
        // would normalise them into one pointer and lose insertion gravity.
        var start = Editor.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward)!;
        var end = Editor.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Forward)!;
        fontFinderInsertionProof = new FontFinderInsertionProof(Editor.Document, start, end, e.Text,
            fontFinderDocumentVersion, formatting.Select(item => (item.Key, item.Value)).ToArray());
    }

    void FontFinderTrackedTextChanged(object sender, TextChangedEventArgs e)
    {
        fontFinderDocumentVersion++;
        if (fontFinderApplyingInsertionFormat) return;
        var proof = fontFinderInsertionProof;
        fontFinderInsertionProof = null;
        fontFinderCaretChoice = null;
        fontFinderTypingBridgeDocument = null;
        if (proof == null || !ReferenceEquals(Editor.Document, proof.Document) ||
            proof.Version + 1 != fontFinderDocumentVersion || Editor.IsReadOnly || !Editor.IsEnabled ||
            !Editor.Selection.IsEmpty || !Editor.IsKeyboardFocusWithin)
            return;
        try
        {
            if (proof.Start.CompareTo(proof.End) >= 0 ||
                proof.Start.CompareTo(Editor.Selection.Start) >= 0 ||
                !string.Equals(new TextRange(proof.Start, proof.End).Text, proof.Text, StringComparison.Ordinal) ||
                new TextRange(proof.End, Editor.Selection.Start).Text.Length != 0)
                return;
            var inserted = new TextRange(proof.Start, proof.End);
            if (proof.Formatting.All(item => Equals(inserted.GetPropertyValue(item.Property), item.Value))) return;
            // WPF marks inserted text with the keyboard's language before it
            // applies pending character properties. A language mismatch clears
            // those pending properties. Restore only this proven new span,
            // while the native typing Undo unit is still open. The document's
            // language and the user's keyboard layout remain their own values.
            fontFinderApplyingInsertionFormat = true;
            try
            {
                Editor.BeginChange();
                try
                {
                    foreach (var (property, value) in proof.Formatting)
                        inserted.ApplyPropertyValue(property, value);
                }
                finally { Editor.EndChange(); }
            }
            finally { fontFinderApplyingInsertionFormat = false; }
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    internal IReadOnlyList<string> FontFinderStyleNames()
        => StyleSetStore.StyleNames(ActiveParagraphStyleSet);

    internal void ApplyFontFinderStyle(string setName, string styleName, FontFinderMatch match,
        IReadOnlyList<FontFinderFamily> catalog)
        => ApplyFontFinderStyle(CaptureFontFinderTarget(), setName, styleName, match, catalog);

    internal void ApplyFontFinderStyle(FontFinderTarget target, string setName, string styleName,
        FontFinderMatch match, IReadOnlyList<FontFinderFamily> catalog)
    {
        EnsureFontFinderEditable();
        if (!IsFontFinderTargetCurrent(target))
            throw new InvalidOperationException("The original document or formatting has changed. Close FontFinder and open it again before updating a style.");
        if (!string.Equals(setName, target.SetName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(activeStyleSet, target.SetName, StringComparison.OrdinalIgnoreCase) ||
            !styleSets.TryGetValue(setName, out var current) || !ReferenceEquals(current, target.StyleSet))
            throw new InvalidOperationException("The active style set has changed. Close FontFinder and open it again to choose a style from the current set.");
        var name = StyleSetStore.ResolveStyleName(current, styleName);
        if (name == null)
            throw new InvalidOperationException("That style has been deleted or is no longer in the active style set. Open FontFinder again and choose an existing style.");
        var family = FontFinderInstalledFamily(match, catalog);
        var original = current.Styles[name];
        // Determine assignments against the old definition. Inferring them
        // after changing its font would miss untagged matching paragraphs.
        var paragraphs = EnumerateDocumentParagraphs()
            .Where(paragraph => FontFinderParagraphUsesStyle(paragraph, current, original)).ToList();
        var updatedSet = StyleSetStore.Clone(current);
        var updatedStyle = StyleSetStore.Clone(original);
        updatedStyle.FontFamily = family.Source;
        if (match.Bold is bool styleBold) updatedStyle.Bold = styleBold;
        if (match.Italic is bool styleItalic) updatedStyle.Italic = styleItalic;
        updatedSet.Styles[name] = updatedStyle;
        var updatedSets = new Dictionary<string, StyleSetStore.StyleSet>(styleSets, StringComparer.OrdinalIgnoreCase)
        {
            [setName] = updatedSet
        };
        // Match the style editor's Save Set semantics: a failed save must not
        // leave the live definitions changed. Saved definitions are separate
        // from WPF's native formatting Undo history, as the finder explains.
        StyleSetStore.Save(updatedSets);
        styleSets[setName] = updatedSet;
        Editor.BeginChange();
        try
        {
            foreach (var paragraph in paragraphs)
            {
                paragraph.FontFamily = family;
                if (match.Bold is bool paragraphBold) paragraph.FontWeight = paragraphBold ? FontWeights.Bold : FontWeights.Normal;
                if (match.Italic is bool paragraphItalic) paragraph.FontStyle = paragraphItalic ? FontStyles.Italic : FontStyles.Normal;
                ApplyFontFinderFont(new TextRange(paragraph.ContentStart, paragraph.ContentEnd), family, match);
            }
            target.Applied = true;
        }
        finally { Editor.EndChange(); }
        PrepareDocumentStylesForInterop();
        RefreshStyleSetBox();
        RefreshParagraphStyleBox();
        RefreshFontBox();
        SaveAppSettings();
        MarkDirty($"FontFinder: saved {name} in {setName}; updated {paragraphs.Count} assigned paragraph{(paragraphs.Count == 1 ? "" : "s")}.");
    }

    bool FontFinderParagraphUsesStyle(Paragraph paragraph, StyleSetStore.StyleSet set,
        StyleSetStore.TextStyle wanted)
    {
        if (paragraph.Tag is string tag && tag.StartsWith(ParagraphStyleTagPrefix, StringComparison.Ordinal))
        {
            var identity = tag[ParagraphStyleTagPrefix.Length..];
            if (identity.StartsWith('#')) identity = identity[1..];
            return StyleSetStore.ResolveStyle(set, identity) is { } assigned &&
                string.Equals(assigned.Id, wanted.Id, StringComparison.OrdinalIgnoreCase);
        }
        var inferred = InferParagraphStyle(paragraph, styleSets.Values);
        return inferred != null && StyleSetStore.ResolveStyle(set, inferred) is { } resolved &&
            string.Equals(resolved.Id, wanted.Id, StringComparison.OrdinalIgnoreCase);
    }

    static void ApplyFontFinderFont(TextRange range, FontFamily family, FontFinderMatch match)
    {
        range.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        if (match.Bold is bool bold)
            range.ApplyPropertyValue(TextElement.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
        if (match.Italic is bool italic)
            range.ApplyPropertyValue(TextElement.FontStyleProperty, italic ? FontStyles.Italic : FontStyles.Normal);
    }

    static FontFamily FontFinderInstalledFamily(FontFinderMatch match, IReadOnlyList<FontFinderFamily> catalog)
    {
        if (match == null || catalog == null || string.IsNullOrWhiteSpace(match.Source))
            throw new InvalidOperationException("Choose an installed font from the FontFinder results.");
        var allowed = catalog.FirstOrDefault(font =>
            string.Equals(font.Source, match.Source, StringComparison.OrdinalIgnoreCase));
        var installed = allowed == null ? null : Fonts.SystemFontFamilies.FirstOrDefault(font =>
            string.Equals(font.Source, allowed.Source, StringComparison.OrdinalIgnoreCase));
        if (installed == null)
            throw new InvalidOperationException("That font is not in the current installed font catalogue. Refresh FontFinder and choose an installed font.");
        return installed;
    }

    void EnsureFontFinderEditable()
    {
        if (Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only or editing is disabled. FontFinder cannot apply a font or change a style.");
    }
}
