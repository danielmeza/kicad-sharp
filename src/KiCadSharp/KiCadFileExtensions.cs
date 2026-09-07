using System;

namespace KiCadSharp
{
    /// <summary>
    /// The file extensions KiCad uses, as constants.
    /// </summary>
    public static class KiCadFileExtensions
    {
        /// <summary>A project file: <c>.kicad_pro</c>.</summary>
        public const string Project = ".kicad_pro";

        /// <summary>A schematic sheet: <c>.kicad_sch</c>.</summary>
        public const string Schematic = ".kicad_sch";

        /// <summary>A board: <c>.kicad_pcb</c>.</summary>
        public const string PCB = ".kicad_pcb";

        /// <summary>A symbol library: <c>.kicad_sym</c>.</summary>
        public const string SymbolLibrary = ".kicad_sym";

        /// <summary>A single footprint: <c>.kicad_mod</c>.</summary>
        public const string Footprint = ".kicad_mod";

        /// <summary>Custom design rules: <c>.kicad_dru</c>.</summary>
        public const string DesignRules = ".kicad_dru";

        /// <summary>A drawing sheet (title block): <c>.kicad_wks</c>.</summary>
        public const string Worksheet = ".kicad_wks";

        /// <summary>Per-user project local settings: <c>.kicad_prl</c>.</summary>
        public const string ProjectLocal = ".kicad_prl";
    }
}
