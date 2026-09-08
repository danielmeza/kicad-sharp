using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// The document layer, measured against boards this repository did not write.
/// </summary>
/// <remarks>
/// <para>
/// Every other <c>.kicad_pcb</c> under <c>data/</c> came out of one project's own generators. A
/// suite made of files we wrote measures one spelling and reads as if it measured the format: the
/// generator and the reader share every assumption, so an assumption that is wrong is invisible in
/// both. These three came from three unrelated hardware projects, were written by <c>pcbnew</c>
/// itself, and were vendored verbatim — see each directory's <c>README.md</c> for the repository,
/// the commit and the licence.
/// </para>
/// <para>
/// What they are here to cover, which nothing else here had: hundreds of routed segments and vias,
/// zones the filler actually filled, four-layer stackups, footprints with 3D models, routed arcs,
/// KiCad 10's <c>(generated …)</c> length-tuning patterns, and — most of all — both spellings of a
/// net, side by side.
/// </para>
/// <para>
/// The numbers are measured from these bytes. Replacing a file changes what the tests mean, so
/// replace the numbers with it.
/// </para>
/// </remarks>
public class RealWorldBoardTests
{
    // ----------------------------------------------------------------- the files are what we think

    [Fact]
    public void M2PcieAdapter_IsTheMeasuredFile()
    {
        Assert.Equal(1_865_463, new FileInfo(TestData.M2PcieAdapterBoard).Length);

        var root = SExpression.Load(TestData.M2PcieAdapterBoard);
        Assert.Equal("kicad_pcb", root.Token);
        Assert.Equal("20241229", root.GetChildValue("version"));
        Assert.Equal("pcbnew", root.GetChildValue("generator"));
        Assert.Equal("9.0", root.GetChildValue("generator_version"));

        Assert.Equal(66, root.GetChildren("net").Count());
        Assert.Equal(571, root.GetChildren("segment").Count());
        Assert.Equal(216, root.GetChildren("arc").Count());
        Assert.Equal(92, root.GetChildren("via").Count());
        Assert.Equal(5, root.GetChildren("zone").Count());
        Assert.Equal(20, root.GetChildren("footprint").Count());
        Assert.Equal(24, root.GetChild("layers")!.Children.Count);
        Assert.NotNull(root.GetChild("title_block"));
        Assert.NotNull(root.GetChild("setup"));
    }

    [Fact]
    public void BitaxeGamma_IsTheMeasuredFile()
    {
        Assert.Equal(1_221_219, new FileInfo(TestData.BitaxeGammaBoard).Length);

        var root = SExpression.Load(TestData.BitaxeGammaBoard);
        Assert.Equal("20241229", root.GetChildValue("version"));
        Assert.Equal("pcbnew", root.GetChildValue("generator"));
        Assert.Equal("9.0", root.GetChildValue("generator_version"));

        Assert.Equal(102, root.GetChildren("net").Count());
        Assert.Equal(825, root.GetChildren("segment").Count());
        Assert.Equal(200, root.GetChildren("via").Count());
        Assert.Equal(17, root.GetChildren("zone").Count());
        Assert.Equal(147, root.GetChildren("footprint").Count());
        Assert.Equal(31, root.GetChild("layers")!.Children.Count);

        // No title block: this one was never given a title in eeschema.
        Assert.Null(root.GetChild("title_block"));
    }

    [Fact]
    public void SNEdge_IsTheMeasuredFileAndIsKiCad10()
    {
        Assert.Equal(333_929, new FileInfo(TestData.SNEdgeBoard).Length);

        var root = SExpression.Load(TestData.SNEdgeBoard);
        Assert.Equal("20260206", root.GetChildValue("version"));
        Assert.Equal("pcbnew", root.GetChildValue("generator"));
        Assert.Equal("10.0", root.GetChildValue("generator_version"));

        // The KiCad 10 shape, in the file: no board-level net table at all.
        Assert.Empty(root.GetChildren("net"));

        Assert.Equal(257, root.GetChildren("segment").Count());
        Assert.Equal(37, root.GetChildren("arc").Count());
        Assert.Equal(88, root.GetChildren("via").Count());
        Assert.Equal(2, root.GetChildren("zone").Count());
        Assert.Equal(37, root.GetChildren("footprint").Count());
        Assert.Equal(29, root.GetChild("layers")!.Children.Count);

        // Six length-tuning meanders, which is a form KiCad 9 introduced and no other fixture has.
        Assert.Equal(6, root.GetChildren("generated").Count());
        Assert.Equal("tuning_pattern", root.GetChildren("generated").First().GetChildValue("type"));
    }

