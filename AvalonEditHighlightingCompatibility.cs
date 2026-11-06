using ICSharpCode.AvalonEdit.Highlighting;
using System.Runtime.CompilerServices;

namespace CtrlZed;

/// <summary>
/// AvalonEdit renamed its built-in Visual Basic highlighting definition from
/// "VBNET" to "VB". Beta 30 still requested the legacy name, so register a
/// compatibility alias before the Macro Manager is opened.
/// </summary>
public static class AvalonEditHighlightingCompatibility
{
    [ModuleInitializer]
    public static void RegisterLegacyVisualBasicAlias()
    {
        var manager=HighlightingManager.Instance;
        if(manager.GetDefinition("VBNET")!=null)return;

        var visualBasic=manager.GetDefinition("VB");
        if(visualBasic!=null)
            manager.RegisterHighlighting("VBNET",Array.Empty<string>(),visualBasic);
    }
}
