# CtrlZed Language Assistance

CtrlZed combines conventional local spelling checks with optional contextual assistance. The two services deliberately have different jobs.

## Red spelling marks

Ordinary spelling is checked locally and remains available without an AI account or network connection. A red squiggly underline means that CtrlZed does not recognise the word in the selected dictionary or personal dictionary.

Right-click the marked word to see conventional spelling suggestions, ignore it for the document, or add it to the personal dictionary.

## Contextual spelling assistance

When contextual assistance is enabled, CtrlZed may examine a completed sentence after its final punctuation and following space. It can use the sentence to distinguish between plausible spelling candidates. Automatic correction is limited to high-confidence cases and remains immediately undoable.

AI assistance is optional. When enabled, CtrlZed sends only the completed sentence, the marked word and the permitted correction candidates. It does not send the entire document for an ordinary spelling decision.

## When CtrlZed leaves the red mark

Context does not make every unrecognisable word understandable. A fragment such as `berr` may have several possible intentions, or none that can be established reliably from the sentence. In that situation CtrlZed does not guess. The original word remains in place with its red spelling mark so that the writer can choose the correction.

This is deliberate. A visible unresolved word is safer than a fluent but incorrect automatic substitution.

The same principle applies to names, acronyms, specialist terminology, invented words, dialect, quoted material and ambiguous corrections. These normally remain suggestions rather than silent changes.

## Writing modes

Writing modes tell later grammar, style, thesaurus and poetry services what kind of document is being written. They adjust the tolerance and priorities of suggestions; they do not force the text into a house style.

British English is the default language profile.
