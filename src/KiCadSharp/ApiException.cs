using System;

namespace KiCadSharp
{
    /// <summary>
    /// Thrown when KiCad answers a command with a status other than OK, or answers with something
    /// that is not the expected reply type.
    /// </summary>
    /// <remarks>
    /// Public because it is what the API throws and the XML docs name it. It used to be
    /// <c>internal</c>, so a consumer in another assembly could not <c>catch</c> it by name and had
    /// to fall back to <see cref="Exception"/>.
    /// </remarks>
    [Serializable]
    public class ApiException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        public ApiException()
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        public ApiException(string? message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and a cause.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        public ApiException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
