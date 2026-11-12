# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Current baseline and latest prerelease

The protected `main` branch remains at **Beta 34.2**. The latest published prerelease is **[Beta 38 (0.38.0)](https://github.com/martynwalker/CtrlZed/releases/tag/v0.38.0-beta.38)**, built from the `beta38` branch.

### Beta 35 — safe paged editing foundation

- **Paged Editing** and **Continuous Editing** views, with the selected view retained locally and in portable profiles.
- A cached, non-destructive page map calculated from an idle document snapshot on a separate STA worker.
- Current page and total page count, with stale calculations cancelled when editing resumes.

### Beta 36 — print preview

- **File → Print** opens printer settings, then CtrlZed's own page review before sending the job.
- **Print** sends the same paginator and printer ticket shown in the preview; **Cancel** sends no job.
- The user accepted this workflow as a pass on 11 November 2026. The standard Windows print dialogue itself still does not display the preview.

### Beta 37 — page navigation

- The current-page indicator in Paged Editing is clickable.
- Enter a page number to move the caret to the first mapped text position and bring it into view.
- Navigation uses the cached page map and does not edit or rebuild the document.
- The Windows build, self-contained publish, responsive launch and existing functional self-test passed. The [Beta 37 prerelease](https://github.com/martynwalker/CtrlZed/releases/tag/v0.37.0-beta.37) is available for user review.

### Beta 38 — floating image placement

- Right-click a picture to choose **Inline**, **Behind Text** or **In Front of Text**.
- Floating pictures can be dragged; drag the lower-right handle to resize while keeping the aspect ratio.
- Macro signatures inserted through a zero-size Canvas are recognised and can be moved, resized or converted to Inline.
- Native `.czd` files retain the placement; page preview and printing respect the selected layer.
- Ordinary picture insertion remains Inline by default.

## Feature set

The current feature set includes:
### DOCX interchange

DOCX support uses the MIT-licensed Open XML SDK 3.5.1. It remains deliberately conservative.

Currently targeted for round-trip:

- paragraphs and text
- bold, italic and underline
- basic font family and size
- paragraph alignment
- manual page breaks
- basic Heading 1/2/3 import
- tables

Not yet claimed as preserved in DOCX:

- CtrlZed custom fields
- CtrlZed header/footer field templates
- advanced Word styles/themes
- tracked changes/comments
- footnotes/endnotes
- complex lists/numbering definitions
- floating objects and Word-specific layout

Use `.czd` as the canonical working format and DOCX as an interchange format.

### Functional release gate

A release must pass:

1. compile on GitHub-hosted Windows
2. self-contained Windows publish
3. executable launch smoke test
4. non-interactive `--self-test`
5. RTF round-trip test
6. DOCX creation/import test
7. table preservation check
8. inline-image round-trip check
9. Open XML schema validation
10. language-intelligence regression checks
11. packaging and prerelease publication

## Macros

Executable `.czm` macros use CtrlZed's approved command language. Extended mode can additionally allow interactive prompts and locally generated document images such as QR codes.

A `.czm` macro may declare:

```text
# SHORTCUT: Ctrl+Alt+M
MENU_NAME="Snowflake..."
MENU_PATH="Writing/Plotting Methods"
MENU_PATH_POSITIONS="7/2"
MENU_POSITION=1
```

Omit `MENU_PATH` to place the macro in the document's right-click **Macros** submenu. Paths can also target existing CtrlZed menus such as `Insert` or `Tools`. `MENU_OPTION` remains supported as a legacy alias for `MENU_POSITION`.

VB.NET and C# macro source can also be stored in the Macro Library. CtrlZed recognises language-native friendly-name constants for display:

```vbnet
Public Const MENU_NAME As String = "Clipboard to Two Columns"
Public Const MENU_POSITION As Integer = 1
```

```csharp
public const string MENU_NAME = "Clipboard to Two Columns";
public const int MENU_POSITION = 1;
```

VB.NET and C# macros can be compiled and run in memory when **Allow compiled VB/C# macros (full trust)** is enabled in Macro Manager. They use a public static/shared `Run(RichTextBox)` entry point. Because compiled macros run with the same Windows permissions as CtrlZed, only run source you trust.

## AI workflows

The AI Manager can return generated material directly into CtrlZed when a direct provider connection is configured.

Document workflow:

- send the instruction plus selected text / document context to the chosen provider
- preview the returned material
- Insert at Cursor or Replace Selection
- recognised CTRLZED_CHART blocks are rendered as document charts

Macro workflow:

- Ask AI from the Macro Manager
- generated `.czm` source returns directly into the macro editing field for review
- nothing is saved or run automatically
- in extended mode, AI is explicitly informed that implemented commands such as `PROMPT` and `INSERT_QR` are available

Safe macros also support:

- `AI_REWRITE instruction` — sends the current selection to the configured default AI and replaces it
- `AI_INSERT instruction` — asks the configured AI for new material and inserts the result

Direct API credentials are encrypted with Windows DPAPI for the current Windows user and never stored inside `.czd` files. ChatGPT/OpenAI, Claude and Gemini support direct mode. Copilot Chat remains a browser handoff because Microsoft does not expose a general consumer Copilot Chat API.

Browser/free and signed-in handoff remain available for all four providers, with Paste Browser Result available as a manual return route.

## Development principles

- lightweight
- predictable menus and keystrokes
- local-first
- open source
- no commercial document-engine dependency
- AI-authored implementation, open to inspection
- stable behaviour preferred over fashionable rearrangement
- suggestions must remain subordinate to the writer's judgement

## Licence

MIT. See [LICENSE](LICENSE) and [third-party notices](docs/THIRD-PARTY-NOTICES.md).

## Beta 27 highlights

Beta 26.1 adds **Ask AI to Modify…** for the selected macro. CtrlZed sends the current source and language to the existing AI Macro Assistant and loads the proposed revision into the editor for review; saving remains explicit.

Beta 27 adds Ask AI to Modify for selected macros, keyboard-profile shortcut help, non-printing comments, reusable image signatures, named style sets and style-set portability in `.czprofile` archives. Automatic bitmap-to-vector signature tracing remains in Coming Soon.

## Beta 28 highlights

Beta 28 adds a dropdown style selector and RGB/hex colour picker in Styles, whole-document reformatting via Format > Reformat Document (F9 in the Microsoft Word profile), remembered keyboard profiles with profile-aware menu shortcut labels, and user-assignable macro shortcuts with reserved-key and duplicate-assignment checks across Safe, VB.NET and C# macros.

## Beta 29 highlights

Beta 29 carries the selected keyboard profile and active style set in portable `.czprofile` export/import, restoring both immediately when a profile is imported. It builds directly on the corrected Beta 28 baseline.

## Beta 30 highlights

Beta 30 adds Roslyn-backed completion for C# and VB.NET macros, copyable compile-only diagnostics, and a reusable status/message area in the AI Assistant, including explicit confirmation when a result is copied.

## Beta 31 highlights

Beta 31 establishes CtrlZed's language-intelligence foundation:

- restores VB.NET syntax colourisation in Macro Manager;
- adds a top-level Review menu;
- uses British English (`en-GB`) for spelling by default;
- provides red spelling marks, right-click correction choices, Ignore All and Add to Personal Dictionary;
- adds a persistent British-English personal dictionary and manager;
- adds Next Spelling Error on F7 in the Microsoft Word keyboard profile while preserving WordPerfect's F7 behaviour;
- adds selectable writing modes for letters, email, business, technical, academic, legal, journalism, speeches, fiction, novels, novellas, short stories, screenplays, poetry and custom use;
- adds an optional, conservative sentence-completion spelling correction, off by default and immediately undoable;
- optionally uses a configured direct AI provider to disambiguate a completed sentence, but only with explicit permission, only when local confidence is insufficient, and only by choosing from the local spelling candidates;
- exports/imports language settings and personal dictionary words with `.czprofile`.

## Beta 32 highlights

Beta 32 adds the first grammar and style review system:

- thin blue suggestion marks, independent of red spelling marks;
- local rules for clear grammar, punctuation, British usage, repetition, sentence length and selected formal-writing preferences;
- writing-mode-aware tolerance so fiction, dialogue and poetry are not normalised into office prose;
- right-click explanations and replacement choices;
- Review Document with a modeless review pane;
- Apply, Ignore and Ignore Rule controls;
- optional paragraph-scoped AI review, run only on explicit request;
- no automatic grammar or style rewriting;
- Help text explaining why an unintelligible word remains red-underlined rather than being guessed;
- corrected single ampersands in dynamically generated menu captions.

The contextual thesaurus and poetry rhyme assistance remain scheduled for the next language-intelligence release.
