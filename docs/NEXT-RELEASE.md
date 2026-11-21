# CtrlZed — Beta 45.1 release record and remaining queue

Updated 21 November 2026.

## Baselines and acceptance

Beta 45.1 (`0.45.1-beta.45.1`) is the current GitHub `main` baseline. Its [complete Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/44889975241) on source commit `a7df5c6127cdcfb8cf94c34d9bf0e9a39d6dd58e`. The release package was published from that commit; baseline promotion adds only this release documentation. User acceptance of Beta 45.1 is pending.

The previous user-accepted baseline remains Beta 44.5 (`0.44.5-beta.44.5`). Martyn accepted it on 20 November 2026 and confirmed that menu performance was back to normal. Beta 45.1 builds forward from that code and retains its menu, colour palette, navigation, document protection and macro functionality.

## Corrected in Beta 45.1

On 21 November 2026, Martyn reported that normal Ctrl+V lost formatting when copying from a web page or ChatGPT. A [Windows reproduction run](https://github.com/martynwalker/CtrlZed/actions/runs/44888914303) confirmed that the HTML-plus-text clipboard case fell back to plain text; native RTF and CtrlZed/WPF paste retained their formatting.

- [x] Convert HTML clipboard fragments for normal Paste when no native rich format is selected, preserving supported font/colour/emphasis, links, headings and paragraph/line breaks.
- [x] Keep Ctrl+Shift+V as the plain-text command using destination formatting.
- [x] Preserve only the selected CF_HTML fragment while retaining inherited inline formatting and handling UTF-8 byte offsets.
- [x] Exercise both physical shortcuts with browser HTML, RTF and real CtrlZed copied content, checking actual formatting properties, selection replacement, Undo/Redo and the unchanged clipboard.
- [x] Retain the existing document, following-style and menu-responsiveness release gates.

## Implemented in Beta 45

### Named styles and paragraph controls

- [x] Expand each style set beyond the six built-in styles, with additional named styles in the visible Styles list.
- [x] Add New Style, Rename Style and Delete Style controls while protecting the six built-in names.
- [x] Add paragraph spacing before/after, left/right indentation, first-line or hanging indentation, automatic or explicit line height, keep-with-next, keep-together, page-break-before and heading outline level.
- [x] Retain stable custom style identities and rename aliases so existing named macro references remain usable.
- [x] Store style definitions, paragraph identities and paragraph layout in native `.czd` files, and support custom named styles and the implemented paragraph properties in DOCX interchange.
- [x] Apply the active set's current name when copied sets share a custom style identity.
- [x] Share following-style behaviour between Return, safe-macro `NEW_PARAGRAPH` and compiled macros using `EditingCommands.EnterParagraphBreak`. Soft breaks and splits within an existing paragraph keep the current style.

### Suggested filename on first save

- [x] Prefer the first non-empty recognised heading; otherwise use the first sentence.
- [x] Produce a Windows-safe suggestion with normalised whitespace, a sensible length and a fallback for empty/unusable text.
- [x] Supply an editable suggestion to first-save entry points, including Save As for an unsaved document, while allowing the chosen file type and extension to be handled normally.
- [x] Preserve existing filenames on subsequent saves and retain the normal Save dialogue cancellation path.

### Ctrl+Shift+V — paste unformatted text

- [x] Add Edit → Paste Unformatted Text and Ctrl+Shift+V, including shortcut help.
- [x] Insert clipboard text and line breaks using the destination typing format, replacing the selection where appropriate.
- [x] Discard source fonts, sizes, emphasis, colours, embedded objects/images and link behaviour. Visible link text may remain as ordinary text.
- [x] Reserve Ctrl+Shift+V before macro dispatch, retain existing macro definitions and identify reserved-key conflicts.
- [x] Preserve ordinary Ctrl+V rich paste, one-operation Undo and safe no-op behaviour for an empty/image-only clipboard or a read-only editor.

## Verification and promotion

The release gate includes the Windows build, self-contained publish, launch check, document-fidelity/filename self-tests, actual following-style keyboard checks, menu responsiveness, custom-style/native/DOCX persistence and rich/plain-text-paste checks. The last complete run must pass before publishing the release or promoting the GitHub baseline.

The style checks exercise a renamed style in a copied set; paragraph settings on heading/body text and inside a table; native `.czd` save/reopen; custom DOCX style and layout round trips; and Return and macro insertion paths. The filename checks exercise heading/sentence precedence, abbreviations, Windows filename sanitisation, fallback names and length handling.

User acceptance should confirm the suggested name in the native Save dialogue, editing/cancelling it, the expanded Styles screen, typical document layouts and the paste workflow on Martyn's documents. Automated naming checks do not claim to have tested all native Save-dialogue interactions.

Status: Windows build, all functional regressions, packaging and publication passed. The tested source and release documentation are promoted to `main`. User acceptance is pending.

## Remaining queue

The wider Coming Soon work and remaining [language-intelligence backlog](https://github.com/martynwalker/CtrlZed/issues/17) remain separate. This release does not claim to deliver true discrete-page WYSIWYG editing, the AI Font Finder/font installation workflow, experimental generated fonts, bitmap-to-vector signature tracing, Bitwarden integration or complete Word publishing fidelity.
