using System.Runtime.InteropServices;

namespace KiCadSharp.Interop
{
    /// <summary>
    /// The part of nng's C API this client uses, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The IPC client is one REQ socket that dials a unix socket, sends a serialised
    /// <c>ApiRequest</c> and reads back an <c>ApiResponse</c>. That is thirteen entry points, listed
    /// below, and every one of them is here because <see cref="NngRequestSocket"/> calls it. The
    /// managed binding these replace (<c>nng.NET</c>, reached through <c>Rebus.nng</c>) wrapped the
    /// whole of nng -- pub/sub, survey, bus, aio, contexts, streams -- and arrived carrying a
    /// message bus and <c>Newtonsoft.Json</c> for the sake of it.
    /// </para>
    /// <para>
    /// <b>The native library is the same one that shipped before.</b> <c>libnng.so</c> was already
    /// in every consumer's output: it is a native asset of <c>nng.NET</c>, which is why publishing a
    /// consuming application self-contained puts a 540,512-byte <c>libnng.so</c> beside the
    /// executable. This package now carries those same files itself, under
    /// <c>runtimes/&lt;rid&gt;/native/</c>, and no managed nng assembly.
    /// </para>
    /// <para>
    /// nng's own version is exposed by <see cref="nng_version"/>; the binary shipped here reports
    /// <c>1.3.2</c> on Linux and macOS and <c>1.4.0</c> on Windows.
    /// </para>
    /// </remarks>
    internal static partial class Nng
    {
        /// <summary>
        /// The name passed to <see cref="LibraryImportAttribute"/>. The platform decorations line up
        /// with what nng ships: <c>libnng.so</c>, <c>libnng.dylib</c>, <c>nng.dll</c>.
        /// </summary>
        internal const string Library = "nng";

        /// <summary>
        /// Names a <c>libnng</c> to load instead of the one shipped here: an absolute path, or a
        /// name for the platform loader to resolve. This is the supported way onto a platform
        /// upstream publishes no binary for -- see <see cref="NngLibraryResolver"/>.
        /// </summary>
        internal const string LibraryPathVariable = "KICADSHARP_NNG_LIBRARY";

        // nng option names. Strings, because that is nng's own option API.
        internal const string OptionSendTimeout = "send-timeout";
        internal const string OptionReceiveTimeout = "recv-timeout";

        /// <summary>Do not block; return <see cref="Again"/> when the operation would wait.</summary>
        internal const int FlagNonBlock = 2;

        // The three nng error codes this client reasons about by number. Everything else is turned
        // into text by nng_strerror rather than enumerated here.
        internal const int Again = 8;       // NNG_EAGAIN     -- nothing to receive yet
        internal const int Closed = 7;      // NNG_ECLOSED    -- the socket went away underneath us
        internal const int State = 11;      // NNG_ESTATE     -- receive with no request outstanding

        /// <summary>
        /// nng's socket handle: <c>typedef struct nng_socket_s { uint32_t id; } nng_socket</c>,
        /// passed and returned by value. Declared as the struct rather than as a bare
        /// <see cref="uint"/> so the call ABI matches on every architecture, not only the ones where
        /// a one-word struct happens to be passed like an integer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NngSocket
        {
            internal uint Id;
        }

        /// <summary><c>nng_dialer</c>, the same shape. Opened by <see cref="nng_dial"/> and not used again.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NngDialer
        {
            internal uint Id;
        }

        static Nng() => NativeLibrary.SetDllImportResolver(typeof(Nng).Assembly, NngLibraryResolver.Resolve);

        /// <summary>Opens a version-0 REQ socket. The whole client is one of these.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_req0_open(out NngSocket socket);

        /// <summary>Closes the socket, cancelling anything outstanding on it.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_close(NngSocket socket);

        /// <summary>
        /// Connects the socket to <paramref name="url"/> (<c>ipc://…</c> here) and blocks until the
        /// transport handshake completes. Measured: about 10 s against a socket that is bound but
        /// not answering, immediate <c>NNG_ECONNREFUSED</c> against a path that is not there.
        /// </summary>
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int nng_dial(NngSocket socket, string url, out NngDialer dialer, int flags);

        /// <summary>Sends a message. On success nng owns it; on failure the caller still does.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_sendmsg(NngSocket socket, IntPtr message, int flags);

        /// <summary>Receives the reply. Called with <see cref="FlagNonBlock"/>; see <see cref="NngRequestSocket.TryReceive"/>.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_recvmsg(NngSocket socket, out IntPtr message, int flags);

        /// <summary>Allocates an empty message to append the request envelope to.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_msg_alloc(out IntPtr message, nuint size);

        /// <summary>Frees a message this side still owns.</summary>
        [LibraryImport(Library)]
        internal static partial void nng_msg_free(IntPtr message);

        /// <summary>Appends the serialised envelope to the message body.</summary>
        [LibraryImport(Library)]
        internal static partial int nng_msg_append(IntPtr message, byte[] data, nuint size);

        /// <summary>The start of a received message's body.</summary>
        [LibraryImport(Library)]
        internal static partial IntPtr nng_msg_body(IntPtr message);

        /// <summary>The length of a received message's body.</summary>
        [LibraryImport(Library)]
        internal static partial nuint nng_msg_len(IntPtr message);

        /// <summary>nng's own text for an error code. Used rather than a table copied out of nng.h.</summary>
        [LibraryImport(Library)]
        internal static partial IntPtr nng_strerror(int error);

        /// <summary>Sets a millisecond-valued socket option; -1 means no limit.</summary>
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int nng_socket_set_ms(NngSocket socket, string option, int milliseconds);

        /// <summary>The nng release the loaded binary came from. Diagnostics only.</summary>
        [LibraryImport(Library)]
        internal static partial IntPtr nng_version();

        /// <summary>nng's message for <paramref name="error"/>, or a bare code if nng has none.</summary>
        internal static string Describe(int error) =>
            Marshal.PtrToStringUTF8(nng_strerror(error)) ?? $"nng error {error}";

        /// <summary>The nng release string of the loaded binary, for diagnostics.</summary>
        internal static string Version() => Marshal.PtrToStringUTF8(nng_version()) ?? "unknown";
    }
}
