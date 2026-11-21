# Third-Party Notices

CtrlZed itself is released under the MIT licence.

## Open XML SDK

CtrlZed Beta 7 uses **DocumentFormat.OpenXml 3.5.1** for DOCX interchange.

Project: Open XML SDK  
Copyright: Microsoft and contributors  
Licence: MIT

The Open XML SDK source and licence are available from the upstream project:
https://github.com/dotnet/Open-XML-SDK

This dependency is used only for DOCX interchange. CtrlZed's native document format remains .czd.


## Html Agility Pack

CtrlZed Beta 45.1 uses **HtmlAgilityPack 1.13.1** to parse HTML clipboard fragments for formatted paste.

Project: Html Agility Pack

Copyright: ZZZ Projects Inc. and contributors

Licence: MIT

Source and licence: https://github.com/zzzprojects/html-agility-pack

Only the local clipboard HTML parser is used. The paste converter does not load web pages or external resources.
