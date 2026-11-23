# CtrlZed — Beta 49 release record and remaining queue

Updated 23 November 2026.

## Baselines and acceptance

Beta 49 (`0.49.0-beta.49`) is the current development baseline on GitHub `main`. Its [full Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45196647649) on source commit `bae79ec445f4af4a2f9514b24ac5a030777086f2`, including packaging and prerelease publication. User acceptance of Beta 49 is pending.

Beta 48 (`0.48.0-beta.48`) remains the current user-accepted baseline. The [Windows release gate passed](https://github.com/martynwalker/CtrlZed/actions/runs/45036690638) on source commit `0d630c7e8e260ece357abc4b01169c975ed210dc`. The release ZIP comes from that source; baseline promotion adds release documentation. Martyn confirmed Beta 48 as a pass on 22 November 2026, reporting that it works extremely well.

Beta 47 was the preceding development release. Its vector creation was reported working, with a separate low-priority Page Editing display report tracked as [issue #30](https://github.com/martynwalker/CtrlZed/issues/30); overall acceptance was not recorded.

Beta 46.2 was the previous accepted baseline. Martyn confirmed it as a pass on 22 November 2026 after retesting Return/navigation. Beta 48 builds forward from Beta 47 and retains that accepted work.

Beta 45.1 (`0.45.1-beta.45.1`) was the previous user-accepted baseline, confirmed as a pass by Martyn on 21 November 2026. Beta 46.2 builds forward from that source.

## Implemented in Beta 49 — installed Font Finder

- [x] Format → Find a Font, installed-family catalogue, local name/description search and Writing Mode ranking.
- [x] Real editable previews, eight or fewer installed choices, exact family/alias handling and explicit standard emphasis.
- [x] Separate optional Ask AI through the configured direct provider; strict source whitelist and bounded catalogue scope. Document sample text stays local.
- [x] Explicit captured-selection/caret apply and named-style updates preserving other character/paragraph settings, stable identities and aliases.
- [x] Cancellable modeless requests, stale query/mode/provider/document guards and no document changes during search or preview.
- [x] Guard the first inserted text span against WPF clearing pending font properties when keyboard/document languages differ, without changing either language or keyboard layout. Defer interior-word caret changes so existing letters keep their formatting.
- [x] Deterministic Windows search/schema/privacy tests, real modeless windows and Close, both editing views, native input, rich range/Undo/Redo, inside-word source fidelity, stale destinations and named-style/native persistence.

The full Windows release gate passed on 23 November 2026. The release ZIP is built from the tested source above; baseline promotion adds release documentation. The [usage guide](FONT-FINDER.md) records the workflow and scope. Selection formatting uses native document Undo; saved named-style definitions are outside that Undo history. Online catalogue discovery, licence-aware download/install and generated fonts remain queued. Beta 48 remains the accepted baseline until Martyn tests Beta 49.

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

The release gate includes the Windows build, self-contained publish, launch check, deterministic-provider Font Finder and sentence-completion regressions, document-fidelity/filename self-tests, actual following-style keyboard checks, menu responsiveness, custom-style/native/DOCX persistence, rich/plain-text-paste checks and physical paged-editing/picture regressions. The last complete run must pass before publishing the release or promoting the GitHub baseline.

The style checks exercise a renamed style in a copied set; paragraph settings on heading/body text and inside a table; native `.czd` save/reopen; custom DOCX style and layout round trips; and Return and macro insertion paths. The filename checks exercise heading/sentence precedence, abbreviations, Windows filename sanitisation, fallback names and length handling.

Martyn accepted Beta 45.1 as a pass on 21 November 2026. This records overall release acceptance; automated naming checks cover the naming rules and do not independently test every native Save-dialogue interaction.

Status: Beta 49 Windows build, all functional regressions, packaging and publication passed in [run 45196647649](https://github.com/martynwalker/CtrlZed/actions/runs/45196647649). The tested source and release documentation are promoted to `main` as the development baseline. Beta 49 awaits user acceptance; Beta 48 remains accepted. Beta 46.2 user acceptance passed on 22 November 2026 after the Return/navigation retest; Martyn confirmed Beta 48 as a pass on 22 November 2026.

## Remaining queue

The wider Coming Soon work and remaining [language-intelligence backlog](https://github.com/martynwalker/CtrlZed/issues/17) remain separate. This release does not claim to deliver complete page-editing fidelity for fields/comments and advanced layout, online font-catalogue discovery and the font installation workflow, experimental generated fonts, further SVG artwork support, Bitwarden integration or complete Word publishing fidelity.
