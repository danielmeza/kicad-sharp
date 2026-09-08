namespace KiCadSharp.Tests;

/// <summary>
/// Locates the vendored fixtures, and gives a test a scratch directory it owns.
/// </summary>
/// <remarks>
/// The fixtures are copied to the output directory by the project file, so a test can hand a real
/// path to an external tool as well as read it. Nothing here reaches outside the repository: a test
/// that needs the wider corpus is opt-in through <see cref="KiCadCli"/>.
/// </remarks>
public static class TestData
{
    /// <summary>The directory holding the vendored fixtures.</summary>
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>A 108,583-byte, 35-symbol KiCad 10 symbol library holding 112 pins in 67 sub-units.</summary>
    public static string SymbolLibrary => Path.Combine(Root, "orbion.kicad_sym");

    /// <summary>A standalone KiCad 6+ <c>.kicad_mod</c>: the root token is <c>footprint</c>, not <c>module</c>.</summary>
    public static string Footprint => Path.Combine(Root, "LED_0603_1608Metric.kicad_mod");

    /// <summary>A 29,733-byte placed-but-unrouted board: 7 footprints, 16 layers, one net, no copper.</summary>
    public static string PowerInputBoard => Path.Combine(Root, "power-input.kicad_pcb");

    /// <summary>
    /// A 12,454-byte routed 4-layer board: 56 nets, 65 segments, 3 vias, a keepout zone and two
    /// footprints. The only fixture here that has copper on it.
    /// </summary>
    public static string ProbeBoard => Path.Combine(Root, "probe-4layer.kicad_pcb");

    /// <summary>
    /// A 2,539-byte empty 4-layer template: the only fixture with a <c>setup</c>, a 13-layer
    /// stackup and a title block.
    /// </summary>
    public static string StackupBoard => Path.Combine(Root, "orbion-4layer.kicad_pcb");

    /// <summary>
    /// A 5,986-byte board written by pcbnew 10.0.6 itself: <c>(version 20260206)</c>, every net
    /// named rather than numbered, no board-level net table, and a zone the filler filled. The only
    /// fixture here that any KiCad wrote.
    /// </summary>
    public static string Kicad10Board => Path.Combine(Root, "kicad10-pcbnew.kicad_pcb");

    /// <summary>
    /// A 1,865,463-byte KiCad 9 PCIe x4 adapter from Antmicro: 66 net rows, 571 segments, 216
    /// routed arcs, 92 vias, 5 filled zones over 4 copper layers, 20 footprints with 17 3D models,
    /// a 13-layer stackup and a title block. Apache-2.0; see the directory's README for provenance.
    /// </summary>
    public static string M2PcieAdapterBoard => Path.Combine(Root, "antmicro-m2-pcie-adapter", "m2-pcie-adapter.kicad_pcb");

    /// <summary>
    /// A 1,221,219-byte KiCad 9 Bitaxe Gamma miner: 102 net rows, 825 segments, 200 vias, 17 zones
    /// of which 16 are filled and one is a 5-layer keepout, and 147 footprints carrying 479 pads,
    /// 97 3D models and 913 properties. CERN-OHL-S-2.0; see the directory's README.
    /// </summary>
    public static string BitaxeGammaBoard => Path.Combine(Root, "bitaxe-gamma", "bitaxeGamma.kicad_pcb");

    /// <summary>
    /// A 333,929-byte board written by KiCad 10 itself: <c>(version 20260206)</c>, no board-level
    /// net table at all, every net named on the item that carries it, 6 <c>(generated …)</c>
    /// length-tuning patterns, 37 routed arcs and a zone the filler filled into 14 polygons.
    /// CERN-OHL-W-2.0; see the directory's README.
    /// </summary>
    public static string SNEdgeBoard => Path.Combine(Root, "snedge", "SNEdge.kicad_pcb");

    /// <summary>
    /// The three boards vendored from real, third-party hardware projects. They are the fixtures
    /// nobody here wrote, which is the whole point of them: everything else under
    /// <see cref="Root"/> was produced by one project's own tools and therefore agrees with itself.
    /// </summary>
    public static IEnumerable<string> RealWorldBoards => new[] { M2PcieAdapterBoard, BitaxeGammaBoard, SNEdgeBoard };

    /// <summary>Every <c>.kicad_pcb</c> fixture, for the tests that must hold for all of them.</summary>
    public static IEnumerable<string> Boards =>
        new[] { PowerInputBoard, ProbeBoard, StackupBoard, Kicad10Board }.Concat(RealWorldBoards);

    /// <summary>The hierarchy that instantiates one child sheet twice, so every designator is used twice.</summary>
    public static string DuplicateRefsRoot => Path.Combine(Root, "duplicate-refs", "duplicate-refs.kicad_sch");

    /// <summary>The child sheet of <see cref="DuplicateRefsRoot"/>: 45,693 bytes of wires, labels and junctions.</summary>
    public static string DuplicateRefsChild => Path.Combine(Root, "duplicate-refs", "power-input.kicad_sch");

    /// <summary>
    /// A 170,001-byte KiCad 10 sheet with 161 wires, 22 labels, 18 junctions, 8 texts, 8 rectangles,
    /// 4 bus aliases, 74 placed symbols and 19 cached library symbols.
    /// </summary>
    public static string Rs485Bridge => Path.Combine(Root, "orbion-rs485-bridge.kicad_sch");

    /// <summary>
    /// Every <c>.kicad_sch</c> fixture in the repository, so a round-trip test can be run over all of
    /// them rather than over whichever one was remembered.
    /// </summary>
    public static IEnumerable<string> Schematics
    {
        get
        {
            yield return Rs485Bridge;
            yield return DuplicateRefsRoot;
            yield return DuplicateRefsChild;
        }
    }

    /// <summary>
    /// Copies the whole <c>duplicate-refs</c> fixture into a fresh scratch directory and returns the
    /// path of the root schematic inside it. Annotation rewrites files, so no test may run against
    /// the fixture in place.
    /// </summary>
    public static string CopyDuplicateRefs(out string directory)
    {
        directory = NewScratchDirectory();
        var source = Path.Combine(Root, "duplicate-refs");
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        return Path.Combine(directory, "duplicate-refs.kicad_sch");
    }

    /// <summary>Creates an empty directory under the system temp path, unique to this call.</summary>
    public static string NewScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "kicadsharp-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A <c>kicad-cli</c> to shell out to, or <see langword="null"/>. Set <c>KICADSHARP_KICAD_CLI</c>
    /// to opt in; without it the tests that would use it return early rather than fail, because CI
    /// has no KiCad installed.
    /// </summary>
    public static string? KiCadCli
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("KICADSHARP_KICAD_CLI");
            return string.IsNullOrEmpty(configured) ? null : configured;
        }
    }
}
