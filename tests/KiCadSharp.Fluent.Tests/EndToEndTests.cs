using System.Diagnostics;

using KiCadSharp.Documents;

using static KiCadSharp.Fluent.Tests.TestSupport;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// A whole footprint and a whole symbol library, built once as a fluent chain and once as the
/// equivalent run of <c>Add*</c> statements. Saved, the two must be the same file: the fluent layer
/// is another way to write the same document, never a different document.
/// </summary>
/// <remarks>
/// <see cref="KiCadLoadsWhatTheChainBuilt"/> then hands the fluent result to KiCad itself. It is
/// opt-in, as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
/// </remarks>
public class EndToEndTests
{
    private const string FootprintName = "SOIC-8_3.9x4.9mm_P1.27mm";
    private const string ModelPath = "${KICAD10_3DMODEL_DIR}/Package_SO.3dshapes/SOIC-8_3.9x4.9mm_P1.27mm.step";

    // PolygonPointsFirst: KiCad 10.0.6 reads an (fp_poly …) only when (pts …) is its first child —
    // MEASURED, `kicad-cli fp upgrade` refuses the library ("Unable to load library", exit 2) when
    // (layer …) comes first and loads it when the points do. KiCadFpPoly writes its children in the
    // order they are set, so both builds below add the points before the layer and the width. That
    // is a property of the core document type, and the same whichever style builds it.

    // ── The footprint ────────────────────────────────────────────────────────

    private static KiCadFootprint FluentFootprint()
    {
        var footprint = new KiCadFootprint(FootprintName)
            .WithFpText("user", "${REFERENCE}", 0, 0, KiCadLayerNames.FFab)
            .WithPad("1", "smd", "rect", -2.475, -1.905, 1.95, 0.6, FrontSmd)
            .WithPad("2", "smd", "rect", -2.475, -0.635, 1.95, 0.6, FrontSmd)
            .WithPad("3", "smd", "rect", -2.475, 0.635, 1.95, 0.6, FrontSmd)
            .WithPad("4", "smd", "rect", -2.475, 1.905, 1.95, 0.6, FrontSmd)
            .WithPad("5", "smd", "rect", 2.475, 1.905, 1.95, 0.6, FrontSmd)
            .WithPad("6", "smd", "rect", 2.475, 0.635, 1.95, 0.6, FrontSmd)
            .WithPad("7", "smd", "rect", 2.475, -0.635, 1.95, 0.6, FrontSmd)
            .WithPad("8", "smd", "rect", 2.475, -1.905, 1.95, 0.6, FrontSmd)
            .WithLine(-0.975, -2.45, 1.95, -2.45, KiCadLayerNames.FFab, 0.1)
            .WithLine(1.95, -2.45, 1.95, 2.45, KiCadLayerNames.FFab, 0.1)
            .WithLine(1.95, 2.45, -1.95, 2.45, KiCadLayerNames.FFab, 0.1)
            .WithLine(-1.95, 2.45, -1.95, -1.475, KiCadLayerNames.FFab, 0.1)
            .WithLine(-1.95, -1.475, -0.975, -2.45, KiCadLayerNames.FFab, 0.1)
            .WithLine(0, -2.56, 1.95, -2.56, KiCadLayerNames.FSilkS)
            .WithLine(0, 2.56, 1.95, 2.56, KiCadLayerNames.FSilkS)
            .WithLine(-3.7, -2.7, 3.7, -2.7, KiCadLayerNames.FCrtYd, 0.05)
            .WithLine(3.7, -2.7, 3.7, 2.7, KiCadLayerNames.FCrtYd, 0.05)
            .WithLine(3.7, 2.7, -3.7, 2.7, KiCadLayerNames.FCrtYd, 0.05)
            .WithLine(-3.7, 2.7, -3.7, -2.7, KiCadLayerNames.FCrtYd, 0.05)
            .WithCircle(-1.2, -1.7, -1.0, -1.7, KiCadLayerNames.FFab, 0.1)
            .WithModel(ModelPath, model =>
            {
                model.Offset = new KiCadXyz(0, 0, 0);
                model.Scale = new KiCadXyz(1, 1, 1);
                model.Rotation = new KiCadXyz(0, 0, 0);
            });

        // A footprint has no AddPoly: a polygon goes in through the live list, and the list's
        // With returns the list, so it is its own statement. The points go first — see
        // PolygonPointsFirst.
        footprint.Polygons.With(marker =>
        {
            marker.WithPoint(-3.45, -2.465).WithPoint(-3.69, -2.795).WithPoint(-3.21, -2.795).WithPoint(-3.45, -2.465);
            marker.Layer = KiCadLayerNames.FSilkS;
            marker.Width = 0.12;
        });

        return footprint;
    }

