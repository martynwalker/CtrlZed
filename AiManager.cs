using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

public static class AiManager
{
    public static IReadOnlyList<string> DirectProviders { get; }=Array.AsReadOnly(new[]{"ChatGPT","Claude","Gemini"});
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
            return Path.Combine(root,"ai-settings.json");
        }
    }

    public static string GetDefaultProvider()
    {
        var settings=Load();
        return DirectProviders.FirstOrDefault(provider=>provider.Equals(settings.Provider,StringComparison.OrdinalIgnoreCase))??"ChatGPT";
    }

    public static void SetDefaultProvider(string provider)
    {
        var canonical=DirectProviders.FirstOrDefault(name=>name.Equals(provider,StringComparison.OrdinalIgnoreCase))
            ??throw new ArgumentException("Choose ChatGPT, Claude or Gemini as the default direct AI provider.",nameof(provider));
        AiDirectService.WriteConfigurationFiles(new Dictionary<string,byte[]>{{SettingsPath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Settings{Provider=canonical}))}});
    }

    internal static void SaveConfiguration(IReadOnlyList<AiProviderEdit> edits,string defaultProvider)
    {
        var canonical=DirectProviders.FirstOrDefault(name=>name.Equals(defaultProvider,StringComparison.OrdinalIgnoreCase))
            ??throw new ArgumentException("Choose ChatGPT, Claude or Gemini as the default direct AI provider.",nameof(defaultProvider));
        AiDirectService.SaveProfiles(edits,new Dictionary<string,byte[]>{{SettingsPath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Settings{Provider=canonical}))}});
    }

    public static void ShowForMacro(Window owner,string macroExtension,string macroReference,string existingCode,Action<string,string> applyResult,string? initialInstruction=null)
    {
        ShowCore(owner,true,macroExtension,macroReference,existingCode,"","",(result,ext)=>applyResult(result.Trim(),ext),null,initialInstruction);
    }

    public static void ShowForDocument(Window owner,string selectedText,string documentText,Action<string,bool> applyResult)
    {
        ShowCore(owner,false,"","","",selectedText,documentText,null,applyResult,null);
    }

    static void ShowCore(
        Window owner,bool macroMode,string macroExtension,string macroReference,string existingCode,
        string selectedText,string documentText,
        Action<string,string>? applyMacro,Action<string,bool>? applyDocument,string? initialInstruction=null)
    {
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
        var provider=new ComboBox{Width=160,ItemsSource=Providers.Keys.ToList(),SelectedItem=GetDefaultProvider(),Margin=new Thickness(5,0,12,0)};
        providerRow.Children.Add(provider);
        ComboBox? macroTarget=null;
        if(macroMode)
        {
            providerRow.Children.Add(Label("Target"));
            macroTarget=new ComboBox{Width=125,ItemsSource=new[]{"Safe .czm","VB.NET","C#"},Margin=new Thickness(5,0,12,0)};
            macroTarget.SelectedIndex=macroExtension==".vb"?1:macroExtension==".cs"?2:0;
            providerRow.Children.Add(macroTarget);
        }
        var liveWeb=new CheckBox{Content="Use live web/search when available",Foreground=Brushes.White,IsChecked=!macroMode,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,12,0)};
        providerRow.Children.Add(liveWeb);
        var configure=new Button{Content="AI settings…",Margin=new Thickness(5,0,0,0)};
        providerRow.Children.Add(configure);
        Grid.SetRow(providerRow,1);root.Children.Add(providerRow);

        var instruction=new TextBox
        {
            AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Padding=new Thickness(10),FontSize=14,
            Text=!string.IsNullOrWhiteSpace(initialInstruction)?initialInstruction:
                macroMode
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

        var bottom=new Grid{Margin=new Thickness(0,12,0,0)};
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var message=new TextBlock
        {
            Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(4,0,12,0),TextWrapping=TextWrapping.Wrap
        };
        Grid.SetColumn(message,0);bottom.Children.Add(message);
        var bottomButtons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var insert=new Button{Content=macroMode?"Use in Macro Editor":"Insert at Cursor",Width=125,Margin=new Thickness(4)};
        var replace=new Button{Content="Replace Selection",Width=125,Margin=new Thickness(4),Visibility=macroMode?Visibility.Collapsed:Visibility.Visible,IsEnabled=!string.IsNullOrWhiteSpace(selectedText)};
        var copy=new Button{Content="Copy Result",Width=95,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        bottomButtons.Children.Add(insert);bottomButtons.Children.Add(replace);bottomButtons.Children.Add(copy);bottomButtons.Children.Add(close);
        Grid.SetColumn(bottomButtons,1);bottom.Children.Add(bottomButtons);
        Grid.SetRow(bottom,5);root.Children.Add(bottom);

        void SetMessage(string text,bool error=false)
        {
            message.Text=text;
            message.Foreground=error?Brushes.LightSalmon:Brushes.White;
        }

        string CurrentMacroExtension()
        {
            if(!macroMode)return "";
            return macroTarget?.SelectedIndex switch {1=>".vb",2=>".cs",_=>".czm"};
        }

        string BuildPrompt(string? authoringProvider=null)
        {
            var text=instruction.Text.Trim();
            if(macroMode)
            {
                var ext=CurrentMacroExtension();
                var reference=ext==".czm"?macroReference:"";
                return AiResultRenderer.MacroPrompt(text,ext,reference,existingCode,authoringProvider??"");
            }
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
            free.IsEnabled=false;
            signed.IsEnabled=false;
            paste.IsEnabled=false;
            saveDefault.IsEnabled=false;
            insert.IsEnabled=false;
            replace.IsEnabled=false;
            copy.IsEnabled=false;
            heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — working…";
            SetMessage(macroMode?"Generating your macro…":"Generating your document result…");

            var progressText=macroMode?"Generating your macro…":"Generating your document result…";
            var progressWindow=new Window
            {
                Title="CtrlZed AI — working",
                Owner=w,
                Width=430,
                Height=155,
                ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                ShowInTaskbar=false,
                Icon=Branding.CreateIconImageSource()
            };
            var progressPanel=new StackPanel{Margin=new Thickness(20)};
            progressPanel.Children.Add(new TextBlock
            {
                Text=progressText,
                FontSize=16,
                FontWeight=FontWeights.SemiBold,
                Margin=new Thickness(0,0,0,14)
            });
            progressPanel.Children.Add(new ProgressBar
            {
                IsIndeterminate=true,
                Height=18,
                Foreground=new SolidColorBrush(Color.FromRgb(55,150,85)),
                Background=new SolidColorBrush(Color.FromRgb(220,232,222))
            });
            progressPanel.Children.Add(new TextBlock
            {
                Text="The request is running. CtrlZed will return the result here when it is ready.",
                TextWrapping=TextWrapping.Wrap,
                Foreground=Brushes.DimGray,
                Margin=new Thickness(0,10,0,0)
            });
            progressWindow.Content=progressPanel;
            progressWindow.Show();

            try
            {
                result.Text=await AiDirectService.GenerateAsync(name,BuildPrompt(name),liveWeb.IsChecked==true);
                heading.Text=macroMode?"AI Macro Assistant — result ready":"AI Writing & Research Assistant — result ready";
                SetMessage("Result generated successfully.");
            }
            catch(Exception ex)
            {
                heading.Text=macroMode?"AI Macro Assistant":"AI Writing & Research Assistant";
                SetMessage("Direct AI failed: "+ex.Message,true);
                MessageBox.Show(ex.Message,"Direct AI",MessageBoxButton.OK,MessageBoxImage.Error);
            }
            finally
            {
                if(progressWindow.IsVisible)progressWindow.Close();
                generate.IsEnabled=true;
                free.IsEnabled=true;
                signed.IsEnabled=true;
                paste.IsEnabled=true;
                saveDefault.IsEnabled=true;
                insert.IsEnabled=true;
                replace.IsEnabled=!macroMode && !string.IsNullOrWhiteSpace(selectedText);
                copy.IsEnabled=true;
                w.Activate();
            }
        }

        generate.Click+=(_,__)=>Generate();
        configure.Click+=(_,__)=>ConfigureProvider(w,provider.SelectedItem?.ToString()??"ChatGPT");
        saveDefault.Click+=(_,__)=>
        {
            try
            {
                SetDefaultProvider(provider.SelectedItem?.ToString()??"ChatGPT");
                heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — default saved";
                SetMessage("Default AI provider saved.");
            }
            catch(Exception error){SetMessage(error.Message,true);}
        };

        void OpenBrowser(bool privateMode,bool login)
        {
            var providerName=provider.SelectedItem?.ToString()??"ChatGPT";
            try{Clipboard.SetText(BuildPrompt(providerName));}catch{}
            OpenProvider(providerName,privateMode,login);
        }

        free.Click+=(_,__)=>OpenBrowser(true,false);
        signed.Click+=(_,__)=>OpenBrowser(false,true);
        paste.Click+=(_,__)=>
        {
            try
            {
                if(Clipboard.ContainsText())
                {
                    result.Text=Clipboard.GetText();
                    SetMessage("Browser result pasted from clipboard.");
                }
                else SetMessage("The clipboard does not contain text.",true);
            }
            catch(Exception ex)
            {
                SetMessage("Clipboard error: "+ex.Message,true);
                MessageBox.Show(ex.Message,"Clipboard",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        };

        insert.Click+=(_,__)=>
        {
            if(string.IsNullOrWhiteSpace(result.Text))return;
            if(macroMode)
            {
                var providerName=provider.SelectedItem?.ToString()??"Unknown";
                var ext=CurrentMacroExtension();
                applyMacro?.Invoke(AiResultRenderer.EmbedMacroPrompt(result.Text.Trim(),ext,instruction.Text,providerName),ext);
            }
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
            if(string.IsNullOrWhiteSpace(result.Text))
            {
                SetMessage("There is no result to copy.",true);
                return;
            }
            try
            {
                Clipboard.SetText(result.Text);
                SetMessage("Result copied successfully to the clipboard.");
            }
            catch(Exception ex)
            {
                SetMessage("Copy failed: "+ex.Message,true);
            }
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
        new AiSettingsWindow(owner,provider).ShowDialog();
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
