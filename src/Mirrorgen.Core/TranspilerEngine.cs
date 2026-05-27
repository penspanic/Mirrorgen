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
    {
        var tree = CSharpSyntaxTree.ParseText(csharpSource);
        var compilation = CSharpCompilation.Create(
            assemblyName: "MirrorgenInput",
            syntaxTrees: new[] { tree },
            references: TrustedReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var ctx = new EmitContext(compilation.GetSemanticModel(tree));

        // Index every type/method declaration in the tree so the reachability
        // scan can resolve identifier references back to their declaration.
        var typeByName = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var methods = new List<MethodDeclarationSyntax>();
        foreach (var node in tree.GetCompilationUnitRoot().DescendantNodes())
        {
            switch (node)
            {
                case EnumDeclarationSyntax e: typeByName[e.Identifier.Text] = e; break;
                case RecordDeclarationSyntax r: typeByName[r.Identifier.Text] = r; break;
                case ClassDeclarationSyntax c: typeByName[c.Identifier.Text] = c; break;
                case StructDeclarationSyntax s: typeByName[s.Identifier.Text] = s; break;
                case MethodDeclarationSyntax m: methods.Add(m); break;
            }
        }

        // BFS reachability from every explicit [Transpile] entry point.
        var emit = new HashSet<SyntaxNode>();
        var queue = new Queue<SyntaxNode>();
        foreach (var node in typeByName.Values)
        {
            var attrs = TypeAttributeLists(node);
            if (HasTranspileAttribute(attrs))
            {
                if (emit.Add(node)) queue.Enqueue(node);
            }
        }
        foreach (var m in methods)
        {
            if (HasTranspileAttribute(m.AttributeLists))
            {
                if (emit.Add(m)) queue.Enqueue(m);
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
        }

        // Emit in declaration order so output is stable across runs.
        var sb = new StringBuilder();
        bool first = true;
        foreach (var member in tree.GetCompilationUnitRoot().DescendantNodes())
        {
            if (!emit.Contains(member)) continue;
            string? emitted = member switch
            {
                EnumDeclarationSyntax enumDecl => EmitEnum(enumDecl),
                RecordDeclarationSyntax rec => EmitTypeDeclaration(rec, ctx),
                ClassDeclarationSyntax cls => EmitTypeDeclaration(cls, ctx),
                StructDeclarationSyntax str => EmitTypeDeclaration(str, ctx),
                MethodDeclarationSyntax method => EmitMethod(method, ctx),
                _ => null,
            };
            if (emitted is null) continue;
            if (!first) sb.AppendLine();
            sb.Append(emitted);
            first = false;
        }
        return sb.ToString();
    }

    static SyntaxList<AttributeListSyntax> TypeAttributeLists(SyntaxNode node) => node switch
    {
        EnumDeclarationSyntax e => e.AttributeLists,
        BaseTypeDeclarationSyntax bt => bt.AttributeLists,
        _ => default,
    };

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
        public EmitContext(SemanticModel model) { _model = model; }

        public ITypeSymbol? TypeOf(ExpressionSyntax expr)
        {
            var info = _model.GetTypeInfo(expr);
            return info.Type ?? info.ConvertedType;
        }

        public bool IsInt32(ExpressionSyntax expr) =>
            TypeOf(expr)?.SpecialType == SpecialType.System_Int32;

        public ITypeSymbol? LocalTypeOf(VariableDeclaratorSyntax variable) =>
            (_model.GetDeclaredSymbol(variable) as ILocalSymbol)?.Type;

        public IMethodSymbol? InvocationTarget(InvocationExpressionSyntax inv) =>
            _model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
    }

    static readonly Lazy<MetadataReference[]> TrustedReferences = new(BuildTrustedReferences);

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

    static string EmitMethod(MethodDeclarationSyntax method, EmitContext ctx)
    {
        var name = ReadEmitName(method.AttributeLists) ?? method.Identifier.Text;
        var returnType = MapType(method.ReturnType);
        var parameters = string.Join(
            ", ",
            method.ParameterList.Parameters.Select(p =>
                p.Type is null
                    ? throw new NotSupportedException($"Parameter '{p.Identifier.Text}' has no type.")
                    : $"{p.Identifier.Text}: {MapType(p.Type)}"));

        var sb = new StringBuilder();
        sb.Append("export function ").Append(name).Append('(').Append(parameters).Append("): ").Append(returnType).AppendLine(" {");
        sb.Append(EmitMethodBody(method, ctx));
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
        var sb = new StringBuilder();
        sb.Append("export interface ").Append(name).AppendLine(" {");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Positional record parameters become the primary interface members.
        if (decl is RecordDeclarationSyntax rec && rec.ParameterList is { } parameters)
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
                sb.Append(BodyIndent).Append(member).Append(": ").Append(MapType(p.Type)).AppendLine(";");
            }
        }

        // Properties + fields declared in the body.
        foreach (var bodyMember in decl.Members)
        {
            switch (bodyMember)
            {
                case PropertyDeclarationSyntax prop:
                    {
                        var member = prop.Identifier.Text;
                        if (!seen.Add(member)) continue;
                        sb.Append(BodyIndent).Append(member).Append(": ").Append(MapType(prop.Type)).AppendLine(";");
                        break;
                    }
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var member = variable.Identifier.Text;
                        if (!seen.Add(member)) continue;
                        sb.Append(BodyIndent).Append(member).Append(": ").Append(MapType(field.Declaration.Type)).AppendLine(";");
                    }
                    break;
                // Methods / constructors / etc. on a [Transpile] type aren't part of
                // the v0.1 surface — silently skip rather than throw so consumers can
                // freely add server-side helpers next to the data shape.
            }
        }

        sb.AppendLine("}");
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

    static string MapType(TypeSyntax type)
    {
        if (type is ArrayTypeSyntax arr)
        {
            return $"{MapType(arr.ElementType)}[]";
        }
        if (type is NullableTypeSyntax nt)
        {
            return $"{MapType(nt.ElementType)} | null";
        }
        var s = type.ToString();
        return s switch
        {
            "int" or "long" or "short" or "byte" or "sbyte"
                or "uint" or "ulong" or "ushort"
                or "float" or "double" => "number",
            "bool" => "boolean",
            "string" => "string",
            "void" => "void",
            "decimal" or "char" or "object" or "dynamic"
                => throw new NotSupportedException($"Unsupported primitive type: {s}"),
            // Unknown identifier — assume it's a reference to another transpiled
            // type declared in the same compilation. The reachability scan
            // is what ultimately guarantees it ends up emitted.
            _ => s,
        };
    }

    static string MapTypeSymbol(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
        {
            return $"{MapTypeSymbol(arr.ElementType)}[]";
        }
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            return $"{MapTypeSymbol(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated))} | null";
        }
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol nullable && nullable.TypeArguments.Length == 1)
        {
            return $"{MapTypeSymbol(nullable.TypeArguments[0])} | null";
        }
        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_UInt16
                or SpecialType.System_Single or SpecialType.System_Double => "number",
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_String => "string",
            SpecialType.System_Void => "void",
            // Same fallback as MapType: assume reference to another transpiled type.
            _ => type.Name,
        };
    }

    const string BodyIndent = "  ";

    static string EmitMethodBody(MethodDeclarationSyntax method, EmitContext ctx)
    {
        if (method.ExpressionBody is { } eb)
        {
            return $"{BodyIndent}return {EmitExpression(eb.Expression, ctx)};\n";
        }
        if (method.Body is { } block)
        {
            var sb = new StringBuilder();
            foreach (var stmt in block.Statements)
            {
                sb.Append(EmitStatement(stmt, ctx, BodyIndent));
            }
            return sb.ToString();
        }
        throw new NotSupportedException($"Method '{method.Identifier.Text}' has no body.");
    }

    static string EmitStatement(StatementSyntax stmt, EmitContext ctx, string indent)
    {
        switch (stmt)
        {
            case ReturnStatementSyntax { Expression: null }:
                return $"{indent}return;\n";
            case ReturnStatementSyntax ret:
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
                return $"{indent}{EmitExpression(exprStmt.Expression, ctx)};\n";
            case ForStatementSyntax forStmt:
                return EmitForStatement(forStmt, ctx, indent);
            case ForEachStatementSyntax fe:
                return EmitForEachStatement(fe, ctx, indent);
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
                tsType = MapTypeSymbol(symbolType);
            }
            else
            {
                tsType = MapType(decl.Type);
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
        if (collectionType is not IArrayTypeSymbol)
        {
            throw new NotSupportedException(
                $"foreach only supports T[] in v0.1; got '{collectionType?.ToDisplayString() ?? "unknown"}'. Move LINQ / List<T> enumeration outside the transpile boundary.");
        }

        var collection = EmitExpression(fe.Expression, ctx);
        var sb = new StringBuilder();
        sb.Append(indent).Append("for (const ").Append(fe.Identifier.Text).Append(" of ").Append(collection).AppendLine(") {");
        sb.Append(EmitBranchBody(fe.Statement, ctx, indent + BodyIndent));
        sb.Append(indent).AppendLine("}");
        return sb.ToString();
    }

    static string EmitLocalDeclaration(LocalDeclarationStatementSyntax local, EmitContext ctx, string indent)
    {
        var declaration = local.Declaration;
        if (declaration.Variables.Count != 1)
        {
            throw new NotSupportedException("Multi-variable declarations are not yet supported.");
        }
        var variable = declaration.Variables[0];

        string tsType;
        if (declaration.Type.IsVar)
        {
            var symbolType = ctx.LocalTypeOf(variable)
                ?? throw new NotSupportedException($"Cannot resolve type of local '{variable.Identifier.Text}'.");
            tsType = MapTypeSymbol(symbolType);
        }
        else
        {
            tsType = MapType(declaration.Type);
        }

        var initEmit = variable.Initializer is { } init
            ? $" = {EmitExpression(init.Value, ctx)}"
            : string.Empty;
        return $"{indent}let {variable.Identifier.Text}: {tsType}{initEmit};\n";
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
                return id.Identifier.Text;
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
                return $"{EmitExpression(assign.Left, ctx)} {MapAssignmentOperator(assign.OperatorToken)} {EmitExpression(assign.Right, ctx)}";
            case InvocationExpressionSyntax inv:
                return EmitInvocation(inv, ctx);
            case MemberAccessExpressionSyntax member when member.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                return $"{EmitExpression(member.Expression, ctx)}.{member.Name.Identifier.Text}";
            default:
                throw new NotSupportedException($"Unsupported expression: {expr.Kind()}");
        }
    }

    static string EmitInvocation(InvocationExpressionSyntax inv, EmitContext ctx)
    {
        var target = ctx.InvocationTarget(inv)
            ?? throw new NotSupportedException($"Cannot resolve target of invocation '{inv}'.");

        if (!IsTranspileMethodSymbol(target))
        {
            throw new NotSupportedException(
                $"Method '{target.ContainingType?.Name}.{target.Name}' is not marked [Transpile]; calls outside the transpile boundary are not allowed.");
        }

        var args = string.Join(", ",
            inv.ArgumentList.Arguments.Select(a => EmitExpression(a.Expression, ctx)));
        return $"{target.Name}({args})";
    }

    static bool IsTranspileMethodSymbol(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "Mirrorgen.TranspileAttribute") return true;
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

        return $"{left} {op} {right}";
    }

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
                var v = lit.Token.Value
                    ?? throw new NotSupportedException("Numeric literal has no value.");
                return v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v.ToString()!;
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
