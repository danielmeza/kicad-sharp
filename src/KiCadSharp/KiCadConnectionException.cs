namespace KiCadSharp
{
    /// <summary>
    /// KiCad could not be reached: no request and reply were exchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thrown for: no socket path configured; nothing listening at the path, or a socket there that
    /// never completes nng's handshake (nng gives up after about 10 s); no native nng library for
    /// this platform, or a <c>KICADSHARP_NNG_LIBRARY</c> that names something which does not load
    /// or is not nng; a send or a receive that nng refused; a reply that does not parse as an
    /// <c>ApiResponse</c>; and <see cref="KiCadClientSettings.RequestTimeout"/> running out, in which
    /// case the <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.
    /// </para>
    /// <para>
    /// Derives from <see cref="KiCadIpcException"/>, the type to catch for any IPC failure. It used
    /// to derive from <see cref="Exception"/> directly.
    /// </para>
    /// </remarks>
    [Serializable]
    public class KiCadConnectionException : KiCadIpcException
    {
        /// <summary>Creates the exception with no message.</summary>
        public KiCadConnectionException()
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        public KiCadConnectionException(string? message) : base(message)
        {
        }

        /// <summary>Creates the exception with a message and a cause.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        public KiCadConnectionException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}