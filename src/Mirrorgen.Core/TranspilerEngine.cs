using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mirrorgen.Core;

public static class TranspilerEngine
{
    public const string Version = "0.0.1-alpha";

    public static string TranspileSource(string csharpSource)
    {
        var tree = CSharpSyntaxTree.ParseText(csharpSource);
        var root = tree.GetCompilationUnitRoot();

        var sb = new StringBuilder();
        bool first = true;
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!HasTranspileAttribute(method)) continue;
            if (!first) sb.AppendLine();
            sb.Append(TranspileMethod(method));
            first = false;
        }
        return sb.ToString();
    }

    public static string TranspileMethod(MethodDeclarationSyntax method)
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
        sb.Append(EmitMethodBody(method));
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

    static string EmitMethodBody(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody is { } eb)
        {
            return $"  return {EmitExpression(eb.Expression)};\n";
        }
        if (method.Body is { } block)
        {
            var sb = new StringBuilder();
            foreach (var stmt in block.Statements)
            {
                sb.Append(EmitStatement(stmt));
            }
            return sb.ToString();
        }
        throw new NotSupportedException($"Method '{method.Identifier.Text}' has no body.");
    }

    static string EmitStatement(StatementSyntax stmt)
    {
        return stmt switch
        {
            ReturnStatementSyntax { Expression: null } => "  return;\n",
            ReturnStatementSyntax ret => $"  return {EmitExpression(ret.Expression!)};\n",
            _ => throw new NotSupportedException($"Unsupported statement: {stmt.Kind()}"),
        };
    }

    static string EmitExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                return EmitLiteral(lit);
            case IdentifierNameSyntax id:
                return id.Identifier.Text;
            case ParenthesizedExpressionSyntax paren:
                return $"({EmitExpression(paren.Expression)})";
            case BinaryExpressionSyntax bin:
                return $"{EmitExpression(bin.Left)} {MapBinaryOperator(bin.OperatorToken)} {EmitExpression(bin.Right)}";
            case PrefixUnaryExpressionSyntax pre:
                return $"{MapPrefixUnaryOperator(pre.OperatorToken)}{EmitExpression(pre.Operand)}";
            case ConditionalExpressionSyntax cond:
                return $"{EmitExpression(cond.Condition)} ? {EmitExpression(cond.WhenTrue)} : {EmitExpression(cond.WhenFalse)}";
            default:
                throw new NotSupportedException($"Unsupported expression: {expr.Kind()}");
        }
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
