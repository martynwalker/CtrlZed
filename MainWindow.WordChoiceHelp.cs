using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal void InstallBeta33WordChoiceHelp()
    {
        var help=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"Help",StringComparison.OrdinalIgnoreCase));
        if(help==null || help.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedWordChoiceHelp"))return;

        var item=new MenuItem{Header="Word Choice & _Poetry…",Tag="CtrlZedWordChoiceHelp"};
        item.Click+=(_,__)=>ShowBeta33WordChoiceHelp();
        var aboutIndex=help.Items.OfType<object>().ToList()
            .FindIndex(x=>x is MenuItem m && NormalisedMenuHeader(m.Header).StartsWith("About",StringComparison.OrdinalIgnoreCase));
        help.Items.Insert(aboutIndex>=0?Math.Max(0,aboutIndex-1):help.Items.Count,item);
    }

    void ShowBeta33WordChoiceHelp()
    {
        var w=new Window
        {
            Title="CtrlZed Word Choice and Poetry Assistance",Owner=this,
            Width=760,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var text=new TextBlock
        {
            Text="""
CONTEXTUAL THESAURUS

Select a word or short phrase, or place the caret inside a word, then choose Review → Contextual Thesaurus. CtrlZed sends only the marked wording, its paragraph and the selected writing mode to the configured direct AI provider.

Alternatives are ranked for the meaning actually used in the paragraph. Each suggestion explains its nuance and register, such as formal, technical, literary, stronger or gentler. CtrlZed never substitutes a thesaurus choice automatically.

POETRY RHYME ASSISTANT

Place the caret inside a stanza and choose Review → Poetry Rhyme Assistant. Stanzas are separated by blank paragraphs. CtrlZed reviews up to twelve non-empty lines at a time and attempts to infer the likely rhyme relationships.

Strong opportunities are shown with thin green marks. A green mark is not an error: it means CtrlZed has a possible replacement that approximately preserves the meaning while improving an exact, near or slant rhyme with another line.

Rhyme suggestions remain sparse by design. If the software cannot preserve meaning, tone and grammar responsibly, it returns no suggestion rather than forcing a rhyme. The assistant also reports the likely rhyme scheme and explains the effect of each proposal.

PERFORMANCE AND PRIVACY

Neither service runs while you type. Thesaurus and rhyme analysis occur only when explicitly requested. Ordinary editing, spelling and local grammar checks remain local. AI calls require the existing Allow AI language services setting and a configured direct ChatGPT, Claude or Gemini provider.

CONTROL

Every replacement requires an explicit Apply action and remains immediately undoable with Ctrl+Z. Editing the poem clears earlier green marks because the stored suggestions may no longer match the revised stanza.
""",
            TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(22)
        };
        var close=new Button{Content="Close",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,16,16)};
        close.Click+=(_,__)=>w.Close();
        var root=new DockPanel();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        root.Children.Add(new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        w.Content=root;w.ShowDialog();
    }
}

internal static class Beta33WordChoiceHelpBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(new Action(window.InstallBeta33WordChoiceHelp),DispatcherPriority.ApplicationIdle);
    }
}
