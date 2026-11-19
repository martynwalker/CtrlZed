using System.Windows.Controls;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    // A closed menu under the pointer is not an interaction: it must not
    // indefinitely prevent recovery, pagination or review work from resuming.
    internal bool IsMenuInteractionActive
        =>MainMenu!=null && (MainMenu.IsKeyboardFocusWithin ||
            MainMenu.Items.OfType<MenuItem>().Any(item=>item.IsSubmenuOpen)) ||
          Mouse.Captured is ContextMenu { IsOpen:true };
}
