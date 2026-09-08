namespace KiCadSharp
{
    public static partial class KiCadTokens
    {
        /// <summary>
        /// Tokens that belong to the schematic grammar, the <c>.kicad_sch</c> file.
        /// </summary>
        public static class Schematic
        {
            /// <summary>The root form of a schematic: <c>(kicad_sch …)</c>.</summary>
            public const string Root = "kicad_sch";

            /// <summary>The sheet's page number, inside a sheet instance.</summary>
            public const string Page = "page";

            /// <summary>The symbol definitions the sheet caches, so it opens without its libraries.</summary>
            public const string LibSymbols = "lib_symbols";

            // ---------------------------------------------------------------- connectivity

            /// <summary>A wire segment.</summary>
            public const string Wire = "wire";

            /// <summary>A bus.</summary>
            public const string Bus = "bus";

            /// <summary>The stub that joins a wire to a bus.</summary>
            public const string BusEntry = "bus_entry";

            /// <summary>A named bus and the nets in it.</summary>
            public const string BusAlias = "bus_alias";

            /// <summary>A junction dot.</summary>
            public const string Junction = "junction";

            /// <summary>A junction's diameter.</summary>
            public const string Diameter = "diameter";

            /// <summary>A no-connect marker.</summary>
            public const string NoConnect = "no_connect";

            // ---------------------------------------------------------------- labels

            /// <summary>A local label, naming a net on this sheet.</summary>
            public const string Label = "label";

            /// <summary>A global label, naming a net across the whole design.</summary>
            public const string GlobalLabel = "global_label";

            /// <summary>A hierarchical label, the sheet-side end of a sheet pin.</summary>
            public const string HierarchicalLabel = "hierarchical_label";

            /// <summary>A net-class flag, attaching a net class to a net.</summary>
            public const string NetClassFlag = "netclass_flag";

            /// <summary>The direction of a sheet pin or hierarchical label.</summary>
            public const string Shape = "shape";

            // ---------------------------------------------------------------- images

            /// <summary>An embedded bitmap.</summary>
            public const string Image = "image";

            /// <summary>The base64 payload of an embedded bitmap.</summary>
            public const string Data = "data";

            // ---------------------------------------------------------------- hierarchy

            /// <summary>A child sheet placed on this one.</summary>
            public const string Sheet = "sheet";

            /// <summary>The page numbers of every sheet path in the design.</summary>
            public const string SheetInstances = "sheet_instances";

            /// <summary>The per-project instance data of a placed symbol or sheet.</summary>
            public const string Instances = "instances";

            /// <summary>One project inside an <c>(instances …)</c> form.</summary>
            public const string Project = "project";

            /// <summary>One sheet path inside a project's instance data.</summary>
            public const string Path = "path";

            /// <summary>
            /// The designator a placed symbol carries on one sheet path. The footprint grammar uses
            /// the same word as the <em>value</em> of an <c>fp_text</c>'s type, not as a token.
            /// </summary>
            public const string Reference = "reference";

            /// <summary>Which unit of a multi-unit symbol is placed.</summary>
            public const string Unit = "unit";

            // ---------------------------------------------------------------- placed symbols

            /// <summary>The library identifier a placed symbol was taken from.</summary>
            public const string LibId = "lib_id";

            /// <summary>The name a placed symbol uses inside <c>(lib_symbols …)</c>, when it differs.</summary>
            public const string LibName = "lib_name";

            /// <summary>How a placed symbol is mirrored.</summary>
            public const string Mirror = "mirror";

            /// <summary>Which body style (De Morgan alternative) is drawn, KiCad 8 and later.</summary>
            public const string BodyStyle = "body_style";

            /// <summary>KiCad 6's spelling of <see cref="BodyStyle"/>.</summary>
            public const string Convert = "convert";

            /// <summary>Whether the symbol is left out of simulation.</summary>
            public const string ExcludeFromSim = "exclude_from_sim";

            /// <summary>Whether the part is marked do-not-populate.</summary>
            public const string Dnp = "dnp";

            /// <summary>Whether the part appears in the pick-and-place files.</summary>
            public const string InPosFiles = "in_pos_files";
        }
    }
}
