using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        LanguageProfileBridge.ReloadRequested+=ReloadLanguageProfile;
        AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(LanguageReviewPreviewKeyDown),true);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(() =>
        {
            WireReviewShortcutMenu();
            WireSettingsMenu();
            AboutMenu.Click-=About_Click;
            AboutMenu.Click+=Beta31About_Click;
        }));
        Closed+=(_,__)=>LanguageProfileBridge.ReloadRequested-=ReloadLanguageProfile;
    }

    void WireSettingsMenu()
    {
        var fileMenu=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"File",StringComparison.OrdinalIgnoreCase));
        if(fileMenu==null)return;

        var separateLanguage=fileMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedLanguageSettings");
        if(separateLanguage!=null)fileMenu.Items.Remove(separateLanguage);

        var settings=fileMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>NormalisedMenuHeader(x.Header).StartsWith("Settings",StringComparison.OrdinalIgnoreCase));
        if(settings==null || (settings.Tag as string)=="CtrlZedSettingsMenu")return;

        settings.Click-=Settings_Click;
        settings.Header="_Settings";
        settings.Tag="CtrlZedSettingsMenu";
        settings.Items.Clear();

        var general=new MenuItem{Header="_General…"};
        general.Click+=Settings_Click;
        var language=new MenuItem{Header="_Language && Writing Mode…"};
        language.Click+=LanguageSettings_Click;
        settings.Items.Add(general);
        settings.Items.Add(language);
    }

    void WireReviewShortcutMenu()
    {
        var review=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review==null)return;

        void RefreshShortcut()
        {
            var next=review.Items.OfType<MenuItem>().FirstOrDefault();
            if(next!=null)next.InputGestureText=profile==KeyboardProfile.Word?"F7":"";
        }

        review.SubmenuOpened+=(_,__)=>RefreshShortcut();
        RefreshShortcut();
    }

    void LanguageReviewPreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(profile!=KeyboardProfile.Word || Keyboard.Modifiers!=ModifierKeys.None || e.Key!=Key.F7)return;
        NextSpellingError_Click(this,new RoutedEventArgs());
        e.Handled=true;
    }

    void Beta31About_Click(object sender,RoutedEventArgs e)
        =>Branding.CreateAboutWindow(this,"0.31.0 beta 31").ShowDialog();

    void ReloadLanguageProfile()
    {
        var value=LanguageIntelligenceSettingsStore.Load();
        languageSettings.FormatVersion=value.FormatVersion;
        languageSettings.LanguageTag=value.LanguageTag;
        languageSettings.SpellingEnabled=value.SpellingEnabled;
        languageSettings.ContextualSpellingSuggestions=value.ContextualSpellingSuggestions;
        languageSettings.ContextualSpellingAutoCorrect=value.ContextualSpellingAutoCorrect;
        languageSettings.GrammarAndStyleSuggestions=value.GrammarAndStyleSuggestions;
        languageSettings.ContextualThesaurus=value.ContextualThesaurus;
        languageSettings.PoetryRhymeSuggestions=value.PoetryRhymeSuggestions;
        languageSettings.ShowAutomaticCorrectionNotice=value.ShowAutomaticCorrectionNotice;
        languageSettings.AllowAiLanguageServices=value.AllowAiLanguageServices;
        languageSettings.SentenceCompletionDelayMs=value.SentenceCompletionDelayMs;
        languageSettings.WritingMode=value.WritingMode;
        languageSettings.CustomProfileName=value.CustomProfileName;
        ApplyLanguageIntelligenceSettings();
        UpdateLanguageMenuState();
        Status.Text="Language settings and personal dictionary restored from the imported profile.";
    }
}
