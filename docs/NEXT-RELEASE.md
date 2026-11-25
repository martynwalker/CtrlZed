# CtrlZed — Beta 53 and release record

Updated 25 November 2026.

## Baselines and acceptance

Beta 53 (`0.53.0-beta.53`) is the current development baseline on GitHub `main`. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45589758528) on source `799eb1cb1b2f4de135a8b441da64d21cf76276d1`, including packaging and publication on 25 November 2026. The release ZIP comes from that tested source; baseline promotion adds release documentation. The final Wordplay Workshop screenshot was inspected. Beta 53 user acceptance is pending; Beta 52 remains accepted.

Beta 52 (`0.52.0-beta.52`) is the current accepted baseline. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45457086330) on source `9a14abbacfcc5218660767463c557ff336987ad6`, including packaging and publication on 24 November 2026. The release ZIP comes from that tested source; baseline promotion adds release documentation. The final Story Workshop screenshot was inspected. Martyn confirmed Beta 52 and Story Workshop as a first-time pass on 25 November 2026.

Beta 51 (`0.51.0-beta.51`) was the preceding accepted baseline. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45439998902) on source `da6f33cbb2ba01b11a894e48d972e949a139aadc`, including packaging and publication on 24 November 2026. The release ZIP comes from that tested source; baseline promotion adds release documentation. The final Windows dialogue screenshot was inspected. Martyn confirmed Beta 51 and Character Voice as a pass on 24 November 2026.

Beta 50.2 (`0.50.2-beta.50.2`) was the preceding accepted baseline. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45408843008) on source `c4c225e2108d70032bc125e7ee721a0d32b6541b`, including packaging and publication on 24 November 2026. The release ZIP is built from that tested source; baseline promotion adds release documentation. The final Windows toolbar screenshot was inspected. Martyn accepted Beta 50.2 on 24 November 2026 after reviewing the original transparent icon.

Beta 50.1 (`0.50.1-beta.50.1`) was the preceding accepted functional baseline. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45368663583) on source `6d12d8e503cd40fc8b11702df3af88a25923bf6d`, including packaging and publication on 24 November 2026. Martyn reported it as all good on 24 November, with the colour-icon square as a cosmetic follow-up addressed in Beta 50.2.

Beta 50 (`0.50.0-beta.50`) was the preceding development release. The [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45293288540) on source `26afca04def681354f61875b3e8361179519ecfd`, including packaging and publication on 24 November 2026.

Beta 49 (`0.49.0-beta.49`) was the preceding user-accepted baseline. Its [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45196647649) on source commit `bae79ec445f4af4a2f9514b24ac5a030777086f2`, including packaging and prerelease publication. Martyn confirmed Beta 49 as a pass on 23 November 2026.

Beta 48 (`0.48.0-beta.48`) was the preceding user-accepted baseline. The [Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45036690638) on source commit `0d630c7e8e260ece357abc4b01169c975ed210dc`. The release ZIP comes from that source; baseline promotion adds release documentation. Martyn confirmed Beta 48 as a pass on 22 November 2026, reporting that it works extremely well.

