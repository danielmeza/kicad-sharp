using KiCadSharp.Documents;

using static KiCadSharp.Fluent.Tests.TestSupport;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// Every <c>With*</c> on a footprint library, a footprint and a footprint polygon. Each test pins
/// the three things the fluent layer promises: the call returns the parent, the child is there with
/// the values given, and the callback gets that child — already attached when it runs. Each also
/// checks the result against the <c>Add*</c> it mirrors, as text.
/// </summary>
public class FootprintFluentTests
{
    [Fact]
    public void WithFootprint_ReturnsTheLibrary_AppendsTheFootprint_AndHandsItOver()
    {
        var library = new KiCadFootprintLibrary();
        var footprint = new KiCadFootprint("R_0603");
        KiCadFootprint? received = null;
        var attached = false;

        var returned = library.WithFootprint(footprint, f =>
        {
            received = f;
            attached = library.Footprints.Contains(f);
        });

        Assert.Same(library, returned);
        Assert.Same(footprint.Node, Assert.Single(library.Footprints).Node);
        Assert.Same(footprint, received);
        Assert.True(attached, "the callback ran before the footprint was a child of the library");

        var expected = new KiCadFootprintLibrary();
        expected.AddFootprint(new KiCadFootprint("R_0603"));
        Assert.Equal(expected.ToText(), library.ToText());
    }

    [Fact]
    public void WithFootprint_OnASingleFootprintFile_ThrowsWhatAddFootprintThrows_AndNeverCallsBack()
    {
        var single = new KiCadFootprintLibrary(new KiCadFootprint("Alone").Node);
        var called = false;

        Assert.Throws<InvalidOperationException>(() => single.WithFootprint(new KiCadFootprint("Second"), _ => called = true));
        Assert.Throws<InvalidOperationException>(() => single.AddFootprint(new KiCadFootprint("Second")));
        Assert.False(called);
    }

    [Fact]
    public void WithFpText_ReturnsTheFootprint_AppendsTheText_AndHandsItOver()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadFpText? received = null;
        var attached = false;

        var returned = footprint.WithFpText("user", "${REFERENCE}", 0.5, -1.5, KiCadLayerNames.FFab, t =>
        {
            received = t;
            attached = footprint.TextItems.Contains(t);
        });

        Assert.Same(footprint, returned);
        var text = footprint.TextItems[^1];
        Assert.Equal("user", text.Type);
        Assert.Equal("${REFERENCE}", text.Text);
        Assert.Equal(new KiCadPosition(0.5, -1.5), text.Position);
        Assert.Equal(KiCadLayerNames.FFab, text.Layer);
        Assert.Same(text.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadFootprint("R_0603");
        expected.AddFpText("user", "${REFERENCE}", 0.5, -1.5, KiCadLayerNames.FFab);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithPad_ReturnsTheFootprint_AppendsThePad_AndHandsItOver()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadPad? received = null;
        var attached = false;

        var returned = footprint.WithPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, FrontSmd, p =>
        {
            received = p;
            attached = footprint.Pads.Contains(p);
        });

        Assert.Same(footprint, returned);
        var pad = Assert.Single(footprint.Pads);
        Assert.Equal("1", pad.Number);
        Assert.Equal("smd", pad.Type);
        Assert.Equal("rect", pad.Shape);
        Assert.Equal(new KiCadPosition(-0.8, 0), pad.Position);
        Assert.Equal(new KiCadSize(0.9, 0.95), pad.Size);
        Assert.Equal(FrontSmd, pad.Layers);
        Assert.Same(pad.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadFootprint("R_0603");
        expected.AddPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, FrontSmd);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithLine_WithoutAWidth_ReturnsTheFootprint_AndTakesAddLinesOwnDefault()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadFpLine? received = null;
        var attached = false;

        var returned = footprint.WithLine(-1.5, -0.7, 1.5, -0.7, KiCadLayerNames.FSilkS, l =>
        {
            received = l;
            attached = footprint.Lines.Contains(l);
        });

        Assert.Same(footprint, returned);
        var line = Assert.Single(footprint.Lines);
        Assert.Equal(new KiCadPosition(-1.5, -0.7), line.Start);
        Assert.Equal(new KiCadPosition(1.5, -0.7), line.End);
        Assert.Equal(KiCadLayerNames.FSilkS, line.Layer);
        Assert.Same(line.Node, received!.Node);
        Assert.True(attached);

        // The default is the core's, read back from the core rather than restated here.
        var expected = new KiCadFootprint("R_0603");
        var core = expected.AddLine(-1.5, -0.7, 1.5, -0.7, KiCadLayerNames.FSilkS);
        Assert.Equal(core.Width, line.Width);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithLine_WithAWidth_ReturnsTheFootprint_AppendsTheLine_AndHandsItOver()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadFpLine? received = null;

        var returned = footprint.WithLine(-1.5, -0.7, 1.5, -0.7, KiCadLayerNames.FCrtYd, 0.05, l => received = l);

        Assert.Same(footprint, returned);
        var line = Assert.Single(footprint.Lines);
        Assert.Equal(0.05, line.Width);
        Assert.Equal(KiCadLayerNames.FCrtYd, line.Layer);
        Assert.Same(line.Node, received!.Node);

