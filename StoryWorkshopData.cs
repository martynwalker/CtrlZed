using System.IO;
using System.Text.Json.Serialization;

namespace CtrlZed;

public enum StoryWorkshopValueStatus { Proposed, Confirmed }
public enum StoryWorkshopEntryKind { Character, Beat, Clue }
public enum StoryWorkshopTurnRole { User, Assistant }

/// <summary>A writer decision or an explicitly identified suggestion.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StoryWorkshopValue
{
    public string Text { get; set; } = "";
    public StoryWorkshopValueStatus Status { get; set; } = StoryWorkshopValueStatus.Proposed;
}

/// <summary>Stable identity keeps a renamed character, event or clue attached to its notes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StoryWorkshopEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StoryWorkshopEntryKind Kind { get; set; } = StoryWorkshopEntryKind.Character;
    public string Name { get; set; } = "";
    public StoryWorkshopValueStatus Status { get; set; } = StoryWorkshopValueStatus.Proposed;
    public Dictionary<string, StoryWorkshopValue> Fields { get; set; } = new(StringComparer.Ordinal);
    public List<string> CharacterIds { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StoryWorkshopTurn
{
    public StoryWorkshopTurnRole Role { get; set; }
    public string Text { get; set; } = "";
}

public sealed record StoryWorkshopField(string Key, string Label, string Group, string Hint,
    int MaximumLength = StoryWorkshopState.MaximumFieldLength);

/// <summary>The bounded planning record saved inside a native document, never app preferences.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StoryWorkshopState
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumTotalCharacters = 256000;
    public const int MaximumFieldLength = 4000;
    public const int MaximumTurnLength = 12000;
    public const int MaximumCharacters = 64;
    public const int MaximumBeats = 160;
    public const int MaximumClues = 160;
    public const int MaximumTurns = 120;
    public const int MaximumQuestions = 32;
    public const int MaximumQuestionLength = 2000;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WritingMode WritingMode { get; set; } = WritingMode.Novel;
    public long Revision { get; set; }
    public Dictionary<string, StoryWorkshopValue> Story { get; set; } = new(StringComparer.Ordinal);
    public List<StoryWorkshopEntry> Characters { get; set; } = new();
    public List<StoryWorkshopEntry> Beats { get; set; } = new();
    public List<StoryWorkshopEntry> Clues { get; set; } = new();
    public List<StoryWorkshopTurn> Conversation { get; set; } = new();
    public List<string> OpenQuestions { get; set; } = new();
    public List<string> Contradictions { get; set; } = new();

    // Opening the empty Novel workshop is not itself a document edit. A chosen
    // non-default mode, a draft card or a conversation is meaningful metadata.
    [JsonIgnore]
    public bool HasContent => WritingMode != WritingMode.Novel ||
        Story?.Values.Any(value => !string.IsNullOrWhiteSpace(value?.Text)) == true ||
        (Characters?.Count ?? 0) > 0 || (Beats?.Count ?? 0) > 0 || (Clues?.Count ?? 0) > 0 ||
        (Conversation?.Count ?? 0) > 0 || (OpenQuestions?.Count ?? 0) > 0 || (Contradictions?.Count ?? 0) > 0;

    /// <summary>Reject malformed or oversized metadata; never truncate the writer's planning.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw Invalid("This document uses an unsupported Story Workshop metadata version.");
        if (!Enum.IsDefined(WritingMode) || Revision < 0)
            throw Invalid("The Story Workshop mode or revision is invalid.");
        ValidateId(Id, "story");
        long characters = Id.Length;
        ValidateValues(Story, StoryWorkshopSchema.StoryFields, "story", ref characters);
        var ids = new HashSet<string>(StringComparer.Ordinal) { Id };
        ValidateEntries(Characters, StoryWorkshopEntryKind.Character, MaximumCharacters, ids, ref characters);
        ValidateEntries(Beats, StoryWorkshopEntryKind.Beat, MaximumBeats, ids, ref characters);
        ValidateEntries(Clues, StoryWorkshopEntryKind.Clue, MaximumClues, ids, ref characters);
        var characterIds = Characters.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Characters.Concat(Beats).Concat(Clues))
        {
            if (entry.CharacterIds == null || entry.CharacterIds.Count > MaximumCharacters ||
                entry.CharacterIds.Distinct(StringComparer.Ordinal).Count() != entry.CharacterIds.Count)
                throw Invalid("Story Workshop character links must be a bounded list of distinct identities.");
            foreach (var id in entry.CharacterIds)
            {
                ValidateId(id, "linked character");
                if (!characterIds.Contains(id))
                    throw Invalid("A Story Workshop card refers to a character that is not in this document.");
                characters += id.Length;
            }
        }
        if (Conversation == null || Conversation.Count > MaximumTurns)
            throw Invalid($"Story Workshop holds up to {MaximumTurns} conversation turns. Keep the planning notes before starting a fresh conversation.");
        foreach (var turn in Conversation)
        {
            if (turn == null || !Enum.IsDefined(turn.Role))
                throw Invalid("A Story Workshop conversation turn is invalid.");
            ValidateText(turn.Text, MaximumTurnLength, "conversation turn", allowEmpty: false);
            characters += turn.Text.Length;
        }
        ValidateQuestions(OpenQuestions, "open questions", ref characters);
        ValidateQuestions(Contradictions, "continuity questions", ref characters);
        if (characters > MaximumTotalCharacters)
            throw Invalid($"Story Workshop notes and conversation exceed {MaximumTotalCharacters:N0} characters. Keep a shorter planning record; no text has been discarded.");
    }

    public StoryWorkshopState DeepClone()
    {
        Validate();
        return new StoryWorkshopState
        {
            SchemaVersion = SchemaVersion, Id = Id, WritingMode = WritingMode, Revision = Revision,
            Story = CloneValues(Story), Characters = Characters.Select(CloneEntry).ToList(),
            Beats = Beats.Select(CloneEntry).ToList(), Clues = Clues.Select(CloneEntry).ToList(),
            Conversation = Conversation.Select(turn => new StoryWorkshopTurn { Role = turn.Role, Text = turn.Text }).ToList(),
            OpenQuestions = new List<string>(OpenQuestions), Contradictions = new List<string>(Contradictions)
        };
    }

    public static StoryWorkshopValue CloneValue(StoryWorkshopValue value)
        => new() { Text = value.Text, Status = value.Status };

    public static StoryWorkshopEntry CloneEntry(StoryWorkshopEntry entry)
        => new() { Id = entry.Id, Kind = entry.Kind, Name = entry.Name, Status = entry.Status,
            Fields = CloneValues(entry.Fields), CharacterIds = new List<string>(entry.CharacterIds) };

    static Dictionary<string, StoryWorkshopValue> CloneValues(Dictionary<string, StoryWorkshopValue> values)
        => values.ToDictionary(pair => pair.Key, pair => CloneValue(pair.Value), StringComparer.Ordinal);

    static void ValidateEntries(List<StoryWorkshopEntry>? entries, StoryWorkshopEntryKind kind,
        int maximum, HashSet<string> ids, ref long characters)
    {
        if (entries == null || entries.Count > maximum)
            throw Invalid($"Story Workshop supports up to {maximum} {kind.ToString().ToLowerInvariant()} cards.");
        foreach (var entry in entries)
        {
            if (entry == null || entry.Kind != kind || !Enum.IsDefined(entry.Status))
                throw Invalid("A Story Workshop card has an invalid kind or decision status.");
            ValidateId(entry.Id, kind.ToString());
            if (!ids.Add(entry.Id)) throw Invalid("Story Workshop card identities must be unique.");
            ValidateText(entry.Name, 160, "card name");
            characters += entry.Id.Length + entry.Name.Length;
            ValidateValues(entry.Fields, StoryWorkshopSchema.For(kind), kind.ToString(), ref characters);
        }
    }

    static void ValidateValues(Dictionary<string, StoryWorkshopValue>? values,
        IReadOnlyList<StoryWorkshopField> fields, string scope, ref long characters)
    {
        if (values == null || values.Count > fields.Count)
            throw Invalid($"The {scope} field list is invalid.");
        var allowed = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!allowed.TryGetValue(key, out var field) || value == null || !Enum.IsDefined(value.Status))
                throw Invalid($"The {scope} metadata contains an unknown field or invalid decision status.");
            ValidateText(value.Text, field.MaximumLength, field.Label);
            characters += key.Length + value.Text.Length;
        }
    }

    static void ValidateQuestions(List<string>? questions, string label, ref long characters)
    {
        if (questions == null || questions.Count > MaximumQuestions)
            throw Invalid($"Keep no more than {MaximumQuestions} {label} in Story Workshop.");
        foreach (var question in questions)
        {
            ValidateText(question, MaximumQuestionLength, label, allowEmpty: false);
            // The structured editor represents each question as one line.
            // Retain multiline prose in fields/conversation, not in a single
            // list item that would be split into different records on editing.
            if (question.Contains('\r') || question.Contains('\n'))
                throw Invalid("Keep each Story Workshop question or continuity item on one line.");
            characters += question.Length;
        }
    }

    internal static void ValidateId(string? id, string label)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 ||
            id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw Invalid($"The {label} identity is invalid.");
    }

    internal static void ValidateText(string? text, int maximum, string label, bool allowEmpty = true)
    {
        if (text == null || text.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(text))
            throw Invalid($"Keep {label} within {(allowEmpty ? 0 : 1)}–{maximum:N0} characters.");
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\uFFFC' || ch is '\u2028' or '\u2029' ||
                char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
                throw Invalid($"The {label} contains unsupported control characters or embedded objects.");
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    throw Invalid($"The {label} contains invalid Unicode.");
                i++;
            }
            else if (char.IsLowSurrogate(ch)) throw Invalid($"The {label} contains invalid Unicode.");
        }
    }

    static InvalidDataException Invalid(string message) => new(message);
}

