using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Mirrorgen.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubsetAnalyzer : DiagnosticAnalyzer
{
    public const string MG0001Id = "MG0001";
    public const string MG0002Id = "MG0002";

    const string TranspileAttributeFullName = "Mirrorgen.TranspileAttribute";

    const string TaskName = "System.Threading.Tasks.Task";
    const string TaskOfTName = "System.Threading.Tasks.Task<TResult>";
    const string ValueTaskName = "System.Threading.Tasks.ValueTask";
    const string ValueTaskOfTName = "System.Threading.Tasks.ValueTask<TResult>";

    internal static readonly DiagnosticDescriptor MG0001 = new(
        id: MG0001Id,
        title: "LINQ is not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' is annotated [Transpile] but calls '{1}' from System.Linq. LINQ has no transpilable mirror — move it out of the transpile boundary.",
        category: "Mirrorgen.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes LINQ. See docs/CONCEPT.md \"What it doesn't do (on purpose)\".");

    internal static readonly DiagnosticDescriptor MG0002 = new(
        id: MG0002Id,
        title: "async / await / Task are not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' is annotated [Transpile] but uses '{1}'. Asynchrony has no synchronous TS mirror — move it out of the transpile boundary.",
        category: "Mirrorgen.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes async/await/Task. See docs/CONCEPT.md \"What it doesn't do (on purpose)\".");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MG0001, MG0002);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolStartAction(symbolStart =>
        {
            if (symbolStart.Symbol is not IMethodSymbol method) return;
            if (!HasTranspileAttribute(method)) return;

            // MG0002: async modifier on the method itself.
            if (method.IsAsync)
            {
                symbolStart.RegisterSymbolEndAction(endCtx =>
                {
                    endCtx.ReportDiagnostic(Diagnostic.Create(
                        MG0002,
                        method.Locations.FirstOrDefault() ?? Location.None,
                        method.Name,
                        "async"));
                });
            }

            // MG0002: Task / Task<T> / ValueTask / ValueTask<T> return type.
            if (IsTaskLike(method.ReturnType))
            {
                symbolStart.RegisterSymbolEndAction(endCtx =>
                {
                    endCtx.ReportDiagnostic(Diagnostic.Create(
                        MG0002,
                        method.Locations.FirstOrDefault() ?? Location.None,
                        method.Name,
                        method.ReturnType.ToDisplayString()));
                });
            }

            symbolStart.RegisterOperationAction(opContext =>
            {
                switch (opContext.Operation)
                {
                    case IInvocationOperation invocation:
                    {
                        var ns = invocation.TargetMethod.ContainingNamespace?.ToDisplayString();
                        if (ns == "System.Linq")
                        {
                            opContext.ReportDiagnostic(Diagnostic.Create(
                                MG0001,
                                invocation.Syntax.GetLocation(),
                                method.Name,
                                invocation.TargetMethod.Name));
                        }
                        break;
                    }
                    case IAwaitOperation awaitOp:
                    {
                        opContext.ReportDiagnostic(Diagnostic.Create(
                            MG0002,
                            awaitOp.Syntax.GetLocation(),
                            method.Name,
                            "await"));
                        break;
                    }
                }
            }, OperationKind.Invocation, OperationKind.Await);
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

    static bool IsTaskLike(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return name == TaskName || name == TaskOfTName ||
               name == ValueTaskName || name == ValueTaskOfTName;
    }
}
