using System;

using KiCadSharp.Schematics;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The fluent mirror of every <c>Add*</c> on the schematic items:
    /// <see cref="KiCadSchematicLine"/> (wires and buses) and <see cref="KiCadBusAlias"/>.
    /// </summary>
    /// <remarks>
    /// Both <c>Add*</c> return nothing, so neither method here takes a callback. A schematic's wires,
    /// buses and aliases are themselves added through its live lists —
    /// <c>schematic.Wires.With(wire =&gt; wire.WithPoint(0, 0).WithPoint(10, 0))</c>, with
    /// <see cref="NodeListFluentExtensions"/>'s <c>With</c>.
    /// </remarks>
    public static class SchematicFluentExtensions
    {
        /// <summary>
        /// Appends a vertex to a wire or bus, as <see cref="KiCadSchematicLine.AddPoint"/> does, and
        /// returns it.
        /// </summary>
        /// <typeparam name="TLine">
        /// <see cref="KiCadWire"/> or <see cref="KiCadBus"/>. <c>AddPoint</c> is declared on their
        /// shared base; the type parameter is what lets a chain on a wire go on being a wire.
        /// </typeparam>
        /// <param name="line">The wire or bus to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="line"/>.</returns>
        public static TLine WithPoint<TLine>(this TLine line, double x, double y)
            where TLine : KiCadSchematicLine
        {
            ArgumentNullException.ThrowIfNull(line);
            line.AddPoint(x, y);
            return line;
        }

        /// <summary>
        /// Appends a net to the bundle, as <see cref="KiCadBusAlias.AddMember"/> does, and returns the alias.
        /// </summary>
        /// <param name="alias">The alias to append to.</param>
        /// <param name="net">The net name.</param>
        /// <returns><paramref name="alias"/>.</returns>
        public static KiCadBusAlias WithMember(this KiCadBusAlias alias, string net)
        {
            ArgumentNullException.ThrowIfNull(alias);
            alias.AddMember(net);
            return alias;
        }
    }
}