    // ------------------------------------------------- the typed views reach every form in the file

    [Theory]
    [MemberData(nameof(RealWorldBoards))]
    public void TypedViews_ReachEveryFormTheFileHas(string path)
    {
        var board = KiCadBoard.Load(path);
        var root = board.Node;

        // Each list is the file's own children with that token, counted twice: once through the
        // raw tree and once through the view. A view that filtered, cached or rebuilt would drift.
        Assert.Equal(root.GetChildren("net").Count(), board.Nets.Count);
        Assert.Equal(root.GetChildren("footprint").Count(), board.Footprints.Count);
        Assert.Equal(root.GetChildren("segment").Count(), board.Segments.Count);
        Assert.Equal(root.GetChildren("arc").Count(), board.TrackArcs.Count);
        Assert.Equal(root.GetChildren("via").Count(), board.Vias.Count);
        Assert.Equal(root.GetChildren("zone").Count(), board.Zones.Count);
        Assert.Equal(root.GetChildren("gr_line").Count(), board.GraphicLines.Count);
        Assert.Equal(root.GetChildren("gr_rect").Count(), board.GraphicRectangles.Count);
        Assert.Equal(root.GetChildren("gr_circle").Count(), board.GraphicCircles.Count);
        Assert.Equal(root.GetChildren("gr_arc").Count(), board.GraphicArcs.Count);
        Assert.Equal(root.GetChildren("gr_poly").Count(), board.GraphicPolygons.Count);
        Assert.Equal(root.GetChildren("gr_text").Count(), board.Texts.Count);
        Assert.Equal(root.GetChildren("dimension").Count(), board.Dimensions.Count);
        Assert.Equal(root.GetChildren("group").Count(), board.Groups.Count);
        Assert.Equal(root.GetChild("layers")!.Children.Count, board.Layers.Count);
    }

    [Theory]
    [MemberData(nameof(RealWorldBoards))]
    public void EveryCopperItem_SitsOnALayerTheBoardDeclares(string path)
    {
        var board = KiCadBoard.Load(path);
        var declared = board.Layers.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        foreach (var layer in board.Segments.Select(s => s.Layer)
                     .Concat(board.TrackArcs.Select(a => a.Layer))
                     .Concat(board.Vias.SelectMany(v => v.Layers))
                     .Concat(board.Zones.SelectMany(z => z.Layers))
                     .Concat(board.Zones.SelectMany(z => z.FilledPolygons).Select(f => f.Layer)))
        {
            Assert.Contains(layer, declared);
        }
    }

    [Theory]
    [MemberData(nameof(RealWorldBoards))]
    public void EveryFootprint_ReadsAsAPlacedFootprint(string path)
    {
        var board = KiCadBoard.Load(path);

        Assert.NotEmpty(board.Footprints);

        foreach (var footprint in board.Footprints)
        {
            // "Unknown" is what the view returns when the form carries no name at all.
            Assert.NotEqual("Unknown", footprint.Id);
            Assert.False(string.IsNullOrEmpty(footprint.Layer));
            Assert.False(string.IsNullOrEmpty(footprint.Uuid));
            Assert.NotNull(footprint.GetPropertyValue("Reference"));

            foreach (var pad in footprint.Pads)
            {
                Assert.False(string.IsNullOrEmpty(pad.Type));
                Assert.False(string.IsNullOrEmpty(pad.Shape));
                Assert.NotEmpty(pad.Layers);
            }

            foreach (var model in footprint.Models)
            {
                Assert.False(string.IsNullOrEmpty(model.Path));
            }
        }
    }

