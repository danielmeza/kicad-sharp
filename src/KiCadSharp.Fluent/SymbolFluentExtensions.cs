using System;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The fluent mirror of every <c>Add*</c> on <see cref="KiCadSymbolLibrary"/>,
    /// <see cref="KiCadSymbol"/>, <see cref="KiCadSymbolUnit"/> and <see cref="KiCadPolyline"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each method calls the <c>Add*</c> it is named after with the same arguments, hands what that
    /// call returned to the optional <c>configure</c> callback, and returns the object it was called
    /// on.
    /// </para>
    /// <para>
    /// Where the caller builds the child and passes it in — a symbol, a pin, a graphical item — the
    /// method is generic in the child's type, so the callback sees the type that was passed rather
    /// than the base the <c>Add*</c> declares: <c>WithGraphicalItem(new KiCadPolyline(), p =&gt;
    /// p.WithPoint(0, 0))</c> compiles without a cast. The object the callback receives is the one
    /// passed in, which the <c>Add*</c> has just made a child.
    /// </para>
    /// <para>
    /// <see cref="KiCadPolyline.AddPoint"/> returns nothing, so <see cref="WithPoint"/> has no
    /// callback: there is no child view to hand over.
    /// </para>
    /// </remarks>
    public static class SymbolFluentExtensions
    {
        /// <summary>
        /// Appends an existing symbol, as <see cref="KiCadSymbolLibrary.AddSymbol(KiCadSymbol)"/>
        /// does, and returns the library.
        /// </summary>
        /// <typeparam name="TSymbol">The symbol's type, which the callback receives.</typeparam>
        /// <param name="library">The library to append to.</param>
        /// <param name="symbol">The symbol to append.</param>
        /// <param name="configure">Called with <paramref name="symbol"/> once it is a child of the library.</param>
        /// <returns><paramref name="library"/>.</returns>
        public static KiCadSymbolLibrary WithSymbol<TSymbol>(this KiCadSymbolLibrary library, TSymbol symbol, Action<TSymbol>? configure = null)
            where TSymbol : KiCadSymbol
        {
            ArgumentNullException.ThrowIfNull(library);
            library.AddSymbol(symbol);
            return Chain.Then(library, symbol, configure);
        }

        /// <summary>
        /// Creates and appends a new symbol, as <see cref="KiCadSymbolLibrary.AddSymbol(string)"/>
        /// does, and returns the library.
        /// </summary>
        /// <param name="library">The library to append to.</param>
        /// <param name="id">The symbol's name.</param>
        /// <param name="configure">Called with the new symbol — the place to build its units, pins and graphics.</param>
        /// <returns><paramref name="library"/>.</returns>
        public static KiCadSymbolLibrary WithSymbol(this KiCadSymbolLibrary library, string id, Action<KiCadSymbol>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(library);
            return Chain.Then(library, library.AddSymbol(id), configure);
        }

        /// <summary>
        /// Appends a property, or updates it when one with the same key already exists, as
        /// <see cref="KiCadSymbol.AddProperty"/> does, and returns the symbol.
        /// </summary>
        /// <param name="symbol">The symbol to append to.</param>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        /// <param name="configure">Called with the property, new or updated.</param>
        /// <returns><paramref name="symbol"/>.</returns>
        public static KiCadSymbol WithProperty(this KiCadSymbol symbol, string key, string value, Action<KiCadProperty>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            return Chain.Then(symbol, symbol.AddProperty(key, value), configure);
        }

        /// <summary>
        /// Appends a pin to the symbol form itself, as <see cref="KiCadSymbol.AddPin"/> does, and
        /// returns the symbol.
        /// </summary>
        /// <typeparam name="TPin">The pin's type, which the callback receives.</typeparam>
        /// <param name="symbol">The symbol to append to.</param>
        /// <param name="pin">The pin.</param>
        /// <param name="configure">Called with <paramref name="pin"/> once it is a child of the symbol.</param>
        /// <returns><paramref name="symbol"/>.</returns>
        /// <remarks>
        /// KiCad puts pins in a sub-unit, not on the outer form. To match what the editor writes, use
        /// <see cref="WithUnit"/> and <see cref="WithPin{TPin}(KiCadSymbolUnit, TPin, Action{TPin})"/>
        /// on the unit; this one is for symbols built entirely in memory.
        /// </remarks>
        public static KiCadSymbol WithPin<TPin>(this KiCadSymbol symbol, TPin pin, Action<TPin>? configure = null)
            where TPin : KiCadPin
        {
            ArgumentNullException.ThrowIfNull(symbol);
            symbol.AddPin(pin);
            return Chain.Then(symbol, pin, configure);
        }

        /// <summary>
        /// Appends a graphical item to the symbol form itself, as
        /// <see cref="KiCadSymbol.AddGraphicalItem"/> does, and returns the symbol.
        /// </summary>
        /// <typeparam name="TItem">The item's type, which the callback receives.</typeparam>
        /// <param name="symbol">The symbol to append to.</param>
        /// <param name="item">The item: a <see cref="KiCadPolyline"/>, <see cref="KiCadRectangle"/>, <see cref="KiCadCircle"/>, and so on.</param>
        /// <param name="configure">Called with <paramref name="item"/> once it is a child of the symbol.</param>
        /// <returns><paramref name="symbol"/>.</returns>
        public static KiCadSymbol WithGraphicalItem<TItem>(this KiCadSymbol symbol, TItem item, Action<TItem>? configure = null)
            where TItem : KiCadGraphicalItem
        {
            ArgumentNullException.ThrowIfNull(symbol);
            symbol.AddGraphicalItem(item);
            return Chain.Then(symbol, item, configure);
        }

        /// <summary>
        /// Creates and appends a sub-unit, as <see cref="KiCadSymbol.AddUnit"/> does, and returns the
        /// symbol.
        /// </summary>
        /// <param name="symbol">The symbol to append to.</param>
        /// <param name="name">The sub-unit name; KiCad's convention is <c>&lt;symbol&gt;_&lt;unit&gt;_&lt;style&gt;</c>.</param>
        /// <param name="configure">Called with the new sub-unit — the place to add its pins.</param>
        /// <returns><paramref name="symbol"/>.</returns>
        public static KiCadSymbol WithUnit(this KiCadSymbol symbol, string name, Action<KiCadSymbolUnit>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            return Chain.Then(symbol, symbol.AddUnit(name), configure);
        }

        /// <summary>
        /// Appends a pin, as <see cref="KiCadSymbolUnit.AddPin"/> does, and returns the sub-unit.
        /// </summary>
        /// <typeparam name="TPin">The pin's type, which the callback receives.</typeparam>
        /// <param name="unit">The sub-unit to append to.</param>
        /// <param name="pin">The pin.</param>
        /// <param name="configure">Called with <paramref name="pin"/> once it is a child of the sub-unit.</param>
        /// <returns><paramref name="unit"/>.</returns>
        public static KiCadSymbolUnit WithPin<TPin>(this KiCadSymbolUnit unit, TPin pin, Action<TPin>? configure = null)
            where TPin : KiCadPin
        {
            ArgumentNullException.ThrowIfNull(unit);
            unit.AddPin(pin);
            return Chain.Then(unit, pin, configure);
        }

        /// <summary>
        /// Appends a vertex, as <see cref="KiCadPolyline.AddPoint"/> does, and returns the polyline.
        /// </summary>
        /// <param name="polyline">The polyline to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="polyline"/>.</returns>
        public static KiCadPolyline WithPoint(this KiCadPolyline polyline, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(polyline);
            polyline.AddPoint(x, y);
            return polyline;
        }
    }
}
