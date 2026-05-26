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

        var sb = new StringBuilder();
        bool first = true;
        foreach (var method in tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!HasTranspileAttribute(method)) continue;
            if (!first) sb.AppendLine();
            sb.Append(EmitMethod(method, ctx));
            first = false;
        }
        return sb.ToString();
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
        var name = ReadEmitName(method) ?? method.Identifier.Text;
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

    static bool HasTranspileAttribute(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
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

    static string? ReadEmitName(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
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

    static string MapType(TypeSyntax type)
    {
        if (type is ArrayTypeSyntax arr)
        {
            return $"{MapType(arr.ElementType)}[]";
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
            _ => throw new NotSupportedException($"Unsupported type: {s}"),
        };
    }

    static string MapTypeSymbol(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
        {
            return $"{MapTypeSymbol(arr.ElementType)}[]";
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
            _ => throw new NotSupportedException($"Unsupported type: {type.ToDisplayString()}"),
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
            default:
                throw new NotSupportedException($"Unsupported literal: {lit.Token.Kind()}");
        }
    }
}
