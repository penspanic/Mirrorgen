using System.Reflection;
using Mirrorgen.Core;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

switch (args[0])
{
    case "--version":
    case "-v":
        Console.WriteLine(TranspilerEngine.Version);
        return 0;

    case "transpile":
        return RunTranspile(args.AsSpan(1));

    case "fixtures":
        return RunFixtures(args.AsSpan(1));

    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        return 2;
}

static int RunTranspile(ReadOnlySpan<string> rest)
{
    string? inputPath = null;
    string? outputPath = null;

    for (int i = 0; i < rest.Length; i++)
    {
        var a = rest[i];
        switch (a)
        {
            case "-o":
            case "--out":
                if (i + 1 >= rest.Length)
                {
                    Console.Error.WriteLine($"{a} requires a path argument.");
                    return 2;
                }
                outputPath = rest[++i];
                break;
            default:
                if (inputPath is not null)
                {
                    Console.Error.WriteLine($"Unexpected argument: {a}");
                    return 2;
                }
                inputPath = a;
                break;
        }
    }

    if (inputPath is null)
    {
        Console.Error.WriteLine("transpile: missing input file or directory.");
        return 2;
    }

    if (Directory.Exists(inputPath))
    {
        if (outputPath is null)
        {
            Console.Error.WriteLine("transpile: -o <out-dir> is required when input is a directory.");
            return 2;
        }
        var result = BatchTranspiler.TranspileDirectory(inputPath, outputPath);
        Console.Out.WriteLine($"transpile: wrote {result.WrittenCount} file(s), skipped {result.SkippedCount} file(s) with no [Transpile] members.");
        return 0;
    }

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Input not found: {inputPath}");
        return 1;
    }

    var source = File.ReadAllText(inputPath);
    var ts = TranspilerEngine.TranspileSource(source);

    if (outputPath is null)
    {
        Console.Write(ts);
    }
    else
    {
        GeneratedFile.Write(outputPath, ts);
    }
    return 0;
}

static int RunFixtures(ReadOnlySpan<string> rest)
{
    string? assemblyPath = null;
    string? outputPath = null;

    for (int i = 0; i < rest.Length; i++)
    {
        var a = rest[i];
        switch (a)
        {
            case "-o":
            case "--out":
                if (i + 1 >= rest.Length)
                {
                    Console.Error.WriteLine($"{a} requires a path argument.");
                    return 2;
                }
                outputPath = rest[++i];
                break;
            default:
                if (assemblyPath is not null)
                {
                    Console.Error.WriteLine($"Unexpected argument: {a}");
                    return 2;
                }
                assemblyPath = a;
                break;
        }
    }

    if (assemblyPath is null)
    {
        Console.Error.WriteLine("fixtures: missing assembly path.");
        return 2;
    }
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"File not found: {assemblyPath}");
        return 1;
    }

    var asm = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
    var fixtures = FixtureGenerator.GenerateForAssembly(asm);
    var json = FixtureGenerator.SerializeToJson(fixtures);

    if (outputPath is null)
    {
        Console.Write(json);
    }
    else
    {
        GeneratedFile.Write(outputPath, json);
    }
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine($"mirrorgen v{TranspilerEngine.Version}");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  mirrorgen transpile <file.cs> [-o <out.ts>]");
    Console.WriteLine("  mirrorgen transpile <dir>     -o <out-dir>");
    Console.WriteLine("  mirrorgen fixtures <assembly.dll> [-o <out.json>]");
    Console.WriteLine("  mirrorgen --version");
    Console.WriteLine("  mirrorgen --help");
}
