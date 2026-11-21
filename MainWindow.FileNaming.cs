using System.Windows.Documents;

namespace CtrlZed;

public partial class MainWindow
{
    string SuggestedFirstSaveFileName(string extension=".czd")
    {
        var paragraphs=EnumerateDocumentParagraphs().Select(paragraph=>
        {
            var role=InferParagraphStyle(paragraph,styleSets.Values);
            var heading=role!=null && (ResolveStyle(role)?.OutlineLevel>0 ||
                role.Equals("Heading 1",StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Heading 2",StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Heading 3",StringComparison.OrdinalIgnoreCase));
            return (Text:new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text,IsHeading:heading);
        });
        return DocumentFileName.WithExtension(DocumentFileName.Suggest(paragraphs),extension);
    }
}

