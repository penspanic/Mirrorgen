using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Mirrorgen.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubsetAnalyzer : DiagnosticAnalyzer
{
    public const string MG0001Id = "MG0001";
    public const string MG0002Id = "MG0002";
    public const string MG0003Id = "MG0003";
    public const string MG0004Id = "MG0004";
    public const string MG0005Id = "MG0005";
    public const string MG0006Id = "MG0006";

    const string Category = "Mirrorgen.Subset";
    const string TranspileAttributeFullName = "Mirrorgen.TranspileAttribute";
    const string ConceptRef = "See docs/CONCEPT.md \"What it doesn't do (on purpose)\".";

    internal static readonly DiagnosticDescriptor MG0001 = new(
        id: MG0001Id,
        title: "LINQ is not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' is annotated [Transpile] but calls '{1}' from System.Linq. LINQ has no transpilable mirror — move it out of the transpile boundary.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes LINQ. " + ConceptRef);

    internal static readonly DiagnosticDescriptor MG0002 = new(
        id: MG0002Id,
        title: "async / await / Task are not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' is annotated [Transpile] but uses '{1}'. Asynchrony has no synchronous TS mirror — move it out of the transpile boundary.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes async/await/Task. " + ConceptRef);

    internal static readonly DiagnosticDescriptor MG0003 = new(
        id: MG0003Id,
        title: "Span / ref / in / out / unsafe are not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' uses '{1}'. Pointer / ref-like / unsafe constructs have no TS mirror — move it out of the transpile boundary.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes Span<T> / ref / in / out / unsafe / pointers. " + ConceptRef);

    internal static readonly DiagnosticDescriptor MG0004 = new(
        id: MG0004Id,
        title: "throw is not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' contains a throw. Exceptions are not part of the transpilable subset — return a result type instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes exceptions. " + ConceptRef);

    internal static readonly DiagnosticDescriptor MG0005 = new(
        id: MG0005Id,
        title: "Reflection is not allowed in [Transpile] methods",
        messageFormat: "Method '{0}' calls '{1}' from System.Reflection. Reflection has no TS mirror.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes reflection. " + ConceptRef);

    internal static readonly DiagnosticDescriptor MG0006 = new(
        id: MG0006Id,
        title: "Inheritance is not allowed on the declaring type of a [Transpile] method",
        messageFormat: "Type '{0}' (declarer of [Transpile] method '{1}') inherits from '{2}'. Inheritance and virtual dispatch are not part of the transpilable subset.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mirrorgen's transpiled subset deliberately excludes inheritance and virtual dispatch. " + ConceptRef);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MG0001, MG0002, MG0003, MG0004, MG0005, MG0006);

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

            // MG0003: Span<T> / ReadOnlySpan<T> parameter or return type.
            if (IsSpanLike(method.ReturnType))
            {
                ReportOnce(symbolStart, MG0003, method.Locations.FirstOrDefault(),
                    method.Name, method.ReturnType.ToDisplayString());
            }
            foreach (var p in method.Parameters)
            {
                if (p.RefKind != RefKind.None)
                {
                    ReportOnce(symbolStart, MG0003, p.Locations.FirstOrDefault(),
                        method.Name, $"{p.RefKind.ToString().ToLowerInvariant()} parameter '{p.Name}'");
                }
                if (IsSpanLike(p.Type) || p.Type is IPointerTypeSymbol)
                {
                    ReportOnce(symbolStart, MG0003, p.Locations.FirstOrDefault(),
                        method.Name, p.Type.ToDisplayString());
                }
            }
            if (method.ReturnType is IPointerTypeSymbol)
            {
                ReportOnce(symbolStart, MG0003, method.Locations.FirstOrDefault(),
                    method.Name, method.ReturnType.ToDisplayString());
            }

            // MG0003: unsafe modifier on the method itself.
            foreach (var sref in method.DeclaringSyntaxReferences)
            {
                if (sref.GetSyntax() is MethodDeclarationSyntax mds &&
                    mds.Modifiers.Any(m => m.IsKind(SyntaxKind.UnsafeKeyword)))
                {
                    ReportOnce(symbolStart, MG0003, mds.Identifier.GetLocation(),
                        method.Name, "unsafe");
                }
            }

            // MG0006: declaring type inherits from a non-object base class.
            if (method.ContainingType is { } ct &&
                ct.BaseType is { } baseT &&
                baseT.SpecialType != SpecialType.System_Object &&
                ct.TypeKind == TypeKind.Class)
            {
                ReportOnce(symbolStart, MG0006, ct.Locations.FirstOrDefault(),
                    ct.Name, method.Name, baseT.ToDisplayString());
            }

            symbolStart.RegisterOperationAction(opContext =>
            {
                switch (opContext.Operation)
                {
                    case IInvocationOperation invocation:
                    {
                        var ns = invocation.TargetMethod.ContainingNamespace?.ToDisplayString();
                        var containingType = invocation.TargetMethod.ContainingType;
                        if (ns == "System.Linq")
                        {
                            opContext.ReportDiagnostic(Diagnostic.Create(
                                MG0001, invocation.Syntax.GetLocation(),
                                method.Name, invocation.TargetMethod.Name));
                        }
                        else if (IsReflectionInvocation(ns, containingType))
                        {
                            opContext.ReportDiagnostic(Diagnostic.Create(
                                MG0005, invocation.Syntax.GetLocation(),
                                method.Name, $"{containingType?.Name}.{invocation.TargetMethod.Name}"));
                        }
                        break;
                    }
                    case IAwaitOperation awaitOp:
                        opContext.ReportDiagnostic(Diagnostic.Create(
                            MG0002, awaitOp.Syntax.GetLocation(), method.Name, "await"));
                        break;
                    case IThrowOperation throwOp:
                        opContext.ReportDiagnostic(Diagnostic.Create(
                            MG0004, throwOp.Syntax.GetLocation(), method.Name));
                        break;
                }
            }, OperationKind.Invocation, OperationKind.Await, OperationKind.Throw);

            symbolStart.RegisterSyntaxNodeAction(syntaxCtx =>
            {
                if (syntaxCtx.Node is TypeOfExpressionSyntax)
                {
                    syntaxCtx.ReportDiagnostic(Diagnostic.Create(
                        MG0005, syntaxCtx.Node.GetLocation(), method.Name, "typeof"));
                }
            }, SyntaxKind.TypeOfExpression);
        }, SymbolKind.Method);
    }

    static bool IsReflectionInvocation(string? ns, INamedTypeSymbol? containingType)
    {
        if (ns is not null && (ns == "System.Reflection" || ns.StartsWith("System.Reflection.", System.StringComparison.Ordinal)))
        {
            return true;
        }
        // Type / Activator live under System but are reflection in spirit.
        if (containingType is null) return false;
        var typeName = containingType.ToDisplayString();
        return typeName == "System.Type" || typeName == "System.Activator";
    }

    static void ReportOnce(SymbolStartAnalysisContext symbolStart, DiagnosticDescriptor descriptor,
        Location? location, params object[] messageArgs)
    {
        symbolStart.RegisterSymbolEndAction(endCtx =>
        {
            endCtx.ReportDiagnostic(Diagnostic.Create(
                descriptor, location ?? Location.None, messageArgs));
        });
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
        return name == "System.Threading.Tasks.Task" ||
               name == "System.Threading.Tasks.Task<TResult>" ||
               name == "System.Threading.Tasks.ValueTask" ||
               name == "System.Threading.Tasks.ValueTask<TResult>";
    }

    static bool IsSpanLike(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return name == "System.Span<T>" || name == "System.ReadOnlySpan<T>";
    }
}
