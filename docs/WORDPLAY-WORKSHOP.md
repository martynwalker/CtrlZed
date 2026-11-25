# Wordplay Workshop — Beta 53.1

Choose **Review → Wordplay Workshop…**, or use the text right-click menu. Pile words, images, sentences or unfinished fragments into the source box. There is no required list syntax. A fresh workshop can show the current selection as a starting point without making a document edit; **Use current selection** explicitly takes a new selection. Opening and editing make no AI requests.

All choices begin at **General**. They are independent, so philosophical wordplay need not become comedy, and a song can use deadpan humour without requiring rhyme.

| Choice | Purpose and examples |
| --- | --- |
| Form / genre | General, Poetry, Song, Novel, Short story, Dialogue or Speech |
| Style | General, Philosophical, Comic, Serious, Reflective, Surreal, Dark or Lyrical |
| Comic flavour | General, Subtle and understated, Deadpan, Sarcastic, Ironic, Self-deprecating, Cringe or Awkward |
| Wordplay device | General, Banter, Pun, Highbrow, Anti-humour, Double meaning, Paradox, Metaphor or Soundplay |
| Subject and target | General, Observational, Satire, Parody or Insult; add an optional target or intention |
| Physical / situational | General, Situational, Slapstick, Satirical situations, Character comedy or Farce |

Choose **Explore** to request six directions, each with two phrase examples. A direction identifies source anchors, describes the association between them and gives possible wording. The point is to discover connections through meaning, sound, imagery, contrast or a reversal of expectations, rather than merely replacing words with synonyms. General permits a varied, unforced response.

Select a direction and an example, then edit the phrase. **Copy** copies the edited wording as plain text. **Keep favourite** stores that wording with its source anchors, association, options and target. Favourites remain available when you change the source pile or explore again; their provenance belongs to the original experiment. Use a favourite to bring it back to the editable phrase. Its original connection and choices remain available in the Favourites details. Removing a favourite can be reversed with Undo remove during the current session. Temporary edits to an example are kept only when you save a favourite or apply/copy them.

Applying to the manuscript is a separate, explicit action. The window captures the original selection or caret before taking focus. Its destination description and button say whether applying will replace that selection or insert at the caret. **Use current destination** captures a different destination without changing the source pile. A manuscript edit invalidates the old destination; it does not erase your brainstorms. Applying uses the destination's opening format and is one native Undo operation. Caret insertion uses the exact edited wording; include any desired spaces at its edges. Existing field/object/table-boundary safeguards apply.

Changing the source pile, options or target clears the current generated directions and cancels a pending request; favourites are retained. Cancelling, closing or changing documents prevents a late result from being saved into the wrong state. Provider and permission changes cancel unfinished exploration but leave completed results and favourites available for reference, copying or explicit application. Explore requires AI availability; saved ideas can be used offline.

The pile, settings, latest directions and favourites belong to this document and are saved inside **`.czd`** alongside Story Workshop, independently of its cast and plot. Native password protection and recovery include both workshops. Save the document to keep the work on disk. New, legacy and non-native documents start with an empty Wordplay Workshop. Ordinary Save uses `.czd` when either workshop has data. Explicit non-native Save As is a manuscript-body export and preserves the working file, dirty state and recovery; workshop data is omitted from that export and from printing.

AI exploration uses the configured direct ChatGPT, Claude or Gemini provider with AI language services enabled. The request sends the supplied pile, selected options and optional target only. It does not read the surrounding manuscript, Story Workshop, favourite bank or other documents. Suggestions are requested as fresh original possibilities, with no automatic continuation of a recognisable published song. Their quality and usefulness remain the writer's judgement.

Since Beta 53.1, an animated activity bar and an elapsed-time display show that a request is running. This is activity, not an estimated completion percentage. Status messages are blue while busy, green on success, red on errors, and grey for neutral instructions or cancellation. Cancel and Close stop the indicator immediately; a late reply cannot stop a newer request's indicator.

For OpenAI, the workshop now requests a strict JSON response schema directly. A provider refusal, unfinished reply or service failure receives its own message rather than being treated as an association JSON syntax error. One complete enclosing JSON code fence or BOM can be read safely; prose around a response, incomplete JSON and invalid associations are still rejected. Failed replies are not saved or applied. Existing favourites and the manuscript are unchanged. No automatic paid retry is introduced. Other providers keep their existing transport and the same application validation.

The source pile supports up to 12,000 characters and the target up to 1,000. Each generated phrase supports up to 1,000 characters. The document holds up to 40 favourites and 256,000 characters across this workshop record. Oversized or malformed saved records are rejected instead of silently discarding existing notes. Windows release checks use deterministic responses to verify the requested axes, source links, editing, persistence and stale-response guards; they do not assess a live model's wordplay.

The [full Windows release gate](https://github.com/martynwalker/CtrlZed/actions/runs/45615954830) passed on source `43e6ff8a3f3e0ed55732f3d3039e0883b9665bc3` on 25 November 2026, including packaging and publication. Deterministic HTTP/model examples verified successive requests, refusal/incomplete/error handling, activity/elapsed time, green/red status, cancellation and retention of saved work, alongside all existing editing checks. Busy, success and error workshop screenshots were inspected. No live provider calls were made by these tests. Beta 53.1 awaits user acceptance; Beta 52 remains accepted.
