using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using System.Windows.Media;

namespace CtrlZed;

public static class MacroIntellisense
{
    public sealed record Suggestion(string Text,string Description);

    public static IReadOnlyList<Suggestion> GetSuggestions(string source,string extension,int caretOffset,string prefix,string? receiver=null)
    {
        if(string.IsNullOrWhiteSpace(source))return Array.Empty<Suggestion>();
        extension=(extension??"").ToLowerInvariant();
        if(extension is not ".cs" and not ".vb")return Array.Empty<Suggestion>();

        var references=CompiledMacroHost.BuildReferences();
        var injected=extension==".cs"?"using CtrlZed;\n":"Imports Microsoft.VisualBasic\nImports CtrlZed\n";
        var full=injected+source;
        var position=Math.Clamp(injected.Length+caretOffset,0,full.Length);

        SyntaxTree tree;
        Compilation compilation;
        if(extension==".cs")
        {
            tree=CSharpSyntaxTree.ParseText(full,new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest));
            compilation=CSharpCompilation.Create(
                "CtrlZedMacroIntellisense",new[]{tree},references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
        else
        {
            tree=VisualBasicSyntaxTree.ParseText(full,new VisualBasicParseOptions(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest));
            compilation=VisualBasicCompilation.Create(
                "CtrlZedMacroIntellisense",new[]{tree},references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        var model=compilation.GetSemanticModel(tree,true);
        IEnumerable<ISymbol> symbols;

        if(!string.IsNullOrWhiteSpace(receiver))
        {
            var target=model.LookupSymbols(position,name:receiver).FirstOrDefault();
            var type=target switch
            {
                ILocalSymbol s=>s.Type,
                IParameterSymbol s=>s.Type,
                IFieldSymbol s=>s.Type,
                IPropertySymbol s=>s.Type,
                IMethodSymbol s=>s.ReturnType,
                INamedTypeSymbol s=>s,
                _=>null
            };
            symbols=type==null?Array.Empty<ISymbol>():Members(type);
        }
        else symbols=model.LookupSymbols(position);

        var comparison=extension==".vb"?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        return symbols
            .Where(s=>!s.IsImplicitlyDeclared && !string.IsNullOrWhiteSpace(s.Name))
            .Where(s=>string.IsNullOrWhiteSpace(prefix)||s.Name.StartsWith(prefix,comparison))
            .GroupBy(s=>s.Name,extension==".vb"?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal)
            .Select(g=>g.First())
            .OrderBy(s=>s.Name,StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(s=>new Suggestion(s.Name,Describe(s)))
            .ToList();
    }

    static IEnumerable<ISymbol> Members(ITypeSymbol type)
    {
        for(ITypeSymbol? current=type;current!=null;)
        {
            foreach(var member in current.GetMembers())
                if(member.DeclaredAccessibility==Accessibility.Public || member.DeclaredAccessibility==Accessibility.Internal)
                    yield return member;
            current=(current as INamedTypeSymbol)?.BaseType;
        }
    }

    static string Describe(ISymbol symbol)
    {
        try{return symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);}
        catch{return symbol.Kind+" "+symbol.Name;}
    }

    public sealed class CompletionItem : ICompletionData
    {
        readonly Suggestion suggestion;
        public CompletionItem(Suggestion suggestion)=>this.suggestion=suggestion;
        public ImageSource? Image=>null;
        public string Text=>suggestion.Text;
        public object Content=>suggestion.Text;
        public object Description=>suggestion.Description;
        public double Priority=>0;
        public void Complete(TextArea textArea,ISegment completionSegment,EventArgs insertionRequestEventArgs)
            =>textArea.Document.Replace(completionSegment,suggestion.Text);
    }
}
