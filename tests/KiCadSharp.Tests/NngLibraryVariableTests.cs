using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

using KiCadSharp.Interop;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// <c>KICADSHARP_NNG_LIBRARY</c> naming a library that loads and is not nng (#70).
/// </summary>
/// <remarks>
/// <para>
/// It used to surface as the runtime's <see cref="EntryPointNotFoundException"/>, from the first
/// call into nng. The loader's two other failures, no library and one the platform refuses, both
/// reach the caller as a <see cref="KiCadConnectionException"/>. Now this one does too: the resolver
/// looks up every entry point as it loads the library, and refuses a library that lacks one.
/// </para>
/// <para>
/// The end-to-end tests run in a <see cref="ChildProcess"/>. The variable is read once, when nng is
/// first loaded, and this test host loaded the shipped libnng long before. A process cannot load a
/// second one, so the variable only means something in a process that has not loaded nng yet.
/// </para>
/// </remarks>
public class NngLibraryVariableTests
{
    // A child takes a fraction of a second to start. Sixty seconds is for a loaded CI machine, and a
    // child that has not exited by then is a failure rather than a wait.
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A shared library that is certainly not nng, that loads, and that every machine able to run
    /// these tests has: the runtime's own JIT, from the directory the running runtime lives in.
    /// It is already loaded in the child, so loading it again only takes another reference to it.
    /// </summary>
    private static string NotNngLibrary =>
        Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(),
            OperatingSystem.IsWindows() ? "clrjit.dll" : OperatingSystem.IsMacOS() ? "libclrjit.dylib" : "libclrjit.so");

    /// <summary>The libnng this package ships for the running platform.</summary>
    private static string ShippedNng =>
        Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            NngLibraryResolver.PortableRuntimeIdentifier(),
            "native",
            NngLibraryResolver.FileName);

    [ChildProcessFact]
    public void ALibraryThatIsNotNngIsAConnectionFailureThatNamesTheVariableThePathAndTheFunction()
    {
        var library = NotNngLibrary;
        Assert.True(File.Exists(library), $"{library} is missing; the test needs a shared library that is not nng");

        var attempts = ConnectTwiceInAChild(library);

        // What a caller of the public API sees, with the runtime's exception inside it.
        var first = attempts[0];
        Assert.Equal(typeof(KiCadConnectionException).FullName, first.Type);
        Assert.Contains(NngLibraryResolver.LibraryPathVariableName, first.Message);
        Assert.Contains($"'{library}'", first.Message);
        Assert.Contains(nameof(Nng.nng_req0_open), first.Message);
        Assert.Equal(typeof(EntryPointNotFoundException).FullName, first.InnerType);
        Assert.Contains(nameof(Nng.nng_req0_open), first.InnerMessage);

        // The second attempt is refused the same way. The resolver does not remember a library it
        // refused, and nothing else was loaded instead of it.
        Assert.Equal(first, attempts[1]);
    }

    [ChildProcessFact]
    public void TheShippedNngNamedByTheVariableGetsAsFarAsTheDial()
    {
        // The control for the test above: the same child, with the variable naming a real nng. The
        // check lets it through, and the connection fails where it should, at the dial, because
        // nothing listens at the socket.
        var library = ShippedNng;
        Assert.True(File.Exists(library), $"{library} is missing; this package ships no nng for the running platform");

        var attempts = ConnectTwiceInAChild(library);

        foreach (var attempt in attempts)
        {
            Assert.Equal(typeof(KiCadConnectionException).FullName, attempt.Type);
            Assert.Equal(typeof(NngException).FullName, attempt.InnerType);
            Assert.Contains("Connection refused", attempt.Message);
        }
    }

    [Fact]
    public void EveryEntryPointNngDeclaresIsChecked()
    {
        // The check is only as good as its list. A function added to Nng and not to EntryPoints
        // would not be looked up when the library loads, and a library without it would be back to
        // failing with EntryPointNotFoundException on its first call.
        var declared = typeof(Nng)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .Select(method => method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint ?? method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(13, declared.Length);
        Assert.Equal(declared, NngLibraryResolver.EntryPoints.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheShippedNngExportsEveryEntryPoint()
    {
        var library = NativeLibrary.Load(ShippedNng);
        try
        {
            Assert.Empty(NngLibraryResolver.MissingEntryPoints(library));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void ALibraryThatIsNotNngExportsNoneOfThem()
    {
        var library = NativeLibrary.Load(NotNngLibrary);
        try
        {
            Assert.Equal(NngLibraryResolver.EntryPoints, NngLibraryResolver.MissingEntryPoints(library));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void ALibraryWithSomeOfThemIsNamedForWhatItLacks()
    {
        // An nng that lacks only some of the functions: an older one, say, from before
        // nng_socket_set_ms. No such library is at hand to load, so this is the message alone.
        var failure = NngLibraryResolver.NotNng("/opt/nng/libnng.so", [nameof(Nng.nng_socket_set_ms)]);

        Assert.Contains("'/opt/nng/libnng.so'", failure.Message);
        Assert.Contains("does not export nng_socket_set_ms (1 of the 13 nng functions KiCadSharp calls)", failure.Message);
        var inner = Assert.IsType<EntryPointNotFoundException>(failure.InnerException);
        Assert.Contains(nameof(Nng.nng_socket_set_ms), inner.Message);
    }

    /// <summary>
    /// The child's half: connects twice through the public API, and prints what each attempt threw
    /// as one line of JSON. The socket path is one where nothing listens, so a client that gets
    /// past loading nng fails at the dial.
    /// </summary>
    internal static int ConnectTwice()
    {
        using var client = new KiCadIPCClient(
            new KiCadClientSettings
            {
                PipeName = $"ipc://{Path.Combine(Path.GetTempPath(), $"kicadsharp-absent-{Guid.NewGuid():N}.sock")}",
            },
            NullLogger<KiCadIPCClient>.Instance);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Outcome outcome;
            try
            {
                client.Connect().AsTask().GetAwaiter().GetResult();
                outcome = new Outcome(null, null, null, null);
            }
            catch (Exception exception)
            {
                outcome = new Outcome(
                    exception.GetType().FullName,
                    exception.Message,
                    exception.InnerException?.GetType().FullName,
                    exception.InnerException?.Message);
            }

            Console.WriteLine(JsonSerializer.Serialize(outcome));
        }

        return 0;
    }

    private static Outcome[] ConnectTwiceInAChild(string library)
    {
        var (exitCode, output, error) = ChildProcess.Run(
            nameof(ConnectTwice),
            new Dictionary<string, string?> { [NngLibraryResolver.LibraryPathVariableName] = library },
            ChildTimeout);

        var context = $"exit code {exitCode}; stdout:{Environment.NewLine}{string.Join(Environment.NewLine, output)}{Environment.NewLine}stderr:{Environment.NewLine}{error}";
        Assert.True(exitCode == 0, context);
        Assert.True(output.Length == 2, context);

        return output.Select(line => JsonSerializer.Deserialize<Outcome>(line)!).ToArray();
    }

    /// <summary>What one attempt threw, or all nulls when it connected.</summary>
    internal sealed record Outcome(string? Type, string? Message, string? InnerType, string? InnerMessage);
}
