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

    static string SettingsPath
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"ai-settings.json");
        }
    }

    public static string GetDefaultProvider()
    {
        var settings=Load();
        return Providers.ContainsKey(settings.Provider)?settings.Provider:"ChatGPT";
    }

    public static void ShowForMacro(Window owner,string macroReference,string existingCode,Action<string> applyResult)
    {
        ShowCore(owner,true,macroReference,existingCode,"","",result=>applyResult(result.Trim()),null);
    }

    public static void ShowForDocument(Window owner,string selectedText,string documentText,Action<string,bool> applyResult)
    {
        ShowCore(owner,false,"","",selectedText,documentText,null,applyResult);
    }

    static void ShowCore(
        Window owner,bool macroMode,string macroReference,string existingCode,
        string selectedText,string documentText,
        Action<string>? applyMacro,Action<string,bool>? applyDocument)
    {
        var settings=Load();
        var w=new Window
        {
            Title=macroMode?"CtrlZed AI Macro Assistant":"CtrlZed AI Manager",
            Owner=owner,Width=860,Height=720,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Branding.NavyDeep),
            Icon=Branding.CreateIconImageSource()
        };

        var root=new Grid{Margin=new Thickness(18)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(180)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var heading=new TextBlock
        {
            Text=macroMode?"AI Macro Assistant":"AI Writing & Research Assistant",
            FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,
            Margin=new Thickness(0,0,0,8)
        };
        Grid.SetRow(heading,0);root.Children.Add(heading);

        var providerRow=new WrapPanel{Margin=new Thickness(0,0,0,10)};
        providerRow.Children.Add(Label("Provider"));
        var provider=new ComboBox{Width=160,ItemsSource=Providers.Keys.ToList(),SelectedItem=Providers.ContainsKey(settings.Provider)?settings.Provider:"ChatGPT",Margin=new Thickness(5,0,12,0)};
        providerRow.Children.Add(provider);
        var liveWeb=new CheckBox{Content="Use live web/search when available",Foreground=Brushes.White,IsChecked=!macroMode,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,12,0)};
        providerRow.Children.Add(liveWeb);
        var configure=new Button{Content="Configure Direct AI…",Margin=new Thickness(5,0,0,0)};
        providerRow.Children.Add(configure);
        Grid.SetRow(providerRow,1);root.Children.Add(providerRow);

        var instruction=new TextBox
        {
            AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Padding=new Thickness(10),FontSize=14,
            Text=macroMode
                ?"Describe the macro you want. Example: Create a macro that rewrites the highlighted text in the style of Winston Churchill."
                :"Tell the AI what to write, research or transform. Selected text is supplied automatically when present."
        };
        Grid.SetRow(instruction,2);root.Children.Add(instruction);

        var actionRow=new WrapPanel{Margin=new Thickness(0,10,0,10)};
        var generate=new Button{Content="Generate Direct",Width=120,Margin=new Thickness(0,0,8,0)};
        var free=new Button{Content="Open Free / Private",Width=135,Margin=new Thickness(0,0,8,0)};
        var signed=new Button{Content="Open Signed-in",Width=120,Margin=new Thickness(0,0,8,0)};
        var paste=new Button{Content="Paste Browser Result",Width=135,Margin=new Thickness(0,0,8,0)};
        var saveDefault=new Button{Content="Make Default",Width=105};
        actionRow.Children.Add(generate);actionRow.Children.Add(free);actionRow.Children.Add(signed);actionRow.Children.Add(paste);actionRow.Children.Add(saveDefault);
        Grid.SetRow(actionRow,3);root.Children.Add(actionRow);

        var result=new TextBox
        {
            AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            FontFamily=new FontFamily(macroMode?"Consolas":"Segoe UI"),
            FontSize=13.5,Padding=new Thickness(10)
        };
        Grid.SetRow(result,4);root.Children.Add(result);

        var bottom=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var insert=new Button{Content=macroMode?"Use in Macro Editor":"Insert at Cursor",Width=125,Margin=new Thickness(4)};
        var replace=new Button{Content="Replace Selection",Width=125,Margin=new Thickness(4),Visibility=macroMode?Visibility.Collapsed:Visibility.Visible,IsEnabled=!string.IsNullOrWhiteSpace(selectedText)};
        var copy=new Button{Content="Copy Result",Width=95,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        bottom.Children.Add(insert);bottom.Children.Add(replace);bottom.Children.Add(copy);bottom.Children.Add(close);
        Grid.SetRow(bottom,5);root.Children.Add(bottom);

        string BuildPrompt()
        {
            var text=instruction.Text.Trim();
            if(macroMode)return AiResultRenderer.MacroPrompt(text,macroReference,existingCode);
            return AiResultRenderer.DocumentPrompt(text,selectedText,documentText,true);
        }

        async void Generate()
        {
            var name=provider.SelectedItem?.ToString()??"ChatGPT";
            if(name=="Copilot")
            {
                MessageBox.Show("Microsoft Copilot Chat does not provide a general consumer chat API. Use the browser route, then Paste Browser Result.","Copilot",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }

            generate.IsEnabled=false;
            heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — working…";
            try
            {
                result.Text=await AiDirectService.GenerateAsync(name,BuildPrompt(),liveWeb.IsChecked==true);
                heading.Text=macroMode?"AI Macro Assistant — result ready":"AI Writing & Research Assistant — result ready";
            }
            catch(Exception ex)
            {
                heading.Text=macroMode?"AI Macro Assistant":"AI Writing & Research Assistant";
                MessageBox.Show(ex.Message,"Direct AI",MessageBoxButton.OK,MessageBoxImage.Error);
            }
            finally{generate.IsEnabled=true;}
        }

        generate.Click+=(_,__)=>Generate();
        configure.Click+=(_,__)=>ConfigureProvider(w,provider.SelectedItem?.ToString()??"ChatGPT");
        saveDefault.Click+=(_,__)=>
        {
            settings.Provider=provider.SelectedItem?.ToString()??"ChatGPT";
            Save(settings);
            heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — default saved";
        };

        void OpenBrowser(bool privateMode,bool login)
        {
            try{Clipboard.SetText(BuildPrompt());}catch{}
            OpenProvider(provider.SelectedItem?.ToString()??"ChatGPT",privateMode,login);
        }

        free.Click+=(_,__)=>OpenBrowser(true,false);
        signed.Click+=(_,__)=>OpenBrowser(false,true);
        paste.Click+=(_,__)=>
        {
            try{if(Clipboard.ContainsText())result.Text=Clipboard.GetText();}
            catch(Exception ex){MessageBox.Show(ex.Message,"Clipboard",MessageBoxButton.OK,MessageBoxImage.Error);}
        };

        insert.Click+=(_,__)=>
        {
            if(string.IsNullOrWhiteSpace(result.Text))return;
            if(macroMode)applyMacro?.Invoke(result.Text.Trim());
            else applyDocument?.Invoke(result.Text,false);
            w.Close();
        };
        replace.Click+=(_,__)=>
        {
            if(string.IsNullOrWhiteSpace(result.Text))return;
            applyDocument?.Invoke(result.Text,true);
            w.Close();
        };
        copy.Click+=(_,__)=>
        {
            try{Clipboard.SetText(result.Text);}catch{}
        };
        close.Click+=(_,__)=>w.Close();

        w.Content=root;
        w.ShowDialog();
    }

    static TextBlock Label(string text)=>new()
    {
        Text=text,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center
    };

    static void ConfigureProvider(Window owner,string provider)
    {
        if(provider=="Copilot")
        {
            MessageBox.Show(
                "Copilot Chat does not expose a general consumer chat API. CtrlZed therefore uses the browser handoff for Copilot.",
                "Copilot direct integration",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var profile=AiDirectService.GetProfile(provider);
        var w=new Window
        {
            Title=$"Configure {provider} Direct AI",
            Owner=owner,Width=520,Height=300,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var p=new StackPanel{Margin=new Thickness(18)};
        p.Children.Add(new TextBlock{Text="Model"});
        var model=new TextBox{Text=profile.Model,Margin=new Thickness(0,4,0,12)};
        p.Children.Add(model);
        p.Children.Add(new TextBlock{Text=profile.HasKey?"API key (stored securely; leave blank to keep current key)":"API key"});
        var key=new PasswordBox{Margin=new Thickness(0,4,0,10)};
        p.Children.Add(key);
        p.Children.Add(new TextBlock
        {
            Text="The key is encrypted with Windows DPAPI for the current Windows user. It is never stored in CtrlZed documents.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)
        });
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var clear=new Button{Content="Clear Key",Width=85,Margin=new Thickness(4)};
        var save=new Button{Content="Save",Width=85,Margin=new Thickness(4)};
        var cancel=new Button{Content="Cancel",Width=85,Margin=new Thickness(4)};
        buttons.Children.Add(clear);buttons.Children.Add(save);buttons.Children.Add(cancel);
        p.Children.Add(buttons);

        clear.Click+=(_,__)=>
        {
            AiDirectService.SaveProfile(provider,model.Text,"");
            w.DialogResult=true;
        };
        save.Click+=(_,__)=>
        {
            AiDirectService.SaveProfile(provider,model.Text,string.IsNullOrWhiteSpace(key.Password)?null:key.Password);
            w.DialogResult=true;
        };
        cancel.Click+=(_,__)=>w.Close();
        w.Content=p;w.ShowDialog();
    }

    static Settings Load()
    {
        try
        {
            if(File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))??new Settings();
        }
        catch{}
        return new Settings();
    }

    static void Save(Settings settings)
    {
        try{File.WriteAllText(SettingsPath,JsonSerializer.Serialize(settings));}catch{}
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