/// <summary>Field groups drive the editable storyboard and the AI's accepted schema.</summary>
public static class StoryWorkshopSchema
{
    public static IReadOnlyList<StoryWorkshopField> StoryFields { get; } = Array.AsReadOnly(new[]
    {
        F("Title", "Working title", "Story", "A working title may change as the story develops."),
        F("Premise", "Premise", "Story", "Who is this about, what happens, and why does it matter?"),
        F("Genre", "Genre and tone", "Story", "The kind of story and the experience intended for the reader."),
        F("Setting", "Setting", "Story", "Place, period and relevant limits of this story's world."),
        F("Themes", "Themes", "Story", "Questions or tensions explored through events and character choices."),
        F("PlotSummary", "Story outline", "Story", "An evolving summary, retaining uncertainties rather than filling every gap."),
        F("ClimaxTwist", "Twist and revelation", "Twist and logic", "Anchor the exact revelation; distinguish what is settled from possible alternatives."),
        F("FalsePremise", "False premise", "Twist and logic", "What the protagonist and reader initially believe, and why it appears credible."),
        F("PersonalHook", "Why this protagonist?", "Twist and logic", "The personal stake that makes this character vulnerable to this particular problem."),
        F("AntagonistStrategy", "Opposition and manipulation", "Twist and logic", "How an opponent uses the protagonist's strengths, flaws or assumptions."),
        F("TickingClock", "Ticking clock", "Twist and logic", "Why must action happen now, and what will happen if it does not?"),
        F("DreamMeaning", "Dreams, memories and ambiguity", "Twist and logic", "If relevant: their cause, reliability and how their meaning changes. Leave unresolved when the author has not decided."),
        F("Ending", "Ending and aftermath", "Twist and logic", "What is resolved, what changes and what remains open."),
        F("ContinuityNotes", "Continuity and causal logic", "Twist and logic", "Rules, chronology, name questions and consequences that must remain consistent."),
        F("Hook", "Hook and opening", "Five plot anchors", "The opening disturbance, image or event that invites the reader into the story."),
        F("Commitment", "Commitment / first plot point", "Five plot anchors", "The choice that makes withdrawal difficult and commits the protagonist to the problem."),
        F("Midpoint", "Midpoint shift", "Five plot anchors", "A revelation, false victory or reversal that changes the protagonist's approach."),
        F("Crisis", "Crisis / second plot point", "Five plot anchors", "The low point and decisive internal choice before the final confrontation."),
        F("Climax", "Climax and reversal", "Five plot anchors", "The final action and revelation that test the protagonist's beliefs and choices.")
    });

