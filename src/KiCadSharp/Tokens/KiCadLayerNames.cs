namespace KiCadSharp
{
    /// <summary>
    /// The names of KiCad's standard board layers, and the words its layer tables classify them
    /// with.
    /// </summary>
    /// <remarks>
    /// These are values inside a form rather than the heads of forms — <c>(layer "F.Cu")</c> — so
    /// they are not in <see cref="KiCadTokens"/>. They are here for the same reason: a layer name is
    /// KiCad's spelling, not this library's, and a mistyped one reads as a layer that is not there
    /// instead of failing.
    /// </remarks>
    public static class KiCadLayerNames
    {
        /// <summary>The front copper layer.</summary>
        public const string FCu = "F.Cu";

        /// <summary>The back copper layer.</summary>
        public const string BCu = "B.Cu";

        /// <summary>The front silkscreen, in the name the file stores.</summary>
        public const string FSilkS = "F.SilkS";

        /// <summary>The back silkscreen, in the name the file stores.</summary>
        public const string BSilkS = "B.SilkS";

        /// <summary>The front solder mask.</summary>
        public const string FMask = "F.Mask";

        /// <summary>The back solder mask.</summary>
        public const string BMask = "B.Mask";

        /// <summary>The front solder paste.</summary>
        public const string FPaste = "F.Paste";

        /// <summary>The back solder paste.</summary>
        public const string BPaste = "B.Paste";

        /// <summary>The board outline.</summary>
        public const string EdgeCuts = "Edge.Cuts";

        /// <summary>The margin inside the board outline.</summary>
        public const string Margin = "Margin";

        /// <summary>The front courtyard, in the name the file stores.</summary>
        public const string FCrtYd = "F.CrtYd";

        /// <summary>The back courtyard, in the name the file stores.</summary>
        public const string BCrtYd = "B.CrtYd";

        /// <summary>The front fabrication layer.</summary>
        public const string FFab = "F.Fab";

        /// <summary>The back fabrication layer.</summary>
        public const string BFab = "B.Fab";

        /// <summary>The user comments layer, in the name the file stores.</summary>
        public const string CmtsUser = "Cmts.User";

        /// <summary>The user drawings layer, in the name the file stores.</summary>
        public const string DwgsUser = "Dwgs.User";

        // The user-facing names KiCad shows for the layers whose stored name is abbreviated. They
        // appear in a board's layer table as the row's third value.

        /// <summary>What KiCad calls <see cref="FSilkS"/> in its own interface.</summary>
        public const string FSilkscreen = "F.Silkscreen";

        /// <summary>What KiCad calls <see cref="BSilkS"/> in its own interface.</summary>
        public const string BSilkscreen = "B.Silkscreen";

        /// <summary>What KiCad calls <see cref="FCrtYd"/> in its own interface.</summary>
        public const string FCourtyard = "F.Courtyard";

        /// <summary>What KiCad calls <see cref="BCrtYd"/> in its own interface.</summary>
        public const string BCourtyard = "B.Courtyard";

        /// <summary>What KiCad calls <see cref="CmtsUser"/> in its own interface.</summary>
        public const string UserComments = "User.Comments";

        /// <summary>What KiCad calls <see cref="DwgsUser"/> in its own interface.</summary>
        public const string UserDrawings = "User.Drawings";

        /// <summary>A layer table's word for a copper layer that carries a net.</summary>
        public const string TypeSignal = "signal";

        /// <summary>A layer table's word for a layer that carries no copper.</summary>
        public const string TypeUser = "user";

        /// <summary>A stackup's word for a copper layer, where the layer table says <see cref="TypeSignal"/>.</summary>
        public const string TypeCopper = "copper";
    }

    /// <summary>
    /// The keys of the properties KiCad itself writes on a symbol or a sheet.
    /// </summary>
    /// <remarks>
    /// A property is looked up by its key, so the key is part of the format: asking for
    /// <c>"reference"</c> where the file says <c>"Reference"</c> finds nothing and reports nothing.
    /// </remarks>
    public static class KiCadPropertyNames
    {
        /// <summary>The designator, <c>R1</c> or <c>U3</c>.</summary>
        public const string Reference = "Reference";

        /// <summary>The part value.</summary>
        public const string Value = "Value";

        /// <summary>The footprint the symbol is assigned.</summary>
        public const string Footprint = "Footprint";

        /// <summary>The datasheet link.</summary>
        public const string Datasheet = "Datasheet";

        /// <summary>A hierarchical sheet's name.</summary>
        public const string SheetName = "Sheetname";

        /// <summary>The file a hierarchical sheet points at.</summary>
        public const string SheetFile = "Sheetfile";
    }
}