        var expected = new KiCadFootprint("R_0603");
        expected.AddLine(-1.5, -0.7, 1.5, -0.7, KiCadLayerNames.FCrtYd, 0.05);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithCircle_WithoutAWidth_ReturnsTheFootprint_AndTakesAddCirclesOwnDefault()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadFpCircle? received = null;
        var attached = false;

        var returned = footprint.WithCircle(0, 0, 0.25, 0, KiCadLayerNames.FFab, c =>
        {
            received = c;
            attached = footprint.Circles.Contains(c);
        });

        Assert.Same(footprint, returned);
        var circle = Assert.Single(footprint.Circles);
        Assert.Equal(new KiCadPosition(0, 0), circle.Center);
        Assert.Equal(new KiCadPosition(0.25, 0), circle.End);
        Assert.Equal(KiCadLayerNames.FFab, circle.Layer);
        Assert.Same(circle.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadFootprint("R_0603");
        var core = expected.AddCircle(0, 0, 0.25, 0, KiCadLayerNames.FFab);
        Assert.Equal(core.Width, circle.Width);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithCircle_WithAWidth_ReturnsTheFootprint_AppendsTheCircle_AndHandsItOver()
    {
        var footprint = new KiCadFootprint("R_0603");
        KiCadFpCircle? received = null;

        var returned = footprint.WithCircle(0, 0, 0.25, 0, KiCadLayerNames.FSilkS, 0.2, c => received = c);

        Assert.Same(footprint, returned);
        var circle = Assert.Single(footprint.Circles);
        Assert.Equal(0.2, circle.Width);
        Assert.Same(circle.Node, received!.Node);

        var expected = new KiCadFootprint("R_0603");
        expected.AddCircle(0, 0, 0.25, 0, KiCadLayerNames.FSilkS, 0.2);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithModel_ReturnsTheFootprint_AppendsTheModel_AndLetsTheCallbackPlaceIt()
    {
        const string path = "${KICAD10_3DMODEL_DIR}/Resistor_SMD.3dshapes/R_0603_1608Metric.step";
        var footprint = new KiCadFootprint("R_0603");
        KiCadModel? received = null;
        var attached = false;

        var returned = footprint.WithModel(path, m =>
        {
            received = m;
            attached = footprint.Models.Contains(m);
            m.Offset = new KiCadXyz(0, 0, 0);
            m.Scale = new KiCadXyz(1, 1, 1);
            m.Rotation = new KiCadXyz(0, 0, 90);
        });

        Assert.Same(footprint, returned);
        var model = Assert.Single(footprint.Models);
        Assert.Equal(path, model.Path);
        Assert.Equal(new KiCadXyz(0, 0, 90), model.Rotation);
        Assert.Same(model.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadFootprint("R_0603");
        var core = expected.AddModel(path);
        core.Offset = new KiCadXyz(0, 0, 0);
        core.Scale = new KiCadXyz(1, 1, 1);
        core.Rotation = new KiCadXyz(0, 0, 90);
        SameText(expected, footprint);
    }

    [Fact]
    public void WithPoint_OnAPolygon_ReturnsThePolygon_AndAppendsTheVertexInOrder()
    {
        var polygon = new KiCadFpPoly();

        var returned = polygon.WithPoint(0, 0).WithPoint(1.5, 0).WithPoint(1.5, -2.25);

        Assert.Same(polygon, returned);
        Assert.Equal([new KiCadPosition(0, 0), new KiCadPosition(1.5, 0), new KiCadPosition(1.5, -2.25)], polygon.Points);

        var expected = new KiCadFpPoly();
        expected.AddPoint(0, 0);
        expected.AddPoint(1.5, 0);
        expected.AddPoint(1.5, -2.25);
        SameText(expected, polygon);
    }

    [Fact]
    public void WithPoint_OnACurve_ReturnsTheCurve_AndAppendsTheControlPointInOrder()
    {
        var curve = new KiCadFpCurve();

        var returned = curve.WithPoint(0, 0).WithPoint(1, 2).WithPoint(3, 2).WithPoint(4, 0);

        Assert.Same(curve, returned);
        Assert.Equal([new KiCadPosition(0, 0), new KiCadPosition(1, 2), new KiCadPosition(3, 2), new KiCadPosition(4, 0)], curve.Points);

        var expected = new KiCadFpCurve();
        expected.AddPoint(0, 0);
        expected.AddPoint(1, 2);
        expected.AddPoint(3, 2);
        expected.AddPoint(4, 0);
        SameText(expected, curve);
    }

    [Fact]
    public void AnUnconfiguredWith_IsTheAddAndNothingElse()
    {
        // configure is optional everywhere; leaving it out must not change what is written.
        var fluent = new KiCadFootprint("R_0603")
            .WithPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, FrontSmd)
            .WithModel("r.step");

        var imperative = new KiCadFootprint("R_0603");
        imperative.AddPad("1", "smd", "rect", -0.8, 0, 0.9, 0.95, FrontSmd);
        imperative.AddModel("r.step");

        SameText(imperative, fluent);
    }
}
