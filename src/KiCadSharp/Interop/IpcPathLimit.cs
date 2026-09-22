using System.Text;

using static KiCadSharp.KiCadEnvironment;

namespace KiCadSharp.Interop
{
    /// <summary>
    /// How long an <c>ipc://</c> path can be on each platform, and the message for one that is longer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// nng refuses a path that does not fit with <c>NNG_EADDRINVAL</c>, "Address invalid", which
    /// reads like a malformed URL and says nothing about length (#108). So
    /// <see cref="KiCadIPCClient"/> checks the length before it dials, and its
    /// <see cref="KiCadConnectionException"/> names the length and the limit instead.
    /// </para>
    /// <para>
    /// The limits, in bytes of UTF-8, without the terminating NUL:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Linux, 107.</b> The path is copied into <c>sockaddr_un.sun_path</c>, 108 bytes on Linux,
    /// and one that does not fit is refused (nng 1.3.2, <c>posix_sockaddr.c</c>, lines 66-69,
    /// through <c>posix_ipcdial.c</c>, lines 168-172). MEASURED 2026-09-22 against the
    /// <c>libnng.so</c> this package ships for <c>linux-x64</c>: a 107-byte path with nothing
    /// listening dials and fails with "Connection refused", a 108-byte one with "Address invalid".
    /// </description></item>
    /// <item><description>
    /// <b>macOS, 103.</b> The same copy, into a <c>sun_path</c> of 104 bytes. MEASURED 2026-09-22
    /// on GitHub's <c>macos-14</c> runner (<c>osx-arm64</c>, nng 1.3.2), by the same test: 103
    /// "Connection refused", 104 "Address invalid".
    /// </description></item>
    /// <item><description>
    /// <b>Windows, 127.</b> <c>ipc://</c> is a named pipe there, <c>\\.\pipe\</c> followed by the
    /// path, and nng 1.4.0 refuses a path of <c>NNG_MAXADDRLEN</c> (128) characters or more when
    /// the dialer is created (<c>win_ipcdial.c</c>, lines 230-235). MEASURED 2026-09-22 on GitHub's
    /// <c>windows-11-arm</c> runner (<c>win-arm64</c>, nng 1.4.0), by the same test: 127
    /// "Connection refused", 128 "Address invalid".
    /// </description></item>
    /// </list>
    /// <para>
    /// The <c>NNG_MAXADDRLEN</c> check is in the POSIX dialer too (nng 1.3.2, <c>posix_ipcdial.c</c>,
    /// lines 267-271), but <c>sun_path</c> is the smaller of the two there.
    /// </para>
    /// </remarks>
    internal static class IpcPathLimit
    {
        /// <summary>Linux: <c>sun_path</c>, 108 bytes, less its NUL.</summary>
        internal const int Linux = 107;

        /// <summary>macOS: <c>sun_path</c>, 104 bytes, less its NUL.</summary>
        internal const int MacOS = 103;

        /// <summary>Windows: nng's <c>NNG_MAXADDRLEN</c>, 128, less its NUL.</summary>
        internal const int Windows = 127;

        private const string Scheme = "ipc://";

        /// <summary>The limit where this is running.</summary>
        internal static int ForThisPlatform => For(CurrentSocketPlatform);

        /// <summary>The limit on <paramref name="platform"/>.</summary>
        internal static int For(SocketPlatform platform) =>
            platform switch
            {
                SocketPlatform.Windows => Windows,
                SocketPlatform.MacOS => MacOS,
                _ => Linux,
            };

        /// <summary>
        /// Why <paramref name="url"/> cannot be dialled where this is running, or <see langword="null"/>
        /// when its path fits.
        /// </summary>
        internal static string? TooLong(string url) => TooLong(url, CurrentSocketPlatform);

        /// <summary>
        /// Why <paramref name="url"/> cannot be dialled on <paramref name="platform"/>, or
        /// <see langword="null"/> when its path fits, or when the URL is not an <c>ipc://</c> one.
        /// </summary>
        /// <remarks>
        /// The path is everything after <c>ipc://</c>, which is how nng reads the URL: for that
        /// scheme, <c>nni_url_parse</c> takes the rest as the path, and the limit applies to it alone.
        /// </remarks>
        internal static string? TooLong(string url, SocketPlatform platform)
        {
            if (!url.StartsWith(Scheme, StringComparison.Ordinal))
            {
                return null;
            }

            var limit = For(platform);
            var bytes = Encoding.UTF8.GetByteCount(url.AsSpan(Scheme.Length));
            if (bytes <= limit)
            {
                return null;
            }

            var (name, reason) = platform switch
            {
                SocketPlatform.Windows => ("Windows", "nng's NNG_MAXADDRLEN is 128, with its NUL"),
                SocketPlatform.MacOS => ("macOS", "sun_path is 104 bytes, with its NUL"),
                _ => ("Linux", "sun_path is 108 bytes, with its NUL"),
            };

            return $"the socket path is {bytes} bytes long, and {name} takes at most {limit} bytes ({reason}). "
                + $"The path is {nameof(KiCadClientSettings)}.{nameof(KiCadClientSettings.PipeName)}.";
        }
    }
}