    // ------------------------------------------------------------------------------- what they add

    [Fact]
    public void M2PcieAdapter_ReadsFourCopperLayersAThirteenLayerStackupAndFilledZones()
    {
        var board = KiCadBoard.Load(TestData.M2PcieAdapterBoard);

        Assert.Equal(24, board.Layers.Count);
        Assert.Equal(
            new[] { "F.Cu", "In1.Cu", "In2.Cu", "B.Cu" },
            board.Layers.Where(l => l.IsCopper).Select(l => l.Name).ToArray());

        var stackup = board.Setup!.Stackup!;
        Assert.Equal(13, stackup.Layers.Count);

        // Five zones, all poured. One of them spans both inner layers, so the filler wrote it out
        // as two polygons on two different layers under one (zone ...) — the case a single-layer
        // fixture cannot show.
        Assert.Equal(5, board.Zones.Count);
        Assert.All(board.Zones, z => Assert.True(z.IsFilled));
        Assert.Equal(6, board.Zones.Sum(z => z.FilledPolygons.Count));
        Assert.Equal(4_556, board.Zones.Sum(z => z.FilledPolygons.Sum(f => f.Points.Count)));

        var inner = Assert.Single(board.Zones, z => z.Layers.Count > 1);
        Assert.Equal(new[] { "In1.Cu", "In2.Cu" }, inner.Layers.ToArray());
        Assert.Equal("GND", inner.NetName);
        Assert.Equal(2, inner.FilledPolygons.Count);
        Assert.Equal("In1.Cu", inner.FilledPolygons[0].Layer);
        Assert.Equal("In2.Cu", inner.FilledPolygons[1].Layer);

        // 216 routed arcs. probe-4layer has none, so KiCadTrackArc had never been read in bulk.
        Assert.Equal(216, board.TrackArcs.Count);
        Assert.All(board.TrackArcs, a => Assert.True(a.Width > 0));

        // 3D models with a project-relative path variable, which no other fixture carries.
        Assert.Equal(17, board.Footprints.Sum(f => f.Models.Count));
        Assert.StartsWith("${", board.Footprints.SelectMany(f => f.Models).First().Path, StringComparison.Ordinal);

        // Seventeen of the twenty parts are on the back of the board.
        Assert.Equal(17, board.Footprints.Count(f => f.Layer == "B.Cu"));
    }

    [Fact]
    public void BitaxeGamma_ReadsSeventeenZonesIncludingAFiveLayerKeepout()
    {
        var board = KiCadBoard.Load(TestData.BitaxeGammaBoard);

        Assert.Equal(17, board.Zones.Count);
        Assert.Equal(16, board.Zones.Count(z => z.IsFilled));
        Assert.Equal(13_083, board.Zones.Sum(z => z.FilledPolygons.Sum(f => f.Points.Count)));

        var keepout = Assert.Single(board.Zones, z => z.IsKeepout);
        Assert.False(keepout.IsFilled);
        Assert.Empty(keepout.FilledPolygons);
        // In the file's own order, which is not stackup order: the view hands back what is written.
        Assert.Equal(
            new[] { "F.Cu", "B.Cu", "In1.Cu", "In2.Cu", "Edge.Cuts" },
            keepout.Layers.ToArray());

        // Zone priorities are used here, from 0 to 12. Everywhere else they are all 0.
        Assert.Equal(12, board.Zones.Max(z => z.Priority));

        // 479 pads over four pad types and four shapes, 77 of them drilled and 4 of those slotted.
        var pads = board.Footprints.SelectMany(f => f.Pads).ToList();
        Assert.Equal(479, pads.Count);
        Assert.Equal(
            new[] { "connect", "np_thru_hole", "smd", "thru_hole" },
            pads.Select(p => p.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "circle", "oval", "rect", "roundrect" },
            pads.Select(p => p.Shape).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.Equal(77, pads.Count(p => p.Drill is not null));
        Assert.Equal(4, pads.Count(p => p.Drill?.IsOval == true));

        // 913 properties across 147 footprints, and 97 3D models.
        Assert.Equal(913, board.Footprints.Sum(f => f.Properties.Count));
        Assert.Equal(97, board.Footprints.Sum(f => f.Models.Count));
    }

    [Fact]
    public void SNEdge_ReadsAKiCad10BoardThatNamesItsNetsInsteadOfNumberingThem()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);

