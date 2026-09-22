using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KiCadSharp.Tests;

/// <summary>
/// The running KiCad the harness started, if any: <c>scripts/kicad-ipc-container.sh</c> exports
/// one socket for a board and another for a schematic, and the live tests return early without
/// them.
/// </summary>
/// <remarks>
/// The master-only tests also ask the KiCad they reached what it supports, through
/// <see cref="KiCadVersion"/>'s capability flags, and return early on a KiCad that does not.
/// So the same suite passes against the 10.0.6 release image and against the dev image's nightly,
/// and the difference is only how much of it actually ran.
/// </remarks>
internal static class LiveKiCad
{
    /// <summary>The board editor's socket, from <c>KICADSHARP_IPC_SOCKET</c>.</summary>
    internal static string? BoardSocket => Read("KICADSHARP_IPC_SOCKET");

    /// <summary>The schematic editor's socket, from <c>KICADSHARP_IPC_SCHEMATIC_SOCKET</c>.</summary>
    internal static string? SchematicSocket => Read("KICADSHARP_IPC_SCHEMATIC_SOCKET");

    /// <summary>The host directory the board container sees as <c>/project</c>, if the harness said.</summary>
    internal static string? BoardProject => Read("KICADSHARP_IPC_PROJECT");

    /// <summary>The host directory the schematic container sees as <c>/project</c>, if the harness said.</summary>
    internal static string? SchematicProject => Read("KICADSHARP_IPC_SCHEMATIC_PROJECT");

    /// <summary>A client on the given socket, wired the way a consumer would wire it.</summary>
    internal static KiCad Connect(string socket, Action<KiCadClientSettings>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKiCad("kicad-sharp-tests", settings =>
        {
            settings.PipeName = socket;
            configure?.Invoke(settings);
        });
        return services.BuildServiceProvider().GetRequiredService<IKiCadFactory>().Create("kicad-sharp-tests");
    }

    /// <summary>
    /// The board editor, when the harness started one and it supports what the test needs; null
    /// otherwise, which the test takes as "nothing to measure here".
    /// </summary>
    internal static async Task<KiCad?> Board(Func<KiCadVersion, bool>? requires = null)
    {
        return BoardSocket is null ? null : await Reach(BoardSocket, requires);
    }

    /// <summary>Same, for the schematic editor.</summary>
    internal static async Task<KiCad?> Schematic(Func<KiCadVersion, bool>? requires = null)
    {
        return SchematicSocket is null ? null : await Reach(SchematicSocket, requires);
    }

    private static async Task<KiCad?> Reach(string socket, Func<KiCadVersion, bool>? requires)
    {
        var kicad = Connect(socket);
        if (requires is null)
        {
            return kicad;
        }

        var version = await kicad.GetVersion();
        return requires(version) ? kicad : null;
    }

    private static string? Read(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