    private static KiCadFootprint ImperativeFootprint()
    {
        var footprint = new KiCadFootprint(FootprintName);
        footprint.AddFpText("user", "${REFERENCE}", 0, 0, KiCadLayerNames.FFab);
        footprint.AddPad("1", "smd", "rect", -2.475, -1.905, 1.95, 0.6, FrontSmd);
        footprint.AddPad("2", "smd", "rect", -2.475, -0.635, 1.95, 0.6, FrontSmd);
        footprint.AddPad("3", "smd", "rect", -2.475, 0.635, 1.95, 0.6, FrontSmd);
        footprint.AddPad("4", "smd", "rect", -2.475, 1.905, 1.95, 0.6, FrontSmd);
        footprint.AddPad("5", "smd", "rect", 2.475, 1.905, 1.95, 0.6, FrontSmd);
        footprint.AddPad("6", "smd", "rect", 2.475, 0.635, 1.95, 0.6, FrontSmd);
        footprint.AddPad("7", "smd", "rect", 2.475, -0.635, 1.95, 0.6, FrontSmd);
        footprint.AddPad("8", "smd", "rect", 2.475, -1.905, 1.95, 0.6, FrontSmd);
        footprint.AddLine(-0.975, -2.45, 1.95, -2.45, KiCadLayerNames.FFab, 0.1);
        footprint.AddLine(1.95, -2.45, 1.95, 2.45, KiCadLayerNames.FFab, 0.1);
        footprint.AddLine(1.95, 2.45, -1.95, 2.45, KiCadLayerNames.FFab, 0.1);
        footprint.AddLine(-1.95, 2.45, -1.95, -1.475, KiCadLayerNames.FFab, 0.1);
        footprint.AddLine(-1.95, -1.475, -0.975, -2.45, KiCadLayerNames.FFab, 0.1);
        footprint.AddLine(0, -2.56, 1.95, -2.56, KiCadLayerNames.FSilkS);
        footprint.AddLine(0, 2.56, 1.95, 2.56, KiCadLayerNames.FSilkS);
        footprint.AddLine(-3.7, -2.7, 3.7, -2.7, KiCadLayerNames.FCrtYd, 0.05);
        footprint.AddLine(3.7, -2.7, 3.7, 2.7, KiCadLayerNames.FCrtYd, 0.05);
        footprint.AddLine(3.7, 2.7, -3.7, 2.7, KiCadLayerNames.FCrtYd, 0.05);
        footprint.AddLine(-3.7, 2.7, -3.7, -2.7, KiCadLayerNames.FCrtYd, 0.05);
        footprint.AddCircle(-1.2, -1.7, -1.0, -1.7, KiCadLayerNames.FFab, 0.1);

        var model = footprint.AddModel(ModelPath);
        model.Offset = new KiCadXyz(0, 0, 0);
        model.Scale = new KiCadXyz(1, 1, 1);
        model.Rotation = new KiCadXyz(0, 0, 0);

        var marker = footprint.Polygons.Add();
        marker.AddPoint(-3.45, -2.465);
        marker.AddPoint(-3.69, -2.795);
        marker.AddPoint(-3.21, -2.795);
        marker.AddPoint(-3.45, -2.465);
        marker.Layer = KiCadLayerNames.FSilkS;
        marker.Width = 0.12;
        return footprint;
    }

