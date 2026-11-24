using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    bool textColourInstalled;
    static readonly Regex TextColourHex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

    // Tests exercise the real command and focus transitions without leaving a
    // native modal palette awaiting interaction on the Windows build runner.
    internal static Func<MainWindow, string?, string?>? TextColourPickerOverride { get; set; }

    internal void InstallTextColour()
    {
        if (textColourInstalled) return;
        textColourInstalled = true;
        InstallFontFinderTargetTracking();
        void Update()
        {
            var editable = !Editor.IsReadOnly && Editor.IsEnabled;
            if (FindName("TextColourMenu") is UIElement menu) menu.IsEnabled = editable;
            if (FindName("TextColourButton") is UIElement button) button.IsEnabled = editable;
        }
        EventHandler readOnlyChanged = (_, _) => Update();
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty, typeof(RichTextBox));
        descriptor?.AddValueChanged(Editor, readOnlyChanged);
        Editor.IsEnabledChanged += (_, _) => Update();
        Closed += (_, _) => descriptor?.RemoveValueChanged(Editor, readOnlyChanged);
        Update();
    }

    internal FontFinderTarget CaptureTextColourTarget()
    {
        EnsureTextColourEditable();
        return CaptureCharacterFormattingTarget(preservePendingChoice: true);
    }

    void TextColour_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = CaptureTextColourTarget();
            var initial = TextColourInitial(target);
            var selected = TextColourPickerOverride is { } picker
                ? picker(this, initial)
                : NativeColourDialog.Show(this, initial);
            if (selected != null) ApplyTextColour(target, selected);
        }
        catch (Exception error)
        {
            Status.Text = "Text colour could not be applied: " + error.Message;
        }
    }

    internal static string TextColourInitial(FontFinderTarget target)
    {
        var value = target.PendingFormatting.LastOrDefault(item => item.Property == TextElement.ForegroundProperty).Value
            ?? target.Formatting.FirstOrDefault(item => item.Property == TextElement.ForegroundProperty).Value;
        var colour = value is SolidColorBrush solid ? solid.Color : Colors.Black;
        return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    }

    internal void ApplyTextColour(FontFinderTarget target, string hex)
    {
        if (hex == null || !TextColourHex.IsMatch(hex))
            throw new ArgumentException("Choose a colour in #RRGGBB form.", nameof(hex));
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        EnsureTextColourEditable();
        if (!IsFontFinderTargetCurrent(target))
            throw new InvalidOperationException("The original text or caret has changed. Choose Text Colour again at the intended text.");

        var requested = target.PendingFormatting.ToDictionary(item => item.Property, item => item.Value);
        requested[TextElement.ForegroundProperty] = brush;
        fontFinderCaretChoice = null;
        fontFinderTypingBridgeDocument = null;
        fontFinderInsertionProof = null;
        Activate();
        Editor.Focus();
        Editor.BeginChange();
        try
        {
            if (Editor.Selection.Start.CompareTo(target.Start) != 0 || Editor.Selection.End.CompareTo(target.End) != 0)
                Editor.Selection.Select(target.Start, target.End);
            if (!target.IsCaret)
            {
                // A selection can contain mixed fonts, emphasis and sizes.
                // Only foreground is changed; other direct formatting stays.
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            }
            else if (FontFinderCaretHasWordBoundary(target.Start))
            {
                foreach (var (property, value) in target.Formatting)
                    Editor.Selection.ApplyPropertyValue(property, value);
                foreach (var (property, value) in requested)
                    Editor.Selection.ApplyPropertyValue(property, value);
            }
            // At an interior caret WPF would recolour the existing word. The
            // shared insertion bridge applies these choices to new text only.
            target.Applied = true;
        }
        finally { Editor.EndChange(); }
        if (target.IsCaret)
        {
            fontFinderTypingBridgeDocument = Editor.Document;
            fontFinderCaretChoice = new FontFinderCaretChoice(target, fontFinderDocumentVersion,
                requested.Select(item => (item.Key, item.Value)).ToArray(), CaptureFontFinderFormatting());
        }
        RefreshFontBox();
        RefreshParagraphStyleBox();
        MarkDirty(target.IsCaret ? $"Text colour {hex.ToUpperInvariant()} selected for new typing."
            : $"Text colour {hex.ToUpperInvariant()} applied to the selection. Ctrl+Z to undo.");
    }

    bool IsTextColourFormattingInput(object? source)
    {
        if (MainMenu?.IsKeyboardFocusWithin == true) return true;
        var current = source as DependencyObject;
        for (var depth = 0; current != null && depth < 24; depth++)
        {
            if (current is MenuItem || ReferenceEquals(current, MainMenu) ||
                current is FrameworkElement { Name: "TextColourButton" or "TextColourMenu" })
                return true;
            var parent = LogicalTreeHelper.GetParent(current);
            if (parent == null && current is Visual) parent = VisualTreeHelper.GetParent(current);
            current = parent;
        }
        return false;
    }

    void EnsureTextColourEditable()
    {
        if (Editor.IsReadOnly || !Editor.IsEnabled)
            throw new InvalidOperationException("The document is read-only or editing is disabled. Text colour cannot be changed.");
    }
}
