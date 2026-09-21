using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// Adding a view that already belongs somewhere, while walking the list it belongs to (#50).
/// </summary>
/// <remarks>
/// <para>
/// A view is its node, and a node has one parent, so every <c>Add…(view)</c> here <em>moves</em> the
/// node: out of the list it was in and into this one. The obvious copy loop,
/// <c>foreach (var s in source.Symbols) destination.AddSymbol(s)</c>, therefore takes each symbol
/// out of the list it is walking. When the walk read that list live, by position, every move
/// shifted the rest down under it and the loop stepped over the next one: 18 of the 35 symbols in
/// <c>orbion.kicad_sym</c> arrived, 17 stayed behind, and nothing said so.
/// </para>
/// <para>
/// The answer kept is the move — it is what keeps a view and the node it edits one thing — and the
/// change is to enumeration: a <c>foreach</c> over a <see cref="KiCadNodeList{T}"/> walks the
/// children that were there when it started. <c>Count</c>, the indexer, <c>Add</c>,
/// <c>Remove</c> and <c>Insert</c> stay live. These tests pin both halves, over every <c>Add…</c>
/// that takes an existing view.
/// </para>
/// </remarks>
public class AddWhileEnumeratingTests
{
    private const int OrbionSymbols = 35;

    private const int OrbionPins = 112;

    // ------------------------------------------------------------------- the loop in the issue

    [Fact]
    public void TheNaturalLoop_MovesEverySymbol_InOrder_ByteForByte()
    {
        var expected = KiCadSymbolLibrary.Load(TestData.SymbolLibrary).Symbols.Select(s => (s.Id, Text: s.Node.ToText())).ToList();
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var destination = new KiCadSymbolLibrary();

        var visits = 0;
        foreach (var symbol in source.Symbols)
        {
            destination.AddSymbol(symbol);
            visits++;
        }

        // Was 18 of 35, with 17 left behind in the source.
        Assert.Equal(OrbionSymbols, visits);
        Assert.Equal(OrbionSymbols, destination.Symbols.Count);
        Assert.Equal(expected.Select(e => e.Id), destination.Symbols.Select(s => s.Id));

        // Moved, not rebuilt: every symbol is still the bytes KiCad wrote.
        Assert.Equal(expected.Select(e => e.Text), destination.Symbols.Select(s => s.Node.ToText()));

        var output = Path.Combine(TestData.NewScratchDirectory(), "moved.kicad_sym");
        destination.Save(output);
        var reloaded = KiCadSymbolLibrary.Load(output);
        Assert.Equal(OrbionSymbols, reloaded.Symbols.Count);
        Assert.Equal(OrbionPins, reloaded.Symbols.Sum(s => s.Pins.Count));
    }

    [Fact]
    public void AfterTheLoop_TheSourceHoldsNoSymbols_AndStillSavesAsALibrary()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var version = source.Version;
        var generator = source.Generator;
        var destination = new KiCadSymbolLibrary();

        foreach (var symbol in source.Symbols)
        {
            destination.AddSymbol(symbol);
        }

        Assert.Empty(source.Symbols);
        Assert.All(destination.Symbols, s => Assert.Same(destination.Node, s.Node.Parent));

