using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Mirrorgen.Core;

/// <summary>
/// The single way Mirrorgen puts generated text on disk.
///
/// <para>Generation is deterministic: the same sources produce the same bytes.
/// So the common case is not "write a new file", it is "write the file that is
/// already there" — and doing that with a plain <c>File.WriteAllText</c> makes
/// two problems that have nothing to do with what was generated.</para>
///
/// <para><b>Concurrent writers.</b> One output path is routinely written by
/// more than one process at a time: two CI jobs building the same project in a
/// shared workspace, an editor's design-time build alongside a command-line
/// one, or — until this was fixed at the target level too — several
/// target-framework legs of a single build. <c>WriteAllText</c> opens the file
/// for truncation with no share mode, so on Windows the loser gets
/// <c>IOException: the process cannot access the file … because it is being
/// used by another process</c>, and on any platform a reader can observe a
/// half-written file. Writing to a sibling temp file and moving it into place
/// makes the swap atomic, so a reader sees the old bytes or the new ones and
/// never a prefix.</para>
///
/// <para><b>Rewriting unchanged content.</b> Even without a collision, an
/// unconditional write touches mtime on every build. That invalidates
/// downstream caches, wakes file watchers, and — where the output is a
/// checked-in file — makes a no-op build dirty the tree. Comparing first turns
/// the overwhelmingly common case into no file operation at all, which also
/// removes most of the window in which a collision is possible.</para>
///
/// <para>Newlines are normalised to <c>\n</c>. StringBuilder.AppendLine emits
/// <see cref="Environment.NewLine"/>, so the same sources produced CRLF on
/// Windows and LF elsewhere — generated output that differs by host is not
/// deterministic, and where it lands in a repository declaring <c>eol=lf</c> it
/// rewrites bytes git considers unchanged.</para>
/// </summary>
public static class GeneratedFile
{
    /// <summary>What <see cref="Write"/> did, so callers can report it.</summary>
    public enum Outcome
    {
        /// <summary>Content already on disk was identical; the file was not touched.</summary>
        Unchanged,

        /// <summary>The file was created or replaced.</summary>
        Written,
    }

    /// <summary>
    /// How many times to retry the atomic replace before giving up. A collision
    /// is resolved by the other writer finishing, which takes microseconds —
    /// this is a handful of attempts across a few tens of milliseconds, not a
    /// wait loop. Failing after that is deliberate: a lock that outlives it is
    /// something holding the file open, and reporting it beats generating
    /// nothing and claiming success.
    /// </summary>
    private const int ReplaceAttempts = 5;

    /// <summary>
    /// Write <paramref name="content"/> to <paramref name="path"/>, skipping the
    /// write entirely when the file already holds it. Creates the containing
    /// directory. Throws <see cref="IOException"/> if the replace cannot be
    /// completed.
    /// </summary>
    public static Outcome Write(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var normalized = NormalizeNewlines(content);

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (Matches(full, normalized))
        {
            return Outcome.Unchanged;
        }

        Replace(full, normalized);
        return Outcome.Written;
    }

    /// <summary>
    /// Whether the file already holds exactly this content. A file being read
    /// while another writer replaces it is not an answer either way, so any
    /// read failure means "write it" rather than "leave it" — the atomic
    /// replace below is what makes that safe to decide optimistically.
    /// </summary>
    private static bool Matches(string path, string content)
    {
        try
        {
            if (!File.Exists(path)) return false;
            return string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Replace(string path, string content)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            // A distinct temp name per attempt and per process: two writers
            // racing must not collide on the staging file as well, which would
            // just move the same problem one step earlier.
            var temp = $"{path}.mirrorgen-{Environment.ProcessId:x}-{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, content, Utf8NoBom);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                Delete(temp);

                // The other writer is generating the same content from the same
                // sources, so by now the file may already say what this call was
                // going to say. Nothing left to do.
                if (Matches(path, content)) return;

                Thread.Sleep(10 * (attempt + 1));
            }
        }

        throw new IOException(
            $"Mirrorgen could not replace '{path}' after {ReplaceAttempts} attempts. "
            + "Something is holding the file open — most often another build writing the same "
            + "generated output. Point MirrorgenOutput at a per-project intermediate directory "
            + "so concurrent builds do not share one path.",
            last);
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a stray temp file behind is not worth failing generation
            // over, and it is named distinctly enough to be recognisable.
        }
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string NormalizeNewlines(string content) =>
        content.IndexOf('\r') < 0
            ? content
            : content.Replace("\r\n", "\n").Replace("\r", "\n");
}
