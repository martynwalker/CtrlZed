# Character Voice — Beta 51

Select prose and choose **Review → Character Voice…**, or use the text right-click menu. The original passage appears in a separate preview. You can also open the window with an empty caret and paste text into Original.

Search Author, select an author, then search In the voice of and select a character. Typing `Patrick O'` finds Patrick O'Brian; `Jack` finds Captain Jack Aubrey and `Maturin` finds Dr Stephen Maturin. The supplied catalogue also includes Arthur Conan Doyle's Holmes and Watson, and Jane Austen's Elizabeth Bennet and Darcy.

The voice notes describe outlook, knowledge, limitations, humour and contrasting traits. Review or edit them to guide the interpretation. Choose Spoken, Written, Reflection or Addressing crew, and optionally explain your intended meaning or audience. The current Writing Mode supplies the purpose and register.

Choose **Rewrite** to request a preview. The request asks for fresh wording that preserves the passage's meaning, setting, facts and intent; character voice need not turn every subject into a nautical comparison or a joke. Review the result and edit it before applying or copying. The AI's literary interpretation and factual fidelity still need your judgement.

**Replace selection** changes the original captured passage. **Insert at caret** inserts at the captured caret. Merely moving the document selection does not move that destination. **Use current selection / caret** explicitly captures a new destination; a selection also replaces the Original text, while an empty caret retains pasted Original text. The destination description shows what Apply will do.

Apply uses the destination's opening font, size, colour and emphasis, and retains paragraph layout. Selected boundary spaces and paragraph breaks are retained so that untouched neighbours remain separate. A passage with mixed character formatting becomes one opening character format; Ctrl+Z restores the original rich passage in one operation. Ordinary prose within one table cell is supported; crossing table structures, pictures, fields and embedded objects is excluded. Source passages are limited to 16,000 characters and rewrites to 24,000. Pasted-source insertion needs a caret between words; include any desired spaces at its edges in the edited preview.

If the document changes, the old destination and result are invalidated. Capture the intended selection/caret and generate again. Changing the passage, author, character, notes, delivery, hint, provider or Writing Mode also invalidates a pending or completed rewrite. Cancel request and Close cancel work; a late response cannot be applied to another document. A read-only document can still be used alongside pasted-text previews and Copy, but cannot receive a rewrite.

Search boxes filter locally without AI calls. **Find author with AI** and **Find voices with AI** explicitly request further suggestions from the configured direct provider. Character discovery is bound to the selected author, and generated interpretations are labelled as AI suggestions for review. They are not verified literary reference entries. Suggestions remain in memory while this window is open.

Rewriting and AI discovery require AI language services to be enabled in Language settings and a configured direct ChatGPT, Claude or Gemini provider. Discovery sends the search and selected author only. Rewrite sends Original, the selected author/voice notes, Writing Mode, delivery and optional hint; it does not read the surrounding document. Opening, selecting and editing previews do not send text or change the document. This feature does not save passages, generated results or voice profiles to preferences, native files or portable profiles.

Windows release checks use an in-process deterministic transport for discovery schemas, alias searches, cancellation, preview routing, captured-target guards, rich Undo and both editing views. They do not assess a live model's prose quality or provider setup.

The [full Windows release gate](https://github.com/martynwalker/CtrlZed/actions/runs/45439998902) passed on source `da6f33cbb2ba01b11a894e48d972e949a139aadc` on 24 November 2026, including packaging and publication. The final dialogue screenshot was inspected. Martyn confirmed Beta 51 and Character Voice as a pass on 24 November 2026.
