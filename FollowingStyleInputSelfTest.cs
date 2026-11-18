using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal static int RunFollowingStyleInputSelfTest()
    {
        MainWindow? window=null;
        try
        {
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow();
            window.Show();
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.CheckFollowingStyleInput();
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());
            return 120;
        }
        finally
        {
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();
                window.uiRefreshTimer.Stop();
                window.Close();
            }
        }
    }

    void CheckFollowingStyleInput()
    {
        styleSets.Clear();
        styleSets.Add("Default",StyleSetStore.Defaults()["Default"]);
        activeStyleSet="Default";
        foreach(var role in new[]{"Heading 1","Heading 2","Heading 3","Quote","Caption"})
        {
            var heading=styleSets[activeStyleSet].Styles[role];
            heading.Colour="#66CC00";
            heading.Bold=true;
            heading.Italic=true;
            heading.Underline=true;
            heading.SizePt=20;
            heading.Alignment="Centre";
            var original=PrepareStyledInputParagraph(role,"New Heading");
            if(!Equals(original.Tag,ParagraphStyleTagPrefix+role))
                throw new InvalidOperationException("Typing lost the selected paragraph style: "+role);
            VerifyHeadingInput(original,role+" before Return");
            PressReturn();
            var next=Editor.CaretPosition.Paragraph!;
            if(ReferenceEquals(next,original))throw new InvalidOperationException("Return did not insert a paragraph.");
            VerifyHeadingInput((Paragraph)Editor.Document.Blocks.FirstBlock,role+" after Return");
            Editor.Undo();
            if(Editor.Document.Blocks.Count!=2)
                throw new InvalidOperationException("Return and following-style change were not one undo operation.");
            Editor.Redo();
            TypeInput("Normal after heading");
            VerifyNormalTypedParagraph(Editor.CaretPosition.Paragraph!,role);
            PressReturn();
            TypeInput("Still normal");
            VerifyNormalTypedParagraph(Editor.CaretPosition.Paragraph!,role+" second Return");
            VerifyHeadingInput((Paragraph)Editor.Document.Blocks.FirstBlock,role+" after undo, redo and further typing");
        }

        // A soft break or a split within an existing heading retains its style.
        foreach(var softBreak in new[]{true,false})
        {
            var original=PrepareStyledInputParagraph("Heading 1","Heading text");
            if(softBreak)
            {
                SendKeys(new[]{KeyInput(0x10),KeyInput(0x0D),KeyInput(0x0D,true),KeyInput(0x10,true)});
                if(Editor.Document.Blocks.Count!=2)throw new InvalidOperationException("Shift+Return created a new paragraph.");
            }
            else
            {
                Editor.CaretPosition=original.ContentStart.GetPositionAtOffset(5,LogicalDirection.Forward)!;
                PressReturn();
            }
            TypeInput("More");
            var range=new TextRange(Editor.CaretPosition.Paragraph!.ContentStart,Editor.CaretPosition.Paragraph.ContentEnd);
            if(!Equals(range.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold))
                throw new InvalidOperationException("A soft break or mid-heading split unexpectedly changed style.");
        }

        // Imported DOCX headings retain their identity even when their visual
        // formatting differs from the active style set.
        var importedPath=Path.Combine(Path.GetTempPath(),"CtrlZedFollowingStyle_"+Guid.NewGuid().ToString("N")+".docx");
        try
        {
            var original=PrepareStyledInputParagraph("Heading 1","Imported Heading");
            DocxInterop.Save(importedPath,Editor.Document);
            Editor.Document=DocxInterop.Load(importedPath);
            original=(Paragraph)Editor.Document.Blocks.FirstBlock;
            Editor.CaretPosition=original.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            Editor.Focus();
            PumpInput();
            PressReturn();
            TypeInput("Normal after imported heading");
            VerifyNormalTypedParagraph(Editor.CaretPosition.Paragraph!,"imported DOCX Heading 1");
        }
        finally { if(File.Exists(importedPath))File.Delete(importedPath); }
    }

    static void VerifyHeadingInput(Paragraph paragraph,string context)
    {
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        var weight=range.GetPropertyValue(TextElement.FontWeightProperty);
        var colour=range.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
        if(range.Text.Trim()!="New Heading" || !Equals(weight,FontWeights.Bold) || colour?.Color!=Color.FromRgb(102,204,0))
            throw new InvalidOperationException($"Heading formatting changed {context}: text={range.Text.Trim()}, weight={weight}, colour={colour}.");
    }

    Paragraph PrepareInputParagraph()
    {
        var original=new Paragraph();
        var document=new FlowDocument(original);
        document.Blocks.Add(new Paragraph(new Run("Existing body text")));
        Editor.Document=document;
        Editor.CaretPosition=original.ContentStart;
        Editor.Focus();
        Keyboard.Focus(Editor);
        PumpInput();
        if(!Editor.IsKeyboardFocused)throw new InvalidOperationException("The input test could not focus the editor.");
        return original;
    }

    Paragraph PrepareStyledInputParagraph(string role,string text)
    {
        var paragraph=PrepareInputParagraph();
        TypeInput(text);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
        ApplyParagraphStyle(role);
        Editor.CaretPosition=paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        PumpInput();
        return paragraph;
    }

    void VerifyNormalTypedParagraph(Paragraph paragraph,string context)
    {
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        var expected=styleSets[activeStyleSet].Styles["Normal"];
        if(string.IsNullOrWhiteSpace(range.Text) ||
           !Equals(paragraph.Tag,ParagraphStyleTagPrefix+"Normal") ||
           !Equals(range.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Normal) ||
           !Equals(range.GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Normal) ||
           !Equals(range.GetPropertyValue(TextElement.FontSizeProperty),expected.SizePt*96/72) ||
           (range.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color!=Colors.Black ||
           (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations && decorations.Count>0) ||
           paragraph.TextAlignment!=TextAlignment.Left)
            throw new InvalidOperationException("Actual text input retained heading formatting after "+context+". "+range.Text);
    }

    static void PressReturn()=>SendKeys(new[]{KeyInput(0x0D),KeyInput(0x0D,true)});
    static void TypeInput(string text)
    {
        var inputs=new System.Collections.Generic.List<NativeInput>();
        foreach(var c in text)
        {
            if(c!=' ' && (c<'A' || c>'Z') && (c<'a' || c>'z'))
                throw new InvalidOperationException("The keyboard fixture requires ASCII letters and spaces.");
            var shift=c>='A' && c<='Z';
            var key=(ushort)(c==' '?0x20:char.ToUpperInvariant(c));
            if(shift)inputs.Add(KeyInput(0x10));
            inputs.Add(KeyInput(key));
            inputs.Add(KeyInput(key,true));
            if(shift)inputs.Add(KeyInput(0x10,true));
        }
        SendKeys(inputs.ToArray());
    }

    static NativeInput KeyInput(ushort key,bool up=false)
        =>new(){Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{Key=key,Flags=up?2U:0U}}};

    static void SendKeys(NativeInput[] inputs)
    {
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<NativeInput>())!=inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Windows rejected test keyboard input.");
        PumpInput();
    }

    static void PumpInput()
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(80)};
        timer.Tick+=(_,__)=>{timer.Stop();frame.Continue=false;};
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public UIntPtr Extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,NativeInput[] inputs,int size);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
}
