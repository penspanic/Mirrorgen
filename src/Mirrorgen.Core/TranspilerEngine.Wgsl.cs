using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Mirrorgen.Core;

/// <summary>Target surface language for a transpile pass.</summary>
public enum TranspileTarget
{
    /// <summary>TypeScript (the original, mature backend).</summary>
    TypeScript,

    /// <summary>WGSL — GPU shader source. Shares Mirrorgen's Roslyn front-end
    /// (parse, semantic model, [Transpile] entry selection) with the
    /// TypeScript backend but has its own emit walk: WGSL is a typed,
    /// GC-less, per-invocation language so the surface differs structurally
    /// (no ternary, arrays ride uniform/storage bindings, f32-only floats).</summary>
    Wgsl,
}

// WGSL backend. Lives in the TranspilerEngine partial so it can reuse the
// private EmitContext + the front-end helpers (HasTranspileAttribute,
// reference set, …) without widening their visibility. The TypeScript emit
// path is left completely untouched — a parallel emitter, not an in-place
// abstraction — so the TS edge-case suite cannot regress while WGSL grows.
public static partial class TranspilerEngine
{
    /// <summary>Transpile a single C# source string to WGSL. Emits every
    /// <c>[Transpile]</c> method reachable as an entry point in the source
    /// (method-level attribute, or public-static members of a class-level
    /// <c>[Transpile]</c> type). Tuple types referenced by those methods are
    /// emitted as named WGSL structs ahead of the functions.</summary>
    public static string TranspileSourceToWgsl(string csharpSource)
    {
        var tree = CSharpSyntaxTree.ParseText(csharpSource);
        var compilation = CSharpCompilation.Create(
            assemblyName: "MirrorgenWgslInput",
            syntaxTrees: new[] { tree },
            references: TrustedReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var module = new WgslModule();
        var bodies = new StringBuilder();
        bool first = true;
        EmitTreeIntoModule(tree, compilation, typeNames: null, module, bodies, ref first);
        return module.Preamble() + bodies;
    }

    /// <summary>Transpile a set of C# source files to a single WGSL module.
    /// All files share one compilation so cross-type const folding resolves
    /// against the real declarations (e.g. an encoding's tile-id constants).
    /// <paramref name="typeNames"/> restricts emission to methods whose
    /// containing type name is in the set — a build emits only the GPU-bound
    /// subset, leaving TypeScript-only <c>[Transpile]</c> types untouched.
    /// All emitted functions share one struct/binding preamble.</summary>
    public static string TranspileFilesToWgsl(IEnumerable<string> sourceFiles, IReadOnlyCollection<string> typeNames)
    {
        var trees = new List<SyntaxTree>();
        foreach (var file in sourceFiles)
        {
            var full = Path.GetFullPath(file);
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(full), path: full));
        }
        var compilation = CSharpCompilation.Create(
            assemblyName: "MirrorgenWgslBatch",
            syntaxTrees: trees,
            references: PublicTrustedReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var module = new WgslModule();
        var bodies = new StringBuilder();
        bool first = true;
        foreach (var tree in trees)
            EmitTreeIntoModule(tree, compilation, typeNames, module, bodies, ref first);
        return module.Preamble() + bodies;
    }

    // Emit every selected [Transpile] entry in one tree into a shared module,
    // resolving symbols against the (possibly multi-tree) compilation.
    static void EmitTreeIntoModule(
        SyntaxTree tree, Compilation compilation, IReadOnlyCollection<string>? typeNames,
        WgslModule module, StringBuilder bodies, ref bool first)
    {
        var entries = CollectWgslMethodEntries(tree, typeNames);
        if (entries.Count == 0) return;
        var ctx = new EmitContext(compilation.GetSemanticModel(tree), TypeMappingRegistry.Empty);
        foreach (var method in entries)
        {
            if (!first) bodies.AppendLine();
            bodies.Append(new WgslEmitter(ctx, module).EmitMethod(method));
            first = false;
        }
    }

