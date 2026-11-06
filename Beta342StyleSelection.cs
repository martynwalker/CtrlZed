using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    bool beta342StyleSelectionInstalled;
    string? beta342RepeatedStyle;
    TextPointer? beta342SelectionStart;
    TextPointer? beta342SelectionEnd;

    internal void InstallBeta342StyleSelection()
    {
        if(beta342StyleSelectionInstalled)return;
        beta342StyleSelectionInstalled=true;

        StyleBox.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Beta342StyleChoiceMouseDown),true);
        StyleBox.DropDownClosed+=Beta342StyleBoxDropDownClosed;
    }

    void Beta342StyleChoiceMouseDown(object sender,MouseButtonEventArgs e)
    {
        beta342RepeatedStyle=null;
        beta342SelectionStart=null;
        beta342SelectionEnd=null;

        var item=FindComboBoxItem(e.OriginalSource as DependencyObject);
        if(item==null || !ReferenceEquals(StyleBox.SelectedItem,item) || item.Content is not string style)return;

        // SelectionChanged does not fire when the user deliberately chooses
        // the style that is already displayed (most often Normal). Preserve
        // the document selection and reapply that style when the list closes.
        beta342RepeatedStyle=style;
        beta342SelectionStart=Editor.Selection.Start;
        beta342SelectionEnd=Editor.Selection.End;
    }

    void Beta342StyleBoxDropDownClosed(object? sender,EventArgs e)
    {
        var style=beta342RepeatedStyle;
        var start=beta342SelectionStart;
        var end=beta342SelectionEnd;
        beta342RepeatedStyle=null;
        beta342SelectionStart=null;
        beta342SelectionEnd=null;
        if(string.IsNullOrWhiteSpace(style) || start==null || end==null)return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var spec=ResolveStyle(style);
            if(spec==null)return;
            ApplyStyleToRange(new TextRange(start,end),spec);
            MarkDirty($"Style: {style} ({activeStyleSet})");
            Editor.Selection.Select(start,end);
            Editor.Focus();
        }),DispatcherPriority.Input);
    }

    static ComboBoxItem? FindComboBoxItem(DependencyObject? source)
    {
        for(var current=source;current!=null;current=System.Windows.Media.VisualTreeHelper.GetParent(current))
            if(current is ComboBoxItem item)return item;
        return null;
    }
}

internal static class Beta342StyleSelectionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is MainWindow window)window.InstallBeta342StyleSelection();
    }
}
