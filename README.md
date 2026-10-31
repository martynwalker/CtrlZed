# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Beta 3

Beta 3 builds on the verified WPF/RTF editor and adds:

- native `.czd` document files with page settings, header and footer metadata
- RTF and plain-text interchange
- traditional menus and Word-style shortcuts
- initial WordStar 4.0 and WordPerfect 5.1 keyboard profiles
- paragraph styles: Normal, Heading 1/2/3, Quote and Caption
- page setup for A4, A5, US Letter, portrait/landscape and margins
- inline pictures with double-click and right-click Picture Properties
- picture width, alignment and optional caption insertion
- visible header/footer preview with `{page}`, `{pages}`, `{date}` and `{filename}` fields
- save prompts on close/new/open when the document has changed
- automatic recovery snapshot while an unsaved document is dirty
- central C# / VB.NET macro source library; execution remains disabled
- self-contained Windows x64 build produced and smoke-tested by GitHub Actions

## Important limitations

This remains an early beta. Use copies of important documents.

Headers and footers are stored in `.czd` and shown in the editor, but pagination is still WPF FlowDocument-based rather than Word-compatible. Beta 3 print output treats them as simple document header/footer material rather than a full repeating per-page layout engine.

Pictures remain inline. Full floating text wrap and DTP positioning are deliberately deferred.

DOCX import/export, executable macros, complete WordStar/WordPerfect emulation, custom keyboard remapping and non-Windows builds are not yet implemented.

RTF remains the initial Microsoft Word interchange format because Word can open it and save it as DOCX.

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