    [Fact]
    public void AFootprintBuiltFluentlySavesToTheSameFileAsTheAddSequence()
    {
        var scratch = NewScratchDirectory();
        var fluentPath = Path.Combine(scratch, "fluent.kicad_mod");
        var imperativePath = Path.Combine(scratch, "imperative.kicad_mod");

        var fluent = FluentFootprint();
        KiCadFootprintLibrary.SaveFootprint(fluent, fluentPath);
        KiCadFootprintLibrary.SaveFootprint(ImperativeFootprint(), imperativePath);

        // Not two empty files agreeing: the chain built what it says.
        Assert.Equal(8, fluent.Pads.Count);
        Assert.Equal(11, fluent.Lines.Count);
        Assert.Single(fluent.Circles);
        Assert.Equal(4, Assert.Single(fluent.Polygons).Points.Count);
        Assert.Equal(ModelPath, Assert.Single(fluent.Models).Path);

        Assert.Equal(File.ReadAllText(imperativePath), File.ReadAllText(fluentPath));
    }

    // ── The symbol library ───────────────────────────────────────────────────

    private static KiCadPin Pin(string number, double y, double rotation) =>
        new("passive", "line", new KiCadPosition(0, y, rotation), 1.27, "~", number);

    private static KiCadSymbolLibrary FluentLibrary() =>
        new KiCadSymbolLibrary()
            .WithSymbol("R_Fluent", r => r
                .WithProperty(KiCadPropertyNames.Reference, "R")
                .WithProperty(KiCadPropertyNames.Footprint, "Resistor_SMD:R_0603_1608Metric")
                .WithProperty("Description", "Resistor")
                .WithGraphicalItem(new KiCadRectangle(-1.016, -2.54, 1.016, 2.54), body => body.RequireStroke().Width = 0.254)
                .WithUnit("R_Fluent_1_1", unit => unit
                    .WithPin(Pin("1", 3.81, 270))
                    .WithPin(Pin("2", -3.81, 90))))
            .WithSymbol("NetTie", tie => tie
                .WithProperty(KiCadPropertyNames.Reference, "NT")
                .WithGraphicalItem(new KiCadPolyline(), line => line.WithPoint(-1.27, 0).WithPoint(1.27, 0))
                .WithUnit("NetTie_1_1", unit => unit
                    .WithPin(Pin("1", 0, 0), pin => pin.Position = new KiCadPosition(-2.54, 0, 0))
                    .WithPin(Pin("2", 0, 180), pin => pin.Position = new KiCadPosition(2.54, 0, 180))));

    private static KiCadSymbolLibrary ImperativeLibrary()
    {
        var library = new KiCadSymbolLibrary();

        var r = library.AddSymbol("R_Fluent");
        r.AddProperty(KiCadPropertyNames.Reference, "R");
        r.AddProperty(KiCadPropertyNames.Footprint, "Resistor_SMD:R_0603_1608Metric");
        r.AddProperty("Description", "Resistor");
        var body = new KiCadRectangle(-1.016, -2.54, 1.016, 2.54);
        r.AddGraphicalItem(body);
        body.RequireStroke().Width = 0.254;
        var rUnit = r.AddUnit("R_Fluent_1_1");
        rUnit.AddPin(Pin("1", 3.81, 270));
        rUnit.AddPin(Pin("2", -3.81, 90));

        var tie = library.AddSymbol("NetTie");
        tie.AddProperty(KiCadPropertyNames.Reference, "NT");
        var line = new KiCadPolyline();
        tie.AddGraphicalItem(line);
        line.AddPoint(-1.27, 0);
        line.AddPoint(1.27, 0);
        var tieUnit = tie.AddUnit("NetTie_1_1");
        var left = tieUnit.AddPin(Pin("1", 0, 0));
        left.Position = new KiCadPosition(-2.54, 0, 0);
        var right = tieUnit.AddPin(Pin("2", 0, 180));
        right.Position = new KiCadPosition(2.54, 0, 180);

        return library;
    }

