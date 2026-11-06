# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Current release: Beta 29

Beta 29 is the current Windows prerelease. It builds directly on Beta 27 and adds or retains:

- headings navigation pane
- Find/Replace across formatting runs within paragraphs
- table row/column insertion and deletion
- first-pass DOCX import/export, including inline pictures
- DOCX page size and margin exchange
- DOCX headers and footers with Word PAGE / NUMPAGES / FILENAME / DATE / TIME fields
- distinct visual markers for manual page breaks in the CtrlZed editing view
- Save As and a local Recent Files menu
- document Title / Author / Subject / Keywords metadata
- persistent 50–150% editing zoom
- live word, character and page counts
- genuine Word numbering definitions for bullets and numbered lists
- backward-motion CtrlZed brand icon in navy, gold and white
- branded About box and Windows window/taskbar identity
- automated functional self-test mode used by CI
- page guides, page gaps, page preview, headers/footers, fields, pictures, tables, lists, page breaks and recovery
- AI Manager with direct and browser-assisted provider workflows
- safe and extended `.czm` macros
- interactive macro prompts and QR-code insertion in extended mode
- right-click **Macros** submenu on the document canvas
- hierarchical macro menus using `MENU_NAME`, `MENU_POSITION`, `MENU_PATH` and `MENU_PATH_POSITIONS` (with legacy `MENU_OPTION` compatibility)
- friendly macro names in Macro Manager
- explicit rename support for central macro files

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
10. packaging and prerelease publication

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

## Licence

MIT. See [LICENSE](LICENSE) and [third-party notices](docs/THIRD-PARTY-NOTICES.md).


## Beta 27 highlights

Beta 26.1 adds **Ask AI to Modify…** for the selected macro. CtrlZed sends the current source and language to the existing AI Macro Assistant and loads the proposed revision into the editor for review; saving remains explicit.


Beta 27 adds Ask AI to Modify for selected macros, keyboard-profile shortcut help, non-printing comments, reusable image signatures, named style sets and style-set portability in .czprofile archives. Automatic bitmap-to-vector signature tracing remains on the Coming Soon list.


## Beta 28 highlights

Beta 28 adds a dropdown style selector and RGB/hex colour picker in Styles, whole-document reformatting via Format > Reformat Document (F9 in the Microsoft Word profile), remembered keyboard profiles with profile-aware menu shortcut labels, and user-assignable macro shortcuts with reserved-key and duplicate-assignment checks across Safe, VB.NET and C# macros.


## Beta 29 highlights

Beta 29 carries the selected keyboard profile and active style set in portable `.czprofile` export/import, restoring both immediately when a profile is imported. It builds directly on the corrected Beta 28 baseline.
