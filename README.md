# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Current baseline and latest prerelease

The preceding accepted baseline was **[Beta 48 (0.48.0)](https://github.com/martynwalker/CtrlZed/releases/tag/v0.48.0-beta.48)**. The full [Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45036690638) on source commit `0d630c7e8e260ece357abc4b01169c975ed210dc` before publication and baseline promotion. **Martyn confirmed Beta 48 as a pass on 22 November 2026, reporting that it works extremely well.**

The current accepted baseline is **[Beta 49 (0.49.0)](https://github.com/martynwalker/CtrlZed/releases/tag/v0.49.0-beta.49)**. Its full [Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45196647649) on source commit `bae79ec445f4af4a2f9514b24ac5a030777086f2`, including packaging and publication. **Martyn confirmed Beta 49 as a pass on 23 November 2026.**

**[Beta 46.2](https://github.com/martynwalker/CtrlZed/releases/tag/v0.46.2-beta.46.2) was the previous user-accepted baseline**, passed by Martyn on 22 November 2026. It built forward from the previously accepted [Beta 45.1](https://github.com/martynwalker/CtrlZed/releases/tag/v0.45.1-beta.45.1), passed on 21 November 2026, and retains the existing editing, style, clipboard, macro and document-protection features.

The [release record and remaining queue](docs/NEXT-RELEASE.md) distinguishes delivered Beta 45/45.1/46/46.1/46.2/47/48/49 work, verification and later ideas.

### Beta 50 candidate — Google Fonts discovery and installation

- **Format → Find a Font… → Google Fonts…** browses supported static families from a pinned official catalogue, with local search and an optional, explicitly requested AI ranking.
- Read the actual source licence and notices before downloading a real preview. Preview text stays local; search, AI and previews do not install fonts or edit the document.
- Explicit installation is for the current Windows user and preserves existing registrations. Save and restart CtrlZed afterwards to refresh WPF's cached installed-font list, then apply through the existing finder.
- The [Google Fonts guide](docs/ONLINE-FONTS.md) explains catalogue scope, actual faces, installation and availability. Windows candidate verification is in progress. Beta 49 remains the accepted baseline.

### Beta 49 — installed Font Finder

- **Format → Find a Font…** searches installed fonts locally by name or description, previews your own text and offers an explicit **Ask AI** option through the configured direct provider.
- Apply a font family and chosen standard emphasis to captured text, the typing caret or a named style in the active set. Point size, colour, underline and paragraph layout are preserved. Previews do not dirty the document.
- The document sample stays local. AI receives only the request, Writing Mode and installed-font catalogue; returned sources must be real supplied families. No fonts are downloaded or installed.
- Selection formatting is one Undo operation. Updating a named style saves its definition separately from native document Undo; restore the definition through Styles.
- The [usage guide](docs/FONT-FINDER.md) covers setup, search limits, preview controls and saved styles. Google Fonts discovery/download/install and generated fonts remain later work.
- Windows regressions cover native keyboard/document language mismatches, real window focus and Close, exact rich insertion, unchanged existing word formatting at an interior caret, one Undo/Redo and stale destinations in both views. Only a proven newly typed span receives a pending caret choice; document language and keyboard layout are preserved.

### Beta 48 — Finish Sentence

- **Review → Finish Sentence…** and the editor right-click menu open a modeless preview. Choose Comedic, Prophetic, Awkward, Sharp, Cynical, Neutral or a named custom inflection, alongside Subtle/Balanced/Bold strength and an optional meaning hint.
- The current Writing Mode supplies purpose and voice conventions. Three alternatives show their proposed words highlighted within the sentence; edit a completion and explicitly Insert it. A selected short phrase is replaced precisely; an empty selection inserts at the captured caret without overwriting following text.
- Automatic context supplies nearby prose, selected headings and relevant earlier excerpts. Paragraph, Section and Whole document choices are explicit. Larger contexts are processed completely in ordered chunks and combined into continuity/voice notes, cached temporarily in memory.
- Requests run only on Generate through the configured direct AI provider, with AI language services explicitly enabled. Closing, changing options, editing the source or changing mode/provider invalidates old results. Insertion preserves destination formatting and is one Undo operation.
- Custom inflections and last choices are stored in local user settings. Meaning hints and document prose are not persisted in that preferences file; these new preferences are not yet included in portable profile archives.
- Safe macros use `FINISH_SENTENCE Cynical`; trusted compiled macros use `SentenceCompletionMacro.Show(Editor, "Cynical")`. Both open the preview without generating, inserting or dirtying an unchanged document.
- The [usage guide](docs/SENTENCE-COMPLETION.md) explains context, custom inflections, AI setup and macros. Windows tests use a deterministic transport for context/schema, real-window asynchronous behaviour, stale guards, formatting, Undo and both editor views; live model quality is a manual acceptance check.
- The low-priority [Page Editing floating-picture display issue #30](https://github.com/martynwalker/CtrlZed/issues/30) remains deferred as requested.

### Beta 47 — vector signatures

- **Settings → Configure Signature** accepts outlined SVG signatures and can trace PNG/JPEG/BMP/GIF images. The review window shows original/result previews, ink threshold, light-ink mode, the Windows ink-colour palette and SVG export before saving.
- **Tools → Profile Images** accepts SVG, can trace an uploaded/stored raster signature and can export SVG. Ordinary profile-image uploads retain their original raster data unless tracing is selected.
- Existing safe `INSERT_PROFILE_IMAGE {signature} 40 IN_FRONT` and compiled `ProfileImageMacro.Insert` calls retain their interface, 40 mm sizing and inline/front/behind placement.
- Native `.czd` documents and recovery records embed the SVG master and exact dimensions independently of the profile original. Portable profiles carry canonical SVG source bytes. Older bitmap-only documents and `LoadBitmap` macros remain compatible.
- Inline and floating signatures use vector drawing sources in editing and printing. Native/RTF serialization and pagination snapshots use prepared bitmap fallbacks without altering the live vector master. Clipboard, current DOCX and RTF interchange retain the bitmap representation.
- SVG import supports outlined paths, curves, basic shapes, groups/transforms, solid fills/strokes, opacity and viewBox alignment. Text, paint servers/effects, linked content, CSS and root transforms require conversion to supported paths before import.
- Windows regressions check traced holes and colour, SVG rendering/fill rules, live preview readiness, insertion and Cut Undo/Redo, copy/paste pixels, macros, native/profile/recovery round trips, exact sizing and true vector print sources.

### Beta 46.2 — Return after page navigation

- Temporary pagination now reflows automatic page boundaries rather than retaining moved cached boundaries as extra sheets after Return.
- Return at the start of a paragraph with a direct/manual page break retains one leading boundary instead of copying it into blank pages. Named styles with an intentional page-break-before rule retain that rule.
- A [Windows reproduction on Beta 46.1](https://github.com/martynwalker/CtrlZed/actions/runs/45008775733) confirmed an extra temporary page for natural boundaries and five additional real pages from copied manual breaks.
- DOCX pictures decode completely from detached, seekable data before the archive stream closes. This corrects the background inflater/disposal race captured during the Windows release checks; imported-image readiness and pixels are verified after archive closure. [Six repeated document checks](https://github.com/martynwalker/CtrlZed/actions/runs/45010079180) passed without the race.
- Windows regressions physically select a page thumbnail, press Return five times, compare pending/settled/thumbnail/print page counts, re-navigate and Undo. WPF paragraph commands, safe macros and intentional style-owned breaks are also checked.

### Beta 46.1 — scrolling and page thumbnails

- Manual wheel and scrollbar browsing stays where the user chooses; pagination and native layout requests no longer return the view to the caret. Typing and keyboard movement still reveal the insertion point.
- Page thumbnail navigation remains available while the next map updates. Live anchors follow edits, and internal list refreshes preserve selection without initiating navigation.
- Applying a page map no longer invalidates that same map through changes to the displayed page height.
- Page thumbnails refresh after text and picture changes and include floating pictures behind/in front of text, headers and footers.
- Windows regressions exercise actual wheel input, retained browse offsets, typing after browsing, text/picture thumbnail pixels, pending-map clicks and repeat clicks on the selected thumbnail.

### Beta 46 — first stage of page-by-page editing

- Paged Editing displays distinct paper pages, margins and page gaps. Click a page to edit it; typing, selection and Undo use one canonical document and the existing RichTextBox macro target.
- The live page view follows cached print pagination while provisional boundaries keep the caret visible during sustained typing. Heading/page navigation and keyboard movement use the projected page coordinates.
- Pagination snapshots preserve paragraph spacing, exact line height, keep/page-break settings and canonical text offsets through the RTF copy.
- Floating pictures render across the full body of their anchor page, including blank space below short text. An optional native-file page offset retains placement when a picture is dragged to another page.
- Windows regressions exercise actual page clicks, cross-page keyboard selection, Delete/Undo/Redo, caret visibility, macro insertion, document/view-switch fidelity, picture reflow, saveable page offsets and a tall portrait pixel comparison against print preview. The 600-paragraph menu gate runs in Paged mode.

This is the first stage. Field-code display and non-printing inline comments can differ from their printed representation; complex table/oversized-object layout and complete Word publishing fidelity remain later work. Continuous Editing remains available.

### Beta 45.1 — formatted browser paste

- Normal **Ctrl+V**, **Edit → Paste** and context-menu Paste now convert HTML clipboard content when WPF would otherwise select plain text. This covers the browser/ChatGPT case reported on 21 November 2026.
- The converter retains supported fonts, size, colour, emphasis, links, headings and paragraph/line-break structure. CF_HTML byte offsets retain only the copied selection and its inherited inline formatting, including accented characters and emoji.
- Native RTF and CtrlZed/WPF rich clipboard formats keep their existing paste path. **Ctrl+Shift+V** continues to paste unformatted text using the destination format.
- Tests exercise the physical shortcuts with RTF, real CtrlZed copy/paste and browser HTML, including selection replacement, Undo/Redo and an unchanged global clipboard.

### Beta 45 — custom styles and everyday editing

- The Styles list supports additional named styles in each set, with New Style, Rename Style and Delete Style controls. The six built-in style names remain protected for compatibility.
- Paragraph controls include spacing before/after, left/right and first-line or hanging indentation, automatic or explicit line height, keep-with-next, keep-together, page-break-before and heading outline level.
- Custom styles have stable identities. Renamed styles retain aliases for existing named macros; copied sets resolve shared identities using the active set's current names.
- Native `.czd` documents carry their style definitions, paragraph identities and paragraph layout. The supported custom paragraph styles and layout properties also round-trip through DOCX.
- Return, safe-macro `NEW_PARAGRAPH` and compiled macros using `EditingCommands.EnterParagraphBreak` share following-style handling at a paragraph's end. A soft break or a split within existing text retains the current style.
- First save suggests the first non-empty recognised heading, otherwise the first sentence, as an editable Windows-safe filename. Empty documents use a fallback; later saves retain the existing filename.
- **Edit → Paste Unformatted Text**, also **Ctrl+Shift+V**, inserts clipboard text and line breaks using the destination typing format. Source fonts, emphasis, colours, images and link behaviour are not imported; ordinary Ctrl+V remains the rich paste command.
- Ctrl+Shift+V is reserved before macro-shortcut dispatch. A macro previously assigned that key remains stored and can be assigned another available shortcut.
- The accepted Beta 44.5 standard Windows colour palette and menu-responsiveness changes are retained.

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

### Beta 39 — named profile images for macros

- **Tools → Profile Images** stores named PNG, JPEG, BMP or GIF images in the current Windows user's CtrlZed profile.
- Upload an image, name it (for example `signature`), replace it later, or remove it.
- Safe macros can use `INSERT_PROFILE_IMAGE {signature} 40 IN_FRONT`; widths are in millimetres and placement can be `INLINE`, `BEHIND` or `IN_FRONT`.
- Trusted compiled VB.NET/C# macros can call `ProfileImageMacro.Insert(Editor, "signature", 40, PicturePlacementMode.InFrontOfText)`.
- Portable `.czprofile` export/import includes the named image collection.

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

The implemented interchange path covers:

- paragraphs and text; font family, size and colour; bold, italic and underline
- built-in and custom named paragraph styles, stable custom style identities and following-style definitions
- paragraph alignment, spacing, left/right and first-line or hanging indentation, automatic or explicit line height, outline level and paragraph/page-break controls
- tables and supported inline images, including paragraph alignment
- retention of unrelated imported style definitions when saving a document

Custom style and paragraph-layout round trips are checked by the Beta 45 regression suite, including a styled paragraph inside a table. This is support for the implemented subset, not a claim of complete Word fidelity.

Not yet claimed as preserved in DOCX:

- CtrlZed custom fields or header/footer field templates
- every advanced Word style/theme feature
- tracked changes or Word comments
- footnotes/endnotes
- complex lists/numbering definitions
- floating objects and Word-specific publishing layout

Use `.czd` as the canonical working format and DOCX as an interchange format.

### Functional release gate

A release must pass:

1. generation and validation of the pinned static Google Fonts catalogue, compilation on GitHub-hosted Windows and self-contained Windows publication
2. executable launch with a responsive main window
3. `--self-test`, including RTF/DOCX round trips, tables, inline images, Open XML schema validation, language-intelligence checks and filename-suggestion cases
4. `--following-style-test`, exercising actual keyboard input after headings, captions and quotes, repeated Return, soft breaks and Undo/Redo
5. `--menu-performance-test`, exercising repeated menu opening on a 600-paragraph document, preserving commands and resuming deferred maintenance after edits
6. `--style45-test`, covering legacy style data, custom style identities and aliases, paragraph controls, shared Return/macro behaviour and native/DOCX persistence
7. `--plain-text-paste-test`, covering physical Ctrl+V/Ctrl+Shift+V with RTF, CtrlZed/WPF and browser HTML; retained or removed source formatting; selected UTF-8 fragments and inherited styles; paragraphs/soft breaks; Undo/Redo; unchanged clipboard; and empty/image-only/read-only cases
8. `--paged-editing-test`, covering distinct paper geometry, canonical document/Undo preservation, physical page clicks, cross-page keyboard input and caret visibility, macro insertion, image page offsets and pixel-checked picture/print agreement
9. `--sentence-completion-test`, covering response/context contracts, complete-source summaries, preferences, real asynchronous previews, precise rich insertion/Undo, stale mode/source results and macro preview access with a deterministic provider
10. `--vector-signature-test`, covering tracing and SVG fidelity, live preview/threshold interaction, atomic Undo/Redo, named-image macros, native/profile/recovery persistence, compatibility fallbacks and vector print sources
11. `--font-finder-test`, covering installed/local search, strict response contracts, privacy, real asynchronous previews, stale guards, rich font application/Undo and style persistence with a deterministic provider
12. `--online-font-test`, covering pinned catalogue/licences, real font downloads and private previews, explicit UI consent, stale results, per-user installation, collisions, rollback and cleanup
13. packaging and prerelease publication

The filename automation checks the naming rules. The native Save dialogue's suggested name, editing and cancellation are manual acceptance checks. Automated verification is recorded separately from user acceptance.

## Macros

Profile images are managed under **Tools → Profile Images**. For the reusable signature example, save the image under the name `signature`, then use this safe macro command:

```text
INSERT_PROFILE_IMAGE {signature} 40 IN_FRONT
```

This inserts the stored picture at the caret, at 40 mm wide with its aspect ratio retained, in front of text. Use `BEHIND` or `INLINE` for the other placement modes. Profile images travel with `.czprofile` exports.

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

Beta 27 adds Ask AI to Modify for selected macros, keyboard-profile shortcut help, non-printing comments, reusable image signatures, named style sets and style-set portability in `.czprofile` archives. Signature tracing and SVG-native masters were subsequently delivered in Beta 47.

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

At the Beta 32 milestone, contextual thesaurus and poetry rhyme assistance were queued for the next language-intelligence release; they were subsequently delivered in Beta 33. Later language-intelligence improvements remain tracked in [issue #17](https://github.com/martynwalker/CtrlZed/issues/17).

## Building the online font catalogue

Before building a fresh source checkout, run `python scripts/build-online-font-catalog.py --output OnlineFontCatalogue.json`. Python 3 and Git are required for this source preparation step; the distributed Windows application needs neither. The script uses a sparse, blob-filtered checkout of the pinned official repository and downloads metadata/licences, not the full font collection. The Windows workflow also prepares an independently verified test font in its temporary fixture directory; that fixture is not distributed in the portable ZIP.
