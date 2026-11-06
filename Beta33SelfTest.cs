namespace CtrlZed;

public static class Beta33SelfTest
{
    public static int Run()
    {
        try
        {
            var thesaurus=ContextualWordChoiceService.ParseThesaurusResponse(
                """
                {"alternatives":[
                  {"replacement":"lucid","nuance":"especially easy to understand","register":"formal","explanation":"Fits an explanation or argument.","syllables":2},
                  {"replacement":"clear","nuance":"duplicate","register":"neutral","explanation":"Must be rejected because it repeats the source.","syllables":1},
                  {"replacement":"plain","nuance":"direct and uncomplicated","register":"neutral","explanation":"Useful where simplicity is intended.","syllables":1}
                ]}
                ""","clear");
            if(thesaurus.Count!=2)return 60;
            if(thesaurus[0].Replacement!="lucid" || thesaurus[0].Syllables!=2)return 61;
            if(!thesaurus.Any(x=>x.Replacement=="plain"))return 62;

            var lines=new[]{"The distant harbour shone with light","We crossed the water in the night"};
            var rhyme=ContextualWordChoiceService.ParseRhymeResponse(
                """
                {"scheme":"AA","summary":"The couplet already uses an exact rhyme.","opportunities":[
                  {"line":1,"original":"light","replacement":"bright","rhymesWithLine":2,"rhymesWith":"night","rhymeType":"exact","explanation":"Retains the image and strengthens the adjective.","syllables":1},
                  {"line":3,"original":"invalid","replacement":"wrong","rhymesWithLine":1,"rhymesWith":"light","rhymeType":"exact","explanation":"Out-of-range line must be rejected.","syllables":1}
                ]}
                """,lines);
            if(rhyme.Scheme!="AA" || rhyme.Opportunities.Count!=1)return 63;
            var opportunity=rhyme.Opportunities[0];
            if(opportunity.LineNumber!=1 || opportunity.Replacement!="bright" || opportunity.RhymesWithLine!=2)return 64;
            if(opportunity.RhymeType!="exact" || opportunity.Syllables!=1)return 65;

            return 0;
        }
        catch
        {
            return 69;
        }
    }
}
