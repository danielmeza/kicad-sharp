namespace KiCadSharp
{
    /// <summary>
    /// The default VALUES a KiCad document carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="KiCadTokens"/> names the tokens — <c>version</c>, <c>paper</c>,
    /// <c>generator_version</c>. This names what goes inside them, which is a different kind of
    /// fact and was previously written as a bare literal at each use.
    /// </para>
    /// <para>
    /// The format versions matter most. Each document type carried its stamp <b>twice</b>: once as
    /// the constructor's default parameter and once as the fallback in the <c>Version</c> getter,
    /// with nothing making the two agree. Bump one and a document built with the default reports a
    /// different version from one read out of a file that has no <c>version</c> token — a
    /// disagreement no test could see, because both halves were spelled correctly.
    /// </para>
    /// <para>
    /// They are also not values to keep current for their own sake. A stamp says what format the
    /// document is written in; raising it claims a format the writer does not produce, and KiCad
    /// upgrades a file it opens rather than rejecting the claim.
    /// </para>
    /// </remarks>
    public static class KiCadDefaults
    {
        /// <summary>The sheet size a new document is created with: <c>(paper "A4")</c>.</summary>
        public const string Paper = "A4";

        /// <summary>The format stamp of a <c>.kicad_pcb</c> this library writes.</summary>
        public const string BoardVersion = "20241229";

        /// <summary>The format stamp of a <c>.kicad_mod</c> footprint library this library writes.</summary>
        public const string FootprintLibraryVersion = "20211014";

        /// <summary>The format stamp of a <c>.kicad_sym</c> symbol library this library writes.</summary>
        public const string SymbolLibraryVersion = "20211014";

        /// <summary>The board thickness a new board is created with, in millimetres.</summary>
        public const string BoardThickness = "1.6";

        /// <summary>
        /// The prefix marking a reference designator KiCad generates rather than a designer owning.
        /// </summary>
        /// <remarks>
        /// A power symbol answers to <c>#PWR001</c>, numbered per sheet, so two sheets collide by
        /// design. Anything checking designators for uniqueness has to skip these or it reports the
        /// annotator's own bookkeeping as a defect.
        /// </remarks>
        public const char GeneratedReferencePrefix = '#';
    }
}
