using System.Collections.Concurrent;

namespace KiCadSharp.Testing;

/// <summary>
/// A directory under the system temp path that one test owns, deleted when the test disposes it.
/// </summary>
/// <remarks>
/// <para>
/// Take one with <c>using</c>, so the directory goes when the test does, pass or fail:
/// </para>
/// <code>
/// using var scratch = TestData.NewScratchDirectory();
/// var output = Path.Combine(scratch, "board.kicad_pcb");
/// </code>
/// <para>
/// It converts to its path wherever a <see cref="string"/> is expected, so it goes straight into
/// <see cref="System.IO.Path.Combine(string, string)"/>, a working directory or a helper that takes a
/// path.
/// </para>
/// <para>
/// Deleting tolerates a file that is still held open for a moment, as a <c>kicad-cli</c> that has
/// just exited can on Windows: it retries for under a second and then leaves the directory where it
/// is. A directory left behind is not worth failing a test over, and a cleanup that throws would
/// replace the test's own failure with its own.
/// </para>
/// <para>
/// A directory nobody disposes is still deleted, when the test process exits. That covers the one
/// shared by every case of a theory, which no single test owns, and one a test forgot to put in a
/// <c>using</c>. It does not cover a test process that is killed.
/// </para>
/// <para>
/// Set <c>KICADSHARP_KEEP_TEST_FILES=1</c> to keep every directory, for looking at what a failing
/// test wrote. Each is named after the test that created it, followed by a unique suffix.
/// </para>
/// </remarks>
public sealed class ScratchDirectory : IDisposable
{
    /// <summary>The environment variable that keeps every scratch directory when set to <c>1</c>.</summary>
    public const string KeepVariable = "KICADSHARP_KEEP_TEST_FILES";

    /// <summary>
    /// The waits between delete attempts: about 0.8 s in all, which covers a child process's file
    /// handles closing after it exits and never holds a test up for long.
    /// </summary>
    private static readonly int[] RetryMilliseconds = [10, 25, 50, 100, 200, 400];

    /// <summary>Every directory created and not yet disposed, for the sweep at process exit.</summary>
    private static readonly ConcurrentDictionary<ScratchDirectory, byte> Undisposed = new();

    static ScratchDirectory() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var scratch in Undisposed.Keys)
        {
            scratch.Dispose();
        }
    };

    private ScratchDirectory(string path) => Path = path;

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>
    /// Whether scratch directories are kept rather than deleted: <see cref="KeepVariable"/> is set to
    /// anything but an empty string, <c>0</c> or <c>false</c>.
    /// </summary>
    public static bool KeepFiles => IsKeep(Environment.GetEnvironmentVariable(KeepVariable));

    /// <summary>
    /// Creates an empty directory at <c>&lt;temp&gt;/&lt;group&gt;/&lt;owner&gt;-&lt;guid&gt;</c>.
    /// </summary>
    /// <param name="group">The folder under the temp path that holds one test project's directories.</param>
    /// <param name="owner">The test that owns it, so a kept directory can be found by name.</param>
    public static ScratchDirectory Create(string group, string owner)
    {
        var name = new string([.. owner.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(64)]);
        var leaf = name.Length == 0 ? Guid.NewGuid().ToString("n") : $"{name}-{Guid.NewGuid():n}";
        var scratch = new ScratchDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), group, leaf));
        Directory.CreateDirectory(scratch.Path);
        Undisposed[scratch] = 0;
        return scratch;
    }

    /// <summary>The value of <see cref="KeepVariable"/> read as a switch.</summary>
    public static bool IsKeep(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim() != "0"
        && !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The directory's full path.</summary>
    public static implicit operator string(ScratchDirectory scratch) => scratch.Path;

    /// <inheritdoc/>
    public override string ToString() => Path;

    /// <summary>
    /// Deletes the directory and everything in it, unless <see cref="KeepFiles"/>. Never throws: a
    /// directory that still cannot be deleted after the retries is left where it is.
    /// </summary>
    public void Dispose()
    {
        if (!Undisposed.TryRemove(this, out _) || KeepFiles)
        {
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == RetryMilliseconds.Length)
                {
                    return;
                }

                Thread.Sleep(RetryMilliseconds[attempt]);
            }
        }
    }
}
