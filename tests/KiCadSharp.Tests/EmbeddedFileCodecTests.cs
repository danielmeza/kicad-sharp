using System.Security.Cryptography;
using System.Text;

using Google.Protobuf;

using Kiapi.Common.Types;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="EmbeddedFileCodec"/> produces what KiCad's <c>EMBEDDED_FILES</c> produces.
/// </summary>
/// <remarks>
/// The hash vectors were generated with the <c>mmh3</c> Python package (a binding of the reference
/// MurmurHash3), <c>hash_bytes(data, seed=0xABBA2345, x64arch=True)</c>, formatted the way KiCad's
/// <c>HASH_128::ToString</c> formats: the two 64-bit halves as uppercase hex, 16 digits each. The
/// lengths are chosen around the 16-byte block size, because the tail handling is where a port of
/// MurmurHash3 goes wrong, and KiCad's own has a "V1" variant for exactly that reason.
/// </remarks>
public class EmbeddedFileCodecTests
{
    [Theory]
    [InlineData("", "0A8AD5D826E19AE9B0771881583631B3")]
    [InlineData("hello", "C286888BA00E28939A2A3E91350AF99D")]
    public void ComputeHash_MatchesTheReferenceImplementation_ForText(string text, string expected)
    {
        Assert.Equal(expected, EmbeddedFileCodec.ComputeHash(Encoding.ASCII.GetBytes(text)));
    }

    [Theory]
    [InlineData(15, "1ECD41CAA9D465B41ABD4B82DC91C94C")]
    [InlineData(16, "FA03DBB5E3A1C1BAF12D125E128A2FED")]
    [InlineData(17, "4624662CF68C4521488C1BB286C67572")]
    public void ComputeHash_MatchesTheReferenceImplementation_AroundTheBlockSize(int length, string expected)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)i;
        }

        Assert.Equal(expected, EmbeddedFileCodec.ComputeHash(bytes));
    }

    [Fact]
    public void ComputeHash_MatchesTheReferenceImplementation_OverManyBlocks()
    {
        var text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 7));
        Assert.Equal(315, text.Length);
        Assert.Equal("EC538E77DCC9AC996ACF9BD054FD73A0", EmbeddedFileCodec.ComputeHash(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void Pack_ProducesBase64OfAZstdFrame_AndTheHashOfTheRawContent()
    {
        var content = Encoding.ASCII.GetBytes("hello world");

        var file = EmbeddedFileCodec.Pack("hello.txt", content, EmbeddedFileType.EftDatasheet);

        Assert.Equal("hello.txt", file.Name);
        Assert.Equal(EmbeddedFileType.EftDatasheet, file.Type);
        Assert.Equal(EmbeddedFileCodec.ComputeHash(content), file.DataHash);

        // The data field is the base64 text, as ASCII bytes -- not the compressed bytes themselves.
        var encoded = file.Data.ToStringUtf8();
        var compressed = Convert.FromBase64String(encoded);
        Assert.Equal(new byte[] { 0x28, 0xB5, 0x2F, 0xFD }, compressed.Take(4));   // zstd frame magic
    }

    [Fact]
    public void Unpack_ReturnsWhatPackWasGiven()
    {
        var content = RandomNumberGenerator.GetBytes(10_000);

        var file = EmbeddedFileCodec.Pack("blob.bin", content);

        Assert.Equal(content, EmbeddedFileCodec.Unpack(file));
        Assert.True(EmbeddedFileCodec.TryUnpack(file, out var again));
        Assert.Equal(content, again);
    }

    [Fact]
    public void Unpack_RejectsAHashThatDoesNotMatch()
    {
        var file = EmbeddedFileCodec.Pack("a.txt", Encoding.ASCII.GetBytes("abc"));
        file.DataHash = EmbeddedFileCodec.ComputeHash(Encoding.ASCII.GetBytes("abd"));

        var failure = Assert.Throws<InvalidDataException>(() => EmbeddedFileCodec.Unpack(file));

        Assert.Contains("a.txt", failure.Message);
        Assert.Contains("does not match", failure.Message);
        Assert.False(EmbeddedFileCodec.TryUnpack(file, out _));
    }

    [Fact]
    public void Unpack_AcceptsTheSha256HashOlderKiCadFilesCarry()
    {
        var content = Encoding.ASCII.GetBytes("legacy");
        var file = EmbeddedFileCodec.Pack("legacy.txt", content);
        file.DataHash = Convert.ToHexStringLower(SHA256.HashData(content));

        Assert.Equal(content, EmbeddedFileCodec.Unpack(file));
    }

    [Fact]
    public void Unpack_RejectsDataThatIsNotBase64_OrNotZstd()
    {
        var notBase64 = new EmbeddedFile { Name = "x", Data = ByteString.CopyFromUtf8("not base64!"), DataHash = "" };
        Assert.Contains("not base64", Assert.Throws<InvalidDataException>(() => EmbeddedFileCodec.Unpack(notBase64)).Message);

        var notZstd = new EmbeddedFile { Name = "y", Data = ByteString.CopyFromUtf8(Convert.ToBase64String(Encoding.ASCII.GetBytes("plain"))), DataHash = "" };
        Assert.Contains("not a zstd frame", Assert.Throws<InvalidDataException>(() => EmbeddedFileCodec.Unpack(notZstd)).Message);
    }
}
