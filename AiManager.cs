using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

public static class AiManager
{
    sealed class Settings
    {
        public string Provider { get; set; } = "ChatGPT";
    }

    static readonly Dictionary<string,(string Url,string LoginUrl)> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ChatGPT"]=("https://chatgpt.com/","https://chatgpt.com/auth/login"),
        ["Copilot"]=("https://copilot.com/","https://copilot.com/"),
        ["Claude"]=("https://claude.ai/new","https://claude.ai/login"),
        ["Gemini"]=("https://gemini.google.com/app","https://accounts.google.com/")
    };

    public static void Show(Window owner,string macroReference)
    {
        var settingsPath=Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CtrlZed","ai-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        var settings=Load(settingsPath);

        var w=new Window
        {
            Title="CtrlZed AI Manager",
            Owner=owner,
            Width=760,
            Height=620,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Branding.NavyDeep),
            Icon=Branding.CreateIconImageSource()
        };

        var root=new Grid{Margin=new Thickness(18)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var title=new TextBlock
        {
            Text="AI Macro Assistant",
            FontSize=28,FontWeight=FontWeights.SemiBold,
            Foreground=Brushes.White,Margin=new Thickness(0,0,0,4)
        };
        Grid.SetRow(title,0);root.Children.Add(title);

        var intro=new TextBlock
        {
            Text="Describe what you want the macro to do. CtrlZed will prepare a provider-neutral prompt containing the current safe-macro command reference.",
            TextWrapping=TextWrapping.Wrap,
            Foreground=new SolidColorBrush(Color.FromRgb(205,218,236)),
            Margin=new Thickness(0,0,0,14)
        };
        Grid.SetRow(intro,1);root.Children.Add(intro);

        var providerRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
        providerRow.Children.Add(new TextBlock{Text="Provider",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Width=70});
        var provider=new ComboBox{Width=180,ItemsSource=Providers.Keys.ToList(),SelectedItem=Providers.ContainsKey(settings.Provider)?settings.Provider:"ChatGPT"};
        providerRow.Children.Add(provider);
        var defaultText=new TextBlock{Text="  Default: ChatGPT",Foreground=new SolidColorBrush(Branding.GoldBright),VerticalAlignment=VerticalAlignment.Center};
        providerRow.Children.Add(defaultText);
        Grid.SetRow(providerRow,2);root.Children.Add(providerRow);

        var request=new TextBox
        {
            AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Background=Brushes.White,Foreground=Brushes.Black,
            FontFamily=new FontFamily("Segoe UI"),FontSize=14,
            Padding=new Thickness(10),
            Text="Describe the macro you want here..."
        };
        Grid.SetRow(request,3);root.Children.Add(request);

        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        var copy=new Button{Content="Copy Prompt",Width=105,Margin=new Thickness(4)};
        var privateButton=new Button{Content="Open Free / Private",Width=135,Margin=new Thickness(4)};
        var signedButton=new Button{Content="Open Signed-in",Width=120,Margin=new Thickness(4)};
        var saveDefault=new Button{Content="Make Default",Width=105,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        buttons.Children.Add(copy);buttons.Children.Add(privateButton);buttons.Children.Add(signedButton);buttons.Children.Add(saveDefault);buttons.Children.Add(close);
        Grid.SetRow(buttons,4);root.Children.Add(buttons);

        string BuildPrompt()
        {
            var wish=request.Text.Trim();
            if(string.IsNullOrWhiteSpace(wish)||wish.StartsWith("Describe the macro",StringComparison.OrdinalIgnoreCase))
                wish="Create a useful example macro that demonstrates the safe CtrlZed macro language.";
            return
$@"You are helping me write a safe CtrlZed macro.

Return ONLY a complete .czm macro, with comments beginning # where useful. Do not return C#, VB.NET, PowerShell, shell commands, file access, network access, process launching, or unsupported commands.

What I want:
{wish}

Current CtrlZed safe-macro language:
{macroReference}

Rules:
- Use only documented commands.
- Keep the macro deterministic and local.
- Do not assume filesystem, network, clipboard, or process access unless the command reference explicitly provides it.
- Prefer clear comments and straightforward commands.
- If the requested behaviour cannot be done with the current safe language, return a commented .czm file explaining which capability is missing and do not invent a command.";
        }

        void CopyPrompt()
        {
            try
            {
                Clipboard.SetText(BuildPrompt());
                title.Text="AI Macro Assistant — prompt copied";
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message,"Clipboard",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }

        copy.Click+=(_,__)=>CopyPrompt();

        privateButton.Click+=(_,__)=>
        {
            CopyPrompt();
            var name=provider.SelectedItem?.ToString()??"ChatGPT";
            OpenProvider(name,true);
        };

        signedButton.Click+=(_,__)=>
        {
            CopyPrompt();
            var name=provider.SelectedItem?.ToString()??"ChatGPT";
            OpenProvider(name,false,true);
        };

        saveDefault.Click+=(_,__)=>
        {
            settings.Provider=provider.SelectedItem?.ToString()??"ChatGPT";
            Save(settingsPath,settings);
            defaultText.Text="  Default: "+settings.Provider;
        };

        close.Click+=(_,__)=>w.Close();
        w.Content=root;
        w.ShowDialog();
    }

    static Settings Load(string path)
    {
        try
        {
            if(File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))??new Settings();
        }
        catch{}
        return new Settings();
    }

    static void Save(string path,Settings settings)
    {
        try{File.WriteAllText(path,JsonSerializer.Serialize(settings));}catch{}
    }

    static void OpenProvider(string provider,bool privateMode,bool login=false)
    {
        if(!Providers.TryGetValue(provider,out var info))info=Providers["ChatGPT"];
        var url=login?info.LoginUrl:info.Url;
        try
        {
            if(privateMode)
            {
                var edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe");
                if(!File.Exists(edge))
                    edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft","Edge","Application","msedge.exe");
                if(File.Exists(edge))
                {
                    Process.Start(new ProcessStartInfo(edge,$"--inprivate \"{url}\""){UseShellExecute=true});
                    return;
                }
            }
            Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Open AI provider",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }
}
