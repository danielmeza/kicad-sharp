using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="KiCadBoard"/> against a board KiCad itself wrote, and against the board this library
/// writes when it is asked to make one.
/// </summary>
/// <remarks>
/// <para>
/// Every other <c>.kicad_pcb</c> fixture in this repository was produced by orbion-kicad's own
/// generators, which emit <c>(version 20241229)</c> — the KiCad 9 board format. That is a real
/// spelling and reading it matters, but it is not the spelling KiCad 10 writes, so a suite made only
/// of those files measures one half of the format and calls it the whole.
/// </para>
/// <para>
/// <c>kicad10-pcbnew.kicad_pcb</c> is the other half: pcbnew 10.0.6 saved it, so it carries
/// <c>(version 20260206)</c>, names every net by name, and has no board-level net table at all.
/// </para>
/// </remarks>
public class BoardFormatTests
{
    // ------------------------------------------------------- what KiCad 10 actually writes

    [Fact]
    public void Kicad10Fixture_IsTheMeasuredFile()
    {
        Assert.Equal(5_986, new FileInfo(TestData.Kicad10Board).Length);

        var root = SExpression.Load(TestData.Kicad10Board);
        Assert.Equal("kicad_pcb", root.Token);

        // pcbnew 10.0.6 stamps this, not the 20241229 the orbion generators write.
        Assert.Equal("20260206", root.GetChildValue("version"));
        Assert.Equal("pcbnew", root.GetChildValue("generator"));

        // The board-level (net n "NAME") table is gone in this format: copper carries the name.
        Assert.Empty(root.GetChildren("net"));
        Assert.Equal("GND", root.GetChildren("segment").First().GetChild("net")!.GetValue(0));

        Assert.Equal(20, root.GetChild("layers")!.Children.Count);
        Assert.Equal(2, root.GetChildren("segment").Count());
        Assert.Single(root.GetChildren("arc"));
        Assert.Single(root.GetChildren("via"));
        Assert.Single(root.GetChildren("zone"));
        Assert.Single(root.GetChildren("zone").First().GetChildren("filled_polygon"));
        Assert.Single(root.GetChildren("footprint"));
        Assert.Single(root.GetChildren("dimension"));
        Assert.Single(root.GetChildren("group"));
    }

