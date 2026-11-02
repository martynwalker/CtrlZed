namespace CtrlZed;

public static class SafeMacroLanguage
{
    public const string ExtendedReference =
@"EXTENDED MACROS ARE ENABLED.
In addition to every safe command below, you may use:
PROMPT variable prompt text
INSERT_QR text-or-{variable}

Use PROMPT to collect user input into a named variable, then reference it as {variable} in later commands.
Example:
# NAME: Insert QR Code
# DESCRIPTION: Asks for a link and inserts its QR code at the cursor.
PROMPT LINK Enter the link or text for the QR code:
INSERT_QR {LINK}

" + Reference;

    public const string Reference =
@"INSERT_TEXT text
INSERT_FIELD name
NEW_PARAGRAPH
PAGE_BREAK
APPLY_STYLE Normal|Heading 1|Heading 2|Heading 3|Quote|Caption
ALIGN LEFT|CENTRE|RIGHT|JUSTIFY
BULLETS
NUMBERING
SELECT_ALL
FIND_REPLACE find text || replacement text
INSERT_TABLE rows columns
SET_HEADER text
SET_FOOTER text
SET_PROPERTY TITLE|AUTHOR|SUBJECT|KEYWORDS value
AI_REWRITE instruction
AI_INSERT instruction

Metadata comments:
# NAME: Friendly macro name
# DESCRIPTION: What the macro does
# SHORTCUT: Ctrl+Alt+M
MENU_NAME=""Macro Name""
MENU_POSITION=1
MENU_PATH=""Writing/Plotting Methods""
MENU_PATH_POSITIONS=""7/2""

Notes:
- Lines beginning # are comments/metadata.
- MENU_NAME and MENU_POSITION add the macro to a menu.
- Omit MENU_PATH (or set MENU_PARENT=""right-click"") for the editor right-click Macros submenu.
- MENU_PATH=""Writing/Plotting Methods"" creates/reuses that main-menu hierarchy. MENU_PATH_POSITIONS=""7/2"" gives ordering hints for each path level.
- MENU_OPTION remains supported as a legacy alias for MENU_POSITION.
- FIND_REPLACE uses || to separate search and replacement text.
- INSERT_TABLE accepts integer row/column counts from 1 to 50 / 20.
- Safe macros cannot access files, arbitrary network endpoints, processes, credentials or arbitrary .NET code.
- AI_REWRITE and AI_INSERT may call the user's configured AI provider through CtrlZed's restricted AI service; the macro never receives the provider credential.
- Maximum 200 executable commands per run.";
}
