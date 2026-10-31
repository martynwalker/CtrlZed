# CtrlZed

**The word processor that knows its place.**

CtrlZed is a lightweight, keyboard-friendly Windows word processor. The project is human-directed and AI-authored, released under the MIT licence.

## Beta 9

Beta 7 builds on the working `.czd` editor and adds:

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
- automated functional self-test mode used by CI
- existing page guides, page gaps, page preview, headers/footers, fields, pictures, tables, lists, page breaks and recovery

### DOCX interchange

DOCX support uses the MIT-licensed Open XML SDK 3.5.1. It is deliberately conservative in Beta 7.

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

A release must now pass:

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

## Safe macros

Executable `.czm` macros remain restricted to CtrlZed's approved command language. C# and VB.NET source can be stored in the macro library but is not executed because Roslyn compilation is not a security sandbox.

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
