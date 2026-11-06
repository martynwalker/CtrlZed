namespace CtrlZed;

public static class Beta32SelfTest
{
    public static int Run()
    {
        try
        {
            var general=WritingModeProfiles.Get(WritingMode.General);
            var duplicate=GrammarReviewEngine.Analyse("This is is clear.",general);
            if(!duplicate.Any(x=>x.RuleId=="duplicate-word" && x.Replacement=="is"))return 45;

            var technical=WritingModeProfiles.Get(WritingMode.Technical);
            var technicalFindings=GrammarReviewEngine.Analyse("We should of do this in order to finish.",technical);
            if(!technicalFindings.Any(x=>x.RuleId=="modal-of" && x.Replacement=="should have"))return 46;
            if(!technicalFindings.Any(x=>x.RuleId=="wordy-in-order-to" && x.Replacement=="to"))return 47;

            var formal=WritingModeProfiles.Get(WritingMode.FormalLetter);
            var formalFindings=GrammarReviewEngine.Analyse("We can't proceed.",formal);
            if(!formalFindings.Any(x=>x.RuleId=="formal-contraction" && x.Replacement=="cannot"))return 48;

            var novel=WritingModeProfiles.Get(WritingMode.Novel);
            var novelFindings=GrammarReviewEngine.Analyse("We can't proceed.",novel);
            if(novelFindings.Any(x=>x.RuleId=="formal-contraction"))return 49;

            var poetry=WritingModeProfiles.Get(WritingMode.Poetry);
            var poetryFindings=GrammarReviewEngine.Analyse("Bright!!  Night",poetry);
            if(poetryFindings.Any(x=>x.RuleId is "repeated-punctuation" or "multiple-spaces"))return 50;

            var lower=GrammarReviewEngine.Analyse("this begins quietly.",general);
            if(!lower.Any(x=>x.RuleId=="sentence-capital" && x.Replacement=="T"))return 51;

            var british=GrammarReviewEngine.Analyse("This is different than that.",general);
            if(!british.Any(x=>x.RuleId=="british-different-from" && x.Replacement=="different from"))return 52;

            return 0;
        }
        catch
        {
            return 59;
        }
    }
}
