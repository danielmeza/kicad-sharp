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
        // A note on `const`, because the guarantee above has an edge. A public const is inlined
        // into every referencing assembly at ITS compile time. The constructors here therefore
        // read these at run time (`string? version = null` plus `??`) rather than through an
        // optional-parameter default, which would have baked the stamp into a consumer's IL and
        // let a consumer that upgraded the package without rebuilding construct documents with
        // the old stamp while reporting the new one — the same disagreement, surviving across the
        // package boundary this class exists to close.

        /// <summary>The sheet size a new document is created with: <c>(paper "A4")</c>.</summary>
        public const string Paper = "A4";

        /// <summary>The format stamp of a <c>.kicad_pcb</c> this library writes.</summary>
        public const string BoardVersion = "20241229";

        /// <summary>
        /// The format stamp <see cref="Documents.KiCadFootprintLibrary"/> writes, and the fallback
        /// for a <c>.kicad_mod</c> parsed without a <c>version</c> token.
        /// </summary>
        /// <remarks>
        /// <b>Not a <c>.kicad_mod</c> stamp on the write path.</b> That type builds a
        /// <c>kicad_pcb</c> root — its own summary says "board-shaped" — so what it stamps is a
        /// board, and an OLDER format than <see cref="BoardVersion"/>: KiCad 6 against KiCad 9.
        /// Two stamps three lines apart for the same root token is a real divergence, and
        /// collecting the literals is what made it visible. It is reproduced here rather than
        /// reconciled: raising it changes what the type writes, and that is a decision for
        /// whoever knows which readers depend on the old value.
        /// </remarks>
        public const string FootprintLibraryVersion = "20211014";

        /// <summary>The format stamp <see cref="Documents.KiCadSymbolLibrary"/> writes.</summary>
        public const string SymbolLibraryVersion = "20211014";

        /// <summary>The board thickness a new board is created with, in millimetres.</summary>
        /// <remarks>
        /// A number, because its other half is one: <c>KiCadBoardGeneral.Thickness</c> falls back
        /// through <c>ReadChildDouble</c>. Naming only the constructor's side would have left
        /// exactly the twin this class exists to remove, in the one place typing hid it.
        /// </remarks>
        public const double BoardThicknessMm = 1.6;

        /// <summary>The generator name <see cref="Documents.KiCadBoard"/> writes.</summary>
        public const string BoardGenerator = "KiCadSharp";

        /// <summary>The generator name both library document types write.</summary>
        public const string LibraryGenerator = "KiCad Library Importer";

        /// <summary>
        /// The prefix marking a reference designator KiCad generates rather than a designer owning.
        /// </summary>
        /// <remarks>
        /// A power symbol answers to <c>#PWR001</c>, numbered per sheet, so two sheets collide by
        /// design.
        /// <para>
        /// <b>This library does not skip them for you.</b>
        /// <c>SchematicAnnotator.FindDuplicateReferences</c> reports generated designators along
        /// with every other duplicate, and <c>AnnotationTests</c> counts them: 14 duplicates on
        /// its fixture, of which 7 are power and flag symbols. Filtering is the caller's decision;
        /// this constant is what to filter on, and the test suite now does.
        /// </para>
        /// </remarks>
        public const char GeneratedReferencePrefix = '#';
    }
}
