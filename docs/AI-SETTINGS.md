# AI Settings — Beta 54

Open **Tools → AI Settings…**, **File → Settings… → AI Settings…**, or **AI settings…** in a writing tool. This screen configures AI; the separate AI Manager and Macro Assistant remain available for writing, research and macro generation.

Choose ChatGPT, Claude or Gemini to edit that provider's model ID and direct API key. Existing model choices and direct credentials are retained. A stored key is never displayed: leave the key field blank to keep it, enter a replacement, or choose explicit key removal. Switching provider keeps your unsaved edits in the settings window. **Save** applies the drafts; **Cancel** or closing the window discards them. Opening, switching and saving do not contact an AI provider.

Choose the application default on the same screen. A provider can be the default before its key is configured; tools then explain what is missing instead of silently using another provider. The default persists when CtrlZed restarts. Copilot remains a browser handoff in AI Manager, rather than a direct workshop provider.

Wordplay and Story Workshops start with this default each time a new workshop window opens. Their provider dropdowns show the selected provider, model and whether a direct key is configured. Changing a workshop's dropdown affects that window only. It neither changes the application default nor writes provider information into the `.czd` document. An already open workshop keeps its local choice when the application default changes.

Changing provider cancels an unfinished workshop request immediately. Changing its model or key also invalidates unfinished work, including replacing one configured key with another. Cancelled or late replies cannot overwrite the current ideas or plan. Existing results remain available, and starting a fresh request is explicit.

Direct API keys are encrypted using Windows DPAPI for the current Windows user. They are not included in documents or portable profile archives. Named secrets used by compiled macros remain separately managed under **Macro Library → Secrets…**; this release does not copy credentials between the two stores. Macros using `CtrlZedAi.GetModel` continue to read the central model choice.

The automated Windows release gate uses synthetic provider responses and temporary test keys to check settings persistence, cancellation, routing and existing document behaviour. It does not make live AI requests or certify a provider's current model availability.

The [full Windows release gate](https://github.com/martynwalker/CtrlZed/actions/runs/45652237595) passed on source `1fe1371e1180f8ca416992e3c8dddc8eff38649a` on 25 November 2026, including packaging and publication. The settings and both workshop screenshots were inspected. Beta 54 awaits user acceptance; Beta 52 remains accepted.
