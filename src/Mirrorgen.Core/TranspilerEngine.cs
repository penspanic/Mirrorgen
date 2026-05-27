using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Mirrorgen.Core;

public static class TranspilerEngine
{
    public const string Version = "0.0.1-alpha";

    public static string TranspileSource(string csharpSource)
        => TranspileSource(csharpSource, TypeMappingRegistry.Empty, TranspileOptions.Default);

    public static string TranspileSource(string csharpSource, TypeMappingRegistry registry)
        => TranspileSource(csharpSource, registry, TranspileOptions.Default);

    public static string TranspileSource(string csharpSource, TranspileOptions options)
        => TranspileSource(csharpSource, TypeMappingRegistry.Empty, options);

    public static string TranspileSource(string csharpSource, TypeMappingRegistry registry, TranspileOptions options)
    {
        var tree = CSharpSyntaxTree.ParseText(csharpSource);
        var compilation = CSharpCompilation.Create(
            assemblyName: "MirrorgenInput",
            syntaxTrees: new[] { tree },
            references: TrustedReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return TranspileTree(tree, compilation, registry, options);
    }

    /// <summary>
    /// Emits TS for a single SyntaxTree's [Transpile] entry points, but also
    /// pulls in any reachable type declarations from sibling trees in
    /// <paramref name="compilation"/>. This keeps multi-file consumers
    /// honest: a method in <c>Pricing.cs</c> that references a record in
    /// <c>Domain.cs</c> sees the record's TS form inlined into its own .ts
    /// emit, with no separate import step needed.
    /// </summary>
    public static string TranspileTree(SyntaxTree tree, CSharpCompilation compilation, TypeMappingRegistry registry)
        => TranspileTree(tree, compilation, registry, TranspileOptions.Default);

    public static string TranspileTree(SyntaxTree tree, CSharpCompilation compilation, TypeMappingRegistry registry, TranspileOptions options)
    {
        var ctx = new EmitContext(compilation.GetSemanticModel(tree), registry);

        // Index every type/method declaration in every tree so the
        // reachability scan can resolve identifier references back to their
        // declaration even when it lives in a sibling file.
        var typeByName = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var methods = new List<MethodDeclarationSyntax>();
        foreach (var t in compilation.SyntaxTrees)
        {
            foreach (var node in t.GetCompilationUnitRoot().DescendantNodes())
            {
                switch (node)
                {
                    case EnumDeclarationSyntax e: typeByName[e.Identifier.Text] = e; break;
                    case RecordDeclarationSyntax r: typeByName[r.Identifier.Text] = r; break;
                    case ClassDeclarationSyntax c: typeByName[c.Identifier.Text] = c; break;
                    case StructDeclarationSyntax s: typeByName[s.Identifier.Text] = s; break;
                }
            }
        }
        // Methods only emit from the current tree — sibling trees' methods
        // get their own .ts file from this same call further up the batch.
        foreach (var node in tree.GetCompilationUnitRoot().DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m) methods.Add(m);
        }

        // In a multi-tree compilation (batch mode) a file that only declares
        // [Transpile] types is treated as a "domain shape" file — its
        // declarations get pulled into whichever .ts file emits the methods
        // that reference them, so a standalone Domain.ts that nobody
        // imports would just duplicate the type surface. Returning empty
        // here signals BatchTranspiler to skip writing a file for this tree.
        // Single-tree callers (TranspileSource) opt into the "emit
        // everything you've got" behaviour — they have no sibling file to
        // inline into.
        if (compilation.SyntaxTrees.Length > 1)
        {
            bool hasOwnTranspileEntry = false;
            foreach (var m in methods)
            {
                if (HasTranspileAttribute(m.AttributeLists))
                {
                    hasOwnTranspileEntry = true;
                    break;
                }
            }
            if (!hasOwnTranspileEntry)
            {
                // Class-level [Transpile] on a class that holds at least one
                // public static member also makes this file an own-emit unit.
                foreach (var classNode in tree.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!HasTranspileAttribute(classNode.AttributeLists)) continue;
                    if (HasAnyPublicStaticMember(classNode))
                    {
                        hasOwnTranspileEntry = true;
                        break;
                    }
                }
            }
            if (!hasOwnTranspileEntry)
            {
                // Type-level [Transpile] on a record / struct / enum also marks
                // the file as own-emit — every TsGen-compatible DTO landing in
                // its own .ts. Inline reachability from sibling consumers keeps
                // working independently.
                foreach (var typeNode in tree.GetCompilationUnitRoot().DescendantNodes())
                {
                    if (typeNode is RecordDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax &&
                        HasTranspileAttribute(TypeAttributeLists(typeNode)))
                    {
                        hasOwnTranspileEntry = true;
                        break;
                    }
                }
            }
            if (!hasOwnTranspileEntry &&
                options.ScanPathMarkers.Count > 0 &&
                !string.IsNullOrEmpty(tree.FilePath) &&
                options.ScanPathMarkers.Any(marker => tree.FilePath.Contains(marker, StringComparison.Ordinal)))
            {
                foreach (var typeNode in tree.GetCompilationUnitRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    if (typeNode.Modifiers.Any(t => t.IsKind(SyntaxKind.PublicKeyword)))
                    {
                        hasOwnTranspileEntry = true;
                        break;
                    }
                }
            }
            if (!hasOwnTranspileEntry)
            {
                return string.Empty;
            }
        }

        // BFS reachability from every explicit [Transpile] entry point in
        // the current tree. Sibling trees seed their own emit pass; we only
        // pull their declarations in transitively when *this* tree's methods
        // / records reference them.
        var emit = new HashSet<SyntaxNode>();
        var queue = new Queue<SyntaxNode>();
        var fileMatchesScanMarker = options.ScanPathMarkers.Count > 0 &&
            !string.IsNullOrEmpty(tree.FilePath) &&
            options.ScanPathMarkers.Any(marker => tree.FilePath.Contains(marker, StringComparison.Ordinal));

