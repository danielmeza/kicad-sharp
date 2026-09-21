using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KiCadSharp.Tests;

/// <summary>
/// Runs part of a test in a process of its own: this test assembly, started again by the dotnet
/// host that runs the tests, with an argument that names what to do.
/// </summary>
/// <remarks>
/// <para>
/// For what cannot be tested in the test host. A process loads one libnng and keeps it, and by the
/// time a test runs the host has long since loaded the shipped one. Only a fresh process can show
/// what <c>KICADSHARP_NNG_LIBRARY</c> does when nng is loaded (<see cref="NngLibraryVariableTests"/>).
/// </para>
/// <para>
/// The child is the test assembly itself, not a separate helper project. Microsoft.NET.Test.Sdk
/// makes a test project an executable with an empty generated <c>Main</c>. The project turns that
/// off (<c>GenerateProgramFile</c>) so that <see cref="Main"/> takes its place, and nothing else
/// changes: xunit never calls it, and with no arguments it does nothing, as the generated one did.
/// </para>
/// </remarks>
internal static class ChildProcess
{
    private const string Argument = "--kicadsharp-child";

    /// <summary>What a child can be asked to do, by name.</summary>
    private static readonly Dictionary<string, Func<int>> Scenarios = new(StringComparer.Ordinal)
    {
        [nameof(NngLibraryVariableTests.ConnectTwice)] = NngLibraryVariableTests.ConnectTwice,
    };

    /// <summary>
    /// The dotnet host that runs the current runtime, or <see langword="null"/> when there is none
    /// to find. See <see cref="FindHost"/>.
    /// </summary>
    internal static readonly string? Host = FindHost();

    /// <summary>
    /// Why no child process can be started here, or <see langword="null"/> when one can. A test that
    /// needs one is skipped with this as the reason; see <see cref="ChildProcessFactAttribute"/>.
    /// </summary>
    internal static string? CannotStart =>
        Host is null
            ? $"No dotnet host was found for the runtime in {RuntimeEnvironment.GetRuntimeDirectory()}, so there "
                + "is nothing to start this test assembly again with. The test needs the framework-dependent layout "
                + "of a dotnet installation, <root>/shared/Microsoft.NETCore.App/<version>/ with <root>/dotnet beside it, "
                + "or DOTNET_HOST_PATH naming the host."
            : null;

    private static int Main(string[] args) =>
        args is [Argument, var scenario] && Scenarios.TryGetValue(scenario, out var run) ? run() : 0;

    /// <summary>
    /// Starts this assembly as a child process that runs <paramref name="scenario"/>, with
    /// <paramref name="environment"/> laid over the test host's own environment (a
    /// <see langword="null"/> value removes a variable), and waits for it to exit.
    /// </summary>
    internal static (int ExitCode, string[] Output, string Error) Run(
        string scenario, IReadOnlyDictionary<string, string?> environment, TimeSpan timeout)
    {
        if (!Scenarios.ContainsKey(scenario))
        {
            throw new ArgumentException($"no child scenario named {scenario}", nameof(scenario));
        }

        var host = Host ?? throw new InvalidOperationException(CannotStart);
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // `dotnet exec` rather than `dotnet <dll>`, so a file name can never be taken for an SDK
        // command. The assembly's own runtimeconfig.json and deps.json sit beside it and are found
        // there.
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(ChildProcess).Assembly.Location);
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(scenario);

        foreach (var (name, value) in environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{host} did not start");

        // Both streams are read concurrently, so neither can fill its pipe and stall the child.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"the child process for {scenario} did not exit within {timeout}. stderr: {error.Result}");
        }

        // The parameterless overload waits for the redirected streams to reach end of file too.
        process.WaitForExit();

        return (
            process.ExitCode,
            output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            error.Result);
    }

    /// <summary>
    /// The <c>dotnet</c> host of the installation the current runtime comes from.
    /// </summary>
    /// <remarks>
    /// That host runs the child on the same runtime as the tests. The runtime directory is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c>, and the host is
    /// <c>&lt;root&gt;/dotnet</c>. <c>DOTNET_HOST_PATH</c>, which the .NET SDK sets for the
    /// processes it starts, is the fallback.
    /// </remarks>
    private static string? FindHost()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var beside = Path.Combine(root, name);
        if (File.Exists(beside))
        {
            return beside;
        }

        var fromSdk = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(fromSdk) && File.Exists(fromSdk) ? fromSdk : null;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> for a test that starts a <see cref="ChildProcess"/>. It is
/// skipped, with <see cref="ChildProcess.CannotStart"/> as the reason, where no child can be
/// started.
/// </summary>
public sealed class ChildProcessFactAttribute : FactAttribute
{
    public ChildProcessFactAttribute() => Skip = ChildProcess.CannotStart;
}
