namespace KiCadSharp
{
    /// <summary>
    /// The one type to catch around an IPC call: KiCad could not be reached, or it was reached and
    /// the call did not produce a result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every failure <see cref="KiCadIPCClient"/> raises for a request is one of the two types
    /// derived from this, and the underlying failure, when there is one, is the
    /// <see cref="Exception.InnerException"/>:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="KiCadConnectionException"/> -- no request and reply were exchanged: no
    /// socket path, nothing listening at it, no native nng for this platform, a send or receive
    /// that nng refused, a reply that is not an <c>ApiResponse</c> at all, or
    /// <see cref="KiCadClientSettings.RequestTimeout"/> running out.</item>
    /// <item><see cref="ApiException"/> -- KiCad answered, and the answer was not the result:
    /// a status other than <c>AS_OK</c>, which <see cref="ApiException.StatusCode"/> carries, or an
    /// <c>AS_OK</c> whose payload is not the type the command returns.</item>
    /// </list>
    /// <para>
    /// <b>Not included, on purpose:</b> <see cref="OperationCanceledException"/>. A cancelled
    /// <see cref="CancellationToken"/> surfaces as itself, never wrapped, so a caller can tell "I
    /// stopped it" from "it failed". So do the two usage errors, <see cref="ArgumentNullException"/>
    /// for a null command and <see cref="ObjectDisposedException"/> for a call made on a client that
    /// has been disposed: neither says anything about KiCad. A call that was already under way when
    /// the client was disposed ends with <see cref="OperationCanceledException"/>.
    /// </para>
    /// <para>
    /// Abstract, because it is a type to catch rather than one to throw: each failure has a more
    /// specific type that says which of the two it is.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class KiCadIpcException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        protected KiCadIpcException()
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        protected KiCadIpcException(string? message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and a cause.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        protected KiCadIpcException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
