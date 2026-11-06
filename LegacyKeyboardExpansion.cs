using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    bool beta341WordStarBlockPrefix;
    bool beta341WordStarQuickPrefix;

    internal void HandleBeta341LegacyKeyboard(object sender,KeyEventArgs e)
    {
        if(profile==KeyboardProfile.WordPerfect51)
        {
            HandleWordPerfectExpansion(e);
            return;
        }
        if(profile==KeyboardProfile.WordStar40)
            HandleWordStarExpansion(e);
    }

    void HandleWordPerfectExpansion(KeyEventArgs e)
    {
        var modifiers=Keyboard.Modifiers;
        if(modifiers==ModifierKeys.None)
        {
            switch(e.Key)
            {
                case Key.F2:
                    FindReplace_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F4:
                    EditingCommands.IncreaseIndentation.Execute(null,Editor);
                    e.Handled=true;
                    return;
                case Key.F6:
                    EditingCommands.ToggleBold.Execute(null,Editor);
                    e.Handled=true;
                    return;
                case Key.F8:
                    EditingCommands.ToggleUnderline.Execute(null,Editor);
                    e.Handled=true;
                    return;
                case Key.F10:
                    SaveCurrent();
                    e.Handled=true;
                    return;
            }
        }
        else if(modifiers==ModifierKeys.Shift)
        {
            switch(e.Key)
            {
                case Key.F1:
                    Settings_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F4:
                    EditingCommands.DecreaseIndentation.Execute(null,Editor);
                    e.Handled=true;
                    return;
                case Key.F8:
                    PageSetup_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F10:
                    Open_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
            }
        }
        else if(modifiers==ModifierKeys.Alt)
        {
            switch(e.SystemKey)
            {
                case Key.F2:
                    FindReplace_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F6:
                    EditingCommands.AlignRight.Execute(null,Editor);
                    e.Handled=true;
                    return;
                case Key.F8:
                    Styles_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F9:
                    Picture_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
            }
        }
        else if(modifiers==ModifierKeys.Control)
        {
            switch(e.Key)
            {
                case Key.F2:
                    NextSpellingError_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
                case Key.F8:
                    FontBox.Focus();
                    e.Handled=true;
                    return;
                case Key.F9:
                    Fields_Click(this,new RoutedEventArgs());
                    e.Handled=true;
                    return;
            }
        }
    }

    void HandleWordStarExpansion(KeyEventArgs e)
    {
        var modifiers=Keyboard.Modifiers;
        var key=e.Key==Key.System?e.SystemKey:e.Key;

        if(beta341WordStarBlockPrefix)
        {
            beta341WordStarBlockPrefix=false;
            e.Handled=true;
            switch(key)
            {
                case Key.S:
                case Key.D:
                    SaveCurrent();
                    Status.Text="WordStar: document saved; editing continues.";
                    return;
                case Key.X:
                    if(SaveCurrent())Close();
                    return;
                case Key.Q:
                    Close();
                    return;
                case Key.R:
                    Import_Click(this,new RoutedEventArgs());
                    return;
                case Key.P:
                    Print_Click(this,new RoutedEventArgs());
                    return;
                default:
                    Status.Text="WordStar Ctrl+K command not assigned.";
                    return;
            }
        }

        if(beta341WordStarQuickPrefix)
        {
            beta341WordStarQuickPrefix=false;
            e.Handled=true;
            switch(key)
            {
                case Key.S:
                    EditingCommands.MoveToLineStart.Execute(null,Editor);
                    return;
                case Key.D:
                    EditingCommands.MoveToLineEnd.Execute(null,Editor);
                    return;
                case Key.R:
                    EditingCommands.MoveToDocumentStart.Execute(null,Editor);
                    return;
                case Key.C:
                    EditingCommands.MoveToDocumentEnd.Execute(null,Editor);
                    return;
                case Key.F:
                case Key.A:
                    FindReplace_Click(this,new RoutedEventArgs());
                    return;
                default:
                    Status.Text="WordStar Ctrl+Q command not assigned.";
                    return;
            }
        }

        if(modifiers==ModifierKeys.Control && key==Key.K)
        {
            beta341WordStarBlockPrefix=true;
            beta341WordStarQuickPrefix=false;
            wordStarPrefix=false;
            Status.Text="WordStar Ctrl+K …";
            e.Handled=true;
            return;
        }
        if(modifiers==ModifierKeys.Control && key==Key.Q)
        {
            beta341WordStarQuickPrefix=true;
            beta341WordStarBlockPrefix=false;
            wordStarPrefix=false;
            Status.Text="WordStar Ctrl+Q …";
            e.Handled=true;
            return;
        }
        if(modifiers!=ModifierKeys.Control)return;

        var command=key switch
        {
            Key.A=>EditingCommands.MoveLeftByWord,
            Key.F=>EditingCommands.MoveRightByWord,
            Key.R=>EditingCommands.MoveUpByPage,
            Key.C=>EditingCommands.MoveDownByPage,
            Key.W=>EditingCommands.MoveUpByLine,
            Key.Z=>EditingCommands.MoveDownByLine,
            Key.G=>EditingCommands.Delete,
            Key.T=>EditingCommands.DeleteNextWord,
            Key.N=>EditingCommands.EnterParagraphBreak,
            _=>null
        };
        if(command!=null)
        {
            command.Execute(null,Editor);
            e.Handled=true;
            return;
        }

        if(key==Key.Y)
        {
            DeleteCurrentWordStarLine();
            e.Handled=true;
        }
    }

    void DeleteCurrentWordStarLine()
    {
        var paragraph=FindContainingParagraph(Editor.CaretPosition);
        if(paragraph==null)return;
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        range.Text="";
        MarkDirty("WordStar: current line deleted. Ctrl+Z will undo it.");
    }
}

internal static class LegacyKeyboardExpansionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown),true);
    }

    static void OnPreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(sender is MainWindow window)window.HandleBeta341LegacyKeyboard(sender,e);
    }
}
