# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Beta 5

Beta 5 adds:

- native `.czd` documents, still compatible with earlier CtrlZed beta files
- RTF and plain-text interchange
- page setup, paragraph styles, images and captions
- stored headers and footers with repeating paginated print/preview output
- single-page preview navigation with Previous / Next and zoom controls
- built-in fields: `{page}`, `{pages}`, `{date}`, `{time}`, `{filename}`, `{moonphase}`, `{moonicon}`
- document-specific custom fields using `{field:name}`, including nested templates
- a Fields manager with a moon-phase example
- automatic recovery snapshots and save-before-discard prompts
- initial WordStar 4.0 and WordPerfect 5.1 keyboard profiles
- central macro library

### Safe macros

Beta 5 introduces executable `.czm` macros. These are **not arbitrary .NET code**. CtrlZed interprets a deliberately restricted command set:

- `INSERT_TEXT text`
- `INSERT_FIELD name`
- `NEW_PARAGRAPH`
- `APPLY_STYLE style-name`
- `SET_HEADER text`
- `SET_FOOTER text`

A safe macro is limited to 200 commands and has no command for filesystem access, network access, process launching or arbitrary code execution.

C# and VB.NET macro source can still be stored and edited in the central macro library, but it remains non-executable. Roslyn compilation alone is not a security sandbox, so CtrlZed will not pretend otherwise.

## Important limitations

This remains beta software. Use copies of important documents.

The WPF/FlowDocument layout is not a Microsoft Word rendering engine. DOCX import/export, floating DTP-style image wrapping, complete WordStar/WordPerfect emulation and cross-platform builds remain future work.

## Development principles

- lightweight
- predictable menus and keystrokes
- local-first
- open source
- no commercial document-engine dependency
- AI-authored implementation, open to inspection
- stable behaviour preferred over fashionable rearrangement

## Licence

MIT. See [LICENSE](LICENSE).
