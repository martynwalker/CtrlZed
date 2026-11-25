using System.Windows;

namespace CtrlZed;

public partial class MainWindow
{
    void AiSettings_Click(object sender,RoutedEventArgs e)
    {
        var owner=sender is DependencyObject control?Window.GetWindow(control):null;
        var settings=new AiSettingsWindow(owner??this);
        settings.ShowDialog();
        if(settings.Saved)Status.Text="AI settings saved.";
    }
}
