using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="KiCadBoard"/>, over real KiCad 10 boards.
/// </summary>
/// <remarks>
/// <para>
/// Every count here is measured twice: once from the raw s-expression tree, which has no model in
/// between to lose anything, and once through the typed view. A number that only appears on one
/// side would be an assertion about this library's opinion rather than about the file.
/// </para>
/// <para>
/// The property underneath all of it is the same one the document layer exists for: reading a board
/// through the views and saving it must reproduce the file byte for byte, and changing one property
/// must change one property's worth of bytes and nothing else.
/// </para>
/// </remarks>
public class BoardDocumentTests
{
    // --------------------------------------------------------------------- the fixtures themselves

    [Fact]
    public void PowerInputFixture_IsTheMeasuredFile()
    {
        Assert.Equal(29_733, new FileInfo(TestData.PowerInputBoard).Length);

        var root = SExpression.Load(TestData.PowerInputBoard);
        Assert.Equal("kicad_pcb", root.Token);
        Assert.Equal(7, root.GetChildren("footprint").Count());
        Assert.Equal(16, root.GetChild("layers")!.Children.Count);
        Assert.Single(root.GetChildren("net"));
        Assert.Single(root.GetChildren("gr_text"));

        // Placed, not routed: no copper of any kind, and no setup or title block either.
        Assert.Empty(root.GetChildren("segment"));
        Assert.Empty(root.GetChildren("via"));
        Assert.Empty(root.GetChildren("zone"));
        Assert.Null(root.GetChild("setup"));
        Assert.Null(root.GetChild("title_block"));
    }

    [Fact]
    public void ProbeFixture_IsTheMeasuredFile()
    {
        Assert.Equal(12_454, new FileInfo(TestData.ProbeBoard).Length);

        var root = SExpression.Load(TestData.ProbeBoard);
        Assert.Equal(22, root.GetChild("layers")!.Children.Count);
        Assert.Equal(56, root.GetChildren("net").Count());
        Assert.Equal(65, root.GetChildren("segment").Count());
        Assert.Equal(3, root.GetChildren("via").Count());
        Assert.Single(root.GetChildren("zone"));
        Assert.Equal(2, root.GetChildren("footprint").Count());
        Assert.Single(root.GetChildren("gr_line"));
        Assert.Equal(2, root.GetChildren("gr_rect").Count());
        Assert.Single(root.GetChildren("gr_text"));
    }

    [Fact]
    public void StackupFixture_IsTheMeasuredFile()
    {
        Assert.Equal(2_539, new FileInfo(TestData.StackupBoard).Length);

        var root = SExpression.Load(TestData.StackupBoard);
        Assert.Equal(22, root.GetChild("layers")!.Children.Count);
        Assert.Equal(13, root.GetChild("setup")!.GetChild("stackup")!.GetChildren("layer").Count());
        Assert.NotNull(root.GetChild("title_block"));
        Assert.Empty(root.GetChildren("footprint"));
    }

    // ------------------------------------------------------------- the same counts, through the view

    [Fact]
    public void Board_ReadsTheSameCountsAsTheTree()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        Assert.Equal(22, board.Layers.Count);
        Assert.Equal(56, board.Nets.Count);
        Assert.Equal(65, board.Segments.Count);
        Assert.Equal(3, board.Vias.Count);
        Assert.Single(board.Zones);
        Assert.Equal(2, board.Footprints.Count);
        Assert.Single(board.GraphicLines);
        Assert.Equal(2, board.GraphicRectangles.Count);
        Assert.Single(board.Texts);

