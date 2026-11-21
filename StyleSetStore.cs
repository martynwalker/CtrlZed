using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CtrlZed;

public static class StyleSetStore
{
    internal static IReadOnlyList<string> ParagraphStyleNames { get; }=
        Array.AsReadOnly(new[]{"Normal","Heading 1","Heading 2","Heading 3","Quote","Caption"});

    internal static string NormalizeFollowingStyle(string? value)
        =>ParagraphStyleNames.FirstOrDefault(name=>string.Equals(name,value?.Trim(),StringComparison.OrdinalIgnoreCase))??"Normal";

    public static string NormalizeFollowingStyle(string? value,IEnumerable<string> names)
        =>names.FirstOrDefault(name=>string.Equals(name,value?.Trim(),StringComparison.OrdinalIgnoreCase))??"Normal";

    public static string NormalizeFollowingStyle(string? value,StyleSet set)
        =>ResolveStyleName(set,value)??"Normal";

    public static IReadOnlyList<string> StyleNames(StyleSet set)
        =>ParagraphStyleNames.Where(set.Styles.ContainsKey)
            .Concat(set.Styles.Keys.Where(name=>!ParagraphStyleNames.Contains(name,StringComparer.OrdinalIgnoreCase))
                .OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase)).ToArray();

    public sealed class TextStyle
    {
        public string Id { get; set; }="";
        public string FontFamily { get; set; }="Calibri";
        public double SizePt { get; set; }=12;
        public string Colour { get; set; }="#000000";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string FollowingStyle { get; set; }="Normal";
        public string Alignment { get; set; }="Left";
        public double MarginLeft { get; set; }
        public double MarginTop { get; set; }
        public double MarginRight { get; set; }
        public double MarginBottom { get; set; }=8;
        public double FirstLineIndent { get; set; }
        // Stored in WPF device-independent units; zero keeps automatic spacing.
        public double LineHeight { get; set; }
        public bool KeepWithNext { get; set; }
        public bool KeepTogether { get; set; }
        public bool BreakPageBefore { get; set; }
        public int OutlineLevel { get; set; }
    }

    public sealed class StyleSet
    {
        public string Name { get; set; }="Default";
        public Dictionary<string,TextStyle> Styles { get; set; }=new(StringComparer.OrdinalIgnoreCase);
        // Renamed styles retain their old names for existing named macros.
        public Dictionary<string,string> Aliases { get; set; }=new(StringComparer.OrdinalIgnoreCase);
    }

    public static string? ResolveStyleName(StyleSet set,string? value)
    {
        var key=value?.Trim();
        if(string.IsNullOrEmpty(key))return null;
        var direct=set.Styles.Keys.FirstOrDefault(name=>string.Equals(name,key,StringComparison.OrdinalIgnoreCase));
        if(direct!=null)return direct;
        var byId=set.Styles.FirstOrDefault(pair=>string.Equals(pair.Value.Id,key,StringComparison.OrdinalIgnoreCase));
        if(byId.Key!=null)return byId.Key;
        var alias=set.Aliases.FirstOrDefault(pair=>string.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase));
        if(alias.Key==null)return null;
        return set.Styles.FirstOrDefault(pair=>string.Equals(pair.Value.Id,alias.Value,StringComparison.OrdinalIgnoreCase)).Key;
    }

    public static TextStyle? ResolveStyle(StyleSet set,string? value)
    {
        var name=ResolveStyleName(set,value);
        return name!=null?set.Styles[name]:null;
    }

    public static TextStyle Clone(TextStyle value)
        =>new()
        {
            Id=value.Id,FontFamily=value.FontFamily,SizePt=value.SizePt,Colour=value.Colour,
            Bold=value.Bold,Italic=value.Italic,Underline=value.Underline,
            FollowingStyle=value.FollowingStyle,Alignment=value.Alignment,
            MarginLeft=value.MarginLeft,MarginTop=value.MarginTop,MarginRight=value.MarginRight,MarginBottom=value.MarginBottom,
            FirstLineIndent=value.FirstLineIndent,LineHeight=value.LineHeight,
            KeepWithNext=value.KeepWithNext,KeepTogether=value.KeepTogether,BreakPageBefore=value.BreakPageBefore,
            OutlineLevel=value.OutlineLevel
        };

    public static StyleSet Clone(StyleSet value,string? name=null)
        =>new()
        {
            Name=name??value.Name,
            Styles=value.Styles.ToDictionary(pair=>pair.Key,pair=>Clone(pair.Value),StringComparer.OrdinalIgnoreCase),
            Aliases=new Dictionary<string,string>(value.Aliases,StringComparer.OrdinalIgnoreCase)
        };

    public static TextStyle CreateStyle(string name,TextStyle? basedOn=null)
    {
        ValidateStyleName(name);
        var value=basedOn==null?new TextStyle():Clone(basedOn);
        value.Id="custom-"+Guid.NewGuid().ToString("N");
        return value;
    }

    public static string ValidateStyleName(string? value,IEnumerable<string>? existingNames=null)
    {
        var name=value?.Trim()??"";
        if(name.Length==0 || name.Any(char.IsControl))
            throw new InvalidOperationException("Enter a style name without control characters.");
        if(existingNames?.Contains(name,StringComparer.OrdinalIgnoreCase)==true)
            throw new InvalidOperationException("That style name is already in use.");
        return name;
    }

    public static string ValidateStyleName(string? value,StyleSet set)
        =>ValidateStyleName(value,set.Styles.Keys.Concat(set.Aliases.Keys).Concat(set.Styles.Values.Select(style=>style.Id)));

    public static void ValidateStyle(TextStyle value)
    {
        if(string.IsNullOrWhiteSpace(value.FontFamily))throw new InvalidOperationException("Choose a font.");
        if(!double.IsFinite(value.SizePt) || value.SizePt<=0)
            throw new InvalidOperationException("Enter a positive, finite font size.");
        if(new[]{value.MarginLeft,value.MarginTop,value.MarginRight,value.MarginBottom}
            .Any(number=>!double.IsFinite(number) || number<0))
            throw new InvalidOperationException("Paragraph indents and spacing must be finite, non-negative numbers.");
        if(!double.IsFinite(value.FirstLineIndent))
            throw new InvalidOperationException("Enter a finite first-line indent; use a negative value for a hanging indent.");
        if(!double.IsFinite(value.LineHeight) || value.LineHeight<0)
            throw new InvalidOperationException("Line height must be zero for automatic spacing or a positive number.");
        if(value.OutlineLevel<0 || value.OutlineLevel>9)
            throw new InvalidOperationException("Outline level must be Body text or a heading level from 1 to 9.");
    }

    static string LegacyStyleId(string name)
    {
        var builtin=ParagraphStyleNames.FirstOrDefault(value=>string.Equals(value,name,StringComparison.OrdinalIgnoreCase));
        if(builtin!=null)return builtin.Replace(" ","");
        // Older imported custom sets must receive the same ID on every load,
        // even when the user has not yet saved the normalised style set.
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()));
        return "custom-"+Convert.ToHexString(hash.AsSpan(0,16)).ToLowerInvariant();
    }

    public static StyleSet NormalizeSet(StyleSet source)
    {
        var result=new StyleSet{Name=string.IsNullOrWhiteSpace(source.Name)?"Default":source.Name.Trim()};
        foreach(var pair in source.Styles??new Dictionary<string,TextStyle>())
        {
            var name=ValidateStyleName(pair.Key);
            var builtin=ParagraphStyleNames.FirstOrDefault(value=>string.Equals(value,name,StringComparison.OrdinalIgnoreCase));
            name=builtin??name;
            var style=pair.Value==null?new TextStyle():Clone(pair.Value);
            if(builtin!=null || string.IsNullOrWhiteSpace(style.Id))style.Id=LegacyStyleId(name);
            if(string.IsNullOrWhiteSpace(style.FontFamily))style.FontFamily="Calibri";
            if(string.IsNullOrWhiteSpace(style.Colour))style.Colour="#000000";
            style.Alignment=style.Alignment?.Trim().ToLowerInvariant() switch
            {
                "center" or "centre"=>"Centre", "right"=>"Right", "justify"=>"Justify", _=>"Left"
            };
            if(style.OutlineLevel==0 && builtin?.StartsWith("Heading ",StringComparison.Ordinal)==true)
                style.OutlineLevel=int.Parse(builtin[^1..]);
            ValidateStyle(style);
            result.Styles[name]=style;
        }
        foreach(var pair in DefaultStyles())
            result.Styles.TryAdd(pair.Key,pair.Value);
        var ids=new HashSet<string>(ParagraphStyleNames.Select(LegacyStyleId),StringComparer.OrdinalIgnoreCase);
        foreach(var (name,style) in result.Styles.Where(pair=>!ParagraphStyleNames.Contains(pair.Key,StringComparer.OrdinalIgnoreCase)))
            if(!ids.Add(style.Id))
            {
                style.Id=LegacyStyleId(name);
                if(!ids.Add(style.Id))throw new InvalidOperationException("Each style must have a unique identity.");
            }
        foreach(var pair in source.Aliases??new Dictionary<string,string>())
        {
            if(string.IsNullOrWhiteSpace(pair.Key) || result.Styles.ContainsKey(pair.Key.Trim()) || !ids.Contains(pair.Value))continue;
            result.Aliases[pair.Key.Trim()]=pair.Value;
        }
        foreach(var style in result.Styles.Values)
            style.FollowingStyle=NormalizeFollowingStyle(style.FollowingStyle,result);
        return result;
    }

    public static Dictionary<string,StyleSet> NormalizeSets(IReadOnlyDictionary<string,StyleSet>? sets)
    {
        var result=new Dictionary<string,StyleSet>(StringComparer.OrdinalIgnoreCase);
        if(sets!=null)
            foreach(var pair in sets)
            {
                if(string.IsNullOrWhiteSpace(pair.Key) || pair.Value==null)continue;
                var name=pair.Key.Trim();
                var set=NormalizeSet(pair.Value);
                set.Name=name;
                result[name]=set;
            }
        if(!result.ContainsKey("Default"))result["Default"]=new StyleSet{Name="Default",Styles=DefaultStyles()};
        return result;
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
            return value==null?Defaults():NormalizeSets(value);
        }
        catch{return Defaults();}
    }

    public static void Save(IReadOnlyDictionary<string,StyleSet> sets)
        =>File.WriteAllText(PathName,JsonSerializer.Serialize(NormalizeSets(sets),new JsonSerializerOptions{WriteIndented=true}));

    static Dictionary<string,TextStyle> DefaultStyles()
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
        foreach(var (name,style) in set.Styles)
        {
            style.Id=LegacyStyleId(name);
            if(name.StartsWith("Heading ",StringComparison.Ordinal))style.OutlineLevel=int.Parse(name[^1..]);
        }
        return set.Styles;
    }

    public static Dictionary<string,StyleSet> Defaults()
        =>new(StringComparer.OrdinalIgnoreCase){{"Default",new StyleSet{Name="Default",Styles=DefaultStyles()}}};
}

