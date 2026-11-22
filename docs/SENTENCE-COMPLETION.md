# Finish Sentence — Beta 48

Finish Sentence suggests wording at the caret or replacements for a selected phrase. It uses the current Writing Mode and your chosen inflection. Nothing is generated until **Generate**, and nothing in the document changes until **Insert**.

## Use it

1. Put the caret between words in an unfinished sentence, or select complete words within one paragraph.
2. Choose **Review → Finish Sentence…**, or **Finish Sentence…** on the editor's right-click menu.
3. Choose **Comedic**, **Prophetic**, **Awkward**, **Sharp**, **Cynical**, **Neutral**, or a custom inflection. Choose **Subtle**, **Balanced** or **Bold** strength.
4. Optionally describe **What I mean**, for example “quiet, ghostlike, faintly ridiculous”.
5. Choose context and select **Generate**. Review three alternatives in the complete sentence; the proposed words are highlighted.
6. Select a suggestion, edit its proposed wording if useful, then choose **Insert**. **Ctrl+Z** reverses the insertion in one operation.

Following text is retained. The tool inserts only the proposed fragment or replaces only the selected phrase, retaining the destination formatting. Selections must be complete words within one paragraph, no more than 600 characters, without fields, embedded pictures or line breaks. For a larger rewrite, use the existing AI Manager.

The preview is modeless: you can return to the editor. Editing the source invalidates old suggestions; choose Generate again to capture the intended passage. Closing the window cancels its pending request. Read-only documents cannot be changed.

## Writing mode and inflection

Writing Mode sets the purpose and conventions: for example Technical documentation, Speech or Novel. Inflection shapes this particular passage. The exact sentence and paragraph supply wording and boundaries, while wider context helps preserve voice and continuity.

Awkward means deliberate social discomfort or uneasy candour, with grammatical wording. Prophetic supplies foreboding or consequence appropriate to the work; it does not authorise invented factual predictions. All inflections should preserve established meaning, facts, names, tense and point of view.

Expand **Custom inflection**, enter a distinct name and instruction, then **Save custom**. Select a saved custom entry to edit or rename it; **Delete** removes it. Up to 32 named custom inflections are supported. Built-in names are protected.

Custom inflections and last choices are saved in local user settings. One-off meaning hints and document context are not written into that settings file. These new preferences are not yet carried by portable profile archives.

## Context

| Choice | Material supplied |
|---|---|
| Automatic | Current paragraph, nearby prose, selected heading entries and relevant excerpts from a bounded range of earlier paragraphs. Excerpts are labelled; this is not a whole-document review. |
| Paragraph | The current paragraph only. |
| Section | The current heading section, through its subsections, stopping at the next heading of the same or higher level or document end. |
| Whole document | The complete plain-text document. |

The sentence is read on both sides of the caret or selection. Hover over the information line after generating to inspect the context description.

Context longer than 60,000 characters is processed in ordered chunks using additional AI requests, then combined into continuity and voice notes. Every supplied chunk is processed; the resulting notes are a summary and may omit detail. Exact local wording remains in the final request. Notes are cached temporarily in memory for matching source and writing-mode/provider settings; source changes require fresh notes. Up to 5,000,000 wider-context characters are supported; choose a section for a larger source.

## AI setup

Enable **AI language services** in **Language settings** and configure a direct provider and API key in **AI settings**. Finish Sentence uses the configured ChatGPT, Claude or Gemini connection. Copilot/browser handoff does not provide this direct completion workflow.

Only an explicit Generate request sends the selected context to that provider. The editor remains available while requests run. Changes to inflection, hint, strength, context, source text, writing mode, language or provider invalidate old results. Provider errors or malformed responses leave the document unchanged.

## Macros and shortcuts

A safe macro can open a preferred preview:

```text
# NAME: Finish Cynically
# DESCRIPTION: Opens sentence completion using the Cynical inflection.
FINISH_SENTENCE Cynical
```

Omit the argument to use the saved preference. A saved custom name containing spaces may be quoted:

```text
FINISH_SENTENCE "Dry understatement"
```

Inside a trusted compiled VB.NET or C# macro, call:

```text
SentenceCompletionMacro.Show(Editor, "Cynical")
```

These commands open the same preview. They do not generate or insert automatically, and a preview-only macro does not mark an unchanged document as modified. Assign a key combination through Macro Manager using the existing shortcut controls.

## Verification

The Windows release gate uses a deterministic transport to test response validation, complete context coverage, summary limits/cache/cancellation, preferences, real preview controls, stale results, formatting, both editor views, Undo/Redo and safe/compiled macro entry points. It does not spend provider credit or assess the quality of a live model's prose.

The pre-existing low-priority Page Editing floating-picture display issue [#30](https://github.com/martynwalker/CtrlZed/issues/30) remains deferred.