        // Nothing invented where the file has nothing.
        Assert.Empty(board.TrackArcs);
        Assert.Empty(board.GraphicCircles);
        Assert.Empty(board.GraphicArcs);
        Assert.Empty(board.GraphicPolygons);
        Assert.Empty(board.GraphicCurves);
        Assert.Empty(board.Dimensions);
        Assert.Empty(board.Groups);
        Assert.Null(board.Setup);
        Assert.Null(board.TitleBlock);
    }

    [Fact]
    public void Board_ReadsTheFileHeader()
    {
        var board = KiCadBoard.Load(TestData.PowerInputBoard);

        Assert.Equal("20241229", board.Version);
        Assert.Equal("orbion-blocks", board.Generator);
        Assert.Equal("10.0", board.GeneratorVersion);
        Assert.Equal("A4", board.Paper);
        Assert.Equal(7, board.Footprints.Count);
        Assert.Single(board.Nets);

        Assert.NotNull(board.General);
        Assert.Equal(1.6, board.General.Thickness);
        Assert.False(board.General.LegacyTeardrops);

        // A4 here, A3 on the template: the sheet size is per file, not a library default.
        Assert.Equal("A3", KiCadBoard.Load(TestData.StackupBoard).Paper);
    }

    [Fact]
    public void Board_RefusesAFileThatIsNotABoard()
    {
        // A .kicad_sym parses perfectly well as an s-expression; it is just not a board, and reading
        // it as one would report every count as zero rather than say so.
        var error = Assert.Throws<InvalidOperationException>(() => KiCadBoard.Load(TestData.SymbolLibrary));
        Assert.Contains("kicad_symbol_lib", error.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------- detail: nets

    [Fact]
    public void Board_ReadsNetDetail()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        Assert.Equal(56, board.Nets.Count);

        // Net 0 is the no-net net; every board has it and its name is the empty string.
        Assert.Equal(0, board.Nets[0].Code);
        Assert.Equal(string.Empty, board.Nets[0].Name);

        var ground = board.GetNet("GND");
        Assert.NotNull(ground);
        Assert.Equal(1, ground.Code);
        Assert.Equal(ground, board.GetNet(1));

        Assert.Equal(new[] { "", "GND", "+3V3", "+12V", "HV_L" }, board.Nets.Take(5).Select(n => n.Name));
        Assert.Equal(55, board.Nets[^1].Code);
        Assert.Equal("HV_S2", board.Nets[^1].Name);
    }

    // ------------------------------------------------------------------------------ detail: layers

    [Fact]
    public void Board_ReadsLayerDetail()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        Assert.Equal(22, board.Layers.Count);
        Assert.Equal(4, board.Layers.Count(l => l.IsCopper));

        // The layer table's own order is the stackup order, not the ordinal order: F.Cu, the two
        // inner layers, then B.Cu, whose ordinal is 2.
        Assert.Equal(new[] { "F.Cu", "In1.Cu", "In2.Cu", "B.Cu" }, board.Layers.Where(l => l.IsCopper).Select(l => l.Name));
        Assert.Equal(new[] { 0, 4, 6, 2 }, board.Layers.Where(l => l.IsCopper).Select(l => l.Ordinal));

        var inner = board.GetLayer("In1.Cu");
        Assert.NotNull(inner);
        Assert.Equal(4, inner.Ordinal);
        Assert.Equal("power", inner.Type);
        Assert.Null(inner.UserName);

        // A renamed layer carries both names: the canonical one everything else refers to, and the
        // one the editor shows.
        var silk = board.GetLayer("F.SilkS");
        Assert.NotNull(silk);
        Assert.Equal(5, silk.Ordinal);
        Assert.Equal("user", silk.Type);
        Assert.Equal("F.Silkscreen", silk.UserName);
        Assert.False(silk.IsCopper);

        // 16 layers on the two-layer board, 22 on the four-layer one.
        Assert.Equal(16, KiCadBoard.Load(TestData.PowerInputBoard).Layers.Count);
    }

    // ------------------------------------------------------------------------------ detail: copper

    [Fact]
    public void Board_ReadsTrackSegmentDetail()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        Assert.Equal(65, board.Segments.Count);

        var first = board.Segments[0];
        Assert.Equal(new KiCadPosition(10, 10), first.Start);
        Assert.Equal(new KiCadPosition(40, 10), first.End);
        Assert.Equal(0.05, first.Width);
        Assert.Equal("F.Cu", first.Layer);
        Assert.Equal(1, first.Net);
        Assert.Equal("GND", board.GetNet(first.Net)!.Name);
        Assert.Equal("f0000000-0000-4000-8000-000000000101", first.Uuid);
        Assert.False(first.Locked);
        Assert.Equal(30, first.Length);

        // 64 of the 65 are on the front copper; the one exception is the inner-layer track.
        Assert.Equal(64, board.Segments.Count(s => s.Layer == "F.Cu"));
        Assert.Single(board.Segments, s => s.Layer == "In1.Cu");
        Assert.Equal(3, board.Segments.Count(s => s.Net == 1));
        Assert.Equal(new[] { 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.8 }, board.Segments.Select(s => s.Width).Distinct().Order());
    }

    [Fact]
    public void Board_ReadsViaDetail()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        Assert.Equal(3, board.Vias.Count);

        var via = board.Vias[0];
        Assert.Equal(new KiCadPosition(60, 10), via.Position);
        Assert.Equal(0.3, via.Size);
        Assert.Equal(0.15, via.Drill);
        Assert.Equal(new[] { "F.Cu", "B.Cu" }, via.Layers);
        Assert.Equal(1, via.Net);
        Assert.Equal("f0000000-0000-4000-8000-000000000201", via.Uuid);

        // KiCad omits the type for a through via, which is what all three of these are.
        Assert.All(board.Vias, v => Assert.Equal("through", v.ViaType));
    }

    [Fact]
    public void Board_ReadsZoneDetail()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        var zone = Assert.Single(board.Zones);

        Assert.Equal(0, zone.Net);
        Assert.Equal(string.Empty, zone.NetName);
        Assert.Equal(new[] { "F.Cu" }, zone.Layers);
        Assert.Equal("Antenna_Keepout", zone.Name);
        Assert.Equal("f0000000-0000-4000-8000-000000000300", zone.Uuid);
        Assert.Equal(0, zone.Priority);
        Assert.Equal("edge", zone.HatchStyle);
        Assert.Equal(0.508, zone.HatchPitch);
        Assert.Equal(0.25, zone.MinThickness);
        Assert.False(zone.FilledAreasThickness);

        // A rule area, so it pours nothing: the outline exists, the computed copper does not.
        Assert.True(zone.IsKeepout);
        Assert.False(zone.IsFilled);
        Assert.Empty(zone.FilledPolygons);

        Assert.Equal(
            new[]
            {
                new KiCadPosition(70, 40),
                new KiCadPosition(90, 40),
                new KiCadPosition(90, 55),
                new KiCadPosition(70, 55),
            },
            zone.Points);

        Assert.NotNull(zone.Fill);
        Assert.False(zone.Fill.Enabled);
        Assert.Equal(0.508, zone.Fill.ThermalGap);
        Assert.Equal(0.508, zone.Fill.ThermalBridgeWidth);
    }

    // ----------------------------------------------------------------------- detail: setup, stackup

    [Fact]
    public void Board_ReadsTheSetupAndItsStackup()
    {
        var board = KiCadBoard.Load(TestData.StackupBoard);

        Assert.NotNull(board.Setup);
        var stackup = board.Setup.Stackup;
        Assert.NotNull(stackup);

        // 13 stackup layers against 22 table layers: the stackup adds the three dielectrics and
        // leaves out everything that is not physically in the laminate.
        Assert.Equal(13, stackup.Layers.Count);
        Assert.Equal(22, board.Layers.Count);
        Assert.Equal("HASL", stackup.CopperFinish);
        Assert.False(stackup.DielectricConstraints);

        Assert.Equal(4, stackup.Layers.Count(l => l.IsCopper));
        Assert.Equal(3, stackup.Layers.Count(l => l.Material == "FR4"));

        var outer = stackup.GetLayer("F.Cu");
        Assert.NotNull(outer);
        Assert.Equal("copper", outer.Type);
        Assert.Equal(0.035, outer.Thickness);

        var core = stackup.GetLayer("dielectric 2");
        Assert.NotNull(core);
        Assert.Equal("core", core.Type);
        Assert.Equal(1.065, core.Thickness);
        Assert.Equal("FR4", core.Material);
        Assert.False(core.IsCopper);

        // The silk, mask and paste layers carry no thickness, so the sum lands just under the
        // declared 1.6 mm finished board rather than on it.
        Assert.Equal(1.5908, stackup.TotalThickness, 4);
        Assert.Equal(1.6, board.General!.Thickness);
    }

    [Fact]
    public void Board_ReadsTheTitleBlock()
    {
        var board = KiCadBoard.Load(TestData.StackupBoard);

        Assert.NotNull(board.TitleBlock);
        Assert.Equal("Orbion 4-layer board", board.TitleBlock.Title);
        Assert.Equal("Orbion", board.TitleBlock.Company);
        Assert.Single(board.TitleBlock.Comments);
        Assert.Equal(1, board.TitleBlock.Comments[0].Number);
        Assert.Equal("The default for a real product. JLC04161H stackup.", board.TitleBlock.GetComment(1));

        // Blank fields are absent rather than empty, so they read null.
        Assert.Null(board.TitleBlock.Date);
        Assert.Null(board.TitleBlock.Revision);
        Assert.Null(board.TitleBlock.GetComment(2));
    }

    // ------------------------------------------------------------------------- detail: the drawings

    [Fact]
    public void Board_ReadsGraphicsAndText()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        var outline = board.GraphicRectangles[0];
        Assert.Equal(new KiCadPosition(0, 0), outline.Start);
        Assert.Equal(new KiCadPosition(100, 60), outline.End);
        Assert.Equal("Edge.Cuts", outline.Layer);
        Assert.Equal(0.1, outline.Width);
        Assert.Equal(100, outline.Width2D);
        Assert.Equal(60, outline.Height2D);
        Assert.NotNull(outline.Stroke);
        Assert.Equal("default", outline.Stroke.Type);
        Assert.NotNull(outline.Fill);

        // KiCad 7+ writes the board and footprint shapes' fill as a bare (fill no), not as the
        // (fill (type none)) a symbol library uses. Both are the same question with a different word.
        Assert.Equal("no", outline.Fill.Type);
        Assert.False(outline.Fill.IsFilled);

        var line = Assert.Single(board.GraphicLines);
        Assert.Equal(new KiCadPosition(18, 40), line.Start);
        Assert.Equal(new KiCadPosition(26, 40), line.End);
        Assert.Equal(0.2, line.Width);
        Assert.Equal("F.SilkS", line.Layer);

        // An open shape has no (fill ...) form, and reading the property does not give it one.
        Assert.Null(line.Fill);

        var text = Assert.Single(board.Texts);
        Assert.Equal("tiny silk", text.Text);
        Assert.Equal(new KiCadPosition(10, 50), text.Position);
        Assert.Equal("F.SilkS", text.Layer);
        Assert.NotNull(text.FontEffects);
        Assert.Equal(new KiCadSize(0.5, 0.5), text.FontEffects.Size);
        Assert.Equal(0.08, text.FontEffects.Thickness);
    }

    // -------------------------------------------------- a footprint on a board is just a footprint

    [Fact]
    public void Board_Footprints_AreTheSameViewAKicadModGives()
    {
        var board = KiCadBoard.Load(TestData.PowerInputBoard);
        var library = KiCadFootprintLibrary.Load(TestData.PowerInputBoard);

        // Same file, two document types, one answer.
        Assert.Equal(7, board.Footprints.Count);
        Assert.Equal(library.Footprints.Count, board.Footprints.Count);
        Assert.Equal(library.Footprints.Select(f => f.Id), board.Footprints.Select(f => f.Id));

        var terminal = board.Footprints[0];
        Assert.Equal("orbion:TerminalBlock_Phoenix_MKDS-1,5-2-5.08_1x02_P5.08mm_Horizontal", terminal.Id);
        Assert.Equal("F.Cu", terminal.Layer);
        Assert.Equal("J90", terminal.GetPropertyValue("Reference"));
        Assert.Equal("VIN 12-24V 5.08mm", terminal.GetPropertyValue("Value"));
        Assert.Equal(3, terminal.Properties.Count);
        Assert.Equal(2, terminal.Pads.Count);
        Assert.Equal(20, terminal.Lines.Count);
        Assert.Equal(new[] { "through_hole" }, terminal.Attributes);
        Assert.Equal(15, board.Footprints.Sum(f => f.Pads.Count));

        // Asking twice gives two wrappers over one node, which is the whole point of a view.
        Assert.NotSame(board.Footprints[0], board.Footprints[0]);
        Assert.Same(board.Footprints[0].Node, board.Footprints[0].Node);
        Assert.Equal(board.Footprints[0], board.Footprints[0]);

        // And it really is a .kicad_mod's worth of footprint: written out on its own it loads as one.
        var output = Path.Combine(TestData.NewScratchDirectory(), "extracted.kicad_mod");
        KiCadFootprintLibrary.SaveFootprint(terminal, output);

        var standalone = KiCadFootprintLibrary.Load(output);
        Assert.True(standalone.IsSingleFootprint);
        var extracted = Assert.Single(standalone.Footprints);
        Assert.Equal(terminal.Id, extracted.Id);
        Assert.Equal(terminal.Pads.Count, extracted.Pads.Count);
        Assert.Equal(terminal.Lines.Count, extracted.Lines.Count);
        Assert.Equal("J90", extracted.GetPropertyValue("Reference"));
    }

    // ------------------------------------------------------------------- an untouched save is a copy

    [Theory]
    [InlineData("power-input.kicad_pcb", 29_733)]
    [InlineData("probe-4layer.kicad_pcb", 12_454)]
    [InlineData("orbion-4layer.kicad_pcb", 2_539)]
    public void Board_Save_AfterReadingEverything_IsByteIdentical(string fixture, int length)
    {
        var source = Path.Combine(TestData.Root, fixture);
        var board = KiCadBoard.Load(source);

        // Read every property the view offers, including the optional forms. A getter that quietly
        // created the form it was asked for would show up right here as a changed file.
        ReadEverything(board);

        var output = Path.Combine(TestData.NewScratchDirectory(), fixture);
        board.Save(output);

        Assert.Equal(length, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
    }

    // -------------------------------------------------------- one edit is one edit's worth of bytes

    [Fact]
    public void Board_Save_AfterOneEdit_ChangesOnlyThatPropertysBytes()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        // `power` -> `signal` in (4 "In1.Cu" power): five characters become six.
        board.GetLayer("In1.Cu")!.Type = "signal";

        var output = Path.Combine(TestData.NewScratchDirectory(), "probe-4layer.kicad_pcb");
        board.Save(output);

        var before = File.ReadAllText(TestData.ProbeBoard);
        var after = File.ReadAllText(output);

        Assert.Equal(12_454, before.Length);
        Assert.Equal(12_455, after.Length);
        Assert.Equal(12_455, new FileInfo(output).Length);

        // Everything outside one span is the same span of bytes, in the same place.
        var (prefix, suffix) = CommonEnds(before, after);
        Assert.Equal("power", before[prefix..(before.Length - suffix)]);
        Assert.Equal("signal", after[prefix..(after.Length - suffix)]);
    }

    [Fact]
    public void Board_Save_AfterOneNumericEdit_KeepsTheFileLength()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        // (width 0.05) -> (width 0.15) on the first segment: same digits, one of them different.
        board.Segments[0].Width = 0.15;

        var output = Path.Combine(TestData.NewScratchDirectory(), "probe-4layer.kicad_pcb");
        board.Save(output);

        var before = File.ReadAllText(TestData.ProbeBoard);
        var after = File.ReadAllText(output);

        Assert.Equal(before.Length, after.Length);
        Assert.Equal(1, before.Zip(after).Count(pair => pair.First != pair.Second));
    }

    // ------------------------------------------------------------------------------- a round trip

    [Fact]
    public void Board_Modify_Save_Reload_ReadsBackWhatWasWritten()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);

        board.GetNet(5)!.Name = "SIGNAL";
        board.Segments[0].Width = 0.25;
        board.Vias[0].Size = 0.45;
        board.Vias[0].ViaType = "micro";
        board.Zones[0].Priority = 2;
        board.Zones[0].Name = "Antenna_Exclusion";
        board.AddNet(56, "SPARE");
        board.RequireTitleBlock().Title = "Design rule probe";

        var output = Path.Combine(TestData.NewScratchDirectory(), "probe-4layer.kicad_pcb");
        board.Save(output);

        var reloaded = KiCadBoard.Load(output);

        Assert.Equal("SIGNAL", reloaded.GetNet(5)!.Name);
        Assert.Equal(0.25, reloaded.Segments[0].Width);
        Assert.Equal(0.45, reloaded.Vias[0].Size);
        Assert.Equal("micro", reloaded.Vias[0].ViaType);
        Assert.Equal(2, reloaded.Zones[0].Priority);
        Assert.Equal("Antenna_Exclusion", reloaded.Zones[0].Name);
        Assert.Equal(57, reloaded.Nets.Count);
        Assert.Equal("SPARE", reloaded.GetNet(56)!.Name);
        Assert.Equal("Design rule probe", reloaded.TitleBlock!.Title);

        // Everything that was not touched came through unchanged, including the 63 segments and the
        // two vias nobody edited.
        Assert.Equal(65, reloaded.Segments.Count);
        Assert.Equal(3, reloaded.Vias.Count);
        Assert.Equal("through", reloaded.Vias[1].ViaType);
        Assert.Equal(22, reloaded.Layers.Count);
        Assert.Equal(2, reloaded.Footprints.Count);
        Assert.Equal(4, reloaded.Zones[0].Points.Count);
    }

    [Fact]
    public void Board_ViaType_ThroughIsWrittenByLeavingItOut()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);
        var via = board.Vias[0];

        via.ViaType = "blind";
        Assert.Equal("blind", via.ViaType);
        Assert.Equal("blind", via.Node.GetValue(0));

        via.ViaType = "through";
        Assert.Equal("through", via.ViaType);
        Assert.Null(via.Node.GetValue(0));

        // So a board that goes back to `through` is spelled the way KiCad spells it, with no word
        // there at all. The via's own bytes are re-laid-out because the node was touched — the
        // writer only reuses source text for nodes nobody wrote to — but nothing else in the file is.
        var output = Path.Combine(TestData.NewScratchDirectory(), "probe-4layer.kicad_pcb");
        board.Save(output);

        var reloaded = KiCadBoard.Load(output);
        Assert.Equal(3, reloaded.Vias.Count);
        Assert.All(reloaded.Vias, v => Assert.Equal("through", v.ViaType));
        Assert.Null(reloaded.Vias[0].Node.GetValue(0));
        Assert.Equal(new KiCadPosition(60, 10), reloaded.Vias[0].Position);
        Assert.Equal(65, reloaded.Segments.Count);
    }

    // ------------------------------------------------- the forms no fixture in this repository has

    [Fact]
    public void Board_ReadsTheFormsTheFixturesDoNotCarry()
    {
        // Nothing in orbion-kicad uses a track arc, a board arc, a curve, a dimension, a group or a
        // filled zone, so these are hand-written rather than measured — which is why they are in
        // their own test and the numbers above are not.
        var board = KiCadBoard.Parse(
            """
            (kicad_pcb
              (version 20241229)
              (generator "kicad-sharp-tests")
              (net 0 "")
              (net 1 "GND")
              (arc (start 1 1) (mid 2 1.5) (end 3 1) (width 0.25) (layer "B.Cu") (net 1)
                   (uuid "aaaaaaaa-0000-4000-8000-000000000001"))
              (via micro (at 5 5) (size 0.3) (drill 0.15) (layers "F.Cu" "In1.Cu") (net 1))
              (gr_circle (center 10 10) (end 13 10) (stroke (width 0.15) (type solid)) (fill no)
                         (layer "Edge.Cuts"))
              (gr_arc (start 0 0) (mid 1 1) (end 2 0) (stroke (width 0.12) (type default))
                      (layer "F.SilkS"))
              (gr_poly (pts (xy 0 0) (xy 5 0) (xy 5 5)) (stroke (width 0.1) (type solid))
                       (fill solid) (layer "F.Cu"))
              (gr_curve (pts (xy 0 0) (xy 1 2) (xy 3 2) (xy 4 0))
                        (stroke (width 0.1) (type default)) (layer "Dwgs.User"))
              (dimension (type aligned) (layer "Dwgs.User")
                         (uuid "aaaaaaaa-0000-4000-8000-000000000002")
                         (pts (xy 0 0) (xy 20 0)) (height 5)
                         (gr_text "20.0000 mm" (at 10 -5 0) (layer "Dwgs.User")))
              (zone (net 1) (net_name "GND") (layers "F.Cu" "B.Cu") (priority 3)
                    (min_thickness 0.2)
                    (fill yes (thermal_gap 0.4) (thermal_bridge_width 0.4))
                    (polygon (pts (xy 0 0) (xy 10 0) (xy 10 10) (xy 0 10)))
                    (filled_polygon (layer "F.Cu") (pts (xy 0.2 0.2) (xy 9.8 0.2) (xy 9.8 9.8)))
                    (filled_polygon (layer "B.Cu") (pts (xy 0.3 0.3) (xy 9.7 0.3) (xy 9.7 9.7))))
              (group "Power stage" (uuid "aaaaaaaa-0000-4000-8000-000000000003")
                     (members "aaaaaaaa-0000-4000-8000-000000000001"
                              "aaaaaaaa-0000-4000-8000-000000000002"))
              (embedded_fonts no))
            """);

        var arc = Assert.Single(board.TrackArcs);
        Assert.Equal(new KiCadPosition(1, 1), arc.Start);
        Assert.Equal(new KiCadPosition(2, 1.5), arc.Mid);
        Assert.Equal(new KiCadPosition(3, 1), arc.End);
        Assert.Equal(0.25, arc.Width);
        Assert.Equal("B.Cu", arc.Layer);
        Assert.Equal(1, arc.Net);

        // A via that does carry its type, unlike the three in probe-4layer.
        var via = Assert.Single(board.Vias);
        Assert.Equal("micro", via.ViaType);
        Assert.Equal(new[] { "F.Cu", "In1.Cu" }, via.Layers);

        var circle = Assert.Single(board.GraphicCircles);
        Assert.Equal(new KiCadPosition(10, 10), circle.Center);
        Assert.Equal(3, circle.Radius);

        var boardArc = Assert.Single(board.GraphicArcs);
        Assert.Equal(new KiCadPosition(1, 1), boardArc.Mid);
        Assert.Equal(0.12, boardArc.Width);

        var poly = Assert.Single(board.GraphicPolygons);
        Assert.Equal(3, poly.Points.Count);
        Assert.Equal(new KiCadPosition(5, 5), poly.Points[2]);
        Assert.Equal("solid", poly.Fill!.Type);
        Assert.True(poly.Fill.IsFilled);

        var curve = Assert.Single(board.GraphicCurves);
        Assert.Equal(4, curve.Points.Count);
        Assert.Equal(new KiCadPosition(4, 0), curve.Points[3]);

        var dimension = Assert.Single(board.Dimensions);
        Assert.Equal("aligned", dimension.Type);
        Assert.Equal("Dwgs.User", dimension.Layer);
        Assert.Equal(2, dimension.Points.Count);
        Assert.Equal(new KiCadPosition(20, 0), dimension.Points[1]);
        Assert.Equal(5, dimension.Height);
        Assert.NotNull(dimension.Text);
        Assert.Equal("20.0000 mm", dimension.Text.Text);

        var zone = Assert.Single(board.Zones);
        Assert.Equal(1, zone.Net);
        Assert.Equal("GND", zone.NetName);
        Assert.Equal(new[] { "F.Cu", "B.Cu" }, zone.Layers);
        Assert.Equal(3, zone.Priority);
        Assert.False(zone.IsKeepout);
        Assert.True(zone.IsFilled);
        Assert.Equal(4, zone.Points.Count);
        Assert.Equal(2, zone.FilledPolygons.Count);
        Assert.Equal("B.Cu", zone.FilledPolygons[1].Layer);
        Assert.Equal(3, zone.FilledPolygons[1].Points.Count);
        Assert.Equal(0.4, zone.Fill!.ThermalGap);

        var group = Assert.Single(board.Groups);
        Assert.Equal("Power stage", group.Name);
        Assert.Equal("aaaaaaaa-0000-4000-8000-000000000003", group.Uuid);
        Assert.Equal(2, group.Members.Count);
        Assert.Equal(arc.Uuid, group.Members[0]);

        Assert.False(board.EmbeddedFonts);
    }

    [Fact]
    public void ABoardBuiltInMemory_StillSaves()
    {
        var board = new KiCadBoard("kicad-sharp-tests");
        board.AddNet(0, string.Empty);
        board.AddNet(1, "GND");

        var segment = board.Segments.Add();
        segment.Start = new KiCadPosition(0, 0);
        segment.End = new KiCadPosition(10, 0);
        segment.Width = 0.25;
        segment.Layer = "F.Cu";
        segment.Net = 1;

        var stackup = board.RequireSetup().RequireStackup();
        stackup.CopperFinish = "ENIG";

        board.RequireTitleBlock().SetComment(1, "Built, not loaded.");

        var output = Path.Combine(TestData.NewScratchDirectory(), "built.kicad_pcb");
        board.Save(output);

        var reloaded = KiCadBoard.Load(output);
        Assert.Equal("kicad-sharp-tests", reloaded.Generator);

        // The layer table comes with it. Without one KiCad refuses the file outright — see
        // BoardFormatTests.NewBoard_CarriesALayerTableKiCadWillAccept for the measured error.
        Assert.Equal(16, reloaded.Layers.Count);
        Assert.Equal(2, reloaded.Layers.Count(l => l.IsCopper));
        Assert.Equal(2, reloaded.Nets.Count);
        var reloadedSegment = Assert.Single(reloaded.Segments);
        Assert.Equal(new KiCadPosition(10, 0), reloadedSegment.End);
        Assert.Equal(0.25, reloadedSegment.Width);
        Assert.Equal(1, reloadedSegment.Net);
        Assert.Equal("ENIG", reloaded.Setup!.Stackup!.CopperFinish);
        Assert.Equal("Built, not loaded.", reloaded.TitleBlock!.GetComment(1));
        Assert.Equal(10, reloadedSegment.Length);
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>
    /// Touches every readable property on a board and everything hanging off it. It asserts nothing:
    /// its only job is to prove, through the byte-identical save that follows it, that reading a
    /// board does not change it.
    /// </summary>
    internal static void ReadEverythingOn(KiCadBoard board) => ReadEverything(board);

    private static void ReadEverything(KiCadBoard board)
    {
        _ = board.Version;
        _ = board.Generator;
        _ = board.GeneratorVersion;
        _ = board.Paper;
        _ = board.EmbeddedFonts;
        _ = board.Document;
        _ = board.Node;
        _ = board.GetNet(0);
        _ = board.GetNet("GND");
        _ = board.GetLayer("F.Cu");

        if (board.General is { } general)
        {
            _ = general.Thickness;
            _ = general.LegacyTeardrops;
        }

        if (board.TitleBlock is { } titleBlock)
        {
            _ = titleBlock.Title;
            _ = titleBlock.Date;
            _ = titleBlock.Revision;
            _ = titleBlock.Company;
            _ = titleBlock.GetComment(1);
            foreach (var comment in titleBlock.Comments)
            {
                _ = comment.Number;
                _ = comment.Text;
            }
        }

        if (board.Setup is { } setup)
        {
            _ = setup.PadToMaskClearance;
            _ = setup.PadToPasteClearance;
            _ = setup.AllowSoldermaskBridgesInFootprints;

            if (setup.Stackup is { } stackup)
            {
                _ = stackup.CopperFinish;
                _ = stackup.DielectricConstraints;
                _ = stackup.TotalThickness;
                _ = stackup.GetLayer("F.Cu");
                foreach (var layer in stackup.Layers)
                {
                    _ = layer.Name;
                    _ = layer.Type;
                    _ = layer.Thickness;
                    _ = layer.Material;
                    _ = layer.EpsilonR;
                    _ = layer.LossTangent;
                    _ = layer.Color;
                    _ = layer.IsCopper;
                }
            }
        }

        foreach (var layer in board.Layers)
        {
            _ = layer.Ordinal;
            _ = layer.Name;
            _ = layer.Type;
            _ = layer.UserName;
            _ = layer.IsCopper;
        }

        foreach (var net in board.Nets)
        {
            _ = net.Code;
            _ = net.Name;
        }

        foreach (var segment in board.Segments)
        {
            _ = segment.Start;
            _ = segment.End;
            _ = segment.Width;
            _ = segment.Layer;
            _ = segment.Net;
            _ = segment.Uuid;
            _ = segment.Locked;
            _ = segment.Length;
        }

        foreach (var arc in board.TrackArcs)
        {
            _ = arc.Start;
            _ = arc.Mid;
            _ = arc.End;
            _ = arc.Width;
            _ = arc.Layer;
            _ = arc.Net;
        }

        foreach (var via in board.Vias)
        {
            _ = via.ViaType;
            _ = via.Position;
            _ = via.Size;
            _ = via.Drill;
            _ = via.Layers;
            _ = via.Net;
            _ = via.Uuid;
            _ = via.Free;
            _ = via.RemoveUnusedLayers;
        }

        foreach (var zone in board.Zones)
        {
            _ = zone.Net;
            _ = zone.NetName;
            _ = zone.Layers;
            _ = zone.Uuid;
            _ = zone.Name;
            _ = zone.Priority;
            _ = zone.HatchStyle;
            _ = zone.HatchPitch;
            _ = zone.ConnectPadsMode;
            _ = zone.ConnectPadsClearance;
            _ = zone.MinThickness;
            _ = zone.FilledAreasThickness;
            _ = zone.IsFilled;
            _ = zone.IsKeepout;
            _ = zone.Points;

            if (zone.Fill is { } fill)
            {
                _ = fill.Enabled;
                _ = fill.Mode;
                _ = fill.ThermalGap;
                _ = fill.ThermalBridgeWidth;
            }

            foreach (var filled in zone.FilledPolygons)
            {
                _ = filled.Layer;
                _ = filled.IsIsland;
                _ = filled.Points;
            }
        }

        foreach (var item in board.GraphicLines.Cast<KiCadGraphicItem>()
            .Concat(board.GraphicRectangles)
            .Concat(board.GraphicCircles)
            .Concat(board.GraphicArcs)
            .Concat(board.GraphicPolygons)
            .Concat(board.GraphicCurves))
        {
            _ = item.Layer;
            _ = item.Width;
            _ = item.Uuid;
            _ = item.Locked;
            _ = item.Stroke?.Type;
            _ = item.Fill?.Type;
        }

        foreach (var text in board.Texts)
        {
            _ = text.Text;
            _ = text.Position;
            _ = text.Layer;
            _ = text.Uuid;
            _ = text.KnockOut;
            _ = text.FontEffects?.Size;
        }

        foreach (var dimension in board.Dimensions)
        {
            _ = dimension.Type;
            _ = dimension.Layer;
            _ = dimension.Uuid;
            _ = dimension.Points;
            _ = dimension.Height;
            _ = dimension.Text?.Text;
        }

        foreach (var group in board.Groups)
        {
            _ = group.Name;
            _ = group.Uuid;
            _ = group.Members;
        }

        foreach (var footprint in board.Footprints)
        {
            _ = footprint.Id;
            _ = footprint.Layer;
            _ = footprint.Description;
            _ = footprint.Tags;
            _ = footprint.Attributes;
            _ = footprint.GetPropertyValue("Reference");
            _ = footprint.Pads.Count;
            _ = footprint.Lines.Count;
            _ = footprint.Models.Count;
        }
    }

    /// <summary>
    /// Measures how much of two strings is shared at each end. Everything between the two is where
    /// the edit landed; everything outside it is proof that nothing else moved.
    /// </summary>
    private static (int Prefix, int Suffix) CommonEnds(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        var limit = Math.Min(before.Length, after.Length) - prefix;
        while (suffix < limit && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return (prefix, suffix);
    }
}
