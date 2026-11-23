# Google Fonts — Beta 50

Open **Format → Find a Font… → Google Fonts…**. This browser extends the installed-font finder with a supported selection of static fonts from the official Google Fonts repository.

## Search and inspect

The catalogue ships with CtrlZed, so searching family names and supported descriptions needs no Google API key or network connection. It is a snapshot of actual source metadata, not a claim to include every font on Google Fonts. The window identifies its scope and catalogue count. Variable fonts and unsupported faces remain later work.

Select a result to inspect its designer, available faces, source and complete licence/notice text. Available faces are actual regular/bold and normal/italic records in the catalogue; the browser does not invent a missing face.

**Ask AI** is optional and separate from local search. It uses the configured direct provider and requires AI language services to be enabled. Only the request, Writing Mode and bounded catalogue data are supplied. Suggestions must identify a real supplied family; AI cannot choose an arbitrary font-download address. Live provider configuration and suggestion quality require manual acceptance.

## Download a preview

Choose a face and **Download preview**. CtrlZed downloads that font from its recorded official source, checks its bytes and renders a real local preview. Downloading a preview does not install the font or edit the document. Editing the sample changes the preview only. The sample stays on the computer; it is not sent to Google, a subsetting service or an AI provider.

Requests use the pinned source revision `06bfe0d62a98375cb09c942fa18e68bd32cff057` of [google/fonts](https://github.com/google/fonts/tree/06bfe0d62a98375cb09c942fa18e68bd32cff057). The corresponding licence and notices are retained with installed files. Catalogue updates are part of a later CtrlZed build.

## Install and use

After reviewing the displayed terms, tick the licence checkbox and choose **Install for this Windows user**. Installation downloads the supported faces belonging to that family, rather than only the face used for the preview. It preserves existing fonts and reports a conflict instead of replacing an unrelated registration.

Font installation is separate from document editing and is outside document Undo. Fonts are installed for the current Windows user without requesting administrator access. A receipt and the source licence/notice text are retained locally.

WPF caches its installed-font list. After a successful installation, save your document, close and reopen CtrlZed, then use **Format → Find a Font…** to select the installed family and apply it to text or a named style. The browser does not force a restart or apply a temporary cache-file address to the document.

Existing named styles, macros and native documents continue to use ordinary font family names. This feature does not embed font binaries in documents or portable profiles. Another computer needs the corresponding font installed to reproduce its appearance.

## Verification and scope

The release gate includes catalogue-generation checks; exact source, licence and font-byte validation; local and deterministic AI searches; private-file previews; stale-request handling; actual preview-window interaction; and per-user installation, collision, rollback and cleanup checks. It retains the accepted document, menu, style, rich/plain paste, page, vector-signature, sentence-completion and installed-font finder gates. Beta 50 remains a build candidate until those checks and publication complete.

The low-priority Page Editing floating-picture display issue remains tracked separately in [issue #30](https://github.com/martynwalker/CtrlZed/issues/30).