    [Fact]
    public void Board_ReadsTheNetsOfABoardKiCadWrote()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);

        // There is no net table to read, and saying so is the point: an empty Nets is the file's
        // answer here, not a parse failure.
        Assert.Empty(board.Nets);

        // The copper still belongs to a net, and this is where the name lives now.
        Assert.Equal("GND", board.Segments[0].NetName);
        Assert.Equal("GND", board.Segments[1].NetName);
        Assert.Equal("+3V3", board.TrackArcs[0].NetName);
        Assert.Equal("GND", board.Vias[0].NetName);
        Assert.Equal(new[] { "GND", "+3V3" }, board.Footprints[0].Pads.Select(p => p.Net));

        // A zone names its net the same way; KiCad 10 writes no (net_name "…") beside it.
        var zone = Assert.Single(board.Zones);
        Assert.Equal("GND", zone.NetName);
        Assert.Null(zone.Node.GetChild("net_name"));

        // And the KiCad 9 spelling still reads as a code, because both files are real.
        var nine = KiCadBoard.Load(TestData.ProbeBoard);
        Assert.Equal(1, nine.Segments[0].Net);
        Assert.Equal("GND", nine.GetNet(nine.Segments[0].Net)!.Name);
        Assert.Null(nine.Segments[0].NetName);
    }

    [Fact]
    public void Board_ReadsTheRestOfABoardKiCadWrote()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);

        Assert.Equal(20, board.Layers.Count);
        Assert.Equal(2, board.Layers.Count(l => l.IsCopper));
        Assert.Equal("Edge.Cuts", board.GraphicRectangles[0].Layer);
        Assert.Equal("REVIEW", board.Texts[0].Text);

        // The zone the filler filled: an outline the designer drew, and one layer of computed copper.
        var zone = Assert.Single(board.Zones);
        Assert.True(zone.IsFilled);
        Assert.Equal(4, zone.Points.Count);
        var filled = Assert.Single(zone.FilledPolygons);
        Assert.Equal("F.Cu", filled.Layer);
        Assert.Equal(16, filled.Points.Count);

        // The three forms no orbion fixture carries, in KiCad's own spelling rather than a
        // hand-written approximation of it.
        var arc = Assert.Single(board.TrackArcs);
        Assert.Equal(new KiCadPosition(13, 12), arc.Mid);
        Assert.Equal("B.Cu", arc.Layer);

        var dimension = Assert.Single(board.Dimensions);
        Assert.Equal("aligned", dimension.Type);
        Assert.Equal(2, dimension.Points.Count);
        Assert.NotNull(dimension.Text);

        var group = Assert.Single(board.Groups);
        Assert.Equal("Review group", group.Name);
        Assert.Equal(2, group.Members.Count);
        Assert.Contains(arc.Uuid, group.Members);
    }

    [Fact]
    public void Board_Save_OfABoardKiCadWrote_IsByteIdentical()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        BoardDocumentTests.ReadEverythingOn(board);

        var output = Path.Combine(TestData.NewScratchDirectory(), "kicad10-pcbnew.kicad_pcb");
        board.Save(output);

        Assert.Equal(5_986, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(TestData.Kicad10Board), File.ReadAllBytes(output));
    }

    [Fact]
    public void Board_Save_AfterOneEditToABoardKiCadWrote_ChangesOnlyThatPropertysBytes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);

        // 0.25 -> 0.35 on the first segment: same digits, one of them different.
        board.Segments[0].Width = 0.35;

        var output = Path.Combine(TestData.NewScratchDirectory(), "kicad10-pcbnew.kicad_pcb");
        board.Save(output);

        var before = File.ReadAllText(TestData.Kicad10Board);
        var after = File.ReadAllText(output);

        Assert.Equal(before.Length, after.Length);
        Assert.Equal(1, before.Zip(after).Count(pair => pair.First != pair.Second));
    }

    // ------------------------------------------------ a flag that reads true, not only false

    [Fact]
    public void Board_ReadsAKiCadFlagThatIsSet()
    {
        // Every flag in the three orbion fixtures is written `no`, so a ReadFlag that answered
        // `false` to everything would satisfy all of them. This is the other half.
        var board = KiCadBoard.Parse(
            """
            (kicad_pcb
              (version 20241229)
              (general (legacy_teardrops yes))
              (segment (start 0 0) (end 1 0) (width 0.2) (layer "F.Cu") (net 0) (locked yes))
              (via (at 5 5) (size 0.6) (drill 0.3) (free yes) (remove_unused_layers yes))
              (zone (filled_areas_thickness yes))
              (setup (stackup (dielectric_constraints yes)))
              (embedded_fonts yes))
            """);

        Assert.True(board.General!.LegacyTeardrops);
        Assert.True(board.Segments[0].Locked);
        Assert.True(board.Vias[0].Free);
        Assert.True(board.Vias[0].RemoveUnusedLayers);
        Assert.True(board.Zones[0].FilledAreasThickness);
        Assert.True(board.Setup!.Stackup!.DielectricConstraints);
        Assert.True(board.EmbeddedFonts);

        // The KiCad 6 spelling of the same thing: a bare flag with no value.
        var bare = KiCadBoard.Parse("(kicad_pcb (segment (locked)) (embedded_fonts))");
        Assert.True(bare.Segments[0].Locked);
        Assert.True(bare.EmbeddedFonts);
    }

    // --------------------------------------- a board this library builds is one KiCad can open

    [Fact]
    public void NewBoard_CarriesALayerTableKiCadWillAccept()
    {
        // MEASURED: with an empty (layers) form, `kicad-cli pcb drc` on the saved file says
        //     Failed to load board: 0 is not a valid layer count in '…', line 8, offset 9.
        // and pcbnew.LoadBoard returns None. A board with no layer table is not a board.
        var board = new KiCadBoard("kicad-sharp-tests");

        Assert.Equal(16, board.Layers.Count);
        Assert.Equal(2, board.Layers.Count(l => l.IsCopper));
        Assert.Equal(new[] { "F.Cu", "B.Cu" }, board.Layers.Where(l => l.IsCopper).Select(l => l.Name));
        Assert.NotNull(board.GetLayer("Edge.Cuts"));
        Assert.Equal("F.Silkscreen", board.GetLayer("F.SilkS")!.UserName);

        // It survives its own round trip, table and all.
        var output = Path.Combine(TestData.NewScratchDirectory(), "built.kicad_pcb");
        board.Save(output);
        Assert.Equal(16, KiCadBoard.Load(output).Layers.Count);
    }

    // ------------------------------------------- a placed footprint is somewhere on the board

    [Fact]
    public void Board_ReadsWhereAFootprintIsPlaced()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);

        var footprint = Assert.Single(board.Footprints);
        Assert.Equal(new KiCadPosition(8, 10), footprint.Position);
        Assert.Equal("b0000000-0000-4000-8000-000000000010", footprint.Uuid);
        Assert.False(footprint.Locked);

        // Seven footprints on a real board, each somewhere different.
        var placed = KiCadBoard.Load(TestData.PowerInputBoard);
        Assert.Equal(7, placed.Footprints.Select(f => f.Position).Distinct().Count());
    }
}
