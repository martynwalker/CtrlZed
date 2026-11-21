using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CtrlZed;

internal static class NativeColourDialog
{
    const uint RgbInitialColour=0x00000001;
    const uint FullOpen=0x00000002;
    const uint AnyColour=0x00000100;

    // ChooseColor writes custom swatches back into this 16-COLORREF buffer.
    // Keep them for this session even if the settings file cannot be written.
    static readonly uint[] customColours=LoadCustomColours();

    internal static string? Show(Window owner,string? initial)
    {
        var pinnedColours=GCHandle.Alloc(customColours,GCHandleType.Pinned);
        try
        {
            var options=new ChooseColourOptions
            {
                Size=(uint)Marshal.SizeOf<ChooseColourOptions>(),
                Owner=new WindowInteropHelper(owner).EnsureHandle(),
                Result=InitialColourRef(initial),
                CustomColours=pinnedColours.AddrOfPinnedObject(),
                Flags=RgbInitialColour|FullOpen|AnyColour
            };

            bool accepted;
            ComponentDispatcher.PushModal();
            try { accepted=ChooseColor(ref options); }
            finally { ComponentDispatcher.PopModal(); }

            var error=accepted?0:CommDlgExtendedError();
            SaveCustomColours();
            if(accepted)return ColourRefToHex(options.Result);

            if(error!=0)
                MessageBox.Show(owner,$"The Windows colour palette could not be opened (error {error}).",
                    "Choose Colour",MessageBoxButton.OK,MessageBoxImage.Warning);
            return null;
        }
        finally { pinnedColours.Free(); }
    }

    internal static uint ToColourRef(Color colour)
        =>(uint)(colour.R|(colour.G<<8)|(colour.B<<16));

    internal static string ColourRefToHex(uint value)
        =>$"#{value&0xFF:X2}{(value>>8)&0xFF:X2}{(value>>16)&0xFF:X2}";

    internal static uint InitialColourRef(string? initial)
    {
        try
        {
            if(!string.IsNullOrWhiteSpace(initial) && ColorConverter.ConvertFromString(initial) is Color colour)
                return ToColourRef(colour);
        }
        catch { }
        return 0;
    }

    static string CustomColoursPath=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","custom-colours.json");

    static uint[] LoadCustomColours()
    {
        try
        {
            if(File.Exists(CustomColoursPath))
            {
                var saved=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(CustomColoursPath));
                if(saved?.Length==16 && saved.All(colour=>colour<=0xFFFFFF))return saved;
            }
        }
        catch { }
        return Enumerable.Repeat(0xFFFFFFU,16).ToArray();
    }

    static void SaveCustomColours()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CustomColoursPath)!);
            File.WriteAllText(CustomColoursPath,JsonSerializer.Serialize(customColours));
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ChooseColourOptions
    {
        internal uint Size;
        internal IntPtr Owner;
        internal IntPtr Instance;
        internal uint Result;
        internal IntPtr CustomColours;
        internal uint Flags;
        internal IntPtr CustomData;
        internal IntPtr Hook;
        internal IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll",EntryPoint="ChooseColorW",ExactSpelling=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool ChooseColor(ref ChooseColourOptions options);

    [DllImport("comdlg32.dll",ExactSpelling=true)]
    static extern uint CommDlgExtendedError();
}

