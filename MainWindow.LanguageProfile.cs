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
        ContentRendered+=(_,__)=>WireReviewShortcutMenu();
        Closed+=(_,__)=>LanguageProfileBridge.ReloadRequested-=ReloadLanguageProfile;
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
