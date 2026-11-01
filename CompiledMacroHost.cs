using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;

namespace CtrlZed;

public static class CompiledMacroHost
{
    public static async Task RunAsync(string source,string extension,RichTextBox editor,Window owner)
    {
        if(string.IsNullOrWhiteSpace(source))throw new InvalidOperationException("Macro source is empty.");

        extension=(extension??"").ToLowerInvariant();
        if(extension is not ".cs" and not ".vb")
            throw new InvalidOperationException("Compiled macros must use .cs or .vb.");

        var references=BuildReferences();
        var assemblyName="CtrlZedMacro_"+Guid.NewGuid().ToString("N");
        Compilation compilation;

        if(extension==".cs")
        {
            var tree=CSharpSyntaxTree.ParseText(source,new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest));
            compilation=CSharpCompilation.Create(
                assemblyName,
                new[]{tree},
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release));
        }
        else
        {
            var tree=VisualBasicSyntaxTree.ParseText(source,new VisualBasicParseOptions(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest));
            compilation=VisualBasicCompilation.Create(
                assemblyName,
                new[]{tree},
                references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release));
        }

        using var pe=new MemoryStream();
        var emit=compilation.Emit(pe);
        if(!emit.Success)
        {
            var errors=emit.Diagnostics
                .Where(d=>d.Severity==DiagnosticSeverity.Error)
                .Take(25)
                .Select(d=>d.ToString());
            throw new InvalidOperationException("Macro compilation failed:\n\n"+string.Join("\n",errors));
        }

        pe.Position=0;
        var alc=new AssemblyLoadContext(assemblyName,isCollectible:true);
        alc.Resolving+=(_,name)=>AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a=>string.Equals(a.GetName().Name,name.Name,StringComparison.OrdinalIgnoreCase));

        try
        {
            var assembly=alc.LoadFromStream(pe);
            var run=assembly.GetTypes()
                .SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.Static))
                .FirstOrDefault(m=>
                {
                    if(!string.Equals(m.Name,"Run",StringComparison.OrdinalIgnoreCase))return false;
                    var p=m.GetParameters();
                    return p.Length==1 && p[0].ParameterType==typeof(RichTextBox);
                });

            if(run==null)
                throw new InvalidOperationException(
                    "No public static Run(RichTextBox) entry point was found.\n\n"+
                    "VB.NET: Public Sub Run(Editor As RichTextBox)\n"+
                    "C#: public static void Run(RichTextBox Editor)");

            object? result;
            try
            {
                result=run.Invoke(null,new object?[]{editor});
            }
            catch(TargetInvocationException ex)
            {
                throw ex.InnerException??ex;
            }

            if(result is Task task)await task;
        }
        finally
        {
            alc.Unload();
        }
    }

    static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tpa=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if(!string.IsNullOrWhiteSpace(tpa))
            foreach(var path in tpa.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
                if(File.Exists(path))paths.Add(path);

        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if(!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location))
                    paths.Add(assembly.Location);
            }
            catch { }
        }

        paths.Add(typeof(CompiledMacroHost).Assembly.Location);
        paths.Add(typeof(RichTextBox).Assembly.Location);
        paths.Add(typeof(Window).Assembly.Location);

        return paths.Select(MetadataReference.CreateFromFile).ToList();
    }
}