    public static IReadOnlyList<StoryWorkshopField> CharacterFields { get; } = Array.AsReadOnly(new[]
    {
        F("Aliases", "Other names", "Character", "Nicknames and established aliases; a possible name inconsistency belongs in a question until resolved."),
        F("Role", "Role in the story", "Character", "Protagonist, antagonist, ally, witness or another role chosen by the author."),
        F("Traits", "Traits and contradictions", "Character", "Describe contrasting behaviour and the circumstances that reveal it, rather than adjectives alone."),
        F("Relationships", "Relationships", "Character", "Connections, loyalties, tensions and what changes between these people."),
        F("Physiology", "Physiology — body", "Three dimensions", "Age, build, posture, scars, health and physical habits that matter on the page."),
        F("Sociology", "Sociology — environment", "Three dimensions", "Upbringing, class, occupation, education and cultural context."),
        F("Psychology", "Psychology — mind", "Three dimensions", "Temperament, intelligence, insecurities, moral outlook and emotional responses."),
        F("MoralCode", "Core moral code", "Contrasts and triggers", "The line this character will not cross, and situations that test it."),
        F("Baseline", "Baseline demeanour", "Contrasts and triggers", "How the character behaves when little is at stake."),
        F("PositiveTriggers", "What softens them", "Contrasts and triggers", "People or situations that bring out patience, warmth, loyalty or restraint."),
        F("NegativeTriggers", "What sets them off", "Contrasts and triggers", "Actions or situations that trigger anger, fear, withdrawal or destructive habits."),
        F("SignatureTactics", "Signature tactics — voice and action", "Contrasts and triggers", "What the character notices, says and does when facing conflict."),
        F("DominantTrait", "Dominant trait", "Core four", "The primary outward quality the reader encounters."),
        F("RedeemingQuality", "Redeeming quality", "Core four", "Why a reader may care about the character despite their flaws."),
        F("FatalFlaw", "Fatal flaw", "Core four", "The trait or habit that repeatedly creates trouble or limits their choices."),
        F("VoiceRhythm", "Voice and rhythm", "Core four", "Vocabulary, rhythm, humour, silence and how speech changes with the audience."),
        F("Want", "The Want", "1 · Starting state", "The tangible external goal pursued at the start."),
        F("Need", "The Need", "1 · Starting state", "The deeper internal change the character may not yet recognise."),
        F("Ghost", "The Ghost", "1 · Starting state", "A past wound or formative experience; leave it open rather than inventing a trauma."),
        F("Lie", "The Lie", "1 · Starting state", "The false belief that keeps the character from their deeper need."),
        F("Disruption", "Disruption / inciting incident", "2 · Incident and resistance", "The event that unsettles the character's existing life."),
        F("Reluctance", "Resistance and old methods", "2 · Incident and resistance", "How the character resists change or tries their old worldview on the new problem."),
        F("EscalatingStakes", "Escalating stakes", "3 · Rising action and testing", "Increasing pressure, harder decisions and consequences."),
        F("Growth", "Glimpses of growth", "3 · Rising action and testing", "Experiments with new behaviour, often helped or challenged by others."),
        F("Midpoint", "Midpoint shift", "3 · Rising action and testing", "The change from reacting to acting, or an equivalent shift in this character's arc."),
        F("AllIsLost", "All-is-lost moment", "4 · Crisis", "A major defeat and how the character's old habits contributed to it."),
        F("InternalChoice", "The internal choice", "4 · Crisis", "The decisive choice between clinging to the old belief and accepting a deeper need."),
        F("ProofOfChange", "Proof of change", "5 · Climax and resolution", "A consequential action they could not have taken at the beginning; other arc shapes may resist or reverse change."),
        F("NewNormal", "The new normal", "5 · Climax and resolution", "Who the character has become and how the ending contrasts with the beginning.")
    });