    [Fact]
    public void ASymbolLibraryBuiltFluentlySavesToTheSameFileAsTheAddSequence()
    {
        var scratch = NewScratchDirectory();
        var fluentPath = Path.Combine(scratch, "fluent.kicad_sym");
        var imperativePath = Path.Combine(scratch, "imperative.kicad_sym");

        var fluent = FluentLibrary();
        fluent.Save(fluentPath);
        ImperativeLibrary().Save(imperativePath);

        Assert.Equal(["R_Fluent", "NetTie"], fluent.Symbols.Select(s => s.Id));
        Assert.Equal(2, fluent.GetSymbol("R_Fluent")!.Pins.Count);
        Assert.Equal("Resistor", fluent.GetSymbol("R_Fluent")!.GetPropertyValue("Description"));
        Assert.Equal(new KiCadPosition(2.54, 0, 180), fluent.GetSymbol("NetTie")!.Pins[1].Position);

        Assert.Equal(File.ReadAllText(imperativePath), File.ReadAllText(fluentPath));
    }

    // ── KiCad reads it ───────────────────────────────────────────────────────

    [Fact]
    public void KiCadLoadsWhatTheChainBuilt()
    {
        if (KiCadCli is not { } cli)
        {
            return;
        }

        // `upgrade --force` makes KiCad load each file and write back what it loaded, in its own
        // serialiser. Reading that back is how this test sees what KiCad understood — a count
        // KiCad never parsed cannot survive into its output.
        var scratch = NewScratchDirectory();
        var pretty = Directory.CreateDirectory(Path.Combine(scratch, "Fluent.pretty")).FullName;
        KiCadFootprintLibrary.SaveFootprint(FluentFootprint(), Path.Combine(pretty, FootprintName + ".kicad_mod"));
        var prettyOut = Path.Combine(scratch, "FluentOut.pretty");
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", prettyOut, pretty);

        var saved = Assert.Single(KiCadFootprintLibrary.Load(Path.Combine(prettyOut, FootprintName + ".kicad_mod")).Footprints);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], saved.Pads.Select(p => p.Number));
        Assert.Equal(new KiCadPosition(2.475, -1.905), saved.Pads[7].Position);
        Assert.Equal(11, saved.Lines.Count);
        Assert.Single(saved.Circles);
        Assert.Equal(4, Assert.Single(saved.Polygons).Points.Count);
        Assert.Equal(ModelPath, Assert.Single(saved.Models).Path);

        var symbols = Path.Combine(scratch, "fluent.kicad_sym");
        FluentLibrary().Save(symbols);
        var symbolsOut = Path.Combine(scratch, "fluent-out.kicad_sym");
        Run(cli, scratch, "sym", "upgrade", "--force", "-o", symbolsOut, symbols);

        var library = KiCadSymbolLibrary.Load(symbolsOut);
        var resistor = library.GetSymbol("R_Fluent");
        Assert.NotNull(resistor);
        Assert.Equal(["1", "2"], resistor.Pins.Select(p => p.Number).Order(StringComparer.Ordinal));
        Assert.Equal("Resistor", resistor.GetPropertyValue("Description"));
        Assert.Equal("Resistor_SMD:R_0603_1608Metric", resistor.GetPropertyValue(KiCadPropertyNames.Footprint));
        Assert.IsType<KiCadRectangle>(Assert.Single(resistor.GraphicalItems));
        var tie = library.GetSymbol("NetTie");
        Assert.NotNull(tie);
        Assert.Equal(2, tie.Pins.Count);
        Assert.Equal(2, Assert.IsType<KiCadPolyline>(Assert.Single(tie.GraphicalItems)).Points.Count);
    }

    private static void Run(string cli, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(cli) { WorkingDirectory = workingDirectory, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"kicad-cli {string.Join(' ', arguments)} exited {process.ExitCode}:\n{stdout.Result}\n{stderr}");
    }
}
