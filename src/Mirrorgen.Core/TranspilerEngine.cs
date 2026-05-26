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
        var name = method.Identifier.Text;
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
                var n = attr.Name.ToString();
                if (n == "Transpile" || n == "TranspileAttribute") return true;
                if (n.EndsWith(".Transpile", StringComparison.Ordinal)) return true;
                if (n.EndsWith(".TranspileAttribute", StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    static string MapType(TypeSyntax type)
    {
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

    static string EmitMethodBody(MethodDeclarationSyntax method, EmitContext ctx)
    {
        if (method.ExpressionBody is { } eb)
        {
            return $"  return {EmitExpression(eb.Expression, ctx)};\n";
        }
        if (method.Body is { } block)
        {
            var sb = new StringBuilder();
            foreach (var stmt in block.Statements)
            {
                sb.Append(EmitStatement(stmt, ctx));
            }
            return sb.ToString();
        }
        throw new NotSupportedException($"Method '{method.Identifier.Text}' has no body.");
    }

    static string EmitStatement(StatementSyntax stmt, EmitContext ctx)
    {
        return stmt switch
        {
            ReturnStatementSyntax { Expression: null } => "  return;\n",
            ReturnStatementSyntax ret => $"  return {EmitExpression(ret.Expression!, ctx)};\n",
            _ => throw new NotSupportedException($"Unsupported statement: {stmt.Kind()}"),
        };
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
            case ConditionalExpressionSyntax cond:
                return $"{EmitExpression(cond.Condition, ctx)} ? {EmitExpression(cond.WhenTrue, ctx)} : {EmitExpression(cond.WhenFalse, ctx)}";
            default:
                throw new NotSupportedException($"Unsupported expression: {expr.Kind()}");
        }
    }

    static string EmitBinary(BinaryExpressionSyntax bin, EmitContext ctx)
    {
        var op = MapBinaryOperator(bin.OperatorToken);
        var left = EmitExpression(bin.Left, ctx);
        var right = EmitExpression(bin.Right, ctx);

        // Wrap int32 arithmetic so JS truncating semantics match C#. `*` uses
        // Math.imul to avoid the float-mantissa overflow at ~2^53.
        if (IsInt32Arithmetic(bin, ctx))
        {
            return op switch
            {
                "*" => $"Math.imul({left}, {right})",
                _ => $"({left} {op} {right}) | 0",
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