    public static IReadOnlyList<StoryWorkshopField> BeatFields { get; } = Array.AsReadOnly(new[]
    {
        F("Summary", "Event or situation", "Event", "What happens; keep a proposed scene distinct from an agreed event."),
        F("Purpose", "Story purpose", "Event", "Why this event belongs in the story and which character pressure it develops."),
        F("Location", "Location", "Event", "Where it happens, when relevant."),
        F("Chronology", "Timing and order", "Event", "Its position in the story and any relevant real chronology."),
        F("Conflict", "Conflict and choice", "Event", "The pressure and decision that make this more than an isolated incident."),
        F("Consequence", "Consequence", "Event", "What changes because of the event."),
        F("Setup", "Setup", "Event", "What must already be true for this event to work."),
        F("Payoff", "Payoff", "Event", "Which earlier promise, clue or choice is answered here.")
    });

    public static IReadOnlyList<StoryWorkshopField> ClueFields { get; } = Array.AsReadOnly(new[]
    {
        F("FirstReading", "First reading", "Fair-play clue", "What the clue appears to mean before the revelation."),
        F("TrueMeaning", "True meaning", "Fair-play clue", "What a reread should reveal, consistent with what was actually shown."),
        F("Placement", "Placement", "Fair-play clue", "Where the clue is introduced and who can observe it."),
        F("Payoff", "Payoff and revelation", "Fair-play clue", "When and how its real significance becomes clear."),
        F("Fairness", "Fair-play check", "Fair-play clue", "What makes the initial interpretation plausible without concealing essential evidence unfairly.")
    });

    public static IReadOnlyList<StoryWorkshopField> For(StoryWorkshopEntryKind kind) => kind switch
    {
        StoryWorkshopEntryKind.Character => CharacterFields,
        StoryWorkshopEntryKind.Beat => BeatFields,
        StoryWorkshopEntryKind.Clue => ClueFields,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    static StoryWorkshopField F(string key, string label, string group, string hint) => new(key, label, group, hint);
}
