using KiCadSharp.Documents;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// The design is SPELLED the way KiCad spells it — every word quoted where KiCad quotes it and bare
/// where KiCad leaves it bare — not only parsed to the same values.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SpecctraDesignOracleTests"/> compares the two designs after parsing, and a parser
/// strips quotes: <c>"U10-1"</c> and <c>U10-1</c> read the same. Freerouting does not. MEASURED
/// 2026-09-21 on Freerouting 2.4.1 with a design 0.3.0 wrote for an excerpt of ORB-PCB-BRG01: every
/// pin reference was quoted whole, Freerouting logged <c>Non-ansi character '"' found … just after
/// 'TP3-1'</c> for each and read NO pins, and <c>board route</c> reported "0 open, escaped 0/0" on a
/// board with 209 connections to make. KiCad writes a pin reference as <c>PIN_REF::FormatIt</c> does:
/// the component and the pin quoted SEPARATELY, only when each needs it, joined by a bare <c>-</c>.
/// </para>
/// <para>
/// The comparison here is of raw tokens — the text and whether it arrived in quotes — for every
/// word that appears in both files.
/// </para>
/// </remarks>
public class SpecctraSpellingTests
{
    [Theory]
    [MemberData(nameof(SpecctraDesignOracleTests.Boards), MemberType = typeof(SpecctraDesignOracleTests))]
    public void EveryWordIsQuotedWhereKiCadQuotesItAndNowhereElse(string oracle, string boardPath)
    {
        var kicad = Tokens(File.ReadAllText(Path.Combine(TestData.Root, "oracles", oracle)));
        var ours = Tokens(SpecctraDesign.Export(KiCadBoard.Load(boardPath), new SpecctraOptions()));

        var quotedByUs = ours.Where(t => t.Quoted).Select(t => t.Text).Intersect(kicad.Where(t => !t.Quoted).Select(t => t.Text)).ToList();
        var quotedByKiCad = kicad.Where(t => t.Quoted).Select(t => t.Text).Intersect(ours.Where(t => !t.Quoted).Select(t => t.Text)).ToList();

        Assert.True(quotedByUs.Count == 0, $"{quotedByUs.Count} word(s) quoted here and bare in KiCad's design, e.g. {string.Join(", ", quotedByUs.Take(5))}");
        Assert.True(quotedByKiCad.Count == 0, $"{quotedByKiCad.Count} word(s) bare here and quoted in KiCad's design, e.g. {string.Join(", ", quotedByKiCad.Take(5))}");
    }

    /// <summary>
    /// A part whose reference holds a <c>-</c> quotes the reference and not the pin, and reads back as
    /// the same part and pin — <c>PIN_REF::FormatIt</c>'s rule, both ways.
    /// </summary>
    [Fact]
    public void APinReferenceQuotesEachPartOnlyWhenThatPartNeedsIt()
    {
        var pins = new SpecctraNode("pins", new SpecctraPinRef("U10", "1"), new SpecctraPinRef("U-1", "3"), new SpecctraPinRef("R4", "A-1"));

        Assert.Equal("(pins U10-1 \"U-1\"-3 R4-\"A-1\")\n", pins.ToString());

        var read = SpecctraReader.Parse(pins.ToString());
        Assert.Equal([new("U10", "1"), new("U-1", "3"), new("R4", "A-1")], read.PinRefs.ToArray());
        Assert.Equal(["U10-1", "U-1-3", "R4-A-1"], read.Atoms.ToArray());
    }

    /// <summary>
    /// Every token of a Specctra text: its characters and whether they were quoted. A pin reference
    /// KiCad writes as <c>"U-1"-3</c> comes out as two tokens, <c>U-1</c> quoted and <c>-3</c> bare.
    /// </summary>
    internal static IReadOnlyList<(string Text, bool Quoted)> Tokens(string text)
    {
        var tokens = new List<(string, bool)>();
        var quote = '"';
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c is '(' or ')' || char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == quote)
            {
                var end = text.IndexOf(quote, i + 1);
                tokens.Add((text[(i + 1)..end], true));
                i = end + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not '(' and not ')' && text[i] != quote)
            {
                i++;
            }

            var word = text[start..i];
            tokens.Add((word, false));
            if (word == "string_quote")
            {
                // `(string_quote ")`: the character after it is the quote, and is not a token.
                while (char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                quote = text[i++];
            }
        }

        return tokens;
    }
}
