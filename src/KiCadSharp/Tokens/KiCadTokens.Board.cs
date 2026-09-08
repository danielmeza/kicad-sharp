namespace KiCadSharp
{
    public static partial class KiCadTokens
    {
        /// <summary>
        /// Tokens that belong to the board grammar, the <c>.kicad_pcb</c> file.
        /// </summary>
        /// <remarks>
        /// A <c>.kicad_mod</c> library that is really a board file — the shape
        /// <see cref="Documents.KiCadFootprintLibrary"/> writes — uses <see cref="Root"/> and
        /// <see cref="General"/> from here, because that is the format it is writing.
        /// </remarks>
        public static class Board
        {
            /// <summary>The root form of a board file: <c>(kicad_pcb …)</c>.</summary>
            public const string Root = "kicad_pcb";

            /// <summary>The board-wide numbers: <c>(general (thickness 1.6) …)</c>.</summary>
            public const string General = "general";

            /// <summary>Whether the board carries teardrops in the pre-KiCad-8 spelling.</summary>
            public const string LegacyTeardrops = "legacy_teardrops";

            // ---------------------------------------------------------------- setup and stackup

            /// <summary>The board setup: design rules, the stackup, plot parameters.</summary>
            public const string Setup = "setup";

            /// <summary>The physical stackup inside <c>(setup …)</c>.</summary>
            public const string Stackup = "stackup";

            /// <summary>The stackup's plating finish.</summary>
            public const string CopperFinish = "copper_finish";

            /// <summary>Whether the stackup's dielectric constraints are enforced.</summary>
            public const string DielectricConstraints = "dielectric_constraints";

            /// <summary>A stackup layer's material name.</summary>
            public const string Material = "material";

            /// <summary>A dielectric layer's relative permittivity.</summary>
            public const string EpsilonR = "epsilon_r";

            /// <summary>A dielectric layer's loss tangent.</summary>
            public const string LossTangent = "loss_tangent";

            /// <summary>The clearance between a pad and the solder mask.</summary>
            public const string PadToMaskClearance = "pad_to_mask_clearance";

            /// <summary>The clearance between a pad and the solder paste.</summary>
            public const string PadToPasteClearance = "pad_to_paste_clearance";

            /// <summary>Whether solder-mask bridges inside a footprint are allowed.</summary>
            public const string AllowSoldermaskBridgesInFootprints = "allow_soldermask_bridges_in_footprints";

            // ---------------------------------------------------------------- copper

            /// <summary>A straight routed track: <c>(segment (start …) (end …) (width w) …)</c>.</summary>
            public const string Segment = "segment";

            /// <summary>
            /// A routed arc on a copper layer: <c>(arc (start …) (mid …) (end …) …)</c>. A symbol's
            /// graphic arc is spelled the same way and is a different form — see
            /// <see cref="Common.Arc"/>.
            /// </summary>
            public const string TrackArc = "arc";

            /// <summary>A via.</summary>
            public const string Via = "via";

            /// <summary>Whether a via's net is free to be reassigned by the router.</summary>
            public const string Free = "free";

            /// <summary>Whether unused layers are removed from a via's stack.</summary>
            public const string RemoveUnusedLayers = "remove_unused_layers";

            // ---------------------------------------------------------------- zones

            /// <summary>A copper or keepout zone.</summary>
            public const string Zone = "zone";

            /// <summary>The name a zone writes its net under, beside the numbered <c>(net …)</c>.</summary>
            public const string NetName = "net_name";

            /// <summary>A zone's drawn outline.</summary>
            public const string Polygon = "polygon";

            /// <summary>One polygon the zone filler produced.</summary>
            public const string FilledPolygon = "filled_polygon";

            /// <summary>Present on a zone that keeps copper out rather than pouring it.</summary>
            public const string Keepout = "keepout";

            /// <summary>Present on a filled polygon the filler cut off from the rest of the zone.</summary>
            public const string Island = "island";

            /// <summary>A zone's outline hatching: <c>(hatch edge 0.5)</c>.</summary>
            public const string Hatch = "hatch";

            /// <summary>A zone's fill priority; the higher number is poured first.</summary>
            public const string Priority = "priority";

            /// <summary>How a zone connects to the pads inside it.</summary>
            public const string ConnectPads = "connect_pads";

            /// <summary>A clearance, inside the form it qualifies.</summary>
            public const string Clearance = "clearance";

            /// <summary>The thinnest copper a zone may be poured to.</summary>
            public const string MinThickness = "min_thickness";

            /// <summary>Whether the filled areas honour the minimum thickness.</summary>
            public const string FilledAreasThickness = "filled_areas_thickness";

            /// <summary>A zone fill's mode: solid or hatched.</summary>
            public const string Mode = "mode";

            /// <summary>The gap a thermal relief leaves around a pad.</summary>
            public const string ThermalGap = "thermal_gap";

            /// <summary>The width of a thermal relief's spokes.</summary>
            public const string ThermalBridgeWidth = "thermal_bridge_width";

            // ---------------------------------------------------------------- drawings

            /// <summary>A graphic line on the board itself, outside any footprint.</summary>
            public const string GrLine = "gr_line";

            /// <summary>A graphic rectangle on the board.</summary>
            public const string GrRect = "gr_rect";

            /// <summary>A graphic circle on the board.</summary>
            public const string GrCircle = "gr_circle";

            /// <summary>A graphic arc on the board.</summary>
            public const string GrArc = "gr_arc";

            /// <summary>A graphic polygon on the board.</summary>
            public const string GrPoly = "gr_poly";

            /// <summary>A graphic Bézier curve on the board.</summary>
            public const string GrCurve = "gr_curve";

            /// <summary>A text item on the board.</summary>
            public const string GrText = "gr_text";

            /// <summary>Whether a text item is knocked out of the copper or silkscreen around it.</summary>
            public const string Knockout = "knockout";

            /// <summary>A dimension: a measured annotation with its own text.</summary>
            public const string Dimension = "dimension";

            /// <summary>A dimension's height, the offset of its line from what it measures.</summary>
            public const string Height = "height";

            /// <summary>A named set of items that move together.</summary>
            public const string Group = "group";

            // ---------------------------------------------------------------- via types

            /// <summary>A via through the whole board — the value KiCad omits from <c>(via …)</c>.</summary>
            public const string ViaThrough = "through";

            /// <summary>A via from an outer layer to an inner one.</summary>
            public const string ViaBlind = "blind";

            /// <summary>A microvia.</summary>
            public const string ViaMicro = "micro";
        }
    }
}
