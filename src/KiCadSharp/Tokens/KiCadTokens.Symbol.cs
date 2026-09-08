namespace KiCadSharp
{
    public static partial class KiCadTokens
    {
        /// <summary>
        /// Tokens that belong to the symbol grammar: a <c>.kicad_sym</c> library, and the copies of
        /// its symbols a schematic caches in <c>(lib_symbols …)</c>.
        /// </summary>
        /// <remarks>
        /// The forms a symbol draws with — <see cref="Common.Polyline"/>, <see cref="Common.Arc"/>
        /// and the rest — are in <see cref="Common"/>, because a schematic sheet draws with the same
        /// ones. So are <see cref="Common.Symbol"/> and <see cref="Common.Pin"/>.
        /// </remarks>
        public static class Symbol
        {
            /// <summary>The root form of a symbol library: <c>(kicad_symbol_lib …)</c>.</summary>
            public const string LibraryRoot = "kicad_symbol_lib";

            /// <summary>The group that carries the <c>(hide …)</c> flag for pin names.</summary>
            public const string PinNames = "pin_names";

            /// <summary>The group that carries the <c>(hide …)</c> flag for pin numbers.</summary>
            public const string PinNumbers = "pin_numbers";

            /// <summary>A property's ordinal, KiCad 6's <c>(id n)</c>.</summary>
            public const string Id = "id";

            /// <summary>A pin's number, as printed on the part.</summary>
            public const string Number = "number";

            /// <summary>A circle's radius.</summary>
            public const string Radius = "radius";
        }
    }
}
