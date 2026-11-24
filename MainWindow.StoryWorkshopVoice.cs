using System.Text;
using System.Windows;

namespace CtrlZed;

public partial class MainWindow
{
    Action? releaseStoryWorkshopVoicePreview;

    /// <summary>Open a local personality preview; generation remains the writer's explicit choice.</summary>
    internal void OpenStoryWorkshopCharacterVoice(StoryWorkshopEntry character)
    {
        try
        {
            var state=SnapshotStoryWorkshop();
            var current=state.Characters.FirstOrDefault(item=>item.Id==character.Id)
                ??throw new InvalidOperationException("This character is no longer in the document's Story Workshop.");
            var selection=CreateStoryWorkshopCharacterVoice(state,current);
            ShowCharacterVoice(selection.Author,selection.Voice,state.WritingMode);
            if(characterVoiceWindow is not { } preview)return;

            releaseStoryWorkshopVoicePreview?.Invoke();
            void Changed()
            {
                if(ReferenceEquals(characterVoiceWindow,preview))
                {
                    preview.Close();
                    Status.Text="Story character notes changed. Reopen Character Voice to use the current profile.";
                }
            }
            void Closed(object? sender,EventArgs args)=>Release();
            void Release()
            {
                StoryWorkshopChanged-=Changed;StoryWorkshopDocumentChanged-=Changed;preview.Closed-=Closed;
                releaseStoryWorkshopVoicePreview=null;
            }
            StoryWorkshopChanged+=Changed;StoryWorkshopDocumentChanged+=Changed;preview.Closed+=Closed;
            releaseStoryWorkshopVoicePreview=Release;
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.IO.InvalidDataException or ArgumentException)
        {
            MessageBox.Show(this,ex.Message,"Story character voice",MessageBoxButton.OK,MessageBoxImage.Information);
        }
    }

    internal static (VoiceAuthor Author,CharacterVoice Voice) CreateStoryWorkshopCharacterVoice(
        StoryWorkshopState state,StoryWorkshopEntry character)
    {
        state.Validate();
        if(character.Kind!=StoryWorkshopEntryKind.Character)
            throw new InvalidOperationException("Choose a character belonging to this Story Workshop.");
        character=state.Characters.FirstOrDefault(item=>item.Id==character.Id)
            ??throw new InvalidOperationException("Choose a character belonging to this Story Workshop.");
        var name=StoryWorkshopVoiceSingleLine(character.Name);
        if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("Give this character a name before opening Character Voice.");
        if(name.Length>120)throw new InvalidOperationException("Character Voice needs a display name of no more than 120 characters. Shorten the character's name in Story Workshop first.");

        var author=new VoiceAuthor("story-"+state.Id,"This document's characters",
            "Original characters from this document's Story Workshop. Review the editable personality notes before rewriting.");
        var fields=new[]{"VoiceRhythm","Traits","MoralCode","Baseline","PositiveTriggers","NegativeTriggers",
            "SignatureTactics","DominantTrait","RedeemingQuality","FatalFlaw","Psychology","Sociology","Physiology","Role"};
        var descriptors=StoryWorkshopSchema.CharacterFields.ToDictionary(field=>field.Key,StringComparer.Ordinal);
        var text=new StringBuilder("An original character from this document. Use personality and manner of expression without inventing events, knowledge or a new plot. Notes marked Proposed are possibilities for review.\n");
        if(character.Status==StoryWorkshopValueStatus.Proposed)text.AppendLine("The character's name is Proposed.");
        var selected=fields.Where(key=>character.Fields.TryGetValue(key,out var value) && !string.IsNullOrWhiteSpace(value.Text)).ToArray();
        var shortened=false;
        const int contentLimit=3650;
        foreach(var key in selected)
        {
            var value=character.Fields[key];
            var label=descriptors[key].Label+(value.Status==StoryWorkshopValueStatus.Proposed?" [Proposed]":" [Confirmed]")+": ";
            var available=contentLimit-text.Length-label.Length-2;
            if(available<40){shortened=true;break;}
            // Give every selected trait a fair share of the preview; do not let
            // one long note hide all of the character's contrasting behaviours.
            var share=Math.Max(80,(contentLimit-text.Length)/Math.Max(1,selected.Length-Array.IndexOf(selected,key))-label.Length-2);
            var maximum=Math.Min(available,share);
            var note=value.Text.Trim();
            if(note.Length>maximum)
            {
                var cut=Math.Max(1,maximum-12);
                if(cut<note.Length && cut>0 && char.IsHighSurrogate(note[cut-1]))cut--;
                note=note[..cut]+" [excerpt]";shortened=true;
            }
            text.Append(label).AppendLine(note);
        }
        if(selected.Length==0)text.AppendLine("No personality notes have been supplied yet. Add traits or voice and rhythm in Story Workshop, or edit this preview before rewriting.");
        if(shortened)text.AppendLine("Some notes are excerpted or omitted to fit this voice preview; the complete notes remain in Story Workshop. Review before Rewrite.");
        var aliases=character.Fields.TryGetValue("Aliases",out var alias)?StoryWorkshopVoiceSingleLine(alias.Text):"";
        if(alias?.Status==StoryWorkshopValueStatus.Proposed && aliases.Length>0)
        {
            aliases="";
            text.AppendLine("Proposed aliases remain unresolved in Story Workshop and are excluded from this preview.");
        }
        else if(aliases.Length>300)
        {
            aliases=""; // The character remains searchable by its full display name.
            text.AppendLine("Long alias notes are omitted here; their complete text remains in Story Workshop.");
        }
        text.Append("Plot, clues and future character-arc milestones are excluded from this personality preview.");
        var voice=new CharacterVoice("story-character-"+character.Id,author.Id,name,"Current Story Workshop document",aliases,text.ToString());
        if(voice.Profile.Length>4000)throw new InvalidOperationException("The character voice preview exceeds its supported length. Shorten the personality notes first.");
        return (author,voice);
    }

    static string StoryWorkshopVoiceSingleLine(string text)=>text.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ').Trim();
}
