using System.Security.Cryptography;
using System.Text;

namespace KiCadSharp.Tests;

/// <summary>
/// Paths for the sockets the IPC tests listen on and dial. They are short enough whatever
/// <c>TMPDIR</c> is.
/// </summary>
/// <remarks>
/// <para>
/// A Unix socket's path has to fit in <c>sun_path</c>, which is 108 bytes on Linux and 104 on macOS,
/// the terminating NUL included. nng 1.3.2, the <c>libnng</c> this package ships for both, refuses a
/// longer one with <c>NNG_EADDRINVAL</c>, "Address invalid" (<c>posix_sockaddr.c</c>). Measured on
/// Linux: a 107-character path dials, and a 108-character one does not. .NET's
/// <see cref="System.Net.Sockets.UnixDomainSocketEndPoint"/> refuses one too. The tests used to put
/// their sockets straight into <see cref="Path.GetTempPath"/>. So a <c>TMPDIR</c> longer than 52
/// characters failed 4 of them, and one longer than 54 failed every test that starts a peer (#80).
/// </para>
/// <para>
/// Now every socket a test run makes goes into one directory of its own. Its name is
/// <c>kicadsharp-</c> and eight random characters, it is created with mode 0700, and it is deleted
/// when the run ends. It is made in <see cref="Path.GetTempPath"/> when the longest socket path
/// fits there, and in <c>/tmp</c> when it does not. CPython's <c>multiprocessing</c> makes the same
/// choice for its sockets (python/cpython#132124).
/// </para>
/// <para>
/// nng's <c>abstract://</c> sockets on Linux would need no path at all. The nng 1.3.2 shipped here
/// answers "Not supported" to that scheme, and to <c>unix://</c>.
/// </para>
/// <para>
/// On Windows, nng's <c>ipc://</c> is a named pipe, <c>\\.\pipe\</c> followed by the path, and nng
/// takes a path of up to 127 characters (<c>NNG_MAXADDRLEN</c>). There is no <c>/tmp</c> to fall back
/// to, so the directory is always made in <see cref="Path.GetTempPath"/>.
/// </para>
/// </remarks>
internal static class SocketPaths
{
    /// <summary>Unix: macOS's <c>sun_path</c>, the smaller one, less its NUL.</summary>
    internal const int UnixLimit = 103;

    /// <summary>Windows: nng's <c>NNG_MAXADDRLEN</c>, less its NUL.</summary>
    internal const int WindowsLimit = 127;

    /// <summary>The longest <c>purpose</c> <see cref="New"/> takes.</summary>
    private const int MaxPurpose = 8;

    /// <summary>A purpose, a dash, an <see cref="int"/> and <c>.sock</c>.</summary>
    private const int MaxName = MaxPurpose + 1 + 10 + 5;

    private const string Prefix = "kicadsharp-";

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly Lazy<string> RunDirectory = new(CreateRunDirectory, LazyThreadSafetyMode.ExecutionAndPublication);

    private static int _count;

    private static int Limit => OperatingSystem.IsWindows() ? WindowsLimit : UnixLimit;

    /// <summary>
    /// A socket path nothing has used yet, such as <c>/tmp/kicadsharp-a1b2c3d4/peer-7.sock</c>. The
    /// file is not created.
    /// </summary>
    /// <param name="purpose">What the socket is for, up to eight characters: <c>peer</c>, <c>absent</c>.</param>
    internal static string New(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(purpose.Length, MaxPurpose, nameof(purpose));

        var path = Path.Combine(RunDirectory.Value, $"{purpose}-{Interlocked.Increment(ref _count)}.sock");
        var bytes = Encoding.UTF8.GetByteCount(path);
        return bytes <= Limit
            ? path
            : throw new InvalidOperationException($"The socket path '{path}' is {bytes} bytes long, and nng takes at most {Limit} here.");
    }

    /// <summary>The same as <see cref="New"/>, as the <c>ipc://</c> URL nng listens on and dials.</summary>
    internal static string NewUrl(string purpose) => "ipc://" + New(purpose);

    /// <summary>
    /// Where the run's directory goes: <paramref name="tempPath"/> if the longest socket path fits
    /// under it, and otherwise <c>/tmp</c>, except on Windows.
    /// </summary>
    internal static string RootFor(string tempPath)
    {
        var longest = Path.Combine(tempPath, Prefix + new string('x', 8), new string('x', MaxName));
        return Encoding.UTF8.GetByteCount(longest) <= Limit || OperatingSystem.IsWindows() ? tempPath : "/tmp";
    }

    private static string CreateRunDirectory()
    {
        var root = RootFor(Path.GetTempPath());
        string path;
        do
        {
            path = Path.Combine(root, Prefix + RandomNumberGenerator.GetString(Alphabet, 8));
        }
        while (Path.Exists(path));

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Already gone, or not ours to remove any more. The process is exiting, and there
                // is nobody left to tell.
            }
        };

        return path;
    }
}
