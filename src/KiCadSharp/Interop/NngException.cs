namespace KiCadSharp.Interop
{
    /// <summary>
    /// A non-zero return from an nng call. Carries nng's own code and nng's own text for it.
    /// </summary>
    /// <remarks>
    /// Internal on purpose: nng is an implementation detail of the transport, and every one of these
    /// is turned into a <see cref="KiCadConnectionException"/> before it leaves
    /// <see cref="KiCadIPCClient"/>. A consumer catching by name still catches what it caught before.
    /// </remarks>
    internal sealed class NngException : Exception
    {
        internal NngException(string operation, int error)
            : base($"{operation} failed: {Nng.Describe(error)} (nng error {error})")
        {
            Error = error;
            Operation = operation;
        }

        /// <summary>nng's error code, e.g. 5 for <c>NNG_ETIMEDOUT</c>.</summary>
        internal int Error { get; }

        /// <summary>The nng function that returned it.</summary>
        internal string Operation { get; }
    }
}
