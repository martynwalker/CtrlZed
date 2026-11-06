using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace CtrlZed;

public enum EditorViewMode
{
    Paged,
    Continuous
}

public static class EditorViewSettingsStore
{
    sealed class Settings
    {
        public EditorViewMode ViewMode { get; set; }=EditorViewMode.Paged;
    }

    static string SettingsPath
    {
        get
        {
            var root=Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"editor-view.json");
        }
    }

    public static EditorViewMode Load()
    {
        try
        {
            if(File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.ViewMode
                       ??EditorViewMode.Paged;
        }
        catch{ }
        return EditorViewMode.Paged;
    }

    public static void Save(EditorViewMode mode)
    {
        try
        {
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(new Settings{ViewMode=mode},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch{ }
    }
}

public partial class MainWindow
{
    bool beta35ViewModeInstalled;
    EditorViewMode beta35ViewMode=EditorViewMode.Paged;
    MenuItem? beta35PagedViewMenu;
    MenuItem? beta35ContinuousViewMenu;

    internal void InstallBeta35ViewMode()
    {
        if(beta35ViewModeInstalled)return;
        beta35ViewModeInstalled=true;

        var view=FindBeta34MenuItem("View");
        if(view!=null)
        {
            beta35PagedViewMenu=new MenuItem
            {
                Header="_Paged Editing",
                IsCheckable=true,
                StaysOpenOnClick=false
            };
            beta35ContinuousViewMenu=new MenuItem
            {
                Header="_Continuous Editing",
                IsCheckable=true,
                StaysOpenOnClick=false
            };
            beta35PagedViewMenu.Click+=(_,__)=>ApplyBeta35ViewMode(EditorViewMode.Paged,true);
            beta35ContinuousViewMenu.Click+=(_,__)=>ApplyBeta35ViewMode(EditorViewMode.Continuous,true);

            var firstZoom=view.Items.OfType<MenuItem>()
                .Select((item,index)=>(item,index))
                .FirstOrDefault(x=>Beta34CleanHeader(x.item.Header).StartsWith("Zoom ",StringComparison.OrdinalIgnoreCase));
            var insertAt=firstZoom.item==null?view.Items.Count:firstZoom.index;
            if(insertAt>0 && view.Items[insertAt-1] is not Separator)
                view.Items.Insert(insertAt++,new Separator());
            view.Items.Insert(insertAt++,beta35PagedViewMenu);
            view.Items.Insert(insertAt++,beta35ContinuousViewMenu);
            view.Items.Insert(insertAt,new Separator());
        }

        beta35ViewMode=EditorViewSettingsStore.Load();
        ApplyBeta35ViewMode(beta35ViewMode,false);
    }

    internal EditorViewMode CurrentEditorViewMode=>beta35ViewMode;

    internal void ApplyBeta35ViewMode(EditorViewMode mode,bool persist)
    {
        beta35ViewMode=mode;
        var paged=mode==EditorViewMode.Paged;

        if(beta35PagedViewMenu!=null)beta35PagedViewMenu.IsChecked=paged;
        if(beta35ContinuousViewMenu!=null)beta35ContinuousViewMenu.IsChecked=!paged;

        PageGuides.Visibility=paged?Visibility.Visible:Visibility.Collapsed;
        PageBorder.Margin=paged?new Thickness(30):new Thickness(12,18,12,18);
        PageBorder.BorderBrush=paged
            ?new SolidColorBrush(Color.FromRgb(190,188,181))
            :new SolidColorBrush(Color.FromRgb(225,225,225));
        PageBorder.BorderThickness=paged?new Thickness(1):new Thickness(0);
        PageBorder.CornerRadius=paged?new CornerRadius(1):new CornerRadius(0);
        PageBorder.Effect=paged
            ?new DropShadowEffect
            {
                BlurRadius=10,
                Direction=270,
                ShadowDepth=2,
                Opacity=0.18,
                Color=Colors.Black
            }
            :null;

        var scroll=FindVisualAncestor<ScrollViewer>(PageBorder);
        if(scroll!=null)
            scroll.Background=new SolidColorBrush(
                paged?Color.FromRgb(239,236,226):Color.FromRgb(247,247,247));

        PageGuides.InvalidateVisual();
        if(persist)EditorViewSettingsStore.Save(mode);
        OnBeta35ViewModeChanged();
        if(IsLoaded)
            Status.Text=paged
                ?"Paged Editing view selected."
                :"Continuous Editing view selected.";
    }

    static T? FindVisualAncestor<T>(DependencyObject source) where T:DependencyObject
    {
        for(DependencyObject? current=source;current!=null;current=VisualTreeHelper.GetParent(current))
            if(current is T match)return match;
        return null;
    }
}

internal static class Beta35ViewModeBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallBeta35ViewMode),DispatcherPriority.ContextIdle);
    }
}