Beta 47 was the preceding development release. Its vector creation was reported working, with a separate low-priority Page Editing display report tracked as [issue #30](https://github.com/martynwalker/CtrlZed/issues/30); overall acceptance was not recorded.

Beta 46.2 was the previous accepted baseline. Martyn confirmed it as a pass on 22 November 2026 after retesting Return/navigation. Beta 48 builds forward from Beta 47 and retains that accepted work.

Beta 45.1 (`0.45.1-beta.45.1`) was the previous user-accepted baseline, confirmed as a pass by Martyn on 21 November 2026. Beta 46.2 builds forward from that source.

## Implemented in Beta 53 — Wordplay Workshop

- [x] Independent General-default axes for form, style, comic flavour, device, subject/target and physical/situational wordplay.
- [x] Bounded per-document pile, options, generated directions and independent favourites, including native/protected/recovery support alongside Story Workshop.
- [x] Explicit source-linked AI exploration, editable examples, favourite bank, Copy and guarded manuscript application.
- [x] Windows taxonomy, source evidence, UI, cancellation, native/recovery/export and existing release gates.

All Windows release checks passed. They cover independent creative choices, exact-source associations and strict responses, both workshops' native/protected/recovery records, rich manuscript application/Undo, edited favourite retention and provenance, offline saved-idea use, body exports, read-only controls and obsolete responses. Automated AI checks use deterministic examples; live wordplay quality remains the writer's judgement. Beta 52 is accepted. The low-priority Page Editing floating-picture issue #30 remains deferred.

## Implemented in Beta 52 — Story Workshop

- [x] Versioned, bounded per-document planning metadata for cast, characterisation, five arc stages, story logic, event cards, fair-play clues, questions and workshop conversation.
- [x] Native and protected `.czd` capture/restore, recovery and document isolation; native Save and explicit body-only non-native export.
- [x] Guided explicit-request conversation, strict proposal patches and confirmed-decision conflict protection.
- [x] Editable structured character/storyboard views, planning Undo and own-character access to Character Voice.
- [x] Windows metadata, conversation, cancellation, native/recovery/export and existing release gates.

All Windows release checks passed. They cover complete native/protected/recovery metadata, old-file/document isolation, body and print separation, body-export protection, explicit conversational requests, editable cast/arcs/storyboard, planning Undo, own-character voice profiles and late-response guards. Automated AI checks use a deterministic provider; live literary judgement remains the author's decision. Beta 52 is the accepted baseline. The low-priority Page Editing floating-picture issue #30 stays deferred.

## Implemented in Beta 51 — Character Voice

- [x] First-class Review/context-menu preview for selected or pasted prose, with author → character searches.
- [x] Built-in original trait profiles for Aubrey, Maturin, Holmes, Watson, Elizabeth Bennet and Darcy; editable notes, four delivery modes and a meaning/audience hint.
- [x] Local partial-name/alias filtering, with explicit AI discovery for further authors and characters; discovery receives no prose and suggestions are labelled for review.
- [x] Separate explicit rewrite through the configured direct AI provider, current Writing Mode and AI language-service opt-in; strict bounded responses and cancellation/stale guards.
- [x] Captured ordinary-text destination, multi-paragraph replacement or caret insertion, retained opening format and one native Undo.
- [x] Windows deterministic service, preview, cancellation, editing and existing release gates.

All Windows release checks passed, including both-view rich replacement/Undo, selected boundary spaces and paragraph separators with unchanged following formatting, pasted insertion, manual page breaks, explicit private discovery, real preview actions, cancellation and stale configurations. Martyn accepted the feature on 24 November 2026. Automated tests use a deterministic transport; live literary interpretation and prose quality are assessed by the writer. No document text or generated profile/result is persisted by this feature. The low-priority Page Editing floating-picture issue #30 remains deferred.

## Corrected in Beta 50.2 — original colour-picker artwork

Martyn reported Beta 50.1 as functionally all good on 24 November 2026 and requested removal of the square around the colour icon.

- [x] Recover and use the original 32×32 transparent pinwheel from the saved ColourPicker v1.0.0 icon resource, with no screenshot background or crop.
- [x] Remove square button chrome while retaining the same 26×24 clickable target, action and accessible name, with rounded hover/pressed feedback.
- [x] Retain all existing Windows release checks and inspect the toolbar at rest.

All Windows release checks passed and the final toolbar screenshot was inspected. The patch changes presentation only. The embedded PNG is the original 32×32 RGBA resource (SHA-256 `b18675ee0c2d13e878052807679d6d0870026bc008e7f1f67c82c8e66d2cd588`); its transparent corners are retained.

## Implemented in Beta 50.1 — preview clicks and text colour

- [x] Activate a clicked Google Fonts result immediately, open Preview while it downloads and reuse verified cached previews on repeated clicks.
- [x] Keep programmatic first-result selection passive and installation explicitly gated by licence acknowledgement and an Install action.
- [x] Add Format → Text Colour and the supplied colour-picker image in the toolbar before Fields, using the existing native Windows palette.
- [x] Foreground-only selection editing and future-typing colour through the shared insertion bridge, preserving pending font choices and existing text formatting.
- [x] Windows physical-click, palette-routing, cancellation, rich editing/Undo, pending-choice composition and persistence checks alongside all existing gates.

All Windows release checks passed. The product patch builds forward from the tested Beta 50 source; selected foreground is preserved in native, protected-native and DOCX saves, while unchanged character/paragraph properties and original letters remain intact. Martyn reported Beta 50.1 as functionally all good on 24 November 2026, with the icon correction recorded above.

## Implemented in Beta 50 — Google Fonts

- [x] Generate a pinned supported-static-font catalogue from the official source, retaining real face metadata, licence text and notices.
- [x] Local discovery and optional direct-provider AI ranking without sending the document sample.
- [x] Explicit download of a verified real private-file preview, with no document changes or installation during search/preview.
- [x] Explicit per-user installation, existing-registration protection, transaction rollback and source/licence receipts.
- [x] Honest restart guidance for WPF's cached installed-font list; continue applying ordinary installed family names through the accepted finder.
- [x] Windows catalogue/download/preview/UI/install checks alongside every accepted editing gate.

All Windows release checks passed. The catalogue scans 2,042 source families and supplies 1,423 supported static families; 607 variable/unsupported families and 12 families without a recognised complete licence record are excluded. The source pin is `06bfe0d62a98375cb09c942fa18e68bd32cff057` of the official Google Fonts repository. Beta 51 is the accepted baseline. The [Google Fonts guide](ONLINE-FONTS.md) records the workflow and static-font scope. Broader catalogues, variable fonts and generated fonts remain queued.

## Implemented in Beta 49 — installed Font Finder

- [x] Format → Find a Font, installed-family catalogue, local name/description search and Writing Mode ranking.
- [x] Real editable previews, eight or fewer installed choices, exact family/alias handling and explicit standard emphasis.
- [x] Separate optional Ask AI through the configured direct provider; strict source whitelist and bounded catalogue scope. Document sample text stays local.
- [x] Explicit captured-selection/caret apply and named-style updates preserving other character/paragraph settings, stable identities and aliases.
- [x] Cancellable modeless requests, stale query/mode/provider/document guards and no document changes during search or preview.
- [x] Guard the first inserted text span against WPF clearing pending font properties when keyboard/document languages differ, without changing either language or keyboard layout. Defer interior-word caret changes so existing letters keep their formatting.
- [x] Deterministic Windows search/schema/privacy tests, real modeless windows and Close, both editing views, native input, rich range/Undo/Redo, inside-word source fidelity, stale destinations and named-style/native persistence.

The full Windows release gate passed on 23 November 2026. The release ZIP is built from the tested source above; baseline promotion adds release documentation. The [usage guide](FONT-FINDER.md) records the workflow and scope. Selection formatting uses native document Undo; saved named-style definitions are outside that Undo history. Online catalogue discovery, licence-aware download/install and generated fonts remain queued. Martyn accepted Beta 49 as a pass on 23 November 2026.

## Implemented in Beta 48

- [x] Finish Sentence in Review and text context menus, using the current Writing Mode and selected inflection.
- [x] Six built-ins, custom named inflections, three strength levels and an optional meaning hint.
- [x] Three sentence previews with highlighted proposed words, editable wording and explicit insertion.
- [x] Accurate caret insertion or short phrase replacement, retained destination formatting and single-operation Undo.
- [x] Automatic, Paragraph, Section and Whole document context; complete-source ordered summarisation for larger contexts and bounded memory-only cache.
- [x] Explicit AI opt-in and configured provider, cancellation and stale-source/mode/provider protection.
- [x] Safe and compiled macro preview access without automatic generation or document changes; compatibility with older safe-macro starter references.
- [x] Windows deterministic-provider gates for strict responses, context coverage, preferences, cancellation/cache, real UI, rich range fidelity, both views and macro wrappers.

The [usage guide](SENTENCE-COMPLETION.md) records the supported workflow and limits. Live model quality and real-provider configuration are assessed manually; the automated release gate uses a deterministic provider. Custom inflections/last choices persist locally; portable profile-archive inclusion is later work. The previously reported [Page Editing floating-picture issue #30](https://github.com/martynwalker/CtrlZed/issues/30) is explicitly deferred at low priority.

## Implemented in Beta 47

- [x] Import supported outlined SVG signatures and retain a canonical vector master with a readable bitmap fallback.
- [x] Trace raster signatures to cropped, single-ink SVG contours with transparent backgrounds and preserved holes/dots.
- [x] Review original/result previews; adjust threshold, light ink and colour; export SVG and prevent stale trace acceptance.
- [x] Configure the named profile signature and retain existing safe/compiled macro interfaces and 40 mm placement.
- [x] Embed vector masters and exact dimensions in native documents and recovery, independently of the profile original; preserve them in portable profiles.
- [x] Use vector drawing sources for inline/floating editing and printing; retain RTF/current-DOCX bitmap compatibility.
- [x] Gate the release on real preview interaction, insertion/Cut Undo/Redo, clipboard/DOCX fallback pixels, tracing/SVG pixel checks, macros, native/profile/recovery persistence and vector print sources, alongside all accepted editing checks.

SVG scope: outlined paths/basic shapes/groups and solid paints are supported. Text, CSS, gradients/effects, linked/embedded content and SVG root transforms must be converted to supported paths. Clipboard, RTF and current-DOCX interchange use the bitmap fallback; native `.czd` and profiles retain the vector master.

## Corrected in Beta 46.2

A [baseline Windows reproduction](https://github.com/martynwalker/CtrlZed/actions/runs/45008775733) confirmed two causes: old automatic boundary positions became extra provisional sheets after Return, and native paragraph splitting copied direct/manual page breaks into new blank paragraphs.

- [x] Refill provisional pages using current text geometry and actual explicit breaks; keep the settled print map authoritative.
- [x] Retain one direct/manual boundary when Return inserts a blank line before its paragraph; preserve intentional named-style page-break rules.
- [x] Physically navigate by thumbnail and press Return five times, checking live/thumbnail/print page counts and stable re-navigation.
- [x] Check paragraph count, deliberate page-break count and Undo, plus WPF command and safe-macro paragraph insertion.
- [x] Decode imported DOCX pictures from a detached seekable stream, removing the background image-read/ZIP-disposal race established by a captured crash stack. Check decoded pixels and readiness immediately after import; run the full document suite in six normal processes.
- [x] Retain the scrolling, picture, style, clipboard, macro and menu-responsiveness release gates.

## Corrected in Beta 46.1

- [x] Preserve manual wheel/scrollbar browsing through pagination and native bring-into-view requests while retaining caret reveal during editing and keyboard movement.
- [x] Route ordinary paged wheel input to the outer document viewer, respecting Windows wheel settings.
- [x] Remove pagination invalidation caused by applying a map changing the projected page height.
- [x] Keep existing page thumbnails interactive while a new map is pending; follow live anchors and suppress navigation from internal selection restoration.
- [x] Refresh thumbnails after text/picture changes, including both floating-image layers and header/footer text.
- [x] Gate the patch on physical wheel input, retained browse offsets, typing after browsing, thumbnail pixel changes, pending navigation and repeat-click navigation.

## Implemented in Beta 46

- [x] Separate paper pages, margins and gaps with click-to-edit positioning in Paged Editing.
- [x] One canonical RichTextBox/document for typing, selection, clipboard commands, Undo/Redo and existing macros.
- [x] Live provisional page boundaries and automatic caret scrolling while background pagination waits for idle.
- [x] Preserve paragraph layout omitted by RTF and map worker positions back to canonical text offsets.
- [x] Render floating pictures independently of short text slices, preserving full-height signatures and anchor-page positioning.
- [x] Persist an optional picture page offset and apply it in editing, continuous view and printing.
- [x] Gate the release on physical page input, cross-page selection/Undo, visible caret movement, view/document fidelity, macro insertion, picture reflow and tall-image pixel agreement with print preview.
- [x] Retain the 600-paragraph menu-responsiveness check in Paged mode and all existing functional/style/clipboard checks.

Scope: this is the first stage of page-by-page editing. Field-code and non-printing-comment representations can differ from printed output, and advanced tables/oversized objects still need further layout work. Full Word publishing fidelity is not claimed.

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

The release gate includes the Windows build, self-contained publish, launch check, online-font catalogue/download/preview/per-user-installation checks, deterministic-provider installed Font Finder and sentence-completion regressions, document-fidelity/filename self-tests, actual following-style keyboard checks, menu responsiveness, custom-style/native/DOCX persistence, rich/plain-text-paste checks and physical paged-editing/picture regressions. The last complete run must pass before publishing the release or promoting the GitHub baseline.

The style checks exercise a renamed style in a copied set; paragraph settings on heading/body text and inside a table; native `.czd` save/reopen; custom DOCX style and layout round trips; and Return and macro insertion paths. The filename checks exercise heading/sentence precedence, abbreviations, Windows filename sanitisation, fallback names and length handling.

Martyn accepted Beta 45.1 as a pass on 21 November 2026. This records overall release acceptance; automated naming checks cover the naming rules and do not independently test every native Save-dialogue interaction.

Status: Beta 53 Windows build, all functional regressions, packaging and publication passed in [run 45589758528](https://github.com/martynwalker/CtrlZed/actions/runs/45589758528). The tested source and release documentation are promoted to `main` as the development baseline. Beta 53 awaits user acceptance. Martyn confirmed Beta 52 and Story Workshop as a first-time pass on 25 November 2026; Beta 52 remains the accepted baseline.

## Remaining queue

The wider Coming Soon work and remaining [language-intelligence backlog](https://github.com/martynwalker/CtrlZed/issues/17) remain separate. This release does not claim to deliver complete page-editing fidelity for fields/comments and advanced layout, broader online font-catalogues and variable-font support, experimental generated fonts, further SVG artwork support, Bitwarden integration or complete Word publishing fidelity.