        foreach (var node in tree.GetCompilationUnitRoot().DescendantNodes())
        {
            var attrs = TypeAttributeLists(node);
            var hasAttr = attrs.Count > 0 && HasTranspileAttribute(attrs);
            // Directory-marker scan: public types in path-matched files behave
            // as if they had [Transpile] — but only for *type-only* emission
            // (TsGen-equivalent "shape only" semantics; methods are NOT
            // auto-seeded by marker mode alone).
            var markerHit = fileMatchesScanMarker
                && node is BaseTypeDeclarationSyntax markerCandidate
                && markerCandidate.Modifiers.Any(t => t.IsKind(SyntaxKind.PublicKeyword));
            if (hasAttr || markerHit)
            {
                if (emit.Add(node)) queue.Enqueue(node);
                // Class-level [Transpile] also seeds every public static method
                // inside the class as an emit target — saves repeating the
                // attribute on every helper. Per-method [Transpile] still works
                // (idempotent: emit.Add is a HashSet). Marker mode skips this
                // step (shape only).
                if (hasAttr && node is TypeDeclarationSyntax tds &&
                    (node is ClassDeclarationSyntax || node is StructDeclarationSyntax || node is RecordDeclarationSyntax))
                {
                    foreach (var memberMethod in tds.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (IsPublicStaticMethod(memberMethod) && emit.Add(memberMethod))
                            queue.Enqueue(memberMethod);
                    }
                }
            }
        }
        foreach (var m in methods)
        {
            if (HasTranspileAttribute(m.AttributeLists))
            {
                if (emit.Add(m)) queue.Enqueue(m);
            }
        }
        // Method-declaration index: walked from invocation expressions in
        // method bodies so private/internal helpers inside a class-level
        // [Transpile] module pull in transitively. (Public-static seeding
        // alone misses helpers like HilbertCurve.Rotate.) Same-file only —
        // sibling trees seed their own emit pass.
        var methodByName = new Dictionary<(string ContainingType, string MethodName), MethodDeclarationSyntax>();
        foreach (var tds in tree.GetCompilationUnitRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            // Only index methods inside types that themselves have class- /
            // struct- / record-level [Transpile] — otherwise an unrelated
            // helper type would get pulled in by name collision.
            if (!HasTranspileAttribute(tds.AttributeLists)) continue;
            if (tds is not ClassDeclarationSyntax and not StructDeclarationSyntax and not RecordDeclarationSyntax) continue;
            foreach (var m in tds.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword))) continue;
                methodByName[(tds.Identifier.Text, m.Identifier.Text)] = m;
            }
        }

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var refName in ExtractReferencedTypeNames(n))
            {
                if (typeByName.TryGetValue(refName, out var refNode) && emit.Add(refNode))
                {
                    queue.Enqueue(refNode);
                }
            }
            // Pull in same-type static helpers reached via invocation.
            if (n is MethodDeclarationSyntax callerMethod &&
                callerMethod.Parent is TypeDeclarationSyntax callerTds)
            {
                foreach (var invSyntax in callerMethod.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    string? calleeName = invSyntax.Expression switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                        _ => null,
                    };
                    if (calleeName is null) continue;
                    if (methodByName.TryGetValue((callerTds.Identifier.Text, calleeName), out var calleeDecl) &&
                        emit.Add(calleeDecl))
                    {
                        queue.Enqueue(calleeDecl);
                    }
                }
            }
        }

        // Emit pass — walk every tree in the compilation so reachable
        // declarations from sibling files end up inlined into this file's
        // output. Order: declarations first (from current tree, then
        // sibling trees in compilation order), then methods (current tree
        // only). The plugin-mapped types are still suppressed.
        var sb = new StringBuilder();
        bool first = true;
        var emittedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var siblings = new List<SyntaxTree> { tree };
        foreach (var t in compilation.SyntaxTrees)
        {
            if (t != tree) siblings.Add(t);
        }
        foreach (var t in siblings)
        {
            var siblingSemantics = compilation.GetSemanticModel(t);
            var siblingCtx = t == tree ? ctx : new EmitContext(siblingSemantics, registry);
            foreach (var member in t.GetCompilationUnitRoot().DescendantNodes())
            {
                if (!emit.Contains(member)) continue;

                // Types the plugin has remapped (e.g. OrderId -> number) must
                // not also emit their own declaration; the runtime side owns it.
                if (ctx.Registry.Count > 0 && member is BaseTypeDeclarationSyntax decl &&
                    siblingSemantics.GetDeclaredSymbol(decl) is { } declaredSym &&
                    ctx.Registry.TryGet(declaredSym.ToDisplayString(), out _))
                {
                    continue;
                }

                // Partial declarations of the same type emit once — the first
                // call gathers members from every syntax reference. Skip the rest.
                if (member is TypeDeclarationSyntax typeDecl &&
                    !emittedTypeNames.Add(ReadEmitName(typeDecl.AttributeLists) ?? typeDecl.Identifier.Text))
                {
                    continue;
                }

                string? emitted = member switch
                {
                    EnumDeclarationSyntax enumDecl => EmitEnum(enumDecl),
                    RecordDeclarationSyntax rec => EmitTypeDeclaration(rec, siblingCtx),
                    ClassDeclarationSyntax cls => EmitTypeDeclaration(cls, siblingCtx),
                    StructDeclarationSyntax str => EmitTypeDeclaration(str, siblingCtx),
                    // Sibling-tree methods belong to their own file's emit pass.
                    MethodDeclarationSyntax method when t == tree => EmitMethod(method, siblingCtx),
                    _ => null,
                };
                if (emitted is null) continue;
                if (!first) sb.AppendLine();
                sb.Append(emitted);
                first = false;
            }
        }

        var body = sb.ToString();
        if (options.EmitValidators)
        {
            var validators = EmitValidatorsForTypesOutput(body);
            if (validators.Length > 0)
            {
                body = body.TrimEnd() + Environment.NewLine + Environment.NewLine + validators;
            }
        }
        return PrependHelpers(ctx, body);
    }

    // Helper functions emitted lazily at the top of any .ts file that
    // references them. Each key matches a name fed into ctx.UsedHelpers.
    static readonly Dictionary<string, string> HelperDefinitions = new(StringComparer.Ordinal)
    {
        ["bankersRound"] = """
            function __mirrorgen_bankersRound(x: number): number {
              const floor = Math.floor(x);
              const diff = x - floor;
              let rounded: number;
              if (diff > 0.5) rounded = floor + 1;
              else if (diff < 0.5) rounded = floor;
              // exactly 0.5 — round to even, matching C# Math.Round default
              else rounded = floor % 2 === 0 ? floor : floor + 1;
              // Mirror C# negative-zero behaviour: Math.Round(-0.5) returns -0,
              // not +0. vitest's toStrictEqual uses Object.is, so the sign matters.
              return rounded === 0 && x < 0 ? -0 : rounded;
            }
            """,
        ["awayFromZeroRound"] = """
            function __mirrorgen_awayFromZeroRound(x: number): number {
              return x >= 0 ? Math.floor(x + 0.5) : -Math.floor(-x + 0.5);
            }
            """,
    };

    static string PrependHelpers(EmitContext ctx, string body)
    {
        if (ctx.UsedHelpers.Count == 0) return body;
        var sb = new StringBuilder();
        foreach (var name in ctx.UsedHelpers.OrderBy(n => n, StringComparer.Ordinal))
        {
            sb.AppendLine(HelperDefinitions[name]);
            sb.AppendLine();
        }
        sb.Append(body);
        return sb.ToString();
    }

    static SyntaxList<AttributeListSyntax> TypeAttributeLists(SyntaxNode node) => node switch
    {
        EnumDeclarationSyntax e => e.AttributeLists,
        BaseTypeDeclarationSyntax bt => bt.AttributeLists,
        _ => default,
    };

    static bool IsPublicStaticMethod(MethodDeclarationSyntax m) =>
        m.Modifiers.Any(t => t.IsKind(SyntaxKind.PublicKeyword)) &&
        m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword));

    static bool IsAnyStaticMethod(MethodDeclarationSyntax m) =>
        m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword));

    static bool HasAnyPublicStaticMember(ClassDeclarationSyntax cls)
    {
        foreach (var member in cls.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax m when IsPublicStaticMethod(m):
                    return true;
                case FieldDeclarationSyntax f when
                    f.Modifiers.Any(t => t.IsKind(SyntaxKind.PublicKeyword)) &&
                    (f.Modifiers.Any(t => t.IsKind(SyntaxKind.ConstKeyword)) ||
                     f.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword))):
                    return true;
            }
        }
        return false;
    }

    static IEnumerable<string> ExtractReferencedTypeNames(SyntaxNode node)
    {
        switch (node)
        {
            case MethodDeclarationSyntax m:
                foreach (var p in m.ParameterList.Parameters)
                {
                    if (p.Type is { } pt) foreach (var n in NamesIn(pt)) yield return n;
                }
                foreach (var n in NamesIn(m.ReturnType)) yield return n;
                yield break;
            case RecordDeclarationSyntax r:
                if (r.ParameterList is { } pl)
                {
                    foreach (var p in pl.Parameters)
                    {
                        if (p.Type is { } pt) foreach (var n in NamesIn(pt)) yield return n;
                    }
                }
                foreach (var n in NamesInTypeBody(r)) yield return n;
                yield break;
            case ClassDeclarationSyntax c:
                foreach (var n in NamesInTypeBody(c)) yield return n;
                yield break;
            case StructDeclarationSyntax s:
                foreach (var n in NamesInTypeBody(s)) yield return n;
                yield break;
        }
    }

    static IEnumerable<string> NamesInTypeBody(TypeDeclarationSyntax decl)
    {
        foreach (var m in decl.Members)
        {
            switch (m)
            {
                case PropertyDeclarationSyntax prop:
                    foreach (var n in NamesIn(prop.Type)) yield return n;
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var n in NamesIn(field.Declaration.Type)) yield return n;
                    break;
            }
        }
    }

    static IEnumerable<string> NamesIn(TypeSyntax type)
    {
        switch (type)
        {
            case ArrayTypeSyntax arr:
                foreach (var n in NamesIn(arr.ElementType)) yield return n;
                yield break;
            case NullableTypeSyntax nt:
                foreach (var n in NamesIn(nt.ElementType)) yield return n;
                yield break;
            case IdentifierNameSyntax id:
                yield return id.Identifier.Text;
                yield break;
        }
    }

    sealed class EmitContext
    {
        readonly SemanticModel _model;
        public TypeMappingRegistry Registry { get; }
        public HashSet<string> UsedHelpers { get; } = new(StringComparer.Ordinal);

        // ref/out param names of the method currently being emitted. Used so
        // nested return statements (inside an if/for/while body) can rewrite
        // themselves into the destructured-tuple shape. Reset per method.
        public List<string> CurrentRefNames { get; private set; } = new();
        public bool CurrentMethodIsVoidWithRefs { get; private set; }

        public IDisposable PushRefMethodScope(List<string> refNames, bool isVoidWithRefs)
        {
            var prevNames = CurrentRefNames;
            var prevVoid = CurrentMethodIsVoidWithRefs;
            CurrentRefNames = refNames;
            CurrentMethodIsVoidWithRefs = isVoidWithRefs;
            return new ScopePopper(this, prevNames, prevVoid);
        }

        sealed class ScopePopper : IDisposable
        {
            readonly EmitContext _ctx;
            readonly List<string> _prevNames;
            readonly bool _prevVoid;
            public ScopePopper(EmitContext ctx, List<string> prevNames, bool prevVoid)
            { _ctx = ctx; _prevNames = prevNames; _prevVoid = prevVoid; }
            public void Dispose()
            {
                _ctx.CurrentRefNames = _prevNames;
                _ctx.CurrentMethodIsVoidWithRefs = _prevVoid;
            }
        }

        public EmitContext(SemanticModel model, TypeMappingRegistry registry)
        {
            _model = model;
            Registry = registry;
        }

        public ITypeSymbol? TypeOf(ExpressionSyntax expr)
        {
            var info = _model.GetTypeInfo(expr);
            return info.Type ?? info.ConvertedType;
        }

        public ITypeSymbol? ConvertedTypeOf(ExpressionSyntax expr)
        {
            var info = _model.GetTypeInfo(expr);
            return info.ConvertedType ?? info.Type;
        }

        public bool IsInt32(ExpressionSyntax expr) =>
            TypeOf(expr)?.SpecialType == SpecialType.System_Int32;

        public bool IsInt64(ExpressionSyntax expr) =>
            TypeOf(expr)?.SpecialType == SpecialType.System_Int64;

        public bool IsUInt64(ExpressionSyntax expr) =>
            TypeOf(expr)?.SpecialType == SpecialType.System_UInt64;

        public ITypeSymbol? LocalTypeOf(VariableDeclaratorSyntax variable) =>
            (_model.GetDeclaredSymbol(variable) as ILocalSymbol)?.Type;

        public IMethodSymbol? InvocationTarget(InvocationExpressionSyntax inv) =>
            _model.GetSymbolInfo(inv).Symbol as IMethodSymbol;

        public ITypeSymbol? SymbolForTypeSyntax(TypeSyntax type) =>
            _model.GetSymbolInfo(type).Symbol as ITypeSymbol;

        public bool TryGetConstantValue(ExpressionSyntax expr, out object? value)
        {
            var c = _model.GetConstantValue(expr);
            if (c.HasValue) { value = c.Value; return true; }
            value = null;
            return false;
        }

        public ISymbol? SymbolFor(ExpressionSyntax expr) =>
            _model.GetSymbolInfo(expr).Symbol;

        public ISymbol? SymbolForIdentifier(IdentifierNameSyntax id) =>
            _model.GetSymbolInfo(id).Symbol;

        public INamedTypeSymbol? DeclaredTypeSymbol(BaseTypeDeclarationSyntax decl) =>
            _model.GetDeclaredSymbol(decl);
    }

    static readonly Lazy<MetadataReference[]> TrustedReferences = new(BuildTrustedReferences);

    /// <summary>Public accessor for <see cref="BatchTranspiler"/> to share the same BCL reference set.</summary>
    public static MetadataReference[] PublicTrustedReferences => TrustedReferences.Value;

    static MetadataReference[] BuildTrustedReferences()
    {
        // TRUSTED_PLATFORM_ASSEMBLIES contains the full BCL the runtime loaded for us
        // — every reference SemanticModel needs to resolve primitives, string, etc.
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var refs = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        // Mirrorgen.Attributes is part of TPA only when we ship as a tool; add it
        // explicitly so [Mirrorgen.Transpile] resolves either way.
        var attrPath = typeof(Mirrorgen.TranspileAttribute).Assembly.Location;
        if (!string.IsNullOrEmpty(attrPath) && File.Exists(attrPath) &&
            !refs.Any(r => string.Equals(r.Display, attrPath, StringComparison.OrdinalIgnoreCase)))
        {
            refs.Add(MetadataReference.CreateFromFile(attrPath));
        }
        return refs.ToArray();
    }

    static bool IsRefLike(ParameterSyntax p)
    {
        foreach (var mod in p.Modifiers)
        {
            var k = mod.Kind();
            if (k == SyntaxKind.RefKeyword || k == SyntaxKind.OutKeyword)
            {
                return true;
            }
        }
        return false;
    }

    static List<ParameterSyntax> CollectRefParams(MethodDeclarationSyntax method)
    {
        var list = new List<ParameterSyntax>();
        foreach (var p in method.ParameterList.Parameters)
        {
            if (IsRefLike(p)) list.Add(p);
        }
        return list;
    }

    static string EmitMethod(MethodDeclarationSyntax method, EmitContext ctx)
    {
        var name = ReadEmitName(method.AttributeLists) ?? method.Identifier.Text;

        // Generic methods carry their type parameters straight through to
        // TS; v0.2 doesn't translate `where T : …` constraints, so any
        // constraint clause is rejected loudly.
        if (method.ConstraintClauses.Count > 0)
        {
            throw new NotSupportedException(
                $"Generic constraints on '{method.Identifier.Text}' are not supported in v0.2 (got '{string.Join(", ", method.ConstraintClauses.Select(c => c.ToString()))}').");
        }
        var typeParams = method.TypeParameterList is { } tpl && tpl.Parameters.Count > 0
            ? $"<{string.Join(", ", tpl.Parameters.Select(p => p.Identifier.Text))}>"
            : string.Empty;

        var refParams = CollectRefParams(method);
        var isVoid = method.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);

        string returnType;
        if (refParams.Count == 0)
        {
            returnType = MapType(method.ReturnType, ctx);
        }
        else
        {
            // ref/out params land in the return tuple — JS has no by-reference
            // call semantics so the only honest emit is to return everything
            // the method "writes" and let the caller destructure.
            var refTypes = refParams.Select(p => MapType(p.Type!, ctx)).ToList();
            if (isVoid)
            {
                returnType = refTypes.Count == 1 ? refTypes[0] : "[" + string.Join(", ", refTypes) + "]";
            }
            else
            {
                var origRet = MapType(method.ReturnType, ctx);
                var positions = new List<string> { origRet };
                positions.AddRange(refTypes);
                returnType = "[" + string.Join(", ", positions) + "]";
            }
        }

        var parameters = string.Join(
            ", ",
            method.ParameterList.Parameters.Select(p =>
                p.Type is null
                    ? throw new NotSupportedException($"Parameter '{p.Identifier.Text}' has no type.")
                    : $"{p.Identifier.Text}: {MapType(p.Type, ctx)}"));

        var sb = new StringBuilder();
        sb.Append("export function ").Append(name).Append(typeParams).Append('(').Append(parameters).Append("): ").Append(returnType).AppendLine(" {");
        sb.Append(EmitMethodBody(method, ctx, refParams, isVoid));
        sb.AppendLine("}");
        return sb.ToString();
    }

    static bool HasTranspileAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (IsTranspileAttributeSyntax(attr)) return true;
            }
        }
        return false;
    }

    static bool IsTranspileAttributeSyntax(AttributeSyntax attr)
    {
        var n = attr.Name.ToString();
        return n == "Transpile" || n == "TranspileAttribute" ||
               n.EndsWith(".Transpile", StringComparison.Ordinal) ||
               n.EndsWith(".TranspileAttribute", StringComparison.Ordinal);
    }

    static string? ReadEmitName(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (!IsTranspileAttributeSyntax(attr)) continue;
                if (attr.ArgumentList is null) continue;

                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    if (arg.NameEquals?.Name.Identifier.Text != "EmitName") continue;
                    if (arg.Expression is LiteralExpressionSyntax lit &&
                        lit.Token.IsKind(SyntaxKind.StringLiteralToken))
                    {
                        var value = lit.Token.ValueText;
                        return string.IsNullOrEmpty(value) ? null : value;
                    }
                }
            }
        }
        return null;
    }

    static string EmitEnum(EnumDeclarationSyntax decl)
    {
        var name = ReadEmitName(decl.AttributeLists) ?? decl.Identifier.Text;
        var sb = new StringBuilder();
        sb.Append("export enum ").Append(name).AppendLine(" {");

        long implicitValue = 0;
        foreach (var member in decl.Members)
        {
            long value;
            if (member.EqualsValue?.Value is { } expr)
            {
                if (!TryEvaluateConstantInt(expr, out value))
                {
                    throw new NotSupportedException(
                        $"Enum member '{decl.Identifier.Text}.{member.Identifier.Text}' has a non-constant value.");
                }
                implicitValue = value + 1;
            }
            else
            {
                value = implicitValue++;
            }
            sb.Append(BodyIndent).Append(member.Identifier.Text).Append(" = ")
              .Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    static string EmitTypeDeclaration(TypeDeclarationSyntax decl, EmitContext ctx)
    {
        var name = ReadEmitName(decl.AttributeLists) ?? decl.Identifier.Text;
        var interfaceBody = new StringBuilder();
        var consts = new StringBuilder();
        var hasInterfaceMember = false;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Partial declarations: Roslyn merges them at the symbol level. Walk every
        // syntax reference for the declared symbol so members from each partial
        // half land in the same interface. Falls back to the single decl when the
        // symbol can't be resolved (e.g. broken sources).
        var allDecls = new List<TypeDeclarationSyntax> { decl };
        if (ctx.DeclaredTypeSymbol(decl) is { } symbol)
        {
            allDecls.Clear();
            foreach (var sref in symbol.DeclaringSyntaxReferences)
            {
                if (sref.GetSyntax() is TypeDeclarationSyntax td)
                    allDecls.Add(td);
            }
            if (allDecls.Count == 0) allDecls.Add(decl);
        }

        // Positional record parameters become the primary interface members.
        // Use whichever partial actually declares the parameter list.
        foreach (var d in allDecls)
        {
            if (d is RecordDeclarationSyntax rec && rec.ParameterList is { } parameters)
            {
                foreach (var p in parameters.Parameters)
                {
                    if (p.Type is null)
                    {
                        throw new NotSupportedException(
                            $"Record positional parameter '{p.Identifier.Text}' has no type.");
                    }
                    var member = p.Identifier.Text;
                    if (!seen.Add(member)) continue;
                    var opt = p.Type is NullableTypeSyntax ? "?" : "";
                    interfaceBody.Append(BodyIndent).Append(member).Append(opt).Append(": ").Append(MapType(p.Type, ctx)).AppendLine(";");
                    hasInterfaceMember = true;
                }
                break;
            }
        }

        // Properties + fields declared in the body of any partial declaration.
        foreach (var partial in allDecls)
        foreach (var bodyMember in partial.Members)
        {
            switch (bodyMember)
            {
                case PropertyDeclarationSyntax prop:
                    {
                        var member = prop.Identifier.Text;
                        if (!seen.Add(member)) continue;
                        // Expression-bodied get-only (`=> expr`) and computed get
                        // accessors are *behaviour*, not storage. Emitting them
                        // as interface fields would invite callers to set them.
                        // Leave behaviour to method emit; skip from the shape.
                        if (IsComputedProperty(prop)) break;
                        var opt = prop.Type is NullableTypeSyntax ? "?" : "";
                        interfaceBody.Append(BodyIndent).Append(member).Append(opt).Append(": ").Append(MapType(prop.Type, ctx)).AppendLine(";");
                        hasInterfaceMember = true;
                        break;
                    }
                case FieldDeclarationSyntax field:
                    {
                        var isPublic = field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
                        var isConst = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                        var isStatic = field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                        // Non-public members never cross the C# ↔ TS boundary regardless of kind.
                        if (!isPublic) break;
                        var fieldEmitName = ReadEmitName(field.AttributeLists);
                        foreach (var variable in field.Declaration.Variables)
                        {
                            var member = fieldEmitName ?? variable.Identifier.Text;
                            if (!seen.Add(member)) continue;
                            if (isConst && variable.Initializer is not null
                                && ctx.TryGetConstantValue(variable.Initializer.Value, out var constValue)
                                && TryFormatTsLiteral(constValue, out var literal))
                            {
                                var tsType = MapType(field.Declaration.Type, ctx);
                                consts.Append("export const ").Append(member)
                                    .Append(": ").Append(tsType)
                                    .Append(" = ").Append(literal).AppendLine(";");
                            }
                            else if (isConst || isStatic)
                            {
                                // Static / const without a literal we can format — skip rather
                                // than pretend it's an instance interface member.
                            }
                            else
                            {
                                var opt = field.Declaration.Type is NullableTypeSyntax ? "?" : "";
                                interfaceBody.Append(BodyIndent).Append(member).Append(opt).Append(": ").Append(MapType(field.Declaration.Type, ctx)).AppendLine(";");
                                hasInterfaceMember = true;
                            }
                        }
                        break;
                    }
                // Methods / constructors / etc. on a [Transpile] type aren't part of
                // the v0.1 surface — silently skip rather than throw so consumers can
                // freely add server-side helpers next to the data shape.
            }
        }

        // Polymorphic base records (`abstract record TopologyParams;`) emit an
        // empty interface so subtypes that reference the base name still resolve
        // — matches TsGen's behaviour. Const-only static classes skip the
        // empty-interface noise (intent inherited from #46).
        var emitEmptyInterface = !hasInterfaceMember
            && consts.Length == 0
            && decl is RecordDeclarationSyntax or ClassDeclarationSyntax or StructDeclarationSyntax;

        var sb = new StringBuilder();
        if (consts.Length > 0)
        {
            sb.Append(consts);
            if (hasInterfaceMember) sb.AppendLine();
        }
        if (hasInterfaceMember || emitEmptyInterface)
        {
            sb.Append("export interface ").Append(name).AppendLine(" {");
            sb.Append(interfaceBody);
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    static bool TryFormatTsLiteral(object? value, out string literal)
    {
        switch (value)
        {
            case null: literal = "null"; return true;
            case bool b: literal = b ? "true" : "false"; return true;
            case string s: literal = "\"" + EscapeStringLiteral(s) + "\""; return true;
            case char c: literal = "\"" + EscapeStringLiteral(c.ToString()) + "\""; return true;
            case byte u8: literal = u8.ToString(CultureInfo.InvariantCulture); return true;
            case sbyte i8: literal = i8.ToString(CultureInfo.InvariantCulture); return true;
            case short i16: literal = i16.ToString(CultureInfo.InvariantCulture); return true;
            case ushort u16: literal = u16.ToString(CultureInfo.InvariantCulture); return true;
            case int i32: literal = i32.ToString(CultureInfo.InvariantCulture); return true;
            case uint u32: literal = u32.ToString(CultureInfo.InvariantCulture); return true;
            case long i64: literal = i64.ToString(CultureInfo.InvariantCulture) + "n"; return true;
            case ulong u64: literal = u64.ToString(CultureInfo.InvariantCulture) + "n"; return true;
            case float f32: literal = f32.ToString("R", CultureInfo.InvariantCulture); return true;
            case double f64: literal = f64.ToString("R", CultureInfo.InvariantCulture); return true;
            case decimal dec: literal = dec.ToString(CultureInfo.InvariantCulture); return true;
        }
        literal = string.Empty;
        return false;
    }

    // Validator emission — analyses the emitted types output (interfaces + enums)
    // and produces `parseX(value: unknown): X` functions that throw TypeError on
    // shape mismatch. Used when TranspileOptions.EmitValidators is set; gives
    // consumers a runtime gate at the C# ↔ TS boundary.
    static readonly System.Text.RegularExpressions.Regex InterfaceBlockRegex =
        new(@"export interface (\w+) \{([^}]*)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex EnumDeclRegex =
        new(@"export enum (\w+) \{", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex InterfaceMemberRegex =
        new(@"^\s*(\w+)(\??):\s*(.+?);\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

    static string EmitValidatorsForTypesOutput(string typesOutput)
    {
        var interfaces = new List<(string Name, List<(string Field, bool Optional, string Type)> Members)>();
        var enumNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in EnumDeclRegex.Matches(typesOutput))
        {
            enumNames.Add(m.Groups[1].Value);
        }
        foreach (System.Text.RegularExpressions.Match m in InterfaceBlockRegex.Matches(typesOutput))
        {
            var name = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            var props = new List<(string, bool, string)>();
            foreach (System.Text.RegularExpressions.Match pm in InterfaceMemberRegex.Matches(body))
            {
                props.Add((pm.Groups[1].Value, pm.Groups[2].Value == "?", pm.Groups[3].Value.Trim()));
            }
            interfaces.Add((name, props));
        }
        if (interfaces.Count == 0) return string.Empty;

        var interfaceNames = new HashSet<string>(interfaces.Select(i => i.Name), StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var (name, props) in interfaces)
        {
            sb.Append("export function parse").Append(name).Append("(value: unknown): ").Append(name).AppendLine(" {");
            sb.Append(BodyIndent).AppendLine("if (typeof value !== 'object' || value === null) {");
            sb.Append(BodyIndent).Append(BodyIndent).Append("throw new TypeError(`").Append(name).AppendLine(": expected object, got ${typeof value}`);");
            sb.Append(BodyIndent).AppendLine("}");
            sb.Append(BodyIndent).AppendLine("const o = value as Record<string, unknown>;");
            foreach (var (field, optional, type) in props)
            {
                EmitValidatorFieldCheck(sb, name, field, optional, type, interfaceNames, enumNames);
            }
            sb.Append(BodyIndent).Append("return o as unknown as ").Append(name).AppendLine(";");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    static void EmitValidatorFieldCheck(StringBuilder sb, string ifaceName, string field, bool optional, string type, HashSet<string> interfaceNames, HashSet<string> enumNames)
    {
        var path = $"{ifaceName}.{field}";
        var access = $"o[\"{field}\"]";
        sb.Append(BodyIndent).AppendLine("{");
        sb.Append(BodyIndent).Append(BodyIndent).Append("const x = ").Append(access).AppendLine(";");
        // Strip " | null" — we filter null/undefined explicitly when the field
        // is nullable / optional.
        var inner = type.EndsWith(" | null") ? type[..^" | null".Length] : type;
        var nullableOrOptional = optional || type.EndsWith(" | null");
        if (nullableOrOptional)
        {
            sb.Append(BodyIndent).Append(BodyIndent).AppendLine("if (x !== null && x !== undefined) {");
            EmitValidatorTypeCheck(sb, path, "x", inner, interfaceNames, enumNames, "      ");
            sb.Append(BodyIndent).Append(BodyIndent).AppendLine("}");
        }
        else
        {
            sb.Append(BodyIndent).Append(BodyIndent).Append("if (x === undefined) throw new TypeError(`").Append(path).AppendLine(": required`);");
            EmitValidatorTypeCheck(sb, path, "x", inner, interfaceNames, enumNames, "    ");
        }
        sb.Append(BodyIndent).AppendLine("}");
    }

    static void EmitValidatorTypeCheck(StringBuilder sb, string path, string expr, string type, HashSet<string> interfaceNames, HashSet<string> enumNames, string indent)
    {
        if (type.EndsWith("[]"))
        {
            sb.Append(indent).Append("if (!Array.isArray(").Append(expr).Append(")) throw new TypeError(`").Append(path).Append(": expected array, got ${typeof ").Append(expr).AppendLine("}`);");
            return;
        }
        if (type.StartsWith("Record<"))
        {
            sb.Append(indent).Append("if (typeof ").Append(expr).Append(" !== 'object' || ").Append(expr).Append(" === null) throw new TypeError(`").Append(path).Append(": expected object, got ${typeof ").Append(expr).AppendLine("}`);");
            return;
        }
        switch (type)
        {
            case "string":
            case "number":
            case "boolean":
                sb.Append(indent).Append("if (typeof ").Append(expr).Append(" !== '").Append(type).Append("') throw new TypeError(`").Append(path).Append(": expected ").Append(type).Append(", got ${typeof ").Append(expr).AppendLine("}`);");
                return;
            case "bigint":
                sb.Append(indent).Append("if (typeof ").Append(expr).Append(" !== 'bigint') throw new TypeError(`").Append(path).Append(": expected bigint, got ${typeof ").Append(expr).AppendLine("}`);");
                return;
            case "unknown":
                return;
        }
        if (enumNames.Contains(type))
        {
            sb.Append(indent).Append("if (typeof ").Append(expr).Append(" !== 'number' && typeof ").Append(expr).Append(" !== 'string') throw new TypeError(`").Append(path).Append(": expected number or string (").Append(type).Append("), got ${typeof ").Append(expr).AppendLine("}`);");
            return;
        }
        if (interfaceNames.Contains(type))
        {
            sb.Append(indent).Append("parse").Append(type).Append("(").Append(expr).AppendLine(");");
            return;
        }
        // Unknown leaf — best-effort object check.
        sb.Append(indent).Append("if (typeof ").Append(expr).Append(" !== 'object' || ").Append(expr).Append(" === null) throw new TypeError(`").Append(path).Append(": expected object (").Append(type).Append("), got ${typeof ").Append(expr).AppendLine("}`);");
    }

    static string EscapeStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    static bool TryEvaluateConstantInt(ExpressionSyntax expr, out long value)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit when lit.Token.Value is int i:
                value = i;
                return true;
            case LiteralExpressionSyntax lit when lit.Token.Value is long l:
                value = l;
                return true;
            case PrefixUnaryExpressionSyntax { Operand: var inner } u when u.IsKind(SyntaxKind.UnaryMinusExpression):
                if (TryEvaluateConstantInt(inner, out var v)) { value = -v; return true; }
                break;
            case PrefixUnaryExpressionSyntax { Operand: var inner } u when u.IsKind(SyntaxKind.UnaryPlusExpression):
                return TryEvaluateConstantInt(inner, out value);
        }
        value = 0;
        return false;
    }

    static string MapType(TypeSyntax type, EmitContext ctx)
    {
        if (type is ArrayTypeSyntax arr)
        {
            // System.Text.Json serializes byte[] as a base64 string at the wire,
            // not as number[]. Match that contract.
            if (arr.ElementType is PredefinedTypeSyntax { Keyword.ValueText: "byte" })
                return "string";
            return $"{MapType(arr.ElementType, ctx)}[]";
        }
        if (type is NullableTypeSyntax nt)
        {
            return $"{MapType(nt.ElementType, ctx)} | null";
        }
        // `System.Collections.Generic.List<int>` and other qualified references
        // resolve by recursing on the final segment — namespace qualifiers are
        // stripped (TS treats type names by their tail identifier).
        if (type is QualifiedNameSyntax qn)
        {
            return MapType(qn.Right, ctx);
        }
        if (type is GenericNameSyntax gen)
        {
            var genericName = gen.Identifier.Text;
            var args = gen.TypeArgumentList.Arguments;
            if (genericName is "List" or "IReadOnlyList" or "IList"
                or "IEnumerable" or "ICollection" or "IReadOnlyCollection"
                && args.Count == 1)
            {
                return $"{MapType(args[0], ctx)}[]";
            }
            if (genericName is "Dictionary" or "IReadOnlyDictionary" or "IDictionary" && args.Count == 2)
            {
                return $"Record<{MapType(args[0], ctx)}, {MapType(args[1], ctx)}>";
            }
            throw new NotSupportedException($"Unsupported generic type: {type}");
        }

        // Plugin mapping wins over the syntactic fallback so consumers can
        // remap their own domain types onto a TS primitive or runtime import.
        if (ctx.Registry.Count > 0 && ctx.SymbolForTypeSyntax(type) is { } sym)
        {
            if (ctx.Registry.TryGet(sym.ToDisplayString(), out var mapping))
            {
                return mapping.TsTypeName;
            }
        }

        if (type is TupleTypeSyntax tuple)
        {
            return MapTupleType(tuple, ctx);
        }

        var s = type.ToString();
        return s switch
        {
            "int" or "short" or "byte" or "sbyte"
                or "uint" or "ushort"
                or "float" or "double" or "decimal" => "number",
            "bool" => "boolean",
            "string" => "string",
            "void" => "void",
            "long" or "ulong" => "bigint",
            // System.Text.Json default wire encoding for these is an ISO 8601 /
            // RFC string; TS contracts treat them as opaque strings until the
            // consumer chooses a parsing strategy.
            "Guid" or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Uri" => "string",
            // Opaque JSON payloads — DTOs use these as escape hatches; the
            // shape is by definition unknown at the contract level.
            "object" or "dynamic" or "JsonElement" or "JsonNode" or "JsonObject" or "JsonArray" => "unknown",
            "char" => throw new NotSupportedException($"Unsupported primitive type: {s}"),
            // Unknown identifier — assume it's a reference to another transpiled
            // type declared in the same compilation. The reachability scan
            // is what ultimately guarantees it ends up emitted.
            _ => s,
        };
    }

    static string MapTupleType(TupleTypeSyntax tuple, EmitContext ctx)
    {
        // Named elements emit as `{ A: T1; B: T2 }` (object type), unnamed as
        // `[T1, T2]` (TS tuple type). Mixing names with un-named elements is
        // invalid C# at parse time so we don't have to handle the partial case.
        var allNamed = tuple.Elements.All(e => e.Identifier.Text.Length > 0);
        if (allNamed)
        {
            var fields = string.Join("; ",
                tuple.Elements.Select(e => $"{e.Identifier.Text}: {MapType(e.Type, ctx)}"));
            return "{ " + fields + " }";
        }
        var positional = string.Join(", ", tuple.Elements.Select(e => MapType(e.Type, ctx)));
        return "[" + positional + "]";
    }

    static string MapTupleSymbol(INamedTypeSymbol tuple, EmitContext ctx)
    {
        var elements = tuple.TupleElements;
        // Tuple elements have positional default names (`Item1` / `Item2`) when
        // the source didn't name them. CorrespondingTupleField stays null for
        // explicitly named elements — that's how we tell apart `(x, y)` (no
        // names) vs `(IX: x, IY: y)` (named).
        var allExplicitlyNamed = !elements.IsDefault && elements.Length > 0 &&
            elements.All(e => e.CorrespondingTupleField is not null && !e.IsImplicitlyDeclared && e.Name != null &&
                              !e.Name.StartsWith("Item", StringComparison.Ordinal));
        // The check above is brittle (Item1/Item2 collisions when the user
        // explicitly names a field "Item1"). Use the simpler rule: if every
        // element's name is the same as its CorrespondingTupleField name and
        // matches the default Item-N pattern, it's positional.
        bool isPositional = elements.Length > 0 && elements.All(e =>
            e.CorrespondingTupleField is { } ctf && ctf.Name == e.Name &&
            e.Name.StartsWith("Item", StringComparison.Ordinal) &&
            int.TryParse(e.Name.AsSpan(4), out _));
        if (!isPositional)
        {
            var fields = string.Join("; ",
                elements.Select(e => $"{e.Name}: {MapTypeSymbol(e.Type, ctx)}"));
            return "{ " + fields + " }";
        }
        var positional = string.Join(", ", elements.Select(e => MapTypeSymbol(e.Type, ctx)));
        return "[" + positional + "]";
    }

    static string MapTypeSymbol(ITypeSymbol type, EmitContext ctx)
    {
        if (type is IArrayTypeSymbol arr)
        {
            return $"{MapTypeSymbol(arr.ElementType, ctx)}[]";
        }
        if (type is INamedTypeSymbol tupleType && tupleType.IsTupleType)
        {
            return MapTupleSymbol(tupleType, ctx);
        }
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            return $"{MapTypeSymbol(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), ctx)} | null";
        }
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol nullable && nullable.TypeArguments.Length == 1)
        {
            return $"{MapTypeSymbol(nullable.TypeArguments[0], ctx)} | null";
        }
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            if (named.TypeArguments.Length == 1 &&
                def is "System.Collections.Generic.List<T>"
                    or "System.Collections.Generic.IReadOnlyList<T>"
                    or "System.Collections.Generic.IList<T>")
            {
                return $"{MapTypeSymbol(named.TypeArguments[0], ctx)}[]";
            }
            if (named.TypeArguments.Length == 2 &&
                def is "System.Collections.Generic.Dictionary<TKey, TValue>"
                    or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                    or "System.Collections.Generic.IDictionary<TKey, TValue>")
            {
                return $"Record<{MapTypeSymbol(named.TypeArguments[0], ctx)}, {MapTypeSymbol(named.TypeArguments[1], ctx)}>";
            }
        }

        if (ctx.Registry.Count > 0 && ctx.Registry.TryGet(type.ToDisplayString(), out var mapping))
        {
            return mapping.TsTypeName;
        }
        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_UInt32 or SpecialType.System_UInt16
                or SpecialType.System_Single or SpecialType.System_Double => "number",
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_String => "string",
            SpecialType.System_Void => "void",
            SpecialType.System_Int64 or SpecialType.System_UInt64 => "bigint",
            // Same fallback as MapType: assume reference to another transpiled type.
            _ => type.Name,
        };
    }

    const string BodyIndent = "  ";

    static string EmitMethodBody(MethodDeclarationSyntax method, EmitContext ctx)
        => EmitMethodBody(method, ctx, new List<ParameterSyntax>(), isVoid: method.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword));

    static string EmitMethodBody(MethodDeclarationSyntax method, EmitContext ctx, List<ParameterSyntax> refParams, bool isVoid)
    {
        var refNames = refParams.Select(p => p.Identifier.Text).ToList();
        if (method.ExpressionBody is { } eb)
        {
            if (refParams.Count > 0)
            {
                throw new NotSupportedException($"Expression-bodied methods with ref/out params are not supported on '{method.Identifier.Text}'.");
            }
            return $"{BodyIndent}return {EmitExpression(eb.Expression, ctx)};\n";
        }
        if (method.Body is { } block)
        {
            using var _ = ctx.PushRefMethodScope(refNames, isVoid && refNames.Count > 0);
            var sb = new StringBuilder();
            foreach (var stmt in block.Statements)
            {
                sb.Append(EmitStatement(stmt, ctx, BodyIndent));
            }
            // Void method with ref/out params has no explicit `return` — append
            // a synthetic one carrying the ref values so the caller can
            // destructure. Non-void cases must already return on every path
            // (C# enforces it), and EmitStatement rewrote those.
            if (refParams.Count > 0 && isVoid && !EndsWithReturn(block))
            {
                sb.Append(BodyIndent).Append("return ").Append(EmitRefReturnExpression(refNames, isVoid)).AppendLine(";");
            }
            return sb.ToString();
        }
        throw new NotSupportedException($"Method '{method.Identifier.Text}' has no body.");
    }

    static bool EndsWithReturn(BlockSyntax block)
    {
        if (block.Statements.Count == 0) return false;
        var last = block.Statements[block.Statements.Count - 1];
        return last is ReturnStatementSyntax;
    }

    static string EmitRefReturnExpression(List<string> refNames, bool isVoid, string? originalReturnExpr = null)
    {
        if (isVoid)
        {
            return refNames.Count == 1 ? refNames[0] : "[" + string.Join(", ", refNames) + "]";
        }
        var positions = new List<string> { originalReturnExpr ?? "undefined" };
        positions.AddRange(refNames);
        return "[" + string.Join(", ", positions) + "]";
    }

    static string EmitStatement(StatementSyntax stmt, EmitContext ctx, string indent)
    {
        var refNames = ctx.CurrentRefNames;
        var isVoidWithRefs = ctx.CurrentMethodIsVoidWithRefs;
        switch (stmt)
        {
            case ReturnStatementSyntax { Expression: null }:
                if (refNames.Count > 0)
                {
                    return $"{indent}return {EmitRefReturnExpression(refNames, isVoidWithRefs)};\n";
                }
                return $"{indent}return;\n";
            case ReturnStatementSyntax ret:
                if (refNames.Count > 0)
                {
                    var orig = EmitExpression(ret.Expression!, ctx);
                    return $"{indent}return {EmitRefReturnExpression(refNames, isVoidWithRefs, orig)};\n";
                }
                return $"{indent}return {EmitExpression(ret.Expression!, ctx)};\n";
            case BlockSyntax block:
                var sb = new StringBuilder();
                foreach (var inner in block.Statements)
                {
                    sb.Append(EmitStatement(inner, ctx, indent));
                }
                return sb.ToString();
            case IfStatementSyntax ifs:
                return EmitIf(ifs, ctx, indent, leadIndent: true);
            case LocalDeclarationStatementSyntax localDecl:
                return EmitLocalDeclaration(localDecl, ctx, indent);
            case ExpressionStatementSyntax exprStmt:
                if (exprStmt.Expression is InvocationExpressionSyntax callee &&
                    TryEmitRefInvocationStatement(callee, ctx, indent, out var refStmt))
                {
                    return refStmt;
                }
                if (exprStmt.Expression is AssignmentExpressionSyntax tupAssign &&
                    TryEmitTupleDeconstructionDeclaration(tupAssign, ctx, indent, out var tupStmt))
                {
                    return tupStmt;
                }
                return $"{indent}{EmitExpression(exprStmt.Expression, ctx)};\n";
            case ForStatementSyntax forStmt:
                return EmitForStatement(forStmt, ctx, indent);
            case ForEachStatementSyntax fe:
                return EmitForEachStatement(fe, ctx, indent);
            case SwitchStatementSyntax sw:
                return EmitSwitchStatement(sw, ctx, indent);
            case WhileStatementSyntax ws:
                {
                    var w = new StringBuilder();
                    w.Append(indent).Append("while (").Append(EmitExpression(ws.Condition, ctx)).AppendLine(") {");
                    w.Append(EmitBranchBody(ws.Statement, ctx, indent + BodyIndent));
                    w.Append(indent).AppendLine("}");
                    return w.ToString();
                }
            case DoStatementSyntax ds:
                {
                    var d = new StringBuilder();
                    d.Append(indent).AppendLine("do {");
                    d.Append(EmitBranchBody(ds.Statement, ctx, indent + BodyIndent));
                    d.Append(indent).Append("} while (").Append(EmitExpression(ds.Condition, ctx)).AppendLine(");");
                    return d.ToString();
                }
            case BreakStatementSyntax:
                return $"{indent}break;\n";
            case ContinueStatementSyntax:
                return $"{indent}continue;\n";
            case ThrowStatementSyntax thr:
                return EmitThrow(thr, ctx, indent);
            default:
                throw new NotSupportedException($"Unsupported statement: {stmt.Kind()}");
        }
    }

    static string EmitForStatement(ForStatementSyntax forStmt, EmitContext ctx, string indent)
    {
        var initEmit = EmitForInit(forStmt, ctx);
        var cond = forStmt.Condition is { } c ? EmitExpression(c, ctx) : string.Empty;
        var iter = string.Join(", ",
            forStmt.Incrementors.Select(i => EmitExpression(i, ctx)));

        var sb = new StringBuilder();
        sb.Append(indent).Append("for (").Append(initEmit).Append("; ").Append(cond).Append("; ").Append(iter).AppendLine(") {");
        sb.Append(EmitBranchBody(forStmt.Statement, ctx, indent + BodyIndent));
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    static string EmitForInit(ForStatementSyntax forStmt, EmitContext ctx)
    {
        if (forStmt.Declaration is { } decl)
        {
            if (decl.Variables.Count != 1)
            {
                throw new NotSupportedException("Multi-variable for-loop init is not yet supported.");
            }
            var v = decl.Variables[0];
            string tsType;
            if (decl.Type.IsVar)
            {
                var symbolType = ctx.LocalTypeOf(v)
                    ?? throw new NotSupportedException($"Cannot resolve type of for-init '{v.Identifier.Text}'.");
                tsType = MapTypeSymbol(symbolType, ctx);
            }
            else
            {
                tsType = MapType(decl.Type, ctx);
            }
            var init = v.Initializer is { } i ? $" = {EmitExpression(i.Value, ctx)}" : string.Empty;
            return $"let {v.Identifier.Text}: {tsType}{init}";
        }
        if (forStmt.Initializers.Count > 0)
        {
            return string.Join(", ",
                forStmt.Initializers.Select(i => EmitExpression(i, ctx)));
        }
        return string.Empty;
    }

    static string EmitForEachStatement(ForEachStatementSyntax fe, EmitContext ctx, string indent)
    {
        var collectionType = ctx.TypeOf(fe.Expression);
        if (!IsArrayLikeEnumerable(collectionType))
        {
            throw new NotSupportedException(
                $"foreach only supports T[] / List<T> / IReadOnlyList<T> in v0.1; got '{collectionType?.ToDisplayString() ?? "unknown"}'.");
        }

        var collection = EmitExpression(fe.Expression, ctx);
        var sb = new StringBuilder();
        sb.Append(indent).Append("for (const ").Append(fe.Identifier.Text).Append(" of ").Append(collection).AppendLine(") {");
        sb.Append(EmitBranchBody(fe.Statement, ctx, indent + BodyIndent));
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    static string EmitSwitchStatement(SwitchStatementSyntax sw, EmitContext ctx, string indent)
    {
        // If every label is a plain constant / enum-member, fall through to
        // the original TS switch shape. As soon as any section uses a
        // relational / and / or pattern, rewrite the whole switch as an
        // if-else-if chain over a captured copy of the governing
        // expression — TS case labels don't accept those patterns.
        bool needsRewrite = false;
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
            {
                if (label is CasePatternSwitchLabelSyntax cp && !IsConstantLikePattern(cp.Pattern))
                {
                    needsRewrite = true;
                    break;
                }
            }
            if (needsRewrite) break;
        }

        if (needsRewrite)
        {
            return EmitSwitchStatementAsIfElse(sw, ctx, indent);
        }

        var sb = new StringBuilder();
        sb.Append(indent).Append("switch (").Append(EmitExpression(sw.Expression, ctx)).AppendLine(") {");
        var caseIndent = indent + BodyIndent;
        var bodyIndent = caseIndent + BodyIndent;
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax cs:
                        sb.Append(caseIndent).Append("case ")
                          .Append(EmitExpression(cs.Value, ctx)).AppendLine(":");
                        break;
                    case DefaultSwitchLabelSyntax:
                        sb.Append(caseIndent).AppendLine("default:");
                        break;
                    case CasePatternSwitchLabelSyntax cp:
                        sb.Append(caseIndent).Append("case ")
                          .Append(EmitSwitchPattern(cp.Pattern, ctx)).AppendLine(":");
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported switch label: {label.Kind()}");
                }
            }
            foreach (var stmt in section.Statements)
            {
                if (stmt is BreakStatementSyntax)
                {
                    sb.Append(bodyIndent).AppendLine("break;");
                    continue;
                }
                sb.Append(EmitStatement(stmt, ctx, bodyIndent));
            }
        }
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    static bool IsConstantLikePattern(PatternSyntax pattern) => pattern switch
    {
        ConstantPatternSyntax => true,
        _ => false,
    };

    static string EmitSwitchStatementAsIfElse(SwitchStatementSyntax sw, EmitContext ctx, string indent)
    {
        var governing = EmitExpression(sw.Expression, ctx);
        var sb = new StringBuilder();
        sb.Append(indent).Append("{ const _v = ").Append(governing).AppendLine(";");
        var innerIndent = indent + BodyIndent;
        var bodyIndent = innerIndent + BodyIndent;
        bool first = true;
        foreach (var section in sw.Sections)
        {
            string cond = BuildSectionCondition(section, ctx);
            if (cond == "true")
            {
                if (first)
                {
                    sb.Append(innerIndent).AppendLine("{");
                }
                else
                {
                    sb.Append(innerIndent).AppendLine("else {");
                }
            }
            else
            {
                sb.Append(innerIndent).Append(first ? "if (" : "else if (").Append(cond).AppendLine(") {");
            }
            first = false;
            foreach (var stmt in section.Statements)
            {
                // `break;` inside the original switch terminates the case;
                // inside an if-else chain it's redundant (control already
                // leaves the block), so drop it. Other `break;` (inside an
                // inner loop) stay through the regular EmitStatement path,
                // but we can't distinguish here, so we only drop bare top-
                // level break statements.
                if (stmt is BreakStatementSyntax) continue;
                sb.Append(EmitStatement(stmt, ctx, bodyIndent));
            }
            sb.Append(innerIndent).AppendLine("}");
        }
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    static string BuildSectionCondition(SwitchSectionSyntax section, EmitContext ctx)
    {
        // Each section can carry multiple labels — OR them together. A
        // default label collapses the section to `true`.
        var parts = new List<string>();
        foreach (var label in section.Labels)
        {
            switch (label)
            {
                case CaseSwitchLabelSyntax cs:
                    parts.Add($"_v === {EmitExpression(cs.Value, ctx)}");
                    break;
                case DefaultSwitchLabelSyntax:
                    return "true";
                case CasePatternSwitchLabelSyntax cp:
                    parts.Add(EmitPatternCondition(cp.Pattern, "_v", ctx));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported switch label: {label.Kind()}");
            }
        }
        return parts.Count == 1 ? parts[0] : $"({string.Join(" || ", parts)})";
    }

    static string EmitSwitchExpression(SwitchExpressionSyntax swx, EmitContext ctx)
    {
        // C# switch expressions don't have a TS counterpart — emit a self-
        // calling arrow that branches with if-returns over a captured copy of
        // the governing expression. The capture avoids re-evaluating side
        // effects across arms.
        var governing = EmitExpression(swx.GoverningExpression, ctx);
        var sb = new StringBuilder();
        sb.Append("((): ").Append(InferSwitchExpressionResultType(swx, ctx)).Append(" => { ");
        sb.Append("const _v = ").Append(governing).Append("; ");
        foreach (var arm in swx.Arms)
        {
            // Type / var patterns bind a fresh name to the governing value;
            // wrap each binding arm in its own block so the const stays
            // scoped and doesn't collide with sibling arms.
            if (TryGetPatternBinding(arm.Pattern, out var bindName))
            {
                sb.Append("{ const ").Append(bindName).Append(" = _v; ");
                if (arm.WhenClause is { } whenC)
                {
                    sb.Append("if (").Append(EmitExpression(whenC.Condition, ctx)).Append(") return ")
                      .Append(EmitExpression(arm.Expression, ctx)).Append("; ");
                }
                else
                {
                    sb.Append("return ").Append(EmitExpression(arm.Expression, ctx)).Append("; ");
                }
                sb.Append("} ");
                continue;
            }

            string cond = arm.Pattern is DiscardPatternSyntax
                ? "true"
                : EmitPatternCondition(arm.Pattern, "_v", ctx);
            if (arm.WhenClause is { } when)
            {
                var guard = EmitExpression(when.Condition, ctx);
                cond = cond == "true" ? guard : $"({cond}) && ({guard})";
            }
            if (cond == "true")
            {
                sb.Append("return ").Append(EmitExpression(arm.Expression, ctx)).Append("; ");
            }
            else
            {
                sb.Append("if (").Append(cond).Append(") return ")
                  .Append(EmitExpression(arm.Expression, ctx)).Append("; ");
            }
        }
        // Fall-through guard — C# would throw SwitchExpressionException at
        // runtime if no arm matched. Mirror that loudly so silent undefined
        // values don't leak past the boundary.
        sb.Append("throw new Error(\"switch expression: no arm matched\"); })()");
        return sb.ToString();
    }

    static bool TryGetPatternBinding(PatternSyntax pattern, out string name)
    {
        name = string.Empty;
        // `int n` / `int n when ...`
        if (pattern is DeclarationPatternSyntax dp &&
            dp.Designation is SingleVariableDesignationSyntax dpDes)
        {
            name = dpDes.Identifier.Text;
            return true;
        }
        // `var n` / `var n when ...`
        if (pattern is VarPatternSyntax vp &&
            vp.Designation is SingleVariableDesignationSyntax vpDes)
        {
            name = vpDes.Identifier.Text;
            return true;
        }
        return false;
    }

    static string EmitPatternCondition(PatternSyntax pattern, string governingVar, EmitContext ctx)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax cp:
                return $"{governingVar} === {EmitExpression(cp.Expression, ctx)}";
            case RelationalPatternSyntax rp:
                return $"{governingVar} {MapRelationalPatternOperator(rp.OperatorToken)} {EmitExpression(rp.Expression, ctx)}";
            case ParenthesizedPatternSyntax pp:
                return $"({EmitPatternCondition(pp.Pattern, governingVar, ctx)})";
            case BinaryPatternSyntax bp when bp.IsKind(SyntaxKind.AndPattern):
                return $"({EmitPatternCondition(bp.Left, governingVar, ctx)} && {EmitPatternCondition(bp.Right, governingVar, ctx)})";
            case BinaryPatternSyntax bp when bp.IsKind(SyntaxKind.OrPattern):
                return $"({EmitPatternCondition(bp.Left, governingVar, ctx)} || {EmitPatternCondition(bp.Right, governingVar, ctx)})";
            case DiscardPatternSyntax:
                return "true";
            default:
                throw new NotSupportedException(
                    $"Unsupported switch pattern '{pattern.Kind()}'. Supported: constant, relational, parenthesised, and/or composites, discard (with optional `when` guards).");
        }
    }

    static string MapRelationalPatternOperator(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.GreaterThanToken => ">",
        SyntaxKind.GreaterThanEqualsToken => ">=",
        SyntaxKind.LessThanToken => "<",
        SyntaxKind.LessThanEqualsToken => "<=",
        SyntaxKind.EqualsEqualsToken => "===",
        SyntaxKind.ExclamationEqualsToken => "!==",
        _ => throw new NotSupportedException($"Unsupported relational pattern operator: {token.Kind()}"),
    };

    static string InferSwitchExpressionResultType(SwitchExpressionSyntax swx, EmitContext ctx)
    {
        var type = ctx.TypeOf(swx);
        return type is null ? "unknown" : MapTypeSymbol(type, ctx);
    }

    static string EmitSwitchPattern(PatternSyntax pattern, EmitContext ctx)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax cp:
                return EmitExpression(cp.Expression, ctx);
            default:
                throw new NotSupportedException(
                    $"Unsupported switch pattern '{pattern.Kind()}'. Only constant / enum-member patterns are supported in v0.1.");
        }
    }

    static bool IsListLike(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType) return false;
        var def = named.OriginalDefinition.ToDisplayString();
        return def is "System.Collections.Generic.List<T>"
            or "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.IList<T>";
    }

    static bool IsDictionaryLike(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType) return false;
        var def = named.OriginalDefinition.ToDisplayString();
        return def is "System.Collections.Generic.Dictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
            or "System.Collections.Generic.IDictionary<TKey, TValue>";
    }

    static bool IsArrayLikeEnumerable(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol) return true;
        if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            return def is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.IList<T>";
        }
        return false;
    }

    static string EmitLocalDeclaration(LocalDeclarationStatementSyntax local, EmitContext ctx, string indent)
    {
        var declaration = local.Declaration;
        if (declaration.Variables.Count == 0)
        {
            throw new NotSupportedException("Empty local declaration.");
        }
        // Multi-variable declarations like `int x = 1, y = 2;` get split into
        // one TS `let` per variable. Each variable's individual type is
        // resolved by the semantic model so we don't lose anything to the
        // shared `int` type syntax — they all map identically here anyway.
        var sb = new StringBuilder();
        foreach (var variable in declaration.Variables)
        {
            string tsType;
            if (declaration.Type.IsVar)
            {
                var symbolType = ctx.LocalTypeOf(variable)
                    ?? throw new NotSupportedException($"Cannot resolve type of local '{variable.Identifier.Text}'.");
                tsType = MapTypeSymbol(symbolType, ctx);
            }
            else
            {
                tsType = MapType(declaration.Type, ctx);
            }

            var initEmit = variable.Initializer is { } init
                ? $" = {EmitExpression(init.Value, ctx)}"
                : string.Empty;
            sb.Append(indent).Append("let ").Append(variable.Identifier.Text)
                .Append(": ").Append(tsType).Append(initEmit).AppendLine(";");
        }
        return sb.ToString();
    }

    static string EmitIf(IfStatementSyntax ifs, EmitContext ctx, string indent, bool leadIndent)
    {
        var childIndent = indent + BodyIndent;
        var sb = new StringBuilder();
        if (leadIndent) sb.Append(indent);
        sb.Append("if (").Append(EmitExpression(ifs.Condition, ctx)).AppendLine(") {");
        sb.Append(EmitBranchBody(ifs.Statement, ctx, childIndent));
        sb.Append(indent).Append('}');

        if (ifs.Else is { } elseClause)
        {
            if (elseClause.Statement is IfStatementSyntax nested)
            {
                sb.Append(" else ");
                sb.Append(EmitIf(nested, ctx, indent, leadIndent: false));
            }
            else
            {
                sb.AppendLine(" else {");
                sb.Append(EmitBranchBody(elseClause.Statement, ctx, childIndent));
                sb.Append(indent).AppendLine("}");
            }
        }
        else
        {
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string EmitBranchBody(StatementSyntax stmt, EmitContext ctx, string indent)
    {
        if (stmt is BlockSyntax block)
        {
            var sb = new StringBuilder();
            foreach (var s in block.Statements)
            {
                sb.Append(EmitStatement(s, ctx, indent));
            }
            return sb.ToString();
        }
        return EmitStatement(stmt, ctx, indent);
    }

    static string EmitExpression(ExpressionSyntax expr, EmitContext ctx)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                return EmitLiteral(lit);
            case IdentifierNameSyntax id:
                return ResolveIdentifierEmit(id, ctx);
            case ParenthesizedExpressionSyntax paren:
                return $"({EmitExpression(paren.Expression, ctx)})";
            case BinaryExpressionSyntax bin:
                return EmitBinary(bin, ctx);
            case PrefixUnaryExpressionSyntax pre:
                return $"{MapPrefixUnaryOperator(pre.OperatorToken)}{EmitExpression(pre.Operand, ctx)}";
            case PostfixUnaryExpressionSyntax post:
                return $"{EmitExpression(post.Operand, ctx)}{MapPostfixUnaryOperator(post.OperatorToken)}";
            case ConditionalExpressionSyntax cond:
                return $"{EmitExpression(cond.Condition, ctx)} ? {EmitExpression(cond.WhenTrue, ctx)} : {EmitExpression(cond.WhenFalse, ctx)}";
            case AssignmentExpressionSyntax assign:
                return EmitAssignment(assign, ctx);
            case InvocationExpressionSyntax inv:
                return EmitInvocation(inv, ctx);
            case MemberAccessExpressionSyntax member when member.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                {
                    // List<T>.Count / array.Length / Dictionary<K,V>.Count
                    // all map onto Object.keys(...).length or .length on the
                    // TS side. Translate the common .Count / .Length idiom
                    // so consumers can write idiomatic C#.
                    var receiverType = ctx.TypeOf(member.Expression);
                    var memberName = member.Name.Identifier.Text;
                    if (receiverType is not null)
                    {
                        if (memberName == "Count" && IsListLike(receiverType))
                        {
                            return $"{EmitExpression(member.Expression, ctx)}.length";
                        }
                        if (memberName == "Count" && IsDictionaryLike(receiverType))
                        {
                            return $"Object.keys({EmitExpression(member.Expression, ctx)}).length";
                        }
                        if (memberName == "Length" && receiverType is IArrayTypeSymbol)
                        {
                            return $"{EmitExpression(member.Expression, ctx)}.length";
                        }
                        // Math.PI / Math.E: keep the named TS constant so the
                        // emitted code reads naturally. Without this, the
                        // generic const-field inliner below would replace
                        // `Math.PI` with its literal value (`3.141592653589793`).
                        var receiverDisplay = receiverType.ToDisplayString();
                        if ((receiverDisplay == "System.Math" || receiverDisplay == "System.MathF")
                            && memberName is "PI" or "E" or "Tau")
                        {
                            return $"Math.{memberName}";
                        }
                    }
                    // Cross-class const reference: `OtherClass.SomeConst` inlines to
                    // the literal value via Roslyn's constant evaluation. Enum
                    // member access (`MyEnum.Foo`) is excluded — those keep the
                    // qualified form so the TS enum still indexes correctly.
                    if (ctx.SymbolFor(member) is IFieldSymbol field && field.IsConst &&
                        field.ContainingType?.TypeKind != TypeKind.Enum &&
                        ctx.TryGetConstantValue(member, out var constValue) &&
                        TryFormatTsLiteral(constValue, out var literal))
                    {
                        return literal;
                    }
                    return $"{EmitExpression(member.Expression, ctx)}.{memberName}";
                }
            case SwitchExpressionSyntax swx:
                return EmitSwitchExpression(swx, ctx);
            case ObjectCreationExpressionSyntax oce:
                {
                    // `new List<T>()` -> `[]`. v0.2 doesn't accept any other
                    // constructor invocation inside a [Transpile] body —
                    // consumers want the array-equivalent for collections,
                    // not arbitrary `new`.
                    if (oce.Type is GenericNameSyntax gName && gName.Identifier.Text == "List" &&
                        oce.ArgumentList is { Arguments.Count: 0 } or null)
                    {
                        return "[]";
                    }
                    if (oce.Type is ArrayTypeSyntax arrayType &&
                        oce.ArgumentList is { Arguments.Count: 0 } or null)
                    {
                        return $"new Array<{MapType(arrayType.ElementType, ctx)}>()";
                    }
                    if (TryEmitRecordConstruction(oce, ctx, out var recEmit))
                    {
                        return recEmit;
                    }
                    throw new NotSupportedException(
                        $"Unsupported `new` expression '{oce}'. Supported: `new List<T>()`, `new T[N]`, and constructor of a [Transpile]-marked positional record.");
                }
            case ImplicitObjectCreationExpressionSyntax ioc:
                {
                    // `new()` — target-typed. Resolve the type from the
                    // semantic model. Same restriction as the explicit form.
                    var typed = ctx.TypeOf(ioc);
                    if (typed is INamedTypeSymbol named && named.IsGenericType &&
                        named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
                    {
                        return "[]";
                    }
                    if (TryEmitImplicitRecordConstruction(ioc, ctx, out var iocEmit))
                    {
                        return iocEmit;
                    }
                    throw new NotSupportedException(
                        $"Unsupported target-typed `new()` of '{typed?.ToDisplayString() ?? "unknown"}'. Supported: `new List<T>()` and [Transpile] record `new()`.");
                }
            case ImplicitArrayCreationExpressionSyntax iac:
                {
                    var elems = iac.Initializer.Expressions
                        .Select(e => EmitExpression(e, ctx));
                    return "[" + string.Join(", ", elems) + "]";
                }
            case ArrayCreationExpressionSyntax ace:
                {
                    if (ace.Initializer is { } initializer)
                    {
                        var elems = initializer.Expressions
                            .Select(e => EmitExpression(e, ctx));
                        return "[" + string.Join(", ", elems) + "]";
                    }
                    throw new NotSupportedException(
                        $"Unsupported array `new` expression '{ace}'. Use an initializer (`new[] {{ a, b }}`) or `new T[0]` for empty.");
                }
            case ElementAccessExpressionSyntax ea:
                {
                    var indices = string.Join(", ",
                        ea.ArgumentList.Arguments.Select(a => EmitExpression(a.Expression, ctx)));
                    return $"{EmitExpression(ea.Expression, ctx)}[{indices}]";
                }
            case CastExpressionSyntax cast:
                return EmitCast(cast, ctx);
            case InterpolatedStringExpressionSyntax interp:
                return EmitInterpolatedString(interp, ctx);
            case TupleExpressionSyntax tupleExpr:
                return EmitTupleExpression(tupleExpr, ctx);
            default:
                throw new NotSupportedException($"Unsupported expression: {expr.Kind()}");
        }
    }

    // Match `var (a, b, c) = expr;` (and the typed form `(int a, int b) = expr;`).
    // C# parses this as ExpressionStatement(Assignment(DeclarationExpression(var, ParenVarDesign), rhs)).
    // Emit as a TS destructuring `const` declaration. When RHS has a named-tuple
    // type (e.g. `(int Face, int Level)`), the field names drive object-pattern
    // destructure; otherwise we fall back to positional array destructure.
    static bool TryEmitTupleDeconstructionDeclaration(AssignmentExpressionSyntax assign, EmitContext ctx, string indent, out string emit)
    {
        emit = string.Empty;
        if (!assign.OperatorToken.IsKind(SyntaxKind.EqualsToken)) return false;
        if (assign.Left is not DeclarationExpressionSyntax decl) return false;
        if (decl.Designation is not ParenthesizedVariableDesignationSyntax paren) return false;

        var locals = new List<string>(paren.Variables.Count);
        foreach (var v in paren.Variables)
        {
            switch (v)
            {
                case SingleVariableDesignationSyntax svd:
                    locals.Add(svd.Identifier.Text);
                    break;
                case DiscardDesignationSyntax:
                    locals.Add("_");
                    break;
                default:
                    return false;
            }
        }

        var rhs = EmitExpression(assign.Right, ctx);
        var tupleType = ctx.TypeOf(assign.Right) as INamedTypeSymbol;
        var sb = new StringBuilder();
        sb.Append(indent).Append("const ");
        if (tupleType is { IsTupleType: true } && HasNamedTupleElements(tupleType, locals.Count))
        {
            sb.Append("{ ");
            for (int i = 0; i < locals.Count; i++)
            {
                var field = tupleType.TupleElements[i].Name;
                if (string.IsNullOrEmpty(field)) field = $"Item{i + 1}";
                if (i > 0) sb.Append(", ");
                if (locals[i] == "_")
                {
                    sb.Append(field).Append(": _");
                }
                else if (field == locals[i])
                {
                    sb.Append(locals[i]);
                }
                else
                {
                    sb.Append(field).Append(": ").Append(locals[i]);
                }
            }
            sb.Append(" }");
        }
        else
        {
            sb.Append("[").Append(string.Join(", ", locals)).Append("]");
        }
        sb.Append(" = ").Append(rhs).AppendLine(";");
        emit = sb.ToString();
        return true;
    }

    static bool HasNamedTupleElements(INamedTypeSymbol tuple, int expectedCount)
    {
        if (tuple.TupleElements.Length != expectedCount) return false;
        foreach (var elem in tuple.TupleElements)
        {
            // For an unnamed tuple (`(int, int)`), the element's Name falls
            // back to the underlying ValueTuple field name `ItemN`. A named
            // tuple element has its declared name (`Face` etc.) here, while
            // the underlying field still reads `ItemN`.
            if (elem.Name.StartsWith("Item", StringComparison.Ordinal) &&
                int.TryParse(elem.Name.Substring(4), out _))
            {
                return false;
            }
        }
        return true;
    }

    static bool TryEmitRecordConstruction(ObjectCreationExpressionSyntax oce, EmitContext ctx, out string emit)
    {
        emit = string.Empty;
        if (oce.ArgumentList is null) return false;
        if (ctx.TypeOf(oce) is not INamedTypeSymbol named) return false;
        if (!IsTranspileType(named)) return false;
        var paramNames = GetPositionalRecordParamNames(named);
        if (paramNames is null) return false;
        var args = oce.ArgumentList.Arguments;
        if (paramNames.Count != args.Count) return false;
        var parts = new List<string>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            parts.Add($"{paramNames[i]}: {EmitExpression(args[i].Expression, ctx)}");
        }
        emit = "{ " + string.Join(", ", parts) + " }";
        return true;
    }

    static bool TryEmitImplicitRecordConstruction(ImplicitObjectCreationExpressionSyntax ioc, EmitContext ctx, out string emit)
    {
        emit = string.Empty;
        if (ctx.TypeOf(ioc) is not INamedTypeSymbol named) return false;
        if (!IsTranspileType(named)) return false;
        var paramNames = GetPositionalRecordParamNames(named);
        if (paramNames is null) return false;
        var args = ioc.ArgumentList.Arguments;
        if (paramNames.Count != args.Count) return false;
        var parts = new List<string>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            parts.Add($"{paramNames[i]}: {EmitExpression(args[i].Expression, ctx)}");
        }
        emit = "{ " + string.Join(", ", parts) + " }";
        return true;
    }

    // For identifier references, honour `[Transpile(EmitName="...")]` on the
    // resolved field symbol so that consts renamed for emit (e.g. to avoid
    // cross-class collisions on a common name like `MaxLevel`) flow through
    // body expressions consistently with their declaration.
    static string ResolveIdentifierEmit(IdentifierNameSyntax id, EmitContext ctx)
    {
        var raw = id.Identifier.Text;
        var sym = ctx.SymbolForIdentifier(id);
        if (sym is IFieldSymbol field)
        {
            foreach (var attr in field.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != "Mirrorgen.TranspileAttribute") continue;
                foreach (var kv in attr.NamedArguments)
                {
                    if (kv.Key == "EmitName" && kv.Value.Value is string s && !string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }
        }
        return raw;
    }

    static bool IsComputedProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.ExpressionBody is not null) return true;
        if (prop.AccessorList is null) return false;
        foreach (var acc in prop.AccessorList.Accessors)
        {
            if (acc.Kind() == SyntaxKind.GetAccessorDeclaration &&
                (acc.Body is not null || acc.ExpressionBody is not null))
            {
                return true;
            }
        }
        return false;
    }

    static bool IsTranspileType(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "Mirrorgen.TranspileAttribute") return true;
        }
        return false;
    }

    // Read positional record parameter names (which become interface fields).
    // Returns null when the symbol has no primary constructor we can read.
    static List<string>? GetPositionalRecordParamNames(INamedTypeSymbol type)
    {
        foreach (var sref in type.DeclaringSyntaxReferences)
        {
            if (sref.GetSyntax() is RecordDeclarationSyntax rec && rec.ParameterList is { } parameters)
            {
                var names = new List<string>(parameters.Parameters.Count);
                foreach (var p in parameters.Parameters)
                {
                    names.Add(p.Identifier.Text);
                }
                return names;
            }
        }
        return null;
    }

    static string EmitTupleExpression(TupleExpressionSyntax tupleExpr, EmitContext ctx)
    {
        // Element names come from one of two sources:
        //   1. Inline syntax `(IX: x, IY: y)` — read straight off the argument
        //   2. Target type (the method's declared return type, a local's
        //      declared tuple type, etc.) — read off Roslyn's converted type.
        // If neither names the elements, emit as a positional TS tuple
        // `[expr1, expr2]` and let the consumer decide.
        // ConvertedType is the target tuple — the literal's own inferred Type
        // borrows names from the argument identifiers (e.g. `(x, y)` becomes
        // `(int x, int y)` not `(int IX, int IY)`), which is the opposite of
        // what we want for return-position emit.
        var converted = ctx.ConvertedTypeOf(tupleExpr) as INamedTypeSymbol;
        var args = tupleExpr.Arguments;
        var fields = new List<string>(args.Count);
        bool anyNamed = false;

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? name = arg.NameColon?.Name.Identifier.Text;
            if (name is null && converted is { IsTupleType: true } &&
                i < converted.TupleElements.Length)
            {
                var ctf = converted.TupleElements[i];
                if (ctf.CorrespondingTupleField is { } field && field.Name == ctf.Name &&
                    field.Name.StartsWith("Item", StringComparison.Ordinal) &&
                    int.TryParse(field.Name.AsSpan(4), out _))
                {
                    // Default Item-N — leave as positional.
                }
                else
                {
                    name = ctf.Name;
                }
            }
            var valueExpr = EmitExpression(arg.Expression, ctx);
            if (name is not null)
            {
                anyNamed = true;
                fields.Add($"{name}: {valueExpr}");
            }
            else
            {
                fields.Add(valueExpr);
            }
        }

        if (anyNamed)
        {
            // Backfill positional entries with Item-N names so the TS object
            // shape stays consistent. If the consumer didn't name some args,
            // they get Item1/Item2/... so the output is still a valid object.
            for (int i = 0; i < fields.Count; i++)
            {
                if (!fields[i].Contains(':'))
                {
                    fields[i] = $"Item{i + 1}: {fields[i]}";
                }
            }
            return "{ " + string.Join(", ", fields) + " }";
        }
        return "[" + string.Join(", ", fields) + "]";
    }

    static string EmitInterpolatedString(InterpolatedStringExpressionSyntax interp, EmitContext ctx)
    {
        // C# $"…" → TS `…`. Each InterpolatedStringTextSyntax keeps the raw
        // text run; each InterpolationSyntax holds an expression.
        // Format clauses (`:F2` etc.) and alignment (`,5`) aren't supported
        // — they'd need explicit toFixed/padStart shims we don't have yet.
        var sb = new StringBuilder("`");
        foreach (var part in interp.Contents)
        {
            switch (part)
            {
                case InterpolatedStringTextSyntax text:
                    foreach (var ch in text.TextToken.ValueText)
                    {
                        switch (ch)
                        {
                            case '`': sb.Append("\\`"); break;
                            case '\\': sb.Append("\\\\"); break;
                            case '$': sb.Append("\\$"); break;
                            case '\n': sb.Append("\\n"); break;
                            case '\r': sb.Append("\\r"); break;
                            default: sb.Append(ch); break;
                        }
                    }
                    break;
                case InterpolationSyntax intr:
                    if (intr.AlignmentClause is not null)
                    {
                        throw new NotSupportedException(
                            "Interpolation alignment clauses (`,N`) are not supported.");
                    }
                    if (intr.FormatClause is { } fmt)
                    {
                        var expr = EmitExpression(intr.Expression, ctx);
                        var fmtEmit = ApplyInterpolationFormat(expr, fmt.FormatStringToken.ValueText, ctx.TypeOf(intr.Expression));
                        sb.Append("${").Append(fmtEmit).Append('}');
                    }
                    else
                    {
                        sb.Append("${").Append(EmitExpression(intr.Expression, ctx)).Append('}');
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported interpolated content: {part.Kind()}");
            }
        }
        sb.Append('`');
        return sb.ToString();
    }

    // Apply a C# composite-format specifier to a TS expression. Supports the
    // subset that actually appears in the OFF topology mirrors:
    //   X / X{N}  — uppercase hex, zero-padded to N digits
    //   x / x{N}  — lowercase hex, zero-padded to N digits
    //   D / D{N}  — base-10 with zero-padding
    // For BigInt sources we call `.toString(16)` directly; number sources go
    // via `Number(...).toString(16)` to match `ToString("X16")` on uint64. The
    // padding is exact to .NET's semantics: pad on the *unsigned* hex digits.
    static string ApplyInterpolationFormat(string expr, string format, ITypeSymbol? sourceType)
    {
        if (string.IsNullOrEmpty(format))
        {
            return expr;
        }
        var first = format[0];
        var digitsPart = format.Length > 1 ? format.Substring(1) : string.Empty;
        int padTo = 0;
        if (digitsPart.Length > 0 && !int.TryParse(digitsPart, NumberStyles.None, CultureInfo.InvariantCulture, out padTo))
        {
            throw new NotSupportedException(
                $"Unsupported interpolation format specifier '{format}'. Padding width must be a decimal integer.");
        }
        bool isBigInt = sourceType?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64;
        switch (first)
        {
            case 'X':
            case 'x':
                {
                    var hex = isBigInt
                        ? $"({expr}).toString(16)"
                        : $"Number({expr}).toString(16)";
                    if (first == 'X') hex = $"{hex}.toUpperCase()";
                    if (padTo > 0) hex = $"{hex}.padStart({padTo}, '0')";
                    return hex;
                }
            case 'D':
            case 'd':
                {
                    var dec = isBigInt
                        ? $"({expr}).toString()"
                        : $"String({expr})";
                    if (padTo > 0) dec = $"{dec}.padStart({padTo}, '0')";
                    return dec;
                }
            default:
                throw new NotSupportedException(
                    $"Unsupported interpolation format specifier '{format}'. Supported: X / x / D (with optional zero-pad width).");
        }
    }

    static string EmitCast(CastExpressionSyntax cast, EmitContext ctx)
    {
        var inner = EmitExpression(cast.Expression, ctx);
        var targetSymbol = ctx.SymbolForTypeSyntax(cast.Type);
        if (targetSymbol is null)
        {
            return $"({inner})";
        }
        var srcSpecial = ctx.TypeOf(cast.Expression)?.SpecialType ?? SpecialType.None;
        var srcIsBigInt = srcSpecial == SpecialType.System_Int64 || srcSpecial == SpecialType.System_UInt64;
        bool srcIsNumber =
            srcSpecial is SpecialType.System_Int32 or SpecialType.System_Int16
            or SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_UInt32 or SpecialType.System_UInt16
            or SpecialType.System_Single or SpecialType.System_Double;

        switch (targetSymbol.SpecialType)
        {
            case SpecialType.System_Byte:
                return srcIsBigInt
                    ? $"Number({inner} & 0xffn)"
                    : $"(({inner}) & 0xff)";
            case SpecialType.System_SByte:
                return srcIsBigInt
                    ? $"((Number({inner} & 0xffn) << 24) >> 24)"
                    : $"((({inner}) << 24) >> 24)";
            case SpecialType.System_Int16:
                return srcIsBigInt
                    ? $"((Number({inner} & 0xffffn) << 16) >> 16)"
                    : $"((({inner}) << 16) >> 16)";
            case SpecialType.System_UInt16:
                return srcIsBigInt
                    ? $"Number({inner} & 0xffffn)"
                    : $"(({inner}) & 0xffff)";
            case SpecialType.System_UInt32:
                // `(uint)x` reinterprets sign. JS `>>> 0` does the same:
                // forces operand into uint32 land before further arithmetic.
                // Without this, `(uint)-1 >= (uint)5` (true in C#) emits as
                // `-1 >= 5` (false in JS) — silent correctness break.
                return srcIsBigInt
                    ? $"Number(BigInt.asUintN(32, {inner}))"
                    : $"(({inner}) >>> 0)";
            case SpecialType.System_Int32:
                // `(int)ulong` / `(int)long` — collapse bigint back to a
                // number with the 32-bit i32 reinterpretation (matches C#
                // unchecked conversion). Without this, `(int)(1UL & x)`
                // emits a bigint that TS refuses to assign to `number`.
                if (srcIsBigInt)
                {
                    return $"Number(BigInt.asIntN(32, {inner}))";
                }
                return $"({inner})";
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                // `(ulong)int` / `(long)int` — promote a JS number into bigint
                // so subsequent shifts / bit ops stay in BigInt land. Without
                // this, `(ulong)(s * s) * (ulong)((3 * rx) ^ ry)` emits as a
                // number multiply (bigint × number is a TS type error).
                if (srcIsNumber)
                {
                    return $"BigInt({inner})";
                }
                return $"({inner})";
        }
        // User-declared enum target: TS strict mode refuses `number → MyEnum`
        // without an explicit cast. Emit `as EnumName` so callers that assign
        // the result to an enum-typed variable type-check.
        if (targetSymbol is INamedTypeSymbol enumTarget && enumTarget.TypeKind == TypeKind.Enum)
        {
            return $"({inner} as {enumTarget.Name})";
        }
        // Other casts (float/double) intentionally fall through to a
        // bare paren wrap for now — future issues extend this set.
        return $"({inner})";
    }

    static bool TryEmitRefInvocationStatement(
        InvocationExpressionSyntax inv, EmitContext ctx, string indent, out string emit)
    {
        emit = string.Empty;
        var target = ctx.InvocationTarget(inv);
        if (target is null) return false;
        // Pick out ref/out param positions on the callee.
        var refPositions = new List<int>();
        for (int i = 0; i < target.Parameters.Length; i++)
        {
            var refKind = target.Parameters[i].RefKind;
            if (refKind == RefKind.Ref || refKind == RefKind.Out)
            {
                refPositions.Add(i);
            }
        }
        if (refPositions.Count == 0) return false;

        // Build the destructure-LHS from the caller's argument expressions at
        // those positions. The caller side is e.g. `Rotate(s, ref x, ref y, rx, ry)`
        // — each ref/out arg is an IdentifierName we just lift verbatim.
        var lhs = new List<string>();
        foreach (var pos in refPositions)
        {
            if (pos >= inv.ArgumentList.Arguments.Count) return false;
            var argExpr = inv.ArgumentList.Arguments[pos].Expression;
            lhs.Add(EmitExpression(argExpr, ctx));
        }

        var callExpr = EmitExpression(inv, ctx);
        var isVoid = target.ReturnsVoid;
        if (isVoid)
        {
            if (lhs.Count == 1)
            {
                emit = $"{indent}{lhs[0]} = {callExpr};\n";
            }
            else
            {
                emit = $"{indent}[{string.Join(", ", lhs)}] = {callExpr};\n";
            }
            return true;
        }
        // Non-void ref-method called as a statement — original return is
        // discarded but ref values still need to land. Use a leading `_`.
        var positions = new List<string> { "_" };
        positions.AddRange(lhs);
        emit = $"{indent}[{string.Join(", ", positions)}] = {callExpr};\n";
        return true;
    }

    static string EmitInvocation(InvocationExpressionSyntax inv, EmitContext ctx)
    {
        var target = ctx.InvocationTarget(inv)
            ?? throw new NotSupportedException($"Cannot resolve target of invocation '{inv}'.");

        var args = string.Join(", ",
            inv.ArgumentList.Arguments.Select(a => EmitExpression(a.Expression, ctx)));

        if (TryMapMathInvocation(target, out var jsName))
        {
            var raw = $"Math.{jsName}({args})";
            // Math.Abs(int.MinValue) overflows JS Number land (returns 2^31)
            // while C# unchecked Abs returns int.MinValue. Wrap so both sides
            // produce the same int32 bit pattern.
            if (target.Name == "Abs" && ctx.IsInt32(inv))
            {
                return $"({raw} | 0)";
            }
            return raw;
        }

        if (TryMapMathDivergenceInvocation(inv, target, ctx, out var divEmit))
        {
            return divEmit;
        }

        if (TryMapDictionaryInvocation(inv, target, ctx, out var dictEmit))
        {
            return dictEmit;
        }

        if (TryMapListInvocation(inv, target, ctx, out var listEmit))
        {
            return listEmit;
        }

        if (!IsTranspileMethodSymbol(target))
        {
            throw new NotSupportedException(
                $"Method '{target.ContainingType?.Name}.{target.Name}' is not marked [Transpile]; calls outside the transpile boundary are not allowed.");
        }

        return $"{target.Name}({args})";
    }

    static bool TryMapMathDivergenceInvocation(InvocationExpressionSyntax inv, IMethodSymbol target, EmitContext ctx, out string emit)
    {
        emit = string.Empty;
        if (target.ContainingType is null) return false;
        var containing = target.ContainingType.ToDisplayString();
        if (containing != "System.Math" && containing != "System.MathF") return false;

        var argsList = inv.ArgumentList.Arguments;

        // Math.Truncate -> Math.trunc — identical semantics, just a name swap.
        if (target.Name == "Truncate" && argsList.Count == 1)
        {
            emit = $"Math.trunc({EmitExpression(argsList[0].Expression, ctx)})";
            return true;
        }

        if (target.Name == "Round")
        {
            // Math.Round(x) — default banker's rounding (ToEven).
            if (argsList.Count == 1)
            {
                ctx.UsedHelpers.Add("bankersRound");
                emit = $"__mirrorgen_bankersRound({EmitExpression(argsList[0].Expression, ctx)})";
                return true;
            }
            // Math.Round(x, MidpointRounding.AwayFromZero) or .ToEven.
            if (argsList.Count == 2 &&
                argsList[1].Expression is MemberAccessExpressionSyntax mode)
            {
                var modeName = mode.Name.Identifier.Text;
                if (modeName == "AwayFromZero")
                {
                    ctx.UsedHelpers.Add("awayFromZeroRound");
                    emit = $"__mirrorgen_awayFromZeroRound({EmitExpression(argsList[0].Expression, ctx)})";
                    return true;
                }
                if (modeName == "ToEven")
                {
                    ctx.UsedHelpers.Add("bankersRound");
                    emit = $"__mirrorgen_bankersRound({EmitExpression(argsList[0].Expression, ctx)})";
                    return true;
                }
                throw new NotSupportedException(
                    $"Unsupported MidpointRounding mode '{modeName}'. v0.2 supports ToEven and AwayFromZero.");
            }
            // The (double, int digits) overload would need a different
            // approximation; leave it for later rather than emit something
            // that almost works.
            throw new NotSupportedException(
                $"Math.Round overload with {argsList.Count} arg(s) is not supported in v0.2. Use the 1-arg form or pass MidpointRounding.AwayFromZero / ToEven.");
        }

        return false;
    }

    static bool TryMapListInvocation(InvocationExpressionSyntax inv, IMethodSymbol target, EmitContext ctx, out string emit)
    {
        emit = string.Empty;
        if (target.ContainingType is null) return false;
        var def = target.ContainingType.OriginalDefinition.ToDisplayString();
        if (def is not (
            "System.Collections.Generic.List<T>"
            or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.IReadOnlyList<T>"))
        {
            return false;
        }
        if (inv.Expression is not MemberAccessExpressionSyntax mae) return false;

        var receiver = EmitExpression(mae.Expression, ctx);
        var args = string.Join(", ",
            inv.ArgumentList.Arguments.Select(a => EmitExpression(a.Expression, ctx)));

        // List<T>.Add(x) → push, .Contains(x) → includes(x). Other members
        // (Remove / Insert / etc.) stay rejected for now — v0.2 starter
        // covers the two patterns sample code actually reaches for.
        if (target.Name == "Add" && inv.ArgumentList.Arguments.Count == 1)
        {
            emit = $"{receiver}.push({args})";
            return true;
        }
        if (target.Name == "Contains" && inv.ArgumentList.Arguments.Count == 1)
        {
            emit = $"{receiver}.includes({args})";
            return true;
        }
        return false;
    }

    static bool TryMapDictionaryInvocation(InvocationExpressionSyntax inv, IMethodSymbol target, EmitContext ctx, out string emit)
    {
        emit = string.Empty;
        if (target.ContainingType is null) return false;
        var def = target.ContainingType.OriginalDefinition.ToDisplayString();
        if (def is not (
            "System.Collections.Generic.Dictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
            or "System.Collections.Generic.IDictionary<TKey, TValue>"))
        {
            return false;
        }

        // ContainsKey(key) -> `(key in obj)` (TS-side membership test).
        // The C# member is a method call on an instance, so its expression
        // form is `<receiver>.ContainsKey(<arg>)` — we synthesise the TS
        // shape from the receiver of the member access.
        if (target.Name == "ContainsKey" && inv.Expression is MemberAccessExpressionSyntax mae &&
            inv.ArgumentList.Arguments.Count == 1)
        {
            var receiver = EmitExpression(mae.Expression, ctx);
            var key = EmitExpression(inv.ArgumentList.Arguments[0].Expression, ctx);
            emit = $"({key} in {receiver})";
            return true;
        }
        return false;
    }

    // System.Exception subclasses → JS error constructors. Anything not in
    // the map falls back to plain `Error`. Mirrorgen does not transpile
    // try/catch — these emit so callers that *do* handle errors on the TS
    // side (RangeError-aware code etc.) keep working through the boundary.
    static readonly System.Collections.Generic.Dictionary<string, string> ExceptionTypeMap = new(StringComparer.Ordinal)
    {
        { "ArgumentOutOfRangeException", "RangeError" },
        { "IndexOutOfRangeException", "RangeError" },
        { "OverflowException", "RangeError" },
        { "ArgumentNullException", "TypeError" },
        { "ArgumentException", "TypeError" },
        { "FormatException", "TypeError" },
        { "InvalidCastException", "TypeError" },
        { "InvalidOperationException", "Error" },
        { "NotSupportedException", "Error" },
    };

    static string MapExceptionType(string csName)
        => ExceptionTypeMap.TryGetValue(csName, out var js) ? js : "Error";

    static string EmitThrow(ThrowStatementSyntax thr, EmitContext ctx, string indent)
    {
        if (thr.Expression is not ObjectCreationExpressionSyntax oce)
        {
            // `throw;` (re-throw) — outside Mirrorgen's surface; only catch
            // blocks would emit it, and we don't transpile try/catch.
            throw new NotSupportedException("Bare `throw;` is not supported — Mirrorgen does not transpile try/catch.");
        }

        var jsError = oce.Type switch
        {
            IdentifierNameSyntax i => MapExceptionType(i.Identifier.Text),
            QualifiedNameSyntax q => MapExceptionType(q.Right.Identifier.Text),
            _ => "Error",
        };

        // Pick the first string-typed argument as the message. C# patterns
        // like `throw new ArgumentOutOfRangeException(nameof(x), $"x out of range")`
        // have nameof() as the first arg (parameter name) and the message
        // second — we want the latter on the TS side.
        string message = "\"\"";
        if (oce.ArgumentList is { } args)
        {
            foreach (var a in args.Arguments)
            {
                if (a.Expression is InvocationExpressionSyntax ie &&
                    ie.Expression is IdentifierNameSyntax id &&
                    id.Identifier.Text == "nameof")
                {
                    continue;
                }
                var argType = ctx.TypeOf(a.Expression);
                if (argType?.SpecialType == SpecialType.System_String ||
                    a.Expression is InterpolatedStringExpressionSyntax ||
                    a.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    message = EmitExpression(a.Expression, ctx);
                    break;
                }
            }
        }

        return $"{indent}throw new {jsError}({message});\n";
    }

    // System.Math / System.MathF members whose semantics we trust to be
    // bit-equivalent to JS Math.*. Methods that diverge (Round's banker's
    // rounding, Truncate's sign behaviour around -0, etc.) are intentionally
    // left out and will fall through to the "not [Transpile]" error.
    static readonly System.Collections.Generic.Dictionary<string, string> MathMemberMap = new(StringComparer.Ordinal)
    {
        { "Min", "min" },
        { "Max", "max" },
        { "Abs", "abs" },
        { "Floor", "floor" },
        { "Ceiling", "ceil" },
        { "Sign", "sign" },
        { "Sqrt", "sqrt" },
        { "Pow", "pow" },
        { "Log", "log" },
        { "Log2", "log2" },
        { "Log10", "log10" },
        { "Exp", "exp" },
        { "Sin", "sin" },
        { "Cos", "cos" },
        { "Tan", "tan" },
        { "Asin", "asin" },
        { "Acos", "acos" },
        { "Atan", "atan" },
        { "Atan2", "atan2" },
    };

    static bool TryMapMathInvocation(IMethodSymbol method, out string jsName)
    {
        jsName = string.Empty;
        if (method.ContainingType is null) return false;
        var containing = method.ContainingType.ToDisplayString();
        if (containing != "System.Math" && containing != "System.MathF") return false;
        return MathMemberMap.TryGetValue(method.Name, out jsName!);
    }

    static bool IsTranspileMethodSymbol(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "Mirrorgen.TranspileAttribute") return true;
        }
        // Class-level [Transpile] implicitly marks every static method inside
        // the class — including private helpers. Without this, callers of
        // private helpers (validateLevel, rotate, …) would still hit the
        // boundary-violation guard at emit time even after class-level seeding.
        var containing = method.ContainingType;
        if (containing is not null)
        {
            foreach (var attr in containing.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == "Mirrorgen.TranspileAttribute" &&
                    method.IsStatic) return true;
            }
        }
        return false;
    }

    static string MapAssignmentOperator(SyntaxToken op)
    {
        return op.Kind() switch
        {
            SyntaxKind.EqualsToken => "=",
            SyntaxKind.PlusEqualsToken => "+=",
            SyntaxKind.MinusEqualsToken => "-=",
            SyntaxKind.AsteriskEqualsToken => "*=",
            SyntaxKind.SlashEqualsToken => "/=",
            SyntaxKind.PercentEqualsToken => "%=",
            _ => throw new NotSupportedException($"Unsupported assignment operator: {op.Kind()}"),
        };
    }

    static string EmitAssignment(AssignmentExpressionSyntax assign, EmitContext ctx)
    {
        var op = assign.OperatorToken.Kind();
        // For int32 compound assignment, expand to `a = ((a op b) | 0)` (or
        // Math.imul for *) so the result wraps the same way C# unchecked
        // arithmetic does. Plain `=` and non-int targets pass through.
        if (op != SyntaxKind.EqualsToken && ctx.IsInt32(assign.Left))
        {
            var left = EmitExpression(assign.Left, ctx);
            var right = EmitExpression(assign.Right, ctx);
            string combined = op switch
            {
                SyntaxKind.PlusEqualsToken => $"(({left} + {right}) | 0)",
                SyntaxKind.MinusEqualsToken => $"(({left} - {right}) | 0)",
                SyntaxKind.AsteriskEqualsToken => $"Math.imul({left}, {right})",
                SyntaxKind.SlashEqualsToken => $"(({left} / {right}) | 0)",
                SyntaxKind.PercentEqualsToken => $"(({left} % {right}) | 0)",
                _ => throw new NotSupportedException($"Unsupported compound assignment: {op}"),
            };
            return $"{left} = {combined}";
        }
        // long / ulong compound assignment — BigInt.asIntN / asUintN wrap to
        // mimic the 64-bit C# overflow semantics.
        if (op != SyntaxKind.EqualsToken && (ctx.IsInt64(assign.Left) || ctx.IsUInt64(assign.Left)))
        {
            var left = EmitExpression(assign.Left, ctx);
            var right = EmitExpression(assign.Right, ctx);
            var wrap = ctx.IsInt64(assign.Left) ? "BigInt.asIntN(64" : "BigInt.asUintN(64";
            string binaryOp = op switch
            {
                SyntaxKind.PlusEqualsToken => "+",
                SyntaxKind.MinusEqualsToken => "-",
                SyntaxKind.AsteriskEqualsToken => "*",
                SyntaxKind.SlashEqualsToken => "/",
                SyntaxKind.PercentEqualsToken => "%",
                _ => throw new NotSupportedException($"Unsupported compound assignment: {op}"),
            };
            return $"{left} = {wrap}, {left} {binaryOp} {right})";
        }
        return $"{EmitExpression(assign.Left, ctx)} {MapAssignmentOperator(assign.OperatorToken)} {EmitExpression(assign.Right, ctx)}";
    }

    static string EmitBinary(BinaryExpressionSyntax bin, EmitContext ctx)
    {
        var op = MapBinaryOperator(bin.OperatorToken);
        var left = EmitExpression(bin.Left, ctx);
        var right = EmitExpression(bin.Right, ctx);

        // Wrap int32 arithmetic so JS truncating semantics match C#. `*` uses
        // Math.imul to avoid the float-mantissa overflow at ~2^53. The whole
        // wrap result is parenthesised because `|` binds looser than `<=` etc.
        // so `(a + b) | 0 <= c` would mis-parse as `(a + b) | (0 <= c)`.
        if (IsInt32Arithmetic(bin, ctx))
        {
            return op switch
            {
                "*" => $"Math.imul({left}, {right})",
                _ => $"(({left} {op} {right}) | 0)",
            };
        }

        // Bigint shift in C# allows `ulong << int` / `long << int`. TS BigInt
        // requires the shift count to also be bigint. Mid-emit promotion is
        // the only safe rewrite — caller's intent (shifting a bigint by an
        // int amount) survives identically.
        var kind = bin.OperatorToken.Kind();
        bool isShift = kind == SyntaxKind.LessThanLessThanToken ||
                       kind == SyntaxKind.GreaterThanGreaterThanToken;
        if (isShift)
        {
            var lhsType = ctx.TypeOf(bin.Left)?.SpecialType ?? SpecialType.None;
            var rhsType = ctx.TypeOf(bin.Right)?.SpecialType ?? SpecialType.None;
            bool lhsIsBigInt = lhsType is SpecialType.System_Int64 or SpecialType.System_UInt64;
            bool rhsIsNumber = rhsType is SpecialType.System_Int32 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_UInt32 or SpecialType.System_UInt16;
            if (lhsIsBigInt && rhsIsNumber)
            {
                right = $"BigInt({right})";
            }
        }

        // long / ulong → BigInt arithmetic with explicit asIntN(64) /
        // asUintN(64) wrap so a JS bigint mirrors C# unchecked semantics on
        // overflow. JS bigint is arbitrary-precision otherwise, so the wrap
        // is the only thing that constrains it back to 64-bit.
        if (IsInt64Arithmetic(bin, ctx))
        {
            return $"BigInt.asIntN(64, {left} {op} {right})";
        }
        if (IsUInt64Arithmetic(bin, ctx))
        {
            return $"BigInt.asUintN(64, {left} {op} {right})";
        }

        return $"{left} {op} {right}";
    }

    static bool IsInt64Arithmetic(BinaryExpressionSyntax bin, EmitContext ctx) =>
        IsIntegerArithmeticOperator(bin.OperatorToken.Kind()) && ctx.IsInt64(bin);

    static bool IsUInt64Arithmetic(BinaryExpressionSyntax bin, EmitContext ctx) =>
        IsIntegerArithmeticOperator(bin.OperatorToken.Kind()) && ctx.IsUInt64(bin);

    static bool IsIntegerArithmeticOperator(SyntaxKind k) =>
        k is SyntaxKind.PlusToken or SyntaxKind.MinusToken
          or SyntaxKind.AsteriskToken or SyntaxKind.SlashToken
          or SyntaxKind.PercentToken;

    static bool IsInt32Arithmetic(BinaryExpressionSyntax bin, EmitContext ctx)
    {
        var k = bin.OperatorToken.Kind();
        if (k != SyntaxKind.PlusToken && k != SyntaxKind.MinusToken &&
            k != SyntaxKind.AsteriskToken && k != SyntaxKind.SlashToken &&
            k != SyntaxKind.PercentToken)
        {
            return false;
        }
        return ctx.TypeOf(bin)?.SpecialType == SpecialType.System_Int32;
    }

    static string MapBinaryOperator(SyntaxToken op)
    {
        return op.Kind() switch
        {
            SyntaxKind.PlusToken => "+",
            SyntaxKind.MinusToken => "-",
            SyntaxKind.AsteriskToken => "*",
            SyntaxKind.SlashToken => "/",
            SyntaxKind.PercentToken => "%",
            SyntaxKind.LessThanToken => "<",
            SyntaxKind.GreaterThanToken => ">",
            SyntaxKind.LessThanEqualsToken => "<=",
            SyntaxKind.GreaterThanEqualsToken => ">=",
            SyntaxKind.EqualsEqualsToken => "===",
            SyntaxKind.ExclamationEqualsToken => "!==",
            SyntaxKind.AmpersandAmpersandToken => "&&",
            SyntaxKind.BarBarToken => "||",
            SyntaxKind.AmpersandToken => "&",
            SyntaxKind.BarToken => "|",
            SyntaxKind.CaretToken => "^",
            SyntaxKind.LessThanLessThanToken => "<<",
            SyntaxKind.GreaterThanGreaterThanToken => ">>",
            _ => throw new NotSupportedException($"Unsupported binary operator: {op.Kind()}"),
        };
    }

    static string MapPrefixUnaryOperator(SyntaxToken op)
    {
        return op.Kind() switch
        {
            SyntaxKind.MinusToken => "-",
            SyntaxKind.PlusToken => "+",
            SyntaxKind.ExclamationToken => "!",
            SyntaxKind.TildeToken => "~",
            _ => throw new NotSupportedException($"Unsupported unary operator: {op.Kind()}"),
        };
    }

    static string MapPostfixUnaryOperator(SyntaxToken op)
    {
        return op.Kind() switch
        {
            SyntaxKind.PlusPlusToken => "++",
            SyntaxKind.MinusMinusToken => "--",
            _ => throw new NotSupportedException($"Unsupported postfix unary operator: {op.Kind()}"),
        };
    }

    static string EmitLiteral(LiteralExpressionSyntax lit)
    {
        switch (lit.Token.Kind())
        {
            case SyntaxKind.NumericLiteralToken:
                {
                    var v = lit.Token.Value
                        ?? throw new NotSupportedException("Numeric literal has no value.");
                    // `5L` / `5UL` literals emit as TS bigint literals (`5n`).
                    if (v is long or ulong)
                    {
                        return $"{((IFormattable)v).ToString(null, CultureInfo.InvariantCulture)}n";
                    }
                    return v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v.ToString()!;
                }
            case SyntaxKind.StringLiteralToken:
                return lit.Token.Text;
            case SyntaxKind.TrueKeyword:
                return "true";
            case SyntaxKind.FalseKeyword:
                return "false";
            case SyntaxKind.NullKeyword:
                return "null";
            default:
                throw new NotSupportedException($"Unsupported literal: {lit.Token.Kind()}");
        }
    }
}
