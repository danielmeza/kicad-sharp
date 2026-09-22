using System.Runtime.CompilerServices;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent.Tests;

/// <summary>What more than one test file here needs.</summary>
internal static class TestSupport
{
    /// <summary>The front copper stack an SMD pad sits on.</summary>
    public static readonly string[] FrontSmd = [KiCadLayerNames.FCu, KiCadLayerNames.FPaste, KiCadLayerNames.FMask];

    /// <summary>
    /// Asserts two views are over forms that write out as the same text — which is what "the
    /// <c>With*</c> did exactly what the <c>Add*</c> does" comes down to.
    /// </summary>
    public static void SameText(KiCadNode expected, KiCadNode actual) =>
        Assert.Equal(expected.Node.ToText(), actual.Node.ToText());

    /// <summary>
    /// Creates an empty directory under <c>&lt;temp&gt;/kicadsharp-fluent-tests</c>, unique to this
    /// call and named after the test that asked for it. Take it with <c>using</c> so it is deleted
    /// when the test ends; see <see cref="ScratchDirectory"/>.
    /// </summary>
    public static ScratchDirectory NewScratchDirectory([CallerMemberName] string owner = "") =>
        ScratchDirectory.Create("kicadsharp-fluent-tests", owner);

    /// <summary>
    /// A <c>kicad-cli</c> to shell out to, or <see langword="null"/>. Opt in with
    /// <c>KICADSHARP_KICAD_CLI</c>, as in <c>KiCadSharp.Tests</c>; without it the test that would
    /// use it returns early rather than fail, because CI has no KiCad installed.
    /// </summary>
    public static string? KiCadCli =>
        Environment.GetEnvironmentVariable("KICADSHARP_KICAD_CLI") is { Length: > 0 } configured ? configured : null;
}
