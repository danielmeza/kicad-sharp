using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace KiCadSharp.Interop
{
    /// <summary>
    /// One nng REQ socket, owned by one <see cref="KiCadIPCClient"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole managed surface the IPC client needs: open, dial, send one request, take one reply,
    /// close. It replaces <c>IAPIFactory&lt;INngMsg&gt;</c>, <c>IReqSocket</c>, <c>INngMsg</c> and
    /// <c>NngLoadContext</c> from the <c>nng.NET</c> binding.
    /// </para>
    /// <para>
    /// <b>Receiving does not block.</b> <see cref="TryReceive"/> passes
    /// <see cref="Nng.FlagNonBlock"/> and the caller polls, which is a deliberate choice over a
    /// blocking receive with a timeout. Measured against a REQ socket whose peer is slow to answer:
    /// a blocking <c>nng_recvmsg</c> that hits <c>recv-timeout</c> <b>destroys the outstanding
    /// request</b> -- the next receive returns <c>NNG_ESTATE</c> ("Incorrect state") rather than the
    /// reply, which arrives 1.3 s later and is dropped. A non-blocking receive returns
    /// <c>NNG_EAGAIN</c> and leaves the request standing, so the caller can look again, and can give
    /// up between two looks because a cancellation token said so. That is the only way this client
    /// can be cancelled while a request is on the wire.
    /// </para>
    /// </remarks>
    internal sealed class NngRequestSocket : IDisposable
    {
        private Nng.NngSocket _socket;
        private bool _closed;

        private NngRequestSocket(Nng.NngSocket socket) => _socket = socket;

        /// <summary>
        /// Opens a REQ v0 socket, applies the timeouts and dials <paramref name="url"/>.
        /// </summary>
        /// <returns>
        /// An <b>open, dialled</b> socket that the caller now owns and must dispose --
        /// <see cref="KiCadIPCClient"/> keeps it until <c>Disconnect</c>. The <c>return</c> is inside
        /// the <c>try</c> and there is no <c>finally</c>, so on the way out through it nothing here
        /// closes anything.
        /// </returns>
        /// <remarks>
        /// One operation rather than three so that the failure path has an owner. If any of the three
        /// steps throws, the socket is already open and nobody has a reference to it yet: nothing
        /// would ever call <c>nng_close</c> on it, and there is no finalizer to do so late. The
        /// <c>catch</c> exists for that one case and rethrows unchanged.
        /// <para>
        /// No test asserts that close happens, and that is measured rather than lazy: on nng 1.3.2
        /// leaking 100,000 sockets this way costs no file descriptor and no observable memory, and
        /// socket ids increment monotonically instead of being reused, so a test would pass whether
        /// the <c>catch</c> were here or not.
        /// </para>
        /// </remarks>
        internal static NngRequestSocket Dial(string url, TimeSpan sendTimeout, TimeSpan receiveTimeout)
        {
            var socket = Open();
            try
            {
                socket.SetSendTimeout(sendTimeout);
                socket.SetReceiveTimeout(receiveTimeout);
                socket.Dial(url);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            // Success: still open, and the caller's to close.
            return socket;
        }

        /// <summary>Opens a REQ v0 socket.</summary>
        /// <exception cref="KiCadConnectionException">nng could not be loaded at all.</exception>
        /// <exception cref="NngException">nng refused to open the socket.</exception>
        internal static NngRequestSocket Open()
        {
            int result;
            Nng.NngSocket socket;
            try
            {
                result = Nng.nng_req0_open(out socket);
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
            {
                // The two the loader raises, named rather than caught wholesale. The first call into
                // nng is the one that loads it, so this is the only place a platform with no libnng
                // can surface, and the loader's own message names a file and stops there.
                throw new KiCadConnectionException(NngLibraryResolver.DescribeFailure(), exception);
            }

            Check("nng_req0_open", result);
            return new NngRequestSocket(socket);
        }

        /// <summary>
        /// Connects to <paramref name="url"/>, blocking until nng has a connection or gives up.
        /// </summary>
        /// <remarks>
        /// Measured on this transport: an <c>ipc://</c> path that does not exist fails in under a
        /// millisecond with <c>NNG_ECONNREFUSED</c>; a path that is bound but never completes nng's
        /// handshake -- a KiCad that has opened its socket and is not serving yet -- takes
        /// <b>10.0 s</b> to fail with <c>NNG_ETIMEDOUT</c>. That 10 s is nng's own bound and is the
        /// reason a caller waiting for KiCad to come up must count seconds rather than attempts.
        /// </remarks>
        internal void Dial(string url)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            Check("nng_dial", Nng.nng_dial(_socket, url, out _, 0));
        }

        /// <summary>Bounds <c>nng_sendmsg</c>. <see cref="Timeout.InfiniteTimeSpan"/> removes the bound, which is nng's default.</summary>
        internal void SetSendTimeout(TimeSpan timeout) => SetTimeout(Nng.OptionSendTimeout, timeout);

        /// <summary>
        /// Bounds a blocking <c>nng_recvmsg</c>. This client receives non-blocking so the option
        /// never applies to it; it is set anyway so the socket is not left in a state where some
        /// future blocking receive would wait forever.
        /// </summary>
        internal void SetReceiveTimeout(TimeSpan timeout) => SetTimeout(Nng.OptionReceiveTimeout, timeout);

        private void SetTimeout(string option, TimeSpan timeout)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            var milliseconds = timeout == Timeout.InfiniteTimeSpan
                ? -1
                : (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);

            Check("nng_socket_set_ms", Nng.nng_socket_set_ms(_socket, option, milliseconds));
        }

        /// <summary>
        /// Sends <paramref name="payload"/> as one message, replacing any request already
        /// outstanding.
        /// </summary>
        /// <remarks>
        /// Replacing is what makes an abandoned request safe. Measured: after a request is given up
        /// on, its reply still arrives, and nng discards it -- a fresh request sent afterwards gets
        /// its own reply and not the stale one, because REQ tags each request and matches the reply
        /// to the tag.
        /// </remarks>
        internal void Send(byte[] payload)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            ArgumentNullException.ThrowIfNull(payload);

            Check("nng_msg_alloc", Nng.nng_msg_alloc(out var message, 0));

            try
            {
                Check("nng_msg_append", Nng.nng_msg_append(message, payload, (nuint)payload.Length));

                var result = Nng.nng_sendmsg(_socket, message, 0);
                if (result != 0)
                {
                    throw new NngException("nng_sendmsg", result);
                }

                // nng took ownership; freeing it here would be a double free.
                message = IntPtr.Zero;
            }
            finally
            {
                if (message != IntPtr.Zero)
                {
                    Nng.nng_msg_free(message);
                }
            }
        }

        /// <summary>
        /// Takes the reply if one has arrived. Returns <see langword="false"/> without waiting when
        /// it has not, leaving the request outstanding.
        /// </summary>
        internal bool TryReceive([NotNullWhen(true)] out byte[]? payload)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            var result = Nng.nng_recvmsg(_socket, out var message, Nng.FlagNonBlock);
            if (result == Nng.Again)
            {
                payload = null;
                return false;
            }

            if (result != 0)
            {
                throw new NngException("nng_recvmsg", result);
            }

            try
            {
                var length = (int)Nng.nng_msg_len(message);
                payload = new byte[length];
                if (length > 0)
                {
                    Marshal.Copy(Nng.nng_msg_body(message), payload, 0, length);
                }
            }
            finally
            {
                Nng.nng_msg_free(message);
            }

            return true;
        }

        /// <summary>Closes the socket. Anything waiting on it fails with <c>NNG_ECLOSED</c>.</summary>
        public void Dispose()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Nng.nng_close(_socket);
        }

        private static void Check(string operation, int result)
        {
            if (result != 0)
            {
                throw new NngException(operation, result);
            }
        }
    }
}
