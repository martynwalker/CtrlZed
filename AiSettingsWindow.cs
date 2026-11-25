using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

/// <summary>Local provider configuration, independent of any manuscript or AI request.</summary>
internal sealed class AiSettingsWindow : Window
{
    sealed class Draft
    {
        internal string Model="";
        internal string Key="";
        internal bool RemoveKey;
        internal bool HasStoredKey;
    }

    readonly Dictionary<string,Draft> drafts=new(StringComparer.OrdinalIgnoreCase);
    readonly ComboBox provider;
    readonly TextBox model;
    readonly PasswordBox key;
    readonly TextBlock keyStatus;
    readonly ComboBox defaultProvider;
    readonly CheckBox removeKey;
    readonly TextBlock status;
    string current="";
    bool loading;
    internal bool Saved {get;private set;}

    internal AiSettingsWindow(Window owner,string? initialProvider=null)
    {
        NameScope.SetNameScope(this,new NameScope());
        Title="CtrlZed — AI Settings";Owner=owner;Width=660;Height=570;MinWidth=560;MinHeight=530;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Icon=Branding.CreateIconImageSource();
        foreach(var name in AiManager.DirectProviders)
        {
            var profile=AiDirectService.GetProfile(name);
            drafts.Add(name,new Draft{Model=profile.Model,HasStoredKey=profile.HasKey});
        }

        var root=new Grid{Margin=new Thickness(20)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var heading=new TextBlock{Text="AI Settings",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};
        root.Children.Add(heading);
        var introduction=new TextBlock
        {
            Text="Set up direct AI providers and choose the default for writing tools. A provider selected within a workshop applies to that window only.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,16)
        };
        Grid.SetRow(introduction,1);root.Children.Add(introduction);

        var form=new Grid();form.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(155)});form.ColumnDefinitions.Add(new ColumnDefinition());
        for(var index=0;index<9;index++)form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        void AddLabel(string text,int row)
        {
            var label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,12)};
            Grid.SetRow(label,row);form.Children.Add(label);
        }
        void AddControl(FrameworkElement control,int row)
        {
            Grid.SetColumn(control,1);Grid.SetRow(control,row);control.Margin=new Thickness(0,0,0,12);form.Children.Add(control);
        }
        defaultProvider=new ComboBox{Name="AiSettingsDefault",ItemsSource=AiManager.DirectProviders,SelectedItem=AiManager.GetDefaultProvider(),MinHeight=26};
        AddLabel("Default provider",0);AddControl(defaultProvider,0);RegisterName(defaultProvider.Name,defaultProvider);
        var divider=new Separator{Margin=new Thickness(0,4,0,14)};Grid.SetRow(divider,1);Grid.SetColumnSpan(divider,2);form.Children.Add(divider);
        provider=new ComboBox{Name="AiSettingsProvider",ItemsSource=AiManager.DirectProviders,MinHeight=26};
        AddLabel("Provider to configure",2);AddControl(provider,2);RegisterName(provider.Name,provider);
        model=new TextBox{Name="AiSettingsModel",MinHeight=26,Padding=new Thickness(4),MaxLength=200};
        AddLabel("Model",3);AddControl(model,3);RegisterName(model.Name,model);
        keyStatus=new TextBlock{Name="AiSettingsKeyStatus",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
        AddControl(keyStatus,4);RegisterName(keyStatus.Name,keyStatus);
        key=new PasswordBox{Name="AiSettingsKey",MinHeight=26,Padding=new Thickness(4),MaxLength=16384};
        AddLabel("API key",5);AddControl(key,5);RegisterName(key.Name,key);
        removeKey=new CheckBox{Name="AiSettingsRemoveKey",Content="Remove this provider's saved key on Save"};
        AddControl(removeKey,6);RegisterName(removeKey.Name,removeKey);
        var privacy=new TextBlock
        {
            Text="Leave the API key blank to keep its saved value. Keys are encrypted for the current Windows user and are never stored in documents. Macro-specific named secrets remain in Macro Manager → Secrets…",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,8)
        };
        Grid.SetRow(privacy,7);Grid.SetColumnSpan(privacy,2);form.Children.Add(privacy);
        var local=new TextBlock{Text="Opening or saving these settings makes no AI request. Copilot remains available through the assistant's browser handoff.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
        Grid.SetRow(local,8);Grid.SetColumnSpan(local,2);form.Children.Add(local);
        var scroller=new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetRow(scroller,2);root.Children.Add(scroller);

        status=new TextBlock{Name="AiSettingsStatus",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,10,0,8),Text="Changes are saved together when you choose Save."};
        RegisterName(status.Name,status);Grid.SetRow(status,3);root.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var save=new Button{Name="AiSettingsSave",Content="Save",Width=90,Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,0),IsDefault=true};
        var cancel=new Button{Name="AiSettingsCancel",Content="Cancel",Width=90,Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,0),IsCancel=true};
        buttons.Children.Add(save);buttons.Children.Add(cancel);RegisterName(save.Name,save);RegisterName(cancel.Name,cancel);
        Grid.SetRow(buttons,4);root.Children.Add(buttons);Content=root;

        provider.SelectionChanged+=(_,__)=>SwitchProvider();
        removeKey.Checked+=(_,__)=>{if(!loading)key.IsEnabled=false;};
        removeKey.Unchecked+=(_,__)=>{if(!loading)key.IsEnabled=true;};
        save.Click+=(_,__)=>Save();cancel.Click+=(_,__)=>Close();
        Closed+=(_,__)=>{key.Password="";foreach(var draft in drafts.Values)draft.Key="";};
        provider.SelectedItem=AiManager.DirectProviders.FirstOrDefault(name=>name.Equals(initialProvider,StringComparison.OrdinalIgnoreCase))??AiManager.GetDefaultProvider();
    }

    void CaptureDraft()
    {
        if(current.Length==0)return;
        var draft=drafts[current];draft.Model=model.Text;draft.Key=key.Password;draft.RemoveKey=removeKey.IsChecked==true;
    }

    void SwitchProvider()
    {
        if(loading)return;
        CaptureDraft();current=provider.SelectedItem?.ToString()??AiManager.GetDefaultProvider();
        loading=true;
        try
        {
            var draft=drafts[current];model.Text=draft.Model;key.Password=draft.Key;removeKey.IsChecked=draft.RemoveKey;key.IsEnabled=!draft.RemoveKey;
            keyStatus.Text=draft.HasStoredKey?"A key is saved. Enter a replacement below, or leave it blank to keep it.":"No API key is saved for this provider.";
        }
        finally{loading=false;}
    }

    void Save()
    {
        CaptureDraft();
        try
        {
            var edits=drafts.Select(pair=>new AiProviderEdit(pair.Key,pair.Value.Model,
                pair.Value.RemoveKey?"":string.IsNullOrWhiteSpace(pair.Value.Key)?null:pair.Value.Key)).ToArray();
            AiManager.SaveConfiguration(edits,defaultProvider.SelectedItem?.ToString()??"ChatGPT");
            Saved=true;status.Text="AI settings saved.";status.Foreground=Brushes.DarkGreen;Close();
        }
        catch(Exception error){status.Text=error.Message;status.Foreground=Brushes.Firebrick;}
    }
}
