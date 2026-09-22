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
        /// The format stamp a new, board-shaped <see cref="Documents.KiCadFootprintLibrary"/> writes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is not a fallback for a file with no <c>version</c> token: KiCad reads such a
        /// <c>.kicad_mod</c> as format 0 and such a board as <c>20201115</c>, so
        /// <see cref="Documents.KiCadFootprintLibrary.Version"/> reports <see langword="null"/> there
        /// rather than this (#76).
        /// </para>
        /// <para>
        /// <b>Not a <c>.kicad_mod</c> stamp on the write path.</b> That type builds a
        /// <c>kicad_pcb</c> root — its own summary says "board-shaped" — so what it stamps is a
        /// board, and an OLDER format than <see cref="BoardVersion"/>: KiCad 6 against KiCad 9.
        /// Two stamps three lines apart for the same root token is a real divergence, and
        /// collecting the literals is what made it visible. It is reproduced here rather than
        /// reconciled: raising it changes what the type writes, and that is a decision for
        /// whoever knows which readers depend on the old value. A footprint built in memory is
        /// stamped with <see cref="FootprintVersion"/> instead.
        /// </para>
        /// </remarks>
        public const string FootprintLibraryVersion = "20211014";

        /// <summary>
        /// The format stamp a footprint built in memory, <c>new KiCadFootprint(id)</c>, starts with:
        /// KiCad 10.0.6's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// KiCad reads a footprint by the format its <c>version</c> names, and by format 0 when it has
        /// none. Format 0 is KiCad 5's: it refuses an arc drawn by <c>start</c>/<c>mid</c>/<c>end</c>,
        /// and it makes a footprint with no <c>attr</c> a through-hole one (#63). A new footprint
        /// therefore carries the stamp KiCad 10.0.6 writes for a footprint in a library:
        /// <c>SEXPR_BOARD_FILE_VERSION</c>, <c>pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr.h</c>
        /// line 203, at the tag <c>protos/KICAD_PIN</c> pins.
        /// </para>
        /// <para>
        /// A footprint read from a file keeps whatever version it has, or none. See
        /// <see cref="Documents.KiCadFootprint.Version"/> for a footprint placed on a board.
        /// </para>
        /// </remarks>
        public const string FootprintVersion = "20260206";

        /// <summary>The format stamp a new <see cref="Documents.KiCadSymbolLibrary"/> starts with.</summary>
        /// <remarks>
        /// It describes symbols built in memory. The first symbol a new library takes from another
        /// library replaces it with that library's stamp, because KiCad reads some content differently
        /// by version; see <see cref="Documents.KiCadSymbolLibrary.AddSymbol(Documents.KiCadSymbol)"/>.
        /// </remarks>
        public const string SymbolLibraryVersion = "20211014";

        /// <summary>The board thickness a new board is created with, in millimetres.</summary>
        /// <remarks>
        /// A number, because its other half is one: <c>KiCadBoardGeneral.Thickness</c> falls back
        /// through <c>ReadChildDouble</c>. Naming only the constructor's side would have left
        /// exactly the twin this class exists to remove, in the one place typing hid it.
        /// </remarks>
        public const double BoardThicknessMm = 1.6;

        /// <summary>
        /// The clearance KiCad 10.0.6 reads between a zone and the pads it connects to when the zone
        /// has no <c>(connect_pads (clearance …))</c>, in millimetres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>parseZONE</c> starts from the board's default zone settings (<c>pcbnew/zone.cpp</c>,
        /// line 89), whose clearance is <c>ZONE_CLEARANCE_MM</c> (<c>pcbnew/zone_settings.cpp</c>,
        /// line 49; <c>pcbnew/zones.h</c>, line 36), and a <c>(clearance …)</c> inside
        /// <c>(connect_pads …)</c> overrides it (<c>pcb_io_kicad_sexpr_parser.cpp</c>, lines 7979–7982).
        /// MEASURED against kicad-cli 10.0.6: <c>pcb upgrade</c> re-saves a zone that has no
        /// <c>(connect_pads …)</c> with <c>(connect_pads (clearance 0.5))</c>.
        /// </para>
        /// <para>
        /// It has one half only, the getter's, <c>KiCadZone.ConnectPadsClearance</c>: KiCad writes
        /// a clearance on every zone, so no constructor here writes one. The other constant of its
        /// kind, <see cref="BoardThicknessMm"/>, has both halves.
        /// </para>
        /// <para>
        /// <b>A legacy board can change it.</b> A <c>(setup (zone_clearance …))</c>, which KiCad 5
        /// wrote and KiCad 10 still reads (<c>pcb_io_kicad_sexpr_parser.cpp</c>, lines 2550–2554),
        /// replaces the default for every zone on that board that has no clearance of its own;
        /// MEASURED: <c>pcb upgrade</c> re-saves such a zone with the setup's 0.3 rather than 0.5. A
        /// zone view does not know its board, so it reports this constant there too (#102).
        /// </para>
        /// </remarks>
        public const double ZoneClearanceMm = 0.5;

        /// <summary>The generator name <see cref="Documents.KiCadBoard"/> writes.</summary>
        public const string BoardGenerator = "KiCadSharp";

        /// <summary>
        /// The generator name both library document types, and a footprint built in memory, write.
        /// </summary>
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