    // Entry selection for WGSL: methods carrying [Transpile] directly, plus
    // public-static methods inside a class-level [Transpile] type. Emitted in
    // source order, de-duplicated.
    static List<MethodDeclarationSyntax> CollectWgslMethodEntries(
        SyntaxTree tree, IReadOnlyCollection<string>? typeNames)
    {
        // An empty/null set means "no filter" (emit every entry); otherwise a
        // method qualifies only if its containing type's name is listed. This
        // is what lets a project mix WGSL-bound types with TypeScript-only
        // [Transpile] types (e.g. const exports, expression-bodied helpers)
        // the WGSL emitter can't render.
        bool TypeAllowed(string? name) =>
            typeNames is null || typeNames.Count == 0 || (name is not null && typeNames.Contains(name));

        static string? ContainingTypeName(SyntaxNode node) =>
            node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;

        var seen = new HashSet<MethodDeclarationSyntax>();
        var ordered = new List<MethodDeclarationSyntax>();
        void Add(MethodDeclarationSyntax m)
        {
            if (seen.Add(m)) ordered.Add(m);
        }

        var root = tree.GetCompilationUnitRoot();
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (HasTranspileAttribute(m.AttributeLists) && TypeAllowed(ContainingTypeName(m))) Add(m);
        }
        foreach (var tds in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!HasTranspileAttribute(tds.AttributeLists)) continue;
            if (tds is not ClassDeclarationSyntax and not StructDeclarationSyntax and not RecordDeclarationSyntax) continue;
            if (!TypeAllowed(tds.Identifier.Text)) continue;
            foreach (var m in tds.Members.OfType<MethodDeclarationSyntax>())
            {
                if (HasNoTranspileAttribute(m.AttributeLists)) continue;
                if (IsPublicStaticMethod(m)) Add(m);
            }
        }
        return ordered;
    }

    // Module-level accumulator: struct definitions synthesized from C# tuple
    // shapes (WGSL has no anonymous structs, so every named tuple becomes a
    // top-of-file `struct`). Shared across all functions in the emit so a
    // tuple shape declares exactly once.
    sealed class WgslModule
    {
        readonly Dictionary<string, string> _structs = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _bindings = new(StringComparer.Ordinal);

        // Register a storage-buffer binding for an array parameter promoted by
        // [WgslBuffer]. Keyed by the binding variable name; a second binding
        // with the same name but a different declaration is a collision.
        public void AddBuffer(string name, int group, int binding, string elementWgslType)
        {
            var decl = $"@group({group}) @binding({binding}) var<storage, read> {name}: array<{elementWgslType}>;";
            if (_bindings.TryGetValue(name, out var existing))
            {
                if (existing != decl)
                    throw new NotSupportedException($"WGSL: buffer binding '{name}' declared with conflicting layouts.");
            }
            else
            {
                _bindings[name] = decl;
            }
        }

        // Register a named-tuple shape and return its WGSL struct name. The
        // name derives from the field names (R,G,B → MgTuple_RGB) so the
        // syntax-side (param/return types) and symbol-side (tuple literals /
        // element access) agree without threading a shared symbol through.
        public string RegisterTuple(IReadOnlyList<(string Name, string WgslType)> fields)
        {
            var name = "MgTuple_" + string.Concat(fields.Select(f => f.Name));
            var def = BuildStructDef(name, fields);
            if (_structs.TryGetValue(name, out var existing))
            {
                if (existing != def)
                    throw new NotSupportedException(
                        $"WGSL: tuple struct name collision for '{name}' with differing layouts.");
            }
            else
            {
                _structs[name] = def;
            }
            return name;
        }

        static string BuildStructDef(string name, IReadOnlyList<(string Name, string WgslType)> fields)
        {
            var sb = new StringBuilder();
            sb.Append("struct ").Append(name).AppendLine(" {");
            foreach (var (fieldName, wgslType) in fields)
                sb.Append("  ").Append(fieldName).Append(": ").Append(wgslType).AppendLine(",");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string Preamble()
        {
            if (_structs.Count == 0 && _bindings.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            // Structs first — buffer bindings may reference a struct element type.
            foreach (var def in _structs.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value))
            {
                sb.Append(def);
                sb.AppendLine();
            }
            foreach (var decl in _bindings.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value))
                sb.AppendLine(decl);
            if (_bindings.Count > 0) sb.AppendLine();
            return sb.ToString();
        }
    }

    // One instance per method emit — carries the per-method scope (which
    // locals are reassigned, so they emit as `var` not `let`).
    sealed class WgslEmitter
    {
        readonly EmitContext _ctx;
        readonly WgslModule _module;
        readonly HashSet<string> _mutatedLocals = new(StringComparer.Ordinal);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public WgslEmitter(EmitContext ctx, WgslModule module)
        {
            _ctx = ctx;
            _module = module;
        }

        public string EmitMethod(MethodDeclarationSyntax method)
        {
            if (method.Body is null)
                throw new NotSupportedException(
                    $"WGSL: expression-bodied / abstract method '{method.Identifier.Text}' not supported yet.");

            CollectMutatedLocals(method.Body);

            var name = ReadEmitName(method.AttributeLists) ?? method.Identifier.Text;

            // Partition parameters: [WgslBuffer] arrays become module-level
            // storage bindings (dropped from the signature); everything else
            // is a by-value fn argument. A bare array param with no [WgslBuffer]
            // is an error — WGSL can't take an array argument by value.
            var sigParams = new List<string>();
            foreach (var p in method.ParameterList.Parameters)
            {
                if (p.Type is null)
                    throw new NotSupportedException($"WGSL: parameter '{p.Identifier.Text}' has no type.");

                if (TryReadWgslBuffer(p, out var group, out var binding))
                {
                    if (p.Type is not ArrayTypeSyntax arrType)
                        throw new NotSupportedException($"WGSL: [WgslBuffer] parameter '{p.Identifier.Text}' must be an array type.");
                    _module.AddBuffer(p.Identifier.Text, group, binding, MapType(arrType.ElementType));
                    continue;
                }

                if (p.Type is ArrayTypeSyntax)
                    throw new NotSupportedException(
                        $"WGSL: array parameter '{p.Identifier.Text}' must be marked [WgslBuffer] (WGSL has no by-value array args).");

                sigParams.Add($"{p.Identifier.Text}: {MapType(p.Type)}");
            }
            var pars = string.Join(", ", sigParams);

            var sb = new StringBuilder();
            var isVoid = method.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);
            var retWgsl = isVoid ? null : MapType(method.ReturnType);
            sb.Append("fn ").Append(name).Append('(').Append(pars).Append(')');
            if (retWgsl is not null) sb.Append(" -> ").Append(retWgsl);
            sb.AppendLine(" {");
            foreach (var stmt in method.Body.Statements)
                EmitStatement(stmt, 1, sb, retWgsl);
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── Type mapping ──────────────────────────────────────────────────
        // WGSL scalars: f32 (no f64 in core), i32, u32, bool. C# double/float
        // both collapse to f32. Named tuples become module-level structs.
        string MapType(TypeSyntax type)
        {
            if (type is TupleTypeSyntax tuple)
                return RegisterTupleType(tuple);

            var text = type.ToString();
            return text switch
            {
                "double" or "float" => "f32",
                "int" or "short" or "sbyte" => "i32",
                "byte" or "ushort" or "uint" => "u32",
                "bool" => "bool",
                _ => throw new NotSupportedException($"WGSL: type '{text}' not supported yet (W3 arrays/structs)."),
            };
        }

        string RegisterTupleType(TupleTypeSyntax tuple)
        {
            var fields = new List<(string, string)>();
            foreach (var el in tuple.Elements)
            {
                if (el.Identifier.Text.Length == 0)
                    throw new NotSupportedException("WGSL: positional (unnamed) tuples not supported — name the fields.");
                fields.Add((el.Identifier.Text, MapType(el.Type)));
            }
            return _module.RegisterTuple(fields);
        }

        // Resolve a tuple *type symbol* (from semantic info on an expression)
        // to its WGSL struct name, registering it. Used for tuple literals and
        // element access where there's no TupleTypeSyntax to read.
        string RegisterTupleSymbol(INamedTypeSymbol tupleType)
        {
            var fields = new List<(string, string)>();
            foreach (var el in tupleType.TupleElements)
                fields.Add((el.Name, WgslScalar(el.Type) ?? throw new NotSupportedException(
                    $"WGSL: tuple element '{el.Name}' has unsupported type '{el.Type}'.")));
            return _module.RegisterTuple(fields);
        }

        // ── Statements ────────────────────────────────────────────────────
        void EmitStatement(StatementSyntax stmt, int depth, StringBuilder sb, string? retWgsl)
        {
            var pad = Indent(depth);
            switch (stmt)
            {
                case BlockSyntax block:
                    foreach (var s in block.Statements) EmitStatement(s, depth, sb, retWgsl);
                    return;

                case LocalDeclarationStatementSyntax local:
                {
                    // `var c = …` — resolve the inferred type per declarator;
                    // explicit types map directly.
                    var explicitTy = local.Declaration.Type.IsVar ? null : MapType(local.Declaration.Type);
                    foreach (var v in local.Declaration.Variables)
                    {
                        var kw = _mutatedLocals.Contains(v.Identifier.Text) ? "var" : "let";
                        var ty = explicitTy ?? MapResolvedType(
                            _ctx.LocalTypeOf(v) ?? throw new NotSupportedException($"WGSL: cannot infer type of 'var {v.Identifier.Text}'."));
                        if (v.Initializer is null)
                        {
                            // Uninitialized C# local, assigned later in branches.
                            // WGSL function-scope `var x: T;` is zero-initialized;
                            // `let` requires an initializer, so this must be `var`.
                            sb.Append(pad).Append("var ").Append(v.Identifier.Text)
                              .Append(": ").Append(ty).AppendLine(";");
                            continue;
                        }
                        sb.Append(pad).Append(kw).Append(' ').Append(v.Identifier.Text)
                          .Append(": ").Append(ty).Append(" = ")
                          .Append(EmitConverted(v.Initializer.Value, ty)).AppendLine(";");
                    }
                    return;
                }

                case ReturnStatementSyntax ret:
                    if (ret.Expression is null) { sb.Append(pad).AppendLine("return;"); return; }
                    sb.Append(pad).Append("return ").Append(EmitConverted(ret.Expression, retWgsl)).AppendLine(";");
                    return;

                case ExpressionStatementSyntax exprStmt:
                    sb.Append(pad).Append(EmitExpression(exprStmt.Expression)).AppendLine(";");
                    return;

                case IfStatementSyntax ifStmt:
                    EmitIf(ifStmt, depth, sb, retWgsl);
                    return;

                case ForStatementSyntax forStmt:
                    EmitFor(forStmt, depth, sb, retWgsl);
                    return;

                default:
                    throw new NotSupportedException($"WGSL: statement '{stmt.Kind()}' not supported yet.");
            }
        }

        void EmitIf(IfStatementSyntax ifStmt, int depth, StringBuilder sb, string? retWgsl)
        {
            var pad = Indent(depth);
            sb.Append(pad).Append("if (").Append(EmitExpression(ifStmt.Condition)).AppendLine(") {");
            EmitStatement(ifStmt.Statement, depth + 1, sb, retWgsl);
            sb.Append(pad).Append('}');
            if (ifStmt.Else is { } elseClause)
            {
                if (elseClause.Statement is IfStatementSyntax)
                {
                    sb.Append(" else ");
                    var inner = new StringBuilder();
                    EmitIf((IfStatementSyntax)elseClause.Statement, depth, inner, retWgsl);
                    sb.Append(inner.ToString().TrimStart());
                    return;
                }
                sb.AppendLine(" else {");
                EmitStatement(elseClause.Statement, depth + 1, sb, retWgsl);
                sb.Append(pad).AppendLine("}");
                return;
            }
            sb.AppendLine();
        }

        void EmitFor(ForStatementSyntax forStmt, int depth, StringBuilder sb, string? retWgsl)
        {
            var pad = Indent(depth);
            // Initializer — a single local decl (the common `for (int i = …)`).
            if (forStmt.Declaration is null || forStmt.Declaration.Variables.Count != 1)
                throw new NotSupportedException("WGSL: only single-variable for-init supported.");
            var initVar = forStmt.Declaration.Variables[0];
            var initType = MapType(forStmt.Declaration.Type);
            var init = $"var {initVar.Identifier.Text}: {initType} = "
                     + EmitConverted(initVar.Initializer!.Value, initType);
            var cond = forStmt.Condition is null ? "true" : EmitExpression(forStmt.Condition);
            if (forStmt.Incrementors.Count != 1)
                throw new NotSupportedException("WGSL: exactly one for-incrementor supported.");
            var incr = EmitForIncrementor(forStmt.Incrementors[0]);

            sb.Append(pad).Append("for (").Append(init).Append("; ").Append(cond).Append("; ").Append(incr).AppendLine(") {");
            EmitStatement(forStmt.Statement, depth + 1, sb, retWgsl);
            sb.Append(pad).AppendLine("}");
        }

        // WGSL has no `i++`; lower to `i = i + 1` (and `i += k` to `i = i + k`).
        string EmitForIncrementor(ExpressionSyntax incr)
        {
            switch (incr)
            {
                case PostfixUnaryExpressionSyntax post when post.IsKind(SyntaxKind.PostIncrementExpression):
                    return EmitExpression(post.Operand) + " = " + EmitExpression(post.Operand) + " + 1";
                case PostfixUnaryExpressionSyntax post when post.IsKind(SyntaxKind.PostDecrementExpression):
                    return EmitExpression(post.Operand) + " = " + EmitExpression(post.Operand) + " - 1";
                case PrefixUnaryExpressionSyntax pre when pre.IsKind(SyntaxKind.PreIncrementExpression):
                    return EmitExpression(pre.Operand) + " = " + EmitExpression(pre.Operand) + " + 1";
                case PrefixUnaryExpressionSyntax pre when pre.IsKind(SyntaxKind.PreDecrementExpression):
                    return EmitExpression(pre.Operand) + " = " + EmitExpression(pre.Operand) + " - 1";
                case AssignmentExpressionSyntax asg when asg.IsKind(SyntaxKind.AddAssignmentExpression):
                    return EmitExpression(asg.Left) + " = " + EmitExpression(asg.Left) + " + " + EmitExpression(asg.Right);
                case AssignmentExpressionSyntax asg when asg.IsKind(SyntaxKind.SubtractAssignmentExpression):
                    return EmitExpression(asg.Left) + " = " + EmitExpression(asg.Left) + " - " + EmitExpression(asg.Right);
                default:
                    throw new NotSupportedException($"WGSL: for-incrementor '{incr.Kind()}' not supported.");
            }
        }

        // ── Expressions ───────────────────────────────────────────────────
        string EmitExpression(ExpressionSyntax expr)
        {
            switch (expr)
            {
                case LiteralExpressionSyntax lit:
                    return EmitLiteral(lit);

                case IdentifierNameSyntax id:
                    return id.Identifier.Text;

                case ParenthesizedExpressionSyntax paren:
                    return "(" + EmitExpression(paren.Expression) + ")";

                case MemberAccessExpressionSyntax member:
                    // Cross-type const reference (e.g. an encoding's BedrockTileId)
                    // folds to its literal value — WGSL has no external named
                    // constants. Array .Length is not a constant, so it falls
                    // through to the arrayLength lowering below; tuple/struct
                    // field access (a.R) likewise has no constant value.
                    if (_ctx.TryGetConstantValue(member, out var memberConst) && memberConst is not null)
                        return EmitConstant(memberConst, _ctx.TypeOf(member));
                    // Array .Length → arrayLength(&buf). C# Array.Length is int,
                    // so coerce the u32 WGSL builtin back to i32 to match the
                    // surrounding integer arithmetic.
                    if (member.Name.Identifier.Text == "Length" && _ctx.TypeOf(member.Expression) is IArrayTypeSymbol)
                        return "i32(arrayLength(&" + EmitExpression(member.Expression) + "))";
                    // Tuple element / struct field access: a.R → a.R.
                    return EmitExpression(member.Expression) + "." + member.Name.Identifier.Text;

                case ElementAccessExpressionSyntax elem:
                    if (elem.ArgumentList.Arguments.Count != 1)
                        throw new NotSupportedException("WGSL: only single-index element access supported.");
                    return EmitExpression(elem.Expression) + "[" + EmitExpression(elem.ArgumentList.Arguments[0].Expression) + "]";

                case InvocationExpressionSyntax inv:
                    return EmitInvocation(inv);

                case TupleExpressionSyntax tupleLit:
                    return EmitTupleLiteral(tupleLit);

                case CastExpressionSyntax cast:
                    return EmitCast(cast);

                case PrefixUnaryExpressionSyntax pre:
                    return pre.OperatorToken.Text + EmitExpression(pre.Operand);

                case BinaryExpressionSyntax bin:
                    return EmitOperand(bin.Left) + " " + bin.OperatorToken.Text + " " + EmitOperand(bin.Right);

                case AssignmentExpressionSyntax asg:
                    return EmitExpression(asg.Left) + " " + asg.OperatorToken.Text + " " + EmitExpression(asg.Right);

                case ConditionalExpressionSyntax cond:
                    // WGSL has no ternary — lower to select(false, true, cond).
                    return "select(" + EmitExpression(cond.WhenFalse) + ", "
                         + EmitExpression(cond.WhenTrue) + ", "
                         + EmitExpression(cond.Condition) + ")";

                default:
                    throw new NotSupportedException($"WGSL: expression '{expr.Kind()}' not supported yet.");
            }
        }

        string EmitTupleLiteral(TupleExpressionSyntax tupleLit)
        {
            // The literal's converted type carries the target field names (R,G,B)
            // even when the arguments are bare locals (r, g, bl).
            if (_ctx.ConvertedTypeOf(tupleLit) is not INamedTypeSymbol named || !named.IsTupleType)
                throw new NotSupportedException("WGSL: tuple literal with no resolvable tuple type.");
            var structName = RegisterTupleSymbol(named);
            var args = string.Join(", ", tupleLit.Arguments.Select(a => EmitExpression(a.Expression)));
            return structName + "(" + args + ")";
        }

        // C# numeric cast → WGSL conversion. Integer narrowing to byte mirrors
        // C# `(byte)`: truncate toward zero (WGSL u32() does), then low 8 bits.
        string EmitCast(CastExpressionSyntax cast)
        {
            var inner = EmitExpression(cast.Expression);
            var t = cast.Type.ToString();
            return t switch
            {
                "byte" => $"(u32({inner}) & 0xffu)",
                "sbyte" => $"(i32({inner}) & 0xff)",
                "ushort" => $"(u32({inner}) & 0xffffu)",
                "short" => $"(i32({inner}) & 0xffff)",
                "uint" => $"u32({inner})",
                "int" => $"i32({inner})",
                "float" or "double" => $"f32({inner})",
                _ => throw new NotSupportedException($"WGSL: cast to '{t}' not supported yet."),
            };
        }

        // Call to another transpiled function. Arguments are converted to the
        // callee's parameter scalar types (C# would coerce them implicitly).
        // [WgslBuffer] params are bindings, not arguments — they're skipped.
        string EmitInvocation(InvocationExpressionSyntax inv)
        {
            var calleeName = inv.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => throw new NotSupportedException($"WGSL: call target '{inv.Expression.Kind()}' not supported."),
            };
            var target = _ctx.InvocationTarget(inv);
            var args = new List<string>();
            var argList = inv.ArgumentList.Arguments;
            for (int i = 0; i < argList.Count; i++)
            {
                // Arguments bound to the callee's [WgslBuffer] params are dropped:
                // those parameters are module-scope storage bindings (not values)
                // in both the callee signature and at the call site. Passing the
                // binding as a value argument is what naga rejects.
                if (target is not null && i < target.Parameters.Length && IsWgslBufferParam(target.Parameters[i]))
                    continue;
                var argExpr = argList[i].Expression;
                var paramWgsl = target is not null && i < target.Parameters.Length
                    ? WgslScalar(target.Parameters[i].Type)
                    : null;
                args.Add(EmitConverted(argExpr, paramWgsl));
            }
            return calleeName + "(" + string.Join(", ", args) + ")";
        }

        static bool IsWgslBufferParam(IParameterSymbol p)
        {
            foreach (var a in p.GetAttributes())
                if (a.AttributeClass?.Name is "WgslBufferAttribute" or "WgslBuffer")
                    return true;
            return false;
        }

        // Resolve a semantic type symbol (from `var` inference) to its WGSL
        // type: named tuple → struct, scalar → f32/i32/u32.
        string MapResolvedType(ITypeSymbol t)
        {
            if (t is INamedTypeSymbol { IsTupleType: true } tuple)
                return RegisterTupleSymbol(tuple);
            return WgslScalar(t) ?? throw new NotSupportedException($"WGSL: cannot map inferred type '{t}'.");
        }

        bool TryReadWgslBuffer(ParameterSyntax p, out int group, out int binding)
        {
            group = 0;
            binding = 0;
            foreach (var list in p.AttributeLists)
            {
                foreach (var attr in list.Attributes)
                {
                    var n = attr.Name.ToString();
                    var last = n.Contains('.') ? n[(n.LastIndexOf('.') + 1)..] : n;
                    if (last is not ("WgslBuffer" or "WgslBufferAttribute")) continue;
                    if (attr.ArgumentList is { } al)
                    {
                        foreach (var arg in al.Arguments)
                        {
                            var member = arg.NameEquals?.Name.Identifier.Text;
                            if (member is null) continue;
                            if (_ctx.TryGetConstantValue(arg.Expression, out var v) && v is int iv)
                            {
                                if (member == "Group") group = iv;
                                else if (member == "Binding") binding = iv;
                            }
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        // Emit a binary operand, inserting the explicit conversion C# performed
        // implicitly. WGSL has no numeric promotion, so `byte - byte` (which C#
        // widens to int) must become `i32(b) - i32(a)`, and an int operand in a
        // double expression must become `f32(int)`. Roslyn's ConvertedType is
        // exactly the promoted type C# coerced the operand to.
        string EmitOperand(ExpressionSyntax op)
        {
            var s = EmitExpression(op);
            var natural = WgslScalar(_ctx.TypeOf(op));
            var converted = WgslScalar(_ctx.ConvertedTypeOf(op));
            if (natural is not null && converted is not null && natural != converted)
                return converted + "(" + s + ")";
            return s;
        }

        // Convert an expression to a known target WGSL scalar type (local-init,
        // return). No-op for matching / non-numeric (struct) values.
        string EmitConverted(ExpressionSyntax expr, string? targetWgsl)
        {
            var s = EmitExpression(expr);
            if (targetWgsl is null) return s;
            var natural = WgslScalar(_ctx.TypeOf(expr));
            if (natural is not null && (targetWgsl is "f32" or "i32" or "u32") && natural != targetWgsl)
                return targetWgsl + "(" + s + ")";
            return s;
        }

        string EmitLiteral(LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) return "true";
            if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) return "false";

            if (_ctx.TryGetConstantValue(lit, out var v) && v is not null)
                return EmitConstant(v, _ctx.TypeOf(lit));
            throw new NotSupportedException($"WGSL: literal '{lit.Token.Text}' not supported yet.");
        }

        // Format a compile-time constant value (literal or folded const member)
        // as a WGSL literal. `ty` is the expression's C# type so an integer
        // constant assigned into a float context still emits with a decimal
        // point. Integer-suffix rules mirror EmitLiteral's scalar mapping
        // (u32 family gets the `u` suffix; i32 family is bare).
        string EmitConstant(object v, ITypeSymbol? ty)
        {
            switch (v)
            {
                case bool bo: return bo ? "true" : "false";
                case double d: return FormatFloat(d);
                case float f: return FormatFloat(f);
                case int i: return IsFloatType(ty) ? FormatFloat(i) : i.ToString(Inv);
                case long l: return IsFloatType(ty) ? FormatFloat(l) : l.ToString(Inv);
                case short sh: return IsFloatType(ty) ? FormatFloat(sh) : ((int)sh).ToString(Inv);
                case sbyte sb: return IsFloatType(ty) ? FormatFloat(sb) : ((int)sb).ToString(Inv);
                case uint ui: return ui.ToString(Inv) + "u";
                case ushort us: return ((uint)us).ToString(Inv) + "u";
                case byte b: return ((uint)b).ToString(Inv) + "u";
            }
            throw new NotSupportedException($"WGSL: constant of type '{v.GetType().Name}' not supported yet.");
        }

        // WGSL scalar name for a C# numeric type, or null if not a supported
        // scalar (structs / arrays / bool return null — conversions skip them).
        static string? WgslScalar(ITypeSymbol? t) => t?.SpecialType switch
        {
            SpecialType.System_Double or SpecialType.System_Single => "f32",
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_SByte => "i32",
            SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 => "u32",
            _ => null,
        };

        static bool IsFloatType(ITypeSymbol? ty) =>
            ty?.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

        // WGSL float literals need a decimal point (or exponent) or they parse
        // as abstract-int and fail to unify with f32. Round-trip format, then
        // ensure a fractional part.
        static string FormatFloat(double d)
        {
            var s = d.ToString("R", Inv);
            if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0
                && s.IndexOf("Inf", StringComparison.Ordinal) < 0
                && s.IndexOf("NaN", StringComparison.Ordinal) < 0)
            {
                s += ".0";
            }
            return s;
        }

        // ── Scope analysis ────────────────────────────────────────────────
        // A local that is the target of any assignment (=, +=, …) or ++/--
        // after its declaration must be a WGSL `var`; everything else is `let`.
        void CollectMutatedLocals(SyntaxNode body)
        {
            foreach (var node in body.DescendantNodes())
            {
                switch (node)
                {
                    case AssignmentExpressionSyntax { Left: IdentifierNameSyntax id }:
                        _mutatedLocals.Add(id.Identifier.Text);
                        break;
                    case PrefixUnaryExpressionSyntax { Operand: IdentifierNameSyntax pid } pre
                        when pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression):
                        _mutatedLocals.Add(pid.Identifier.Text);
                        break;
                    case PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax pid }:
                        _mutatedLocals.Add(pid.Identifier.Text);
                        break;
                }
            }
        }

        static string Indent(int depth) => new string(' ', depth * 2);
    }
}