        Assert.Equal("20260206", board.Version);
        Assert.Equal("pcbnew", board.Generator);
        Assert.Equal("10.0", board.GeneratorVersion);

        // Every one of the 257 segments carries a name and no number. This is the read that used to
        // report 65 tracks as belonging to no net at all, and it is measured here on a board KiCad
        // wrote rather than on one shaped to prove the point.
        Assert.Equal(257, board.Segments.Count);
        Assert.All(board.Segments, s => Assert.False(string.IsNullOrEmpty(s.NetName)));
        Assert.All(board.Segments, s => Assert.Equal(0, s.Net));

        Assert.Equal(88, board.Vias.Count);
        Assert.All(board.Vias, v => Assert.False(string.IsNullOrEmpty(v.NetName)));
        Assert.All(board.Vias, v => Assert.Equal("through", v.ViaType));
        Assert.All(board.Vias, v => Assert.Equal(new[] { "F.Cu", "B.Cu" }, v.Layers.ToArray()));

        Assert.Equal(37, board.TrackArcs.Count);
        Assert.All(board.TrackArcs, a => Assert.False(string.IsNullOrEmpty(a.NetName)));

        // The pads name their nets the same way.
        Assert.Equal(88, board.Footprints.Sum(f => f.Pads.Count));
        Assert.All(board.Footprints.SelectMany(f => f.Pads), p => Assert.False(string.IsNullOrEmpty(p.Net)));

        // Twenty-one distinct nets on a board whose net table has nothing in it.
        var nets = board.Segments.Select(s => s.NetName!)
            .Concat(board.Vias.Select(v => v.NetName!))
            .Concat(board.TrackArcs.Select(a => a.NetName!))
            .Concat(board.Footprints.SelectMany(f => f.Pads).Select(p => p.Net!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(21, nets.Count);
        Assert.Contains("GND", nets);
    }

    [Fact]
    public void SNEdge_ReadsAZoneTheFillerFilledAndAKeepoutBesideIt()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);

        Assert.Equal(2, board.Zones.Count);

        var keepout = Assert.Single(board.Zones, z => z.IsKeepout);
        Assert.Equal(new[] { "F.Cu" }, keepout.Layers.ToArray());
        Assert.Equal(7, keepout.Points.Count);
        Assert.Empty(keepout.FilledPolygons);

        var poured = Assert.Single(board.Zones, z => !z.IsKeepout);
        Assert.True(poured.IsFilled);
        Assert.Equal("GND", poured.NetName);
        Assert.Equal(new[] { "F.Cu", "B.Cu" }, poured.Layers.ToArray());
        Assert.Equal(4, poured.Points.Count);

