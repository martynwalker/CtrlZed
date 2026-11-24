# Text Colour — Beta 50.1

Choose **Format → Text Colour…**, or click the coloured picker icon immediately before **Fields** in the small toolbar. Both controls open the existing standard Windows colour palette and share its custom swatches.

Select text first to change its foreground colour. Font family, point size, bold, italic, underline and paragraph layout are retained. The colour change is one Undo operation.

With an empty selection, the colour applies to future typing at the captured caret. Existing letters remain unchanged, including when the caret is inside a word. A font chosen before the colour is retained; opening or cancelling the palette preserves that pending choice.

The palette starts with the current effective colour. Cancel leaves the document unchanged. Read-only documents cannot be coloured, and a result is rejected if its captured document or formatting has changed while the palette is open.

Text colour changes direct character formatting. To change a saved named style definition, use the existing Styles editor. No new macro command or keyboard shortcut is introduced.

The Windows release gate checks both controls, the supplied icon and its toolbar position, rich selected text, actual caret typing in both views, pending-font composition, cancellation/stale destinations, Undo/Redo and native/protected/DOCX colour persistence. Candidate verification is in progress.
