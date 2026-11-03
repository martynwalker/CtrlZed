using System.IO;
using System.Text.Json;

namespace CtrlZed;

public static class StyleSetStore
{
    public sealed class TextStyle
    {
        public string FontFamily { get; set; }="Calibri";
        public double SizePt { get; set; }=12;
        public string Colour { get; set; }="#000000";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string Alignment { get; set; }="Left";
        public double MarginLeft { get; set; }
        public double MarginTop { get; set; }
        public double MarginRight { get; set; }
        public double MarginBottom { get; set; }=8;
    }

    public sealed class StyleSet
    {
        public string Name { get; set; }="Default";
        public Dictionary<string,TextStyle> Styles { get; set; }=new(StringComparer.OrdinalIgnoreCase);
    }

    static string PathName
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"style-sets.json");
        }
    }

    public static Dictionary<string,StyleSet> Load()
    {
        try
        {
            if(!File.Exists(PathName))return Defaults();
            var value=JsonSerializer.Deserialize<Dictionary<string,StyleSet>>(File.ReadAllText(PathName));
            return value==null?Defaults():new(value,StringComparer.OrdinalIgnoreCase);
        }
        catch{return Defaults();}
    }

    public static void Save(IReadOnlyDictionary<string,StyleSet> sets)
        =>File.WriteAllText(PathName,JsonSerializer.Serialize(sets,new JsonSerializerOptions{WriteIndented=true}));

    public static Dictionary<string,StyleSet> Defaults()
    {
        TextStyle S(double size,bool bold=false,bool italic=false,string align="Left",double l=0,double t=0,double r=0,double b=8)
            =>new(){SizePt=size,Bold=bold,Italic=italic,Alignment=align,MarginLeft=l,MarginTop=t,MarginRight=r,MarginBottom=b};
        var set=new StyleSet{Name="Default"};
        set.Styles["Normal"]=S(12);
        set.Styles["Heading 1"]=S(18,true,false,"Left",0,12,0,8);
        set.Styles["Heading 2"]=S(15,true,false,"Left",0,10,0,6);
        set.Styles["Heading 3"]=S(12.75,true,false,"Left",0,8,0,5);
        set.Styles["Quote"]=S(12,false,true,"Left",27,6,27,6);
        set.Styles["Caption"]=S(10,false,true,"Center",0,4,0,4);
        return new(StringComparer.OrdinalIgnoreCase){{"Default",set}};
    }
}
