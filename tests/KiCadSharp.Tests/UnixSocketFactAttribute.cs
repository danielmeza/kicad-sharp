namespace KiCadSharp.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that binds a Unix socket for nng to dial, as
/// <see cref="HeldHandshake"/> does. Skipped on Windows, where nng's <c>ipc://</c> is a named pipe
/// (<c>\\.\pipe\</c> followed by the path) and a Unix socket bound at that path is never reached: the
/// dial fails at once with connection refused, and a test that waits for it to arrive times out (#106).
/// </summary>
public sealed class UnixSocketFactAttribute : FactAttribute
{
    public UnixSocketFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "nng's ipc:// is a named pipe on Windows, so the Unix socket this test binds is never dialed (#106).";
        }
    }
}
