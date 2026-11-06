using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    readonly LanguageIntelligenceSettings languageSettings=LanguageIntelligenceSettingsStore.Load();
    bool languageUiInstalled;
    MenuItem? spellingEnabledMenu;
    MenuItem? writingModeMenu;
    TextBlock? writingModeStatus;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if(languageUiInstalled)return;
        languageUiInstalled=true;

        ApplyLanguageIntelligenceSettings();
        InstallLanguageMenus();
        InstallWritingModeStatus();

        // Handle spelling errors before CtrlZed's general editor context menu.
        AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(LanguagePreviewMouseDown),true);
    }

    void ApplyLanguageIntelligenceSettings()
    {
        languageSettings.Normalise();
        SpellCheck.SetIsEnabled(Editor,languageSettings.SpellingEnabled);
        try{Editor.Language=XmlLanguage.GetLanguage(languageSettings.LanguageTag);}
        catch{Editor.Language=XmlLanguage.GetLanguage("en-GB");}

        try
        {
            var dictionaries=SpellCheck.GetCustomDictionaries(Editor);
            dictionaries.Clear();
            if(languageSettings.SpellingEnabled)
                dictionaries.Add(PersonalDictionaryStore.EnsureDictionary());
        }
        catch(Exception ex)
        {
            Status.Text="Personal dictionary could not be loaded: "+ex.Message;
        }

        UpdateLanguageMenuState();
    }

    void InstallLanguageMenus()
    {
        var fileMenu=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"File",StringComparison.OrdinalIgnoreCase));
        if(fileMenu!=null && !fileMenu.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedLanguageSettings"))
        {
            var languageItem=new MenuItem{Header="Language && _Writing Mode…",Tag="CtrlZedLanguageSettings"};
            languageItem.Click+=LanguageSettings_Click;
            var settingsIndex=fileMenu.Items.OfType<object>().ToList().FindIndex(x=>x is MenuItem m && NormalisedMenuHeader(m.Header).StartsWith("Settings",StringComparison.OrdinalIgnoreCase));
            fileMenu.Items.Insert(settingsIndex>=0?settingsIndex+1:Math.Max(0,fileMenu.Items.Count-1),languageItem);
        }

        if(MainMenu.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedReviewMenu"))return;

        var review=new MenuItem{Header="_Review",Tag="CtrlZedReviewMenu"};
        var nextError=new MenuItem{Header="_Next Spelling Error",InputGestureText="F7"};
        nextError.Click+=NextSpellingError_Click;
        spellingEnabledMenu=new MenuItem{Header="Show _Spelling Marks",IsCheckable=true};
        spellingEnabledMenu.Click+=(_,__)=>
        {
            languageSettings.SpellingEnabled=spellingEnabledMenu.IsChecked;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            ApplyLanguageIntelligenceSettings();
            Status.Text=languageSettings.SpellingEnabled?"Spelling marks enabled.":"Spelling marks hidden.";
        };
        var dictionary=new MenuItem{Header="_Personal Dictionary…"};
        dictionary.Click+=PersonalDictionary_Click;
        var language=new MenuItem{Header="Language && _Writing Mode…"};
        language.Click+=LanguageSettings_Click;

        writingModeMenu=new MenuItem{Header="Writing _Mode"};
        foreach(var profile in WritingModeProfiles.All)
        {
            var item=new MenuItem{Header=profile.DisplayName,IsCheckable=true,Tag=profile.Mode};
            item.Click+=(_,__)=>
            {
                languageSettings.WritingMode=(WritingMode)item.Tag;
                if(languageSettings.WritingMode!=WritingMode.Poetry)
                    languageSettings.PoetryRhymeSuggestions=false;
                LanguageIntelligenceSettingsStore.Save(languageSettings);
                UpdateLanguageMenuState();
                Status.Text="Writing mode: "+profile.DisplayName;
            };
            writingModeMenu.Items.Add(item);
        }

        var grammar=new MenuItem{Header="Grammar && Style Suggestions — Beta 32",IsEnabled=false};
        var thesaurus=new MenuItem{Header="Contextual Thesaurus — Beta 33",IsEnabled=false};
        var rhyme=new MenuItem{Header="Poetry Rhyme Assistant — Beta 33",IsEnabled=false};

        review.Items.Add(nextError);
        review.Items.Add(spellingEnabledMenu);
        review.Items.Add(dictionary);
        review.Items.Add(new Separator());
        review.Items.Add(language);
        review.Items.Add(writingModeMenu);
        review.Items.Add(new Separator());
        review.Items.Add(grammar);
        review.Items.Add(thesaurus);
        review.Items.Add(rhyme);

        var keyboardIndex=MainMenu.Items.OfType<object>().ToList().FindIndex(x=>x is MenuItem m && string.Equals(NormalisedMenuHeader(m.Header),"Keyboard",StringComparison.OrdinalIgnoreCase));
        MainMenu.Items.Insert(keyboardIndex>=0?keyboardIndex:MainMenu.Items.Count,review);
        UpdateLanguageMenuState();
    }

    void InstallWritingModeStatus()
    {
        if(Content is not DockPanel root)return;
        var statusBar=root.Children.OfType<StatusBar>().FirstOrDefault();
        if(statusBar==null)return;
        statusBar.Items.Add(new Separator());
        writingModeStatus=new TextBlock();
        statusBar.Items.Add(new StatusBarItem{Content=writingModeStatus});
        UpdateLanguageMenuState();
    }

    void UpdateLanguageMenuState()
    {
        if(spellingEnabledMenu!=null)spellingEnabledMenu.IsChecked=languageSettings.SpellingEnabled;
        if(writingModeMenu!=null)
        {
            foreach(var item in writingModeMenu.Items.OfType<MenuItem>())
                item.IsChecked=item.Tag is WritingMode mode && mode==languageSettings.WritingMode;
        }
        if(writingModeStatus!=null)
            writingModeStatus.Text=WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName;
    }

    void LanguagePreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Right || !Editor.IsMouseOver || !languageSettings.SpellingEnabled)return;
        var point=e.GetPosition(Editor);
        var pointer=Editor.GetPositionFromPoint(point,true);
        if(pointer==null)return;

        var spellingError=Editor.GetSpellingError(pointer);
        var errorRange=Editor.GetSpellingErrorRange(pointer);
        if(spellingError==null || errorRange==null)return;

        e.Handled=true;
        Editor.CaretPosition=pointer;
        var misspelt=errorRange.Text.Trim();
        var menu=new ContextMenu();
        var suggestions=spellingError.Suggestions.Cast<string>().Take(8).ToList();
        if(suggestions.Count==0)
            menu.Items.Add(new MenuItem{Header="(No spelling suggestions)",IsEnabled=false});
        else
        {
            foreach(var suggestion in suggestions)
            {
                var item=new MenuItem
                {
                    Header=suggestion,
                    Command=EditingCommands.CorrectSpellingError,
                    CommandParameter=suggestion,
                    CommandTarget=Editor,
                    FontWeight=FontWeights.SemiBold
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var ignoreAll=new MenuItem
        {
            Header="Ignore All in Document",
            Command=EditingCommands.IgnoreSpellingError,
            CommandTarget=Editor
        };
        menu.Items.Add(ignoreAll);
        var add=new MenuItem{Header=$"Add ‘{misspelt}’ to Personal Dictionary"};
        add.Click+=(_,__)=>
        {
            if(PersonalDictionaryStore.Add(misspelt))
            {
                ApplyLanguageIntelligenceSettings();
                Status.Text=$"Added ‘{misspelt}’ to the personal dictionary.";
            }
            else Status.Text=$"‘{misspelt}’ is already in the personal dictionary.";
        };
        menu.Items.Add(add);

        AddMacroSubmenu(menu);
        if(menu.Items.Count>0)menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem{Header="Cut",Command=ApplicationCommands.Cut,CommandTarget=Editor,InputGestureText="Ctrl+X"});
        menu.Items.Add(new MenuItem{Header="Copy",Command=ApplicationCommands.Copy,CommandTarget=Editor,InputGestureText="Ctrl+C"});
        menu.Items.Add(new MenuItem{Header="Paste",Command=ApplicationCommands.Paste,CommandTarget=Editor,InputGestureText="Ctrl+V"});
        menu.PlacementTarget=Editor;
        menu.IsOpen=true;
    }

    void NextSpellingError_Click(object? sender,RoutedEventArgs e)
    {
        if(!languageSettings.SpellingEnabled)
        {
            Status.Text="Spelling marks are disabled.";
            return;
        }

        var position=Editor.GetNextSpellingErrorPosition(Editor.CaretPosition,LogicalDirection.Forward);
        position??=Editor.GetNextSpellingErrorPosition(Editor.Document.ContentStart,LogicalDirection.Forward);
        if(position==null)
        {
            Status.Text="No spelling errors found.";
            return;
        }

        var range=Editor.GetSpellingErrorRange(position);
        if(range==null)
        {
            Status.Text="No spelling errors found.";
            return;
        }
        Editor.Selection.Select(range.Start,range.End);
        Editor.Focus();
        Status.Text="Spelling: "+range.Text.Trim()+" — right-click for suggestions.";
    }

    void LanguageSettings_Click(object? sender,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Language and Writing Mode",Owner=this,Width=650,Height=650,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Spelling",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var spelling=new CheckBox{Content="Show red spelling marks and right-click suggestions",IsChecked=languageSettings.SpellingEnabled,Margin=new Thickness(0,3,0,5)};
        var contextual=new CheckBox{Content="Use sentence context to improve spelling suggestions",IsChecked=languageSettings.ContextualSpellingSuggestions,Margin=new Thickness(0,3,0,5)};
        var autoCorrect=new CheckBox{Content="Automatically correct only high-confidence spelling after sentence completion",IsChecked=languageSettings.ContextualSpellingAutoCorrect,Margin=new Thickness(0,3,0,5)};
        var notice=new CheckBox{Content="Show a brief notice after an automatic correction",IsChecked=languageSettings.ShowAutomaticCorrectionNotice,Margin=new Thickness(20,0,0,5)};
        var allowAi=new CheckBox{Content="Allow AI language services when a local check is insufficient",IsChecked=languageSettings.AllowAiLanguageServices,Margin=new Thickness(0,3,0,5)};
        panel.Children.Add(spelling);panel.Children.Add(contextual);panel.Children.Add(autoCorrect);panel.Children.Add(notice);panel.Children.Add(allowAi);
        panel.Children.Add(new TextBlock
        {
            Text="Ordinary spelling remains local. Contextual correction is conservative, sentence-scoped, visible and immediately undoable. It is off by default.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,14)
        });

        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock{Text="Language",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        var language=new ComboBox{Margin=new Thickness(0,0,0,6),Width=250,HorizontalAlignment=HorizontalAlignment.Left};
        language.Items.Add(new ComboBoxItem{Content="English (United Kingdom)",Tag="en-GB",IsSelected=true});
        panel.Children.Add(language);
        panel.Children.Add(new TextBlock{Text="British English is the Beta 31 default. Further document languages can be added without changing the language-service design.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,14)});

        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock{Text="Writing mode",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        var mode=new ComboBox{ItemsSource=WritingModeProfiles.All,DisplayMemberPath="DisplayName",Width=310,HorizontalAlignment=HorizontalAlignment.Left};
        mode.SelectedItem=WritingModeProfiles.Get(languageSettings.WritingMode);
        var modeDescription=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,7,0,8)};
        void RefreshDescription()
        {
            if(mode.SelectedItem is WritingModeProfile profile)modeDescription.Text=profile.Description;
        }
        mode.SelectionChanged+=(_,__)=>RefreshDescription();RefreshDescription();
        panel.Children.Add(mode);panel.Children.Add(modeDescription);

        var dictionary=new Button{Content="Manage Personal Dictionary…",Width=190,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,4)};
        dictionary.Click+=PersonalDictionary_Click;
        panel.Children.Add(dictionary);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        var apply=new Button{Content="Apply",Width=85,Margin=new Thickness(4),IsDefault=true};
        var close=new Button{Content="Close",Width=85,Margin=new Thickness(4),IsCancel=true};
        apply.Click+=(_,__)=>
        {
            languageSettings.LanguageTag="en-GB";
            languageSettings.SpellingEnabled=spelling.IsChecked==true;
            languageSettings.ContextualSpellingSuggestions=contextual.IsChecked==true;
            languageSettings.ContextualSpellingAutoCorrect=autoCorrect.IsChecked==true;
            languageSettings.ShowAutomaticCorrectionNotice=notice.IsChecked==true;
            languageSettings.AllowAiLanguageServices=allowAi.IsChecked==true;
            if(mode.SelectedItem is WritingModeProfile profile)languageSettings.WritingMode=profile.Mode;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            ApplyLanguageIntelligenceSettings();
            Status.Text="Language settings applied — "+WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName+".";
        };
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(apply);buttons.Children.Add(close);panel.Children.Add(buttons);
        w.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        w.ShowDialog();
    }

    void PersonalDictionary_Click(object? sender,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Personal Dictionary — English (United Kingdom)",Owner=this,Width=520,Height=520,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new DockPanel{Margin=new Thickness(14)};
        var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var entryRow=new DockPanel();
        var add=new Button{Content="Add",Width=75,Margin=new Thickness(8,0,0,0)};
        DockPanel.SetDock(add,Dock.Right);entryRow.Children.Add(add);
        var entry=new TextBox{MinWidth=280};entryRow.Children.Add(entry);
        bottom.Children.Add(entryRow);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var remove=new Button{Content="Remove Selected",Width=120,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        buttons.Children.Add(remove);buttons.Children.Add(close);bottom.Children.Add(buttons);
        var list=new ListBox();root.Children.Add(list);
        void Refresh()=>list.ItemsSource=PersonalDictionaryStore.LoadWords();
        add.Click+=(_,__)=>
        {
            if(PersonalDictionaryStore.Add(entry.Text))
            {
                entry.Clear();Refresh();ApplyLanguageIntelligenceSettings();
                Status.Text="Word added to personal dictionary.";
            }
        };
        entry.KeyDown+=(_,args)=>
        {
            if(args.Key==Key.Enter){add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));args.Handled=true;}
        };
        remove.Click+=(_,__)=>
        {
            if(list.SelectedItem is string word && PersonalDictionaryStore.Remove(word))
            {
                Refresh();ApplyLanguageIntelligenceSettings();
                Status.Text="Word removed from personal dictionary.";
            }
        };
        close.Click+=(_,__)=>w.Close();
        Refresh();w.Content=root;w.ShowDialog();
    }
}
