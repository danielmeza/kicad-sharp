namespace KiCadSharp
{
    public static partial class KiCadTokens
    {
        /// <summary>
        /// Tokens that belong to the footprint grammar: the whole of a <c>.kicad_mod</c>, and the
        /// <c>(footprint …)</c> forms a board carries.
        /// </summary>
        public static class Footprint
        {
            /// <summary>The root form of a footprint: <c>(footprint "Lib:Name" …)</c>.</summary>
            public const string Root = "footprint";

            /// <summary>KiCad 5's spelling of <see cref="Root"/>, still readable.</summary>
            public const string LegacyRoot = "module";

            /// <summary>The footprint's human-readable description.</summary>
            public const string Descr = "descr";

            /// <summary>The footprint's search keywords.</summary>
            public const string Tags = "tags";

            /// <summary>The footprint's attributes: <c>(attr smd through_hole …)</c>.</summary>
            public const string Attr = "attr";

            /// <summary>KiCad 5's last-edited timestamp, superseded by <see cref="Common.Uuid"/>.</summary>
            public const string Tedit = "tedit";

            /// <summary>KiCad 5's instance timestamp, superseded by <see cref="Common.Uuid"/>.</summary>
            public const string Tstamp = "tstamp";

            // ---------------------------------------------------------------- contents

            /// <summary>A pad.</summary>
            public const string Pad = "pad";

            /// <summary>A 3D model reference.</summary>
            public const string Model = "model";

            /// <summary>A displacement, inside a pad or a <c>(model …)</c>.</summary>
            public const string Offset = "offset";

            /// <summary>A 3D model's rotation.</summary>
            public const string Rotate = "rotate";

            /// <summary>A text item inside a footprint.</summary>
            public const string FpText = "fp_text";

            /// <summary>A line inside a footprint.</summary>
            public const string FpLine = "fp_line";

            /// <summary>A rectangle inside a footprint.</summary>
            public const string FpRect = "fp_rect";

            /// <summary>A circle inside a footprint.</summary>
            public const string FpCircle = "fp_circle";

            /// <summary>An arc inside a footprint.</summary>
            public const string FpArc = "fp_arc";

            /// <summary>A polygon inside a footprint.</summary>
            public const string FpPoly = "fp_poly";

            // ---------------------------------------------------------------- fp_text kinds

            // The first value of an (fp_text …), not a token of its own.

            /// <summary>The <c>fp_text</c> that shows the placed footprint's designator.</summary>
            public const string TextTypeReference = "reference";

            /// <summary>The <c>fp_text</c> that shows the part value.</summary>
            public const string TextTypeValue = "value";

            /// <summary>Any other <c>fp_text</c>.</summary>
            public const string TextTypeUser = "user";
        }
    }
}
