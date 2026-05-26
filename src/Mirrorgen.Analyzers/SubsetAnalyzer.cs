using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Mirrorgen.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubsetAnalyzer : DiagnosticAnalyzer
{
    public const string MG0001Id = "MG0001";

    const string TranspileAttributeFullName = "Mirrorgen.TranspileAttribute";

    internal static readonly DiagnosticDescriptor MG0001 = new(
        id: MG0001Id,
        title: "LINQ is not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' is annotated [Transpile] but calls '{1}' from System.Linq. LINQ has no transpilable mirror — move it out of the transpile boundary.",
        category: "Mirrorgen.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes LINQ. See docs/CONCEPT.md \"What it doesn't do (on purpose)\".");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MG0001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolStartAction(symbolStart =>
        {
            if (symbolStart.Symbol is not IMethodSymbol method) return;
            if (!HasTranspileAttribute(method)) return;

            symbolStart.RegisterOperationAction(opContext =>
            {
                if (opContext.Operation is not IInvocationOperation invocation) return;
                var target = invocation.TargetMethod;
                var ns = target.ContainingNamespace?.ToDisplayString();
                if (ns == "System.Linq")
                {
                    opContext.ReportDiagnostic(Diagnostic.Create(
                        MG0001,
                        invocation.Syntax.GetLocation(),
                        method.Name,
                        target.Name));
                }
            }, OperationKind.Invocation);
        }, SymbolKind.Method);
    }

    static bool HasTranspileAttribute(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == TranspileAttributeFullName) return true;
        }
        return false;
    }
}
