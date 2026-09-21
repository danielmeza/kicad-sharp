using System;

using Kiapi.Common;

namespace KiCadSharp
{
    /// <summary>
    /// KiCad answered a request, and the answer was not the result: a status other than
    /// <c>AS_OK</c>, or an <c>AS_OK</c> whose payload is not the type the command returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StatusCode"/> is the status KiCad sent, which is what a caller branches on.
    /// <c>AS_UNHANDLED</c> in particular is how KiCad says it has no handler for a command -- KiCad
    /// 10.0.6's <c>KICAD_API_SERVER</c> answers "no handler available for request of type ..." with
    /// it -- so it is how a caller learns that the KiCad it is talking to does not have a command,
    /// without keying anything on a version number:
    /// </para>
    /// <code>
    /// catch (ApiException e) when (e.StatusCode == ApiStatusCode.AsUnhandled)
    /// {
    ///     // this KiCad does not have the command
    /// }
    /// </code>
    /// <para>
    /// Derives from <see cref="KiCadIpcException"/>, the type to catch for any IPC failure. It used
    /// to derive from <see cref="Exception"/> directly, and before 0.2.0 it was <c>internal</c>.
    /// </para>
    /// </remarks>
    [Serializable]
    public class ApiException : KiCadIpcException
    {
        /// <summary>Creates the exception with no message and no status.</summary>
        public ApiException()
        {
        }

        /// <summary>Creates the exception with a message and no status.</summary>
        /// <param name="message">What went wrong.</param>
        public ApiException(string? message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message, a cause and no status.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        public ApiException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Creates the exception for a reply KiCad answered with <paramref name="statusCode"/>.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="statusCode">The status in KiCad's reply.</param>
        /// <param name="errorMessage">KiCad's own text for the failure, if it sent any.</param>
        /// <param name="innerException">The underlying failure, if there is one.</param>
        public ApiException(string? message, ApiStatusCode statusCode, string? errorMessage = null, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        /// <summary>
        /// The status in KiCad's reply, or <see langword="null"/> when the exception was not raised
        /// for a reply.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Anything but <c>AS_OK</c> is KiCad refusing: <c>AS_UNHANDLED</c> (no handler for the
        /// command), <c>AS_BAD_REQUEST</c>, <c>AS_NOT_READY</c> (KiCad has only just started),
        /// <c>AS_BUSY</c>, <c>AS_TOKEN_MISMATCH</c> (the request carried another KiCad's token), and
        /// the rest of <see cref="ApiStatusCode"/>. A reply that carries no status at all reads as
        /// <c>AS_UNKNOWN</c>, the protobuf default, which is also how KiCad's own Python client reads
        /// it.
        /// </para>
        /// <para>
        /// <c>AS_OK</c> means KiCad said the command succeeded and the payload was missing, or was
        /// not the type the command returns.
        /// </para>
        /// <para>
        /// <see langword="null"/> for <see cref="KiCad.GetBoard"/> finding no board open, which KiCad
        /// reports as a successful answer with no documents in it, and for an exception built with
        /// one of the constructors that take no status.
        /// </para>
        /// </remarks>
        public ApiStatusCode? StatusCode { get; }

        /// <summary>
        /// KiCad's own text for the failure, the <c>error_message</c> of the reply's status. Empty
        /// when KiCad sent none.
        /// </summary>
        public string ErrorMessage { get; } = string.Empty;
    }
}
