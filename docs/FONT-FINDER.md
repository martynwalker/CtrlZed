# Find a Font — Beta 49

Open **Format → Find a Font…**. The first release searches fonts already installed on the current Windows computer. It does not download or install fonts.

## Search and preview

Select the text you want to change, or place the typing caret, before opening the finder. A bounded sample of that text or paragraph appears in the editable preview box. Editing the sample or preview size does not change the document.

Enter a family name, such as `Arial`, or a description, such as `a traditional serif for a novel` or `a clean sans serif for a technical report`, and choose **Search installed**. Local searches use installed names, a small set of known font characteristics and the current Writing Mode. They require no AI account. Fonts without known characteristics remain available by name; the local search does not analyse every font's visual design.

Up to eight installed families appear as samples with reasons. Exact installed names take precedence over emphasis words within a family name: `Arial Rounded MT Bold` is a complete family name. `Arial Bold` requests Arial with standard bold formatting.

**Ask AI** is separate and runs only when chosen. Enable AI language services in Language settings and configure a direct ChatGPT, Claude or Gemini provider in AI settings. Copilot's browser handoff cannot supply direct font suggestions. Requests send the description, Writing Mode and installed-font catalogue; preview text from the document stays on the computer. For an unusually large catalogue a ranked subset is supplied and identified as such. Results are checked against the supplied installed families before being offered.

AI reasons and rankings are suggestions for the writer to assess. Automated tests use a deterministic provider; real-provider configuration and suggestion quality require manual acceptance.

## Apply to text or a style

Select a sample, choose the destination and explicitly apply it. Weight and Slant each offer **As requested**, **Keep existing**, and explicit regular/bold or normal/italic choices. As requested preserves existing emphasis unless the description explicitly asks for a change. Only font family and chosen emphasis are applied; document point size, colour, underline and paragraph layout are retained. Preview size is never applied.

**Selection / typing caret** applies to the captured selection, or sets the font for future typing at the captured caret. Applying a font to selected text is one document Undo operation. Text outside the selection is retained. At a caret inside a word, the existing letters also retain their formatting; the choice takes effect when you type. If you move elsewhere, use **Use current selection / caret** to capture the new destination.

**Named style** updates and saves that style in the displayed active set and changes the font of paragraphs assigned to it in the current document. Other style parameters, stable identities and rename aliases are retained. Saved style definitions are outside native document Undo; restore their definition through **Format → Styles**. Document formatting Undo does not restore a saved style definition.

Editing the document, opening another document, making it read-only or changing the active style set can invalidate a captured destination. Capture it again before applying. Old asynchronous results are discarded after the request, Writing Mode or AI settings change, or after closing the finder.

Font discovery happens when the finder opens, not when a menu opens. Existing font formatting persists through native documents, profiles and the supported interchange paths. No new font files are embedded by this feature; another computer needs the corresponding font installed to reproduce its appearance.

## Later work

Supported static Google Fonts discovery, licence review, downloads and per-user Windows installation are available in [Beta 50](ONLINE-FONTS.md). Broader catalogues, variable fonts and experimental generated fonts remain queued. The low-priority Page Editing floating-picture display issue is tracked separately in [issue #30](https://github.com/martynwalker/CtrlZed/issues/30).

## Release verification

The [Windows release gate](https://github.com/martynwalker/CtrlZed/actions/runs/45196647649) passed on source `bae79ec445f4af4a2f9514b24ac5a030777086f2` on 23 November 2026. It checks local and deterministic AI searches, privacy, actual preview/focus/Close behaviour, selected-range and caret typing in both views, inside-word source fidelity, native Undo/Redo, keyboard/document language mismatch, stale destinations and style/native persistence. Existing document, menu, rich/plain paste, sentence completion, vector and paged editing gates also passed. Martyn confirmed Beta 49 as a pass on 23 November 2026.