        var output = Path.Combine(TestData.NewScratchDirectory(), "emptied.kicad_sym");
        source.Save(output);
        var reloaded = KiCadSymbolLibrary.Load(output);
        Assert.Empty(reloaded.Symbols);
        Assert.Equal(version, reloaded.Version);
        Assert.Equal(generator, reloaded.Generator);
    }

    [Fact]
    public void TheViewYouAdded_IsTheSymbolInTheDestination()
    {
        // What a copy would have broken: the view handed to AddSymbol is the one that ends up in the
        // destination, so an edit made through it after the add lands there.
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var destination = new KiCadSymbolLibrary();
        var symbol = source.GetSymbol("Conn_01x02");
        Assert.NotNull(symbol);

        var added = destination.AddSymbol(symbol);
        symbol.AddProperty("Reference", "P");

        Assert.Equal(symbol, added);
        Assert.Contains(symbol, destination.Symbols);
        Assert.DoesNotContain(symbol, source.Symbols);
        Assert.Equal("P", destination.GetSymbol("Conn_01x02")?.GetPropertyValue("Reference"));
        Assert.Null(source.GetSymbol("Conn_01x02"));
    }

    [Fact]
    public void KeepingTheSource_TakesACopy_AndTheCopyIsStillTheSameBytes()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var before = source.ToText();
        var destination = new KiCadSymbolLibrary();

        foreach (var symbol in source.Symbols)
        {
            destination.AddSymbol(new KiCadSymbol(symbol.Node.Clone()));
        }

        Assert.Equal(OrbionSymbols, source.Symbols.Count);
        Assert.Equal(before, source.ToText());
        Assert.Equal(source.Symbols.Select(s => s.Node.ToText()), destination.Symbols.Select(s => s.Node.ToText()));
    }

    // ------------------------------------------------------------ the indexer and Remove stay live

    [Fact]
    public void TheIndexerAndCount_AreLive_SoAMoveShiftsTheRestDown()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var destination = new KiCadSymbolLibrary();
        var symbols = source.Symbols;
        var second = symbols[1];

        destination.AddSymbol(symbols[0]);

        Assert.Equal(OrbionSymbols - 1, symbols.Count);
        Assert.Equal(second, symbols[0]);
    }

    [Fact]
    public void AForwardIndexLoop_SeesTheListShrink_AsDocumented()
    {
        // The indexer is live, as a List<T>'s is, so walking forward by index while moving out of
        // the list steps over every other element. The documentation says to use foreach, walk
        // backwards, or take [0] until the list is empty; this pins why.
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var destination = new KiCadSymbolLibrary();

        for (var i = 0; i < source.Symbols.Count; i++)
        {
            destination.AddSymbol(source.Symbols[i]);
        }

        Assert.Equal(18, destination.Symbols.Count);
        Assert.Equal(17, source.Symbols.Count);
    }

    [Fact]
    public void TakingTheFirstUntilEmpty_MovesEverySymbol()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var destination = new KiCadSymbolLibrary();

        while (source.Symbols.Count > 0)
        {
            destination.AddSymbol(source.Symbols[0]);
        }

        Assert.Equal(OrbionSymbols, destination.Symbols.Count);
    }

    [Fact]
    public void RemovingWhileEnumerating_RemovesEveryOneAskedFor()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var connectors = library.Symbols.Count(s => s.Id.StartsWith("Conn_", StringComparison.Ordinal));
        Assert.True(connectors > 1, "the fixture needs several adjacent symbols of one kind to show a skip");

        var removed = 0;
        foreach (var symbol in library.Symbols)
        {
            if (symbol.Id.StartsWith("Conn_", StringComparison.Ordinal) && library.Symbols.Remove(symbol))
            {
                removed++;
            }
        }

        Assert.Equal(connectors, removed);
        Assert.Equal(OrbionSymbols - connectors, library.Symbols.Count);
        Assert.DoesNotContain(library.Symbols, s => s.Id.StartsWith("Conn_", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingEverySymbolWhileEnumerating_LeavesNone()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        foreach (var symbol in library.Symbols)
        {
            Assert.True(library.RemoveSymbol(symbol.Id));
        }

        Assert.Empty(library.Symbols);
    }

    [Fact]
    public void AddingToTheListBeingWalked_Terminates()
    {
        // A live walk also reaches what the loop appends, so this never ended.
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        var visits = 0;
        foreach (var symbol in library.Symbols)
        {
            library.AddSymbol(symbol.CloneAs(symbol.Id + "_Copy"));
            if (++visits > 2 * OrbionSymbols)
            {
                Assert.Fail("the loop walked what it appended");
            }
        }

        Assert.Equal(OrbionSymbols, visits);
        Assert.Equal(2 * OrbionSymbols, library.Symbols.Count);
    }

    [Fact]
    public void AnEnumerationWalksWhatWasThereWhenItStarted_ANewOneWalksWhatIsThereNow()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var symbols = library.Symbols;
        var fifth = symbols[4];

        using var walk = symbols.GetEnumerator();
        library.AddSymbol("ADDED");
        Assert.True(symbols.Remove(fifth));

        var walked = new List<KiCadSymbol>();
        while (walk.MoveNext())
        {
            walked.Add(walk.Current);
        }

        // The walk started before both changes: it has the removed one and not the added one.
        Assert.Equal(OrbionSymbols, walked.Count);
        Assert.Contains(fifth, walked);
        Assert.DoesNotContain(walked, s => s.Id == "ADDED");

        // The list itself never stopped tracking the file.
        Assert.Equal(OrbionSymbols, symbols.Count);
        Assert.DoesNotContain(fifth, symbols);
        Assert.Equal("ADDED", symbols.Last().Id);
    }

    // ----------------------------------------------------------------- every other Add…(view)

    [Fact]
    public void SubUnitAddPin_WhileWalkingAnotherSubUnitsPins_MovesEveryPin()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var unit = source.Symbols.SelectMany(s => s.Units).OrderByDescending(u => u.Pins.Count).First();
        var numbers = unit.Pins.Select(p => p.Number).ToList();
        Assert.True(numbers.Count > 2);

        var target = new KiCadSymbol("TARGET").AddUnit("TARGET_1_1");
        foreach (var pin in unit.Pins)
        {
            target.AddPin(pin);
        }

        Assert.Equal(numbers, target.Pins.Select(p => p.Number));
        Assert.Empty(unit.Pins);
    }

    [Fact]
    public void SymbolAddPin_FromAnotherSymbolsPins_MovesEveryPin()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var symbol = source.Symbols.OrderByDescending(s => s.Pins.Count).First();
        var numbers = symbol.Pins.Select(p => p.Number).ToList();

        var target = new KiCadSymbol("TARGET");
        foreach (var pin in symbol.Pins)
        {
            target.AddPin(pin);
        }

        Assert.Equal(numbers, target.Pins.Select(p => p.Number));
        Assert.Empty(symbol.Pins);
    }

    [Fact]
    public void SymbolAddGraphicalItem_FromASubUnitsDrawing_MovesEveryItem()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var unit = source.Symbols.SelectMany(s => s.Units).OrderByDescending(u => u.GraphicalItems.Count).First();
        var texts = unit.GraphicalItems.Select(g => g.Node.ToText()).ToList();
        Assert.True(texts.Count > 2);

        var target = new KiCadSymbol("TARGET");
        foreach (var item in unit.GraphicalItems)
        {
            target.AddGraphicalItem(item);
        }

        Assert.Equal(texts, target.GraphicalItems.Select(g => g.Node.ToText()));
        Assert.Empty(unit.GraphicalItems);
    }

    [Fact]
    public void PropertiesAdd_WhileWalkingAnotherSymbolsProperties_MovesEveryProperty()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var symbol = source.Symbols.OrderByDescending(s => s.Properties.Count).First();
        var keys = symbol.Properties.Select(p => p.Key).ToList();
        Assert.True(keys.Count > 2);

        var target = new KiCadSymbolLibrary().AddSymbol("TARGET");
        foreach (var property in target.Properties)
        {
            target.Properties.Remove(property);
        }

        foreach (var property in symbol.Properties)
        {
            target.Properties.Add(property);
        }

        Assert.Equal(keys, target.Properties.Select(p => p.Key));
        Assert.Empty(symbol.Properties);
    }

    [Fact]
    public void PropertiesInsert_WhileWalkingAnotherSymbolsProperties_MovesEveryProperty()
    {
        var source = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var symbol = source.Symbols.OrderByDescending(s => s.Properties.Count).First();
        var keys = symbol.Properties.Select(p => p.Key).ToList();

        var target = new KiCadSymbol("TARGET");
        var existing = target.Properties.Count;
        foreach (var property in symbol.Properties)
        {
            target.Properties.Insert(0, property);
        }

        Assert.Equal(keys.Count + existing, target.Properties.Count);
        Assert.Equal(Enumerable.Reverse(keys), target.Properties.Take(keys.Count).Select(p => p.Key));
        Assert.Empty(symbol.Properties);
    }

    [Fact]
    public void FootprintLibraryAddFootprint_FromABoardsFootprints_MovesEveryFootprint()
    {
        var board = KiCadFootprintLibrary.Load(TestData.PowerInputBoard);
        var ids = board.Footprints.Select(f => f.Id).ToList();
        Assert.Equal(7, ids.Count);

        var destination = new KiCadFootprintLibrary();
        foreach (var footprint in board.Footprints)
        {
            destination.AddFootprint(footprint);
        }

        Assert.Equal(ids, destination.Footprints.Select(f => f.Id));
        Assert.Empty(board.Footprints);
    }

    [Fact]
    public void BoardFootprintsAdd_WhileWalkingAnotherBoardsFootprints_MovesEveryFootprint()
    {
        var source = KiCadBoard.Load(TestData.PowerInputBoard);
        var ids = source.Footprints.Select(f => f.Id).ToList();
        Assert.Equal(7, ids.Count);

        var destination = new KiCadBoard();
        foreach (var footprint in source.Footprints)
        {
            destination.Footprints.Add(footprint);
        }

        Assert.Equal(ids, destination.Footprints.Select(f => f.Id));
        Assert.Empty(source.Footprints);
    }

    [Fact]
    public void PadsAdd_WhileWalkingAnotherFootprintsPads_MovesEveryPad()
    {
        var source = KiCadFootprintLibrary.Load(TestData.Footprint).Footprints[0];
        var numbers = source.Pads.Select(p => p.Number).ToList();
        Assert.Equal(2, numbers.Count);

        var target = new KiCadFootprint("TARGET");
        foreach (var pad in source.Pads)
        {
            target.Pads.Add(pad);
        }

        Assert.Equal(numbers, target.Pads.Select(p => p.Number));
        Assert.Empty(source.Pads);
    }

    [Fact]
    public void LibrarySymbolsAdd_WhileWalkingAnotherSheetsCache_MovesEverySymbol()
    {
        var source = KiCadSchematic.Load(TestData.Rs485Bridge);
        var ids = source.LibrarySymbols.Select(s => s.Id).ToList();
        Assert.Equal(19, ids.Count);

        var destination = KiCadSchematic.Parse("(kicad_sch\n\t(version 20250114)\n)\n");
        var cache = destination.RequireLibrarySymbols();
        foreach (var symbol in source.LibrarySymbols)
        {
            cache.Add(symbol);
        }

        Assert.Equal(ids, destination.LibrarySymbols.Select(s => s.Id));
        Assert.Empty(source.LibrarySymbols);
    }

    [Fact]
    public void MovingTheFootprintOutOfAKicadMod_LeavesThatFileWithNone()
    {
        // A .kicad_mod is its footprint: the form is the root of the file. Moving it into a board
        // takes it out of that file, and the library over the file has to say so rather than keep
        // handing out a view of a footprint that now belongs to the board.
        var file = KiCadFootprintLibrary.Load(TestData.Footprint);
        var footprint = file.Footprints[0];
        var id = footprint.Id;
        var destination = new KiCadFootprintLibrary();

        destination.AddFootprint(footprint);

        Assert.Equal(footprint, destination.GetFootprint(id));
        Assert.True(file.IsSingleFootprint);
        Assert.Empty(file.Footprints);
        Assert.Null(file.GetFootprint(id));
        Assert.Null(file.Document.Root);
        Assert.DoesNotContain(id, file.ToText(), StringComparison.Ordinal);
    }
}