        // The filler broke one GND pour into fourteen polygons across two layers, 2,599 points in
        // all. Nothing else here has more than two.
        Assert.Equal(14, poured.FilledPolygons.Count);
        Assert.Equal(2_599, poured.FilledPolygons.Sum(f => f.Points.Count));
        Assert.Equal(
            new[] { "B.Cu", "F.Cu" },
            poured.FilledPolygons.Select(f => f.Layer).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToArray());
    }

    // ------------------------------------------------ the two spellings of "which net is this on?"

    /// <summary>
    /// The same question, asked of both formats, and the two different properties that answer it.
    /// </summary>
    /// <remarks>
    /// This is a measurement of the gap, not an endorsement of it. There is no one property that
    /// answers "which net is this track on" for both spellings: on a KiCad 9 board every segment
    /// carries a number and <see cref="KiCadTrackItem.NetName"/> is <see langword="null"/>; on a
    /// KiCad 10 board every segment carries a name and <see cref="KiCadTrackItem.Net"/> is 0. A
    /// caller has to know which format it is holding. See the pull request that added these
    /// fixtures for what closing it would take.
    /// </remarks>
    [Theory]
    [InlineData("m2-pcie-adapter", 571, false)]
    [InlineData("bitaxeGamma", 825, false)]
    [InlineData("SNEdge", 257, true)]
    public void ANetIsSpelledOneOfTwoWaysAndOnlyOneOfThemIsReadable(string board, int segments, bool namesItsNets)
    {
        var path = TestData.RealWorldBoards.Single(p => Path.GetFileNameWithoutExtension(p) == board);
        var loaded = KiCadBoard.Load(path);

        Assert.Equal(segments, loaded.Segments.Count);

        if (namesItsNets)
        {
            Assert.Equal(segments, loaded.Segments.Count(s => !string.IsNullOrEmpty(s.NetName)));
            Assert.Equal(0, loaded.Segments.Count(s => s.Net != 0));

            // And so the board-level net table, and everything that reads through it, is empty.
            Assert.Empty(loaded.Nets);
            Assert.Null(loaded.GetNet("GND"));
        }
        else
        {
            Assert.Equal(0, loaded.Segments.Count(s => !string.IsNullOrEmpty(s.NetName)));
            Assert.Equal(segments, loaded.Segments.Count(s => s.Net != 0));

            Assert.NotEmpty(loaded.Nets);
            Assert.NotNull(loaded.GetNet("GND"));
        }
    }

    // ------------------------------------------------------------- forms the views do not model yet

    /// <summary>
    /// A form with no view is still in the file after a save. That is the property the document
    /// layer exists for, and these boards are the first fixtures that exercise it on real forms.
    /// </summary>
    [Fact]
    public void FormsWithNoTypedView_SurviveAReadAndASave()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);

        // (generated ...) — length-tuning meanders. No view models them.
        var generated = board.Node.GetChildren("generated").ToList();
        Assert.Equal(6, generated.Count);

        // Nor the schematic backlink on a placed footprint, nor a zone inside a footprint.
        Assert.Equal(37, board.Footprints.Count(f => f.Node.GetChild("path") is not null));
        Assert.Equal(37, board.Footprints.Count(f => f.Node.GetChild("sheetname") is not null));
        Assert.Equal(3, board.Footprints.Sum(f => f.Node.GetChildren("zone").Count()));

        BoardDocumentTests.ReadEverythingOn(board);

        var output = Path.Combine(TestData.NewScratchDirectory(), "SNEdge.kicad_pcb");
        board.Save(output);

        Assert.Equal(File.ReadAllBytes(TestData.SNEdgeBoard), File.ReadAllBytes(output));
    }

    // ------------------------------------------------------------------- one edit is one edit again

    [Theory]
    [MemberData(nameof(RealWorldBoards))]
    public void OneEditOnARealBoard_ChangesOnlyThatPropertysBytes(string path)
    {
        var board = KiCadBoard.Load(path);

        // (width 0.2) -> (width 0.3) on the first segment: one character, on a file with hundreds
        // of thousands of them.
        var before = File.ReadAllText(path);
        var original = board.Segments[0].Width;
        board.Segments[0].Width = original + 0.1;

        var output = Path.Combine(TestData.NewScratchDirectory(), Path.GetFileName(path));
        board.Save(output);
        var after = File.ReadAllText(output);

        Assert.NotEqual(before, after);

        var difference = 0;
        for (var i = 0; i < Math.Min(before.Length, after.Length); i++)
        {
            if (before[i] != after[i])
            {
                difference++;
            }
        }

        // Same length, and the edit is confined to the digits of one number.
        Assert.Equal(before.Length, after.Length);
        Assert.InRange(difference, 1, 4);

        Assert.Equal(original + 0.1, KiCadBoard.Load(output).Segments[0].Width, 6);
    }

    public static TheoryData<string> RealWorldBoards()
    {
        var data = new TheoryData<string>();
        foreach (var board in TestData.RealWorldBoards)
        {
            data.Add(board);
        }

        return data;
    }
}
