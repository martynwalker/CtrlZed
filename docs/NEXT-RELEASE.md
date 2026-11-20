# CtrlZed — next-release plan

Updated 20 November 2026.

## Accepted baseline

Beta 44.5 (`0.44.5-beta.44.5`) is accepted by Martyn as a pass. Menu performance is back to normal. Build forward from this release and retain its menu responsiveness, standard Windows colour palette, style list, following-style behaviour, document protection, navigation and macro functionality.

## Existing queued work retained

The previous style work remains in the development queue:

- Expand the catalogue beyond the six built-in styles, with additional named styles in each style set.
- Add paragraph settings such as paragraph spacing, indentation, line spacing and keeping a heading with the following paragraph.
- Extend saved style definitions while preserving existing style sets, document fidelity and macro compatibility.
- Give Return and macro-inserted paragraphs the same following-style rules.

These additions preserve that queue rather than replacing it. The wider Coming Soon items and the remaining [language-intelligence backlog](https://github.com/martynwalker/CtrlZed/issues/17) remain recorded separately; this update does not move every longer-term item into the next build.

## Confirmed additions for the next release

### Suggested filename on first save

- [ ] On the first save of a document, use its first non-empty recognised heading as the suggested filename.
- [ ] If there is no heading, use the first sentence.
- [ ] Produce a valid Windows filename: remove unsupported characters, normalise whitespace, trim invalid trailing characters and keep a sensible length.
- [ ] Provide a sensible fallback for an empty document or an unusable suggestion.
- [ ] Keep the suggestion editable in the Save dialogue, with the chosen file type and extension handled normally.
- [ ] Apply the same suggestion to first-save entry points, including Save As for an unsaved document.
- [ ] Preserve existing filenames on subsequent saves; cancelling the dialogue must not save a file.

### Ctrl+Shift+V — paste unformatted text

- [ ] Paste clipboard plain text at the caret, replacing the selection where appropriate and preserving line breaks.
- [ ] Discard source rich formatting, including fonts, sizes, bold, italic, underline and colours, plus rich objects and embedded images.
- [ ] Use the destination paragraph's current typing format. Visible text from links may remain, but source hyperlink formatting and behaviour must not be imported.
- [ ] Leave ordinary Ctrl+V unchanged.
- [ ] Reserve Ctrl+Shift+V as a built-in command before macro-shortcut dispatch; preserve macro definitions and surface any assignment conflict without deleting the macro.
- [ ] Show Paste Unformatted Text and its shortcut in the Edit menu and keyboard-shortcut help.
- [ ] Keep the paste undoable; an empty or text-free clipboard must not insert rich content.

## Release checks

Retain the current Windows build, launch, document-fidelity, typing/following-style and menu-responsiveness checks. Add focused checks for heading-versus-sentence naming, safe filename fallback and first-save cancellation, and for source formatting being stripped while line breaks, destination style and Undo are preserved.

Status: planned. These requirements have been recorded; no application implementation or new release is claimed by this planning update.
