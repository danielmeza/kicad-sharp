using System;
using System.Collections.Generic;

namespace KiCadSharp.Specctra
{
    /// <summary>A net class as the router is to see it. Lengths in millimetres.</summary>
    /// <param name="Name">The class name.</param>
    /// <param name="TrackWidth">The width tracks are routed at.</param>
    /// <param name="Clearance">The clearance they keep.</param>
    /// <param name="ViaDiameter">The via the class uses.</param>
    /// <param name="ViaDrill">Its drill.</param>
    public sealed record SpecctraNetClass(string Name, double TrackWidth, double Clearance, double ViaDiameter, double ViaDrill)
    {
        /// <summary>Gets the nets that belong to this class. Ignored on the default class.</summary>
        public IReadOnlyCollection<string> Nets { get; init; } = Array.Empty<string>();
    }

    /// <summary>What <see cref="SpecctraDesign.Export"/> is told that the board file does not say.</summary>
    /// <remarks>
    /// <para>
    /// <b>The net classes are not in the <c>.kicad_pcb</c>.</b> KiCad keeps them in the project
    /// file, resolves every net against their patterns and priorities, and exports the result — so a
    /// board alone does not know its own widths. They are passed in, resolved, rather than guessed
    /// here: resolving KiCad's netclass patterns is its own measured problem, and the caller that
    /// already solves it is the one place that should.
    /// </para>
    /// <para>
    /// The defaults are KiCad's own for a board with no project: 0.2 mm tracks at 0.2 mm, a 0.6 mm
    /// via drilled 0.3 mm, and 0.25 mm around a hole with no copper.
    /// </para>
    /// </remarks>
    public sealed record SpecctraOptions
    {
        /// <summary>Gets the class every net not named elsewhere belongs to.</summary>
        public SpecctraNetClass DefaultClass { get; init; } = new("Default", 0.2, 0.2, 0.6, 0.3);

        /// <summary>Gets the other classes, each with its nets. A net named by two takes the first.</summary>
        public IReadOnlyList<SpecctraNetClass> Classes { get; init; } = Array.Empty<SpecctraNetClass>();

        /// <summary>Gets the clearance kept around a hole with no copper — the board's <c>min_hole_clearance</c>.</summary>
        public double HoleClearance { get; init; } = 0.25;

        /// <summary>Gets the chord error for arcs, circles and rounded corners, millimetres.</summary>
        public double MaxError { get; init; } = Geometry.CopperGeometry.DefaultMaxError;

        /// <summary>Gets the name the design is written under — the first word of the file.</summary>
        public string DesignName { get; init; } = "board";
    }
}
