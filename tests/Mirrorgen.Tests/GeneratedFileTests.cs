using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// The write path, exercised the way builds actually hit it: repeatedly with
/// identical content, and from several processes-worth of concurrency at once.
/// </summary>
public sealed class GeneratedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mirrorgen-write-" + Guid.NewGuid().ToString("N"));

    public GeneratedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Creates_the_file_and_any_missing_directory()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "out.ts");

        var outcome = GeneratedFile.Write(path, "export const a = 1;\n");

        Assert.Equal(GeneratedFile.Outcome.Written, outcome);
        Assert.Equal("export const a = 1;\n", File.ReadAllText(path));
    }

    [Fact]
    public void Reports_unchanged_and_leaves_the_file_alone_when_content_matches()
    {
        var path = Path_("out.ts");
        const string content = "export const a = 1;\n";
        GeneratedFile.Write(path, content);

        // Coarse filesystem timestamps would make an equal mtime prove nothing,
        // so move it far enough back that a rewrite could not reproduce it.
        var marker = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(path, marker);

        var outcome = GeneratedFile.Write(path, content);

        Assert.Equal(GeneratedFile.Outcome.Unchanged, outcome);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Replaces_the_file_when_content_differs()
    {
        var path = Path_("out.ts");
        GeneratedFile.Write(path, "export const a = 1;\n");

        var outcome = GeneratedFile.Write(path, "export const a = 2;\n");

        Assert.Equal(GeneratedFile.Outcome.Written, outcome);
        Assert.Equal("export const a = 2;\n", File.ReadAllText(path));
    }

    /// <summary>
    /// Generated text is assembled with StringBuilder.AppendLine, which emits
    /// Environment.NewLine — so the same sources used to produce CRLF on Windows
    /// and LF everywhere else. Output that differs by host is not deterministic,
    /// and where it lands in a repository declaring `eol=lf` it rewrites bytes
    /// git considers unchanged.
    /// </summary>
    [Fact]
    public void Normalises_newlines_to_lf()
    {
        var path = Path_("out.ts");

        GeneratedFile.Write(path, "line one\r\nline two\rline three\n");

        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal("line one\nline two\nline three\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Content_differing_only_in_newlines_is_unchanged_after_normalisation()
    {
        var path = Path_("out.ts");
        GeneratedFile.Write(path, "a\nb\n");

        var outcome = GeneratedFile.Write(path, "a\r\nb\r\n");

        Assert.Equal(GeneratedFile.Outcome.Unchanged, outcome);
    }

    [Fact]
    public void Writes_utf8_without_a_bom()
    {
        var path = Path_("out.ts");

        GeneratedFile.Write(path, "// ünïcode ✓\n");

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("// ünïcode ✓\n", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// The case that reached CI: several writers on one path at once. Every one
    /// of them must succeed, and the file must end up whole — a truncate-then-
    /// write leaves a reader a prefix, which is a valid-looking TypeScript file
    /// that is missing its tail.
    /// </summary>
    [Fact]
    public void Concurrent_writers_of_the_same_content_all_succeed_and_leave_it_whole()
    {
        var path = Path_("out.ts");
        var content = BigContent();

        var failures = RunConcurrently(16, () => GeneratedFile.Write(path, content));

        Assert.Empty(failures);
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Empty(StrayTempFiles());
    }

    /// <summary>
    /// The same race with each writer producing different text. No writer can
    /// claim which one wins, but the file must hold exactly one of them in full
    /// — never a blend, never a truncation.
    /// </summary>
    [Fact]
    public void Concurrent_writers_of_different_content_leave_exactly_one_whole_version()
    {
        var path = Path_("out.ts");
        var versions = Enumerable.Range(0, 16).Select(i => BigContent(i)).ToArray();

        var failures = RunConcurrently(versions.Length, i => GeneratedFile.Write(path, versions[i]));

        Assert.Empty(failures);
        Assert.Contains(File.ReadAllText(path), versions);
        Assert.Empty(StrayTempFiles());
    }

    /// <summary>
    /// A reader running throughout the race never observes a partial file. This
    /// is the property `File.WriteAllText` cannot offer and the atomic replace
    /// exists for.
    /// </summary>
    [Fact]
    public async Task A_concurrent_reader_never_observes_a_partial_file()
    {
        var path = Path_("out.ts");
        var versions = Enumerable.Range(0, 8).Select(i => BigContent(i)).ToArray();
        GeneratedFile.Write(path, versions[0]);

        var observed = new List<string>();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { observed.Add(File.ReadAllText(path)); }
                catch (IOException) { /* the open handle window; not an observation */ }
            }
        });

        var failures = RunConcurrently(versions.Length, i => GeneratedFile.Write(path, versions[i]));
        stop.Cancel();
        await reader.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(failures);
        Assert.NotEmpty(observed);
        Assert.All(observed, seen => Assert.Contains(seen, versions));
    }

    // Large enough that a non-atomic write is several filesystem operations
    // wide, so a racing reader or writer has a real window to land inside.
    private static string BigContent(int seed = 0)
    {
        var sb = new StringBuilder();
        sb.Append("// version ").Append(seed).Append('\n');
        for (var i = 0; i < 4000; i++)
            sb.Append("export const v").Append(seed).Append('_').Append(i).Append(" = ").Append(i).Append(";\n");
        return sb.ToString();
    }

    private List<Exception> RunConcurrently(int count, Action<int> body)
    {
        var failures = new List<Exception>();
        var gate = new Barrier(count);
        var sync = new object();

        Parallel.For(0, count, i =>
        {
            gate.SignalAndWait();
            try { body(i); }
            catch (Exception ex) { lock (sync) failures.Add(ex); }
        });

        return failures;
    }

    private List<Exception> RunConcurrently(int count, Func<GeneratedFile.Outcome> body) =>
        RunConcurrently(count, _ => body());

    private string[] StrayTempFiles() =>
        Directory.GetFiles(_dir, "*.mirrorgen-*.tmp", SearchOption.AllDirectories);
}
