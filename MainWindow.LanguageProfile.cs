namespace CtrlZed;

public partial class MainWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        LanguageProfileBridge.ReloadRequested+=ReloadLanguageProfile;
        Closed+=(_,__)=>LanguageProfileBridge.ReloadRequested-=ReloadLanguageProfile;
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
