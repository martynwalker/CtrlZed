# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Beta 1

The first test build targets Windows x64 and deliberately keeps the scope small:

- native WPF application
- traditional menus and keyboard shortcuts
- rich text editing
- system fonts and common text sizes
- bold, italic and underline
- inline picture insertion
- RTF open/save for Microsoft Word interchange
- plain-text open/save
- printing
- undo/redo, cut/copy/paste
- visible Word, WordPerfect 5.1 and WordStar 4.0 profile choices (profile remapping is not yet complete)

The GitHub Actions workflow compiles and publishes a self-contained Windows executable, then launches it as a smoke test before packaging the downloadable ZIP.

## Important limitations

This is an early beta. Use copies of important documents. It does not yet provide DOCX support, page-layout fidelity comparable with Word, floating image wrapping, macro execution, headers/footers, custom keyboard remapping, or cross-platform builds.

RTF is the initial interchange format because Microsoft Word can open it and resave as DOCX.

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
