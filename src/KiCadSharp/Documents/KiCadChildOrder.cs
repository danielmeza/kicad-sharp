using System;
using System.Collections.Generic;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// Where a new child goes in the forms whose children KiCad's parser reads by position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of KiCad's grammar does not care about order: a form's children are read in a loop that
    /// switches on each token, so a child appended at the end is read like any other. A few forms are
    /// different. Their parser reads the first child, or the first two or three, by position before
    /// that loop starts, and rejects the whole file when something else is there. Appending, which is
    /// how a child is created everywhere else, then writes a file KiCad will not open. Set an
    /// <c>fp_poly</c>'s layer before its first point and <c>kicad-cli fp upgrade</c> answers
    /// "Unable to load library" (#59).
    /// </para>
    /// <para>
    /// So a child created through here goes where KiCad reads it: after any positional children that
    /// come before it, and before everything else. Nothing that is already in the form moves. A loaded
    /// file keeps its bytes, and a form that some other tool wrote in an order KiCad rejects stays as
    /// it is. Rearranging it is not this library's decision.
    /// </para>
    /// <para>
    /// The rules come from KiCad 10.0.6, the tag <c>protos/KICAD_PIN</c> pins, file
    /// <c>pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr_parser.cpp</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><c>parsePCB_SHAPE</c> (line 3230) reads the geometry of every drawing before its loop over
    /// the rest (line 3521): <c>fp_poly</c>/<c>gr_poly</c> need <c>pts</c> first (3477, check at 3498);
    /// <c>fp_curve</c>/<c>gr_curve</c> need <c>pts</c> first (3362, 3378); <c>fp_line</c>/<c>gr_line</c>
    /// need <c>start</c> then <c>end</c> (3441, 3458 and 3468); <c>fp_rect</c>/<c>gr_rect</c> the same
    /// (3394, 3411 and 3421); <c>fp_circle</c>/<c>gr_circle</c> need <c>center</c> then <c>end</c> (3326,
    /// 3342 and 3353); and <c>fp_arc</c>/<c>gr_arc</c> need <c>start</c>, <c>mid</c>, <c>end</c>
    /// (3296, 3305, 3314), or in a file stamped 20210925 or older <c>start</c>, <c>end</c>,
    /// <c>angle</c> (3265, 3276, 3286).</item>
    /// <item><c>parseDIMENSION</c> (4503) needs <c>type</c> first (4540).</item>
    /// <item>A zone's <c>polygon</c> holds <c>pts</c> and nothing else (8288: <c>pts</c> checked at 8295,
    /// the form closed at 8301).</item>
    /// <item>A zone's <c>filled_polygon</c> reads an optional <c>layer</c> (8320), an optional
    /// <c>island</c> (8339), then <c>pts</c> (8346), and closes (8363).</item>
    /// <item>A board's <c>version</c> is read only as the first child of <c>kicad_pcb</c>
    /// (<c>parseHeader</c>, 1672). A footprint reads it anywhere (5039), but everything read before it
    /// is read as format 0, the reader's starting value (104): an arc with a <c>mid</c> is then parsed
    /// as a KiCad 5 arc and rejected (3262).</item>
    /// </list>
    /// <para>
    /// In <c>eeschema/sch_io/kicad_sexpr/sch_io_kicad_sexpr_parser.cpp</c>, which reads schematics and
    /// symbol libraries, only the file's <c>version</c> is positional: <c>parseHeader</c> takes it as
    /// the first child of <c>kicad_sch</c> or <c>kicad_symbol_lib</c> (918). Every drawing there is
    /// read in a loop, so its <c>pts</c> may sit anywhere, including in a symbol <c>polyline</c> (1831,
    /// <c>pts</c> at 1840), a sheet <c>polyline</c> (4179, 4188), a <c>wire</c> or <c>bus</c> (4257, 4266)
    /// and a <c>bezier</c> (1461 and 1470, 4571 and 4580).
    /// </para>
    /// </remarks>
    internal static class KiCadChildOrder
    {
        private static readonly string[] Points = [KiCadTokens.Common.Pts];

        private static readonly string[] StartEnd = [KiCadTokens.Common.Start, KiCadTokens.Common.End];

        private static readonly string[] CenterEnd = [KiCadTokens.Common.Center, KiCadTokens.Common.End];

        /// <summary>
        /// Both spellings of an arc at once: <c>start mid end</c> since KiCad 6, <c>start end angle</c>
        /// before it. A form has one or the other, and this order puts either one where KiCad reads it.
        /// </summary>
        private static readonly string[] Arc = [KiCadTokens.Common.Start, KiCadTokens.Common.Mid, KiCadTokens.Common.End, KiCadTokens.Common.Angle];

        private static readonly string[] VersionFirst = [KiCadTokens.Common.Version];

        /// <summary>For each form token, the children KiCad reads by position, in the order it reads them.</summary>
        private static readonly Dictionary<string, string[]> Positional = new(StringComparer.Ordinal)
        {
            [KiCadTokens.Footprint.FpPoly] = Points,
            [KiCadTokens.Board.GrPoly] = Points,
            [KiCadTokens.Footprint.FpCurve] = Points,
            [KiCadTokens.Board.GrCurve] = Points,
            [KiCadTokens.Footprint.FpLine] = StartEnd,
            [KiCadTokens.Board.GrLine] = StartEnd,
            [KiCadTokens.Footprint.FpRect] = StartEnd,
            [KiCadTokens.Board.GrRect] = StartEnd,
            [KiCadTokens.Footprint.FpCircle] = CenterEnd,
            [KiCadTokens.Board.GrCircle] = CenterEnd,
            [KiCadTokens.Footprint.FpArc] = Arc,
            [KiCadTokens.Board.GrArc] = Arc,
            [KiCadTokens.Board.Dimension] = [KiCadTokens.Common.Type],
            [KiCadTokens.Board.Polygon] = Points,
            [KiCadTokens.Board.FilledPolygon] = [KiCadTokens.Common.Layer, KiCadTokens.Board.Island, KiCadTokens.Common.Pts],
            [KiCadTokens.Board.Root] = VersionFirst,
            [KiCadTokens.Footprint.Root] = VersionFirst,
            [KiCadTokens.Footprint.LegacyRoot] = VersionFirst,
            [KiCadTokens.Schematic.Root] = VersionFirst,
            [KiCadTokens.Symbol.LibraryRoot] = VersionFirst,
        };

        /// <summary>Gets the child with <paramref name="token"/>, creating it where KiCad reads it when there is none.</summary>
        /// <param name="form">The form that holds, or will hold, the child.</param>
        /// <param name="token">The child's token.</param>
        /// <returns>The child, existing or new.</returns>
        internal static SExpression Require(SExpression form, string token) => form.GetChild(token) ?? Create(form, token);

        /// <summary>
        /// Creates the child with <paramref name="token"/> in its place when it is positional and
        /// missing, so that a <c>SetChildValue</c> that follows writes into it rather than appending
        /// one. Does nothing for a child that exists or that may go anywhere.
        /// </summary>
        /// <param name="form">The form that holds, or will hold, the child.</param>
        /// <param name="token">The child's token.</param>
        internal static void Place(SExpression form, string token)
        {
            if (Rank(form.Token, token) >= 0 && form.GetChild(token) is null)
            {
                Create(form, token);
            }
        }

        /// <summary>Creates an empty child: in its place when it is positional, at the end otherwise.</summary>
        private static SExpression Create(SExpression form, string token)
        {
            var index = InsertionIndex(form, token);
            if (index >= form.Children.Count)
            {
                return form.CreateChild(token);
            }

            var child = new SExpression(token);
            form.Children.Insert(index, child);
            return child;
        }

        /// <summary>
        /// The position among <paramref name="form"/>'s children where a new <paramref name="token"/>
        /// goes: past the leading run of positional children that KiCad reads before it, and in front
        /// of whatever follows that run. A child that may go anywhere goes at the end.
        /// </summary>
        private static int InsertionIndex(SExpression form, string token)
        {
            var rank = Rank(form.Token, token);
            var children = form.Children;
            if (rank < 0)
            {
                return children.Count;
            }

            var index = 0;
            while (index < children.Count && Rank(form.Token, children[index].Token) is var before && before >= 0 && before < rank)
            {
                index++;
            }

            return index;
        }

        /// <summary>Where KiCad reads <paramref name="token"/> inside <paramref name="form"/>, or -1 when it reads it anywhere.</summary>
        private static int Rank(string form, string token) =>
            Positional.TryGetValue(form, out var order) ? Array.IndexOf(order, token) : -1;
    }
}
