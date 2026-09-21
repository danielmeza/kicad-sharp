using System.Buffers.Binary;

namespace KiCadSharp
{
    /// <summary>
    /// MurmurHash3, the x64 128-bit variant, as KiCad's <c>MMH3_HASH</c> computes it.
    /// </summary>
    /// <remarks>
    /// KiCad stamps every embedded file with this hash of its raw content (seed
    /// <see cref="EmbeddedFileCodec.HashSeed"/>) and refuses a file whose hash does not match, so a
    /// client that embeds files has to produce the same digest. KiCad's implementation is a streaming
    /// port of Austin Appleby's reference <c>MurmurHash3_x64_128</c>; this is the one-shot form of
    /// the same algorithm, checked against the reference vectors in the tests.
    /// </remarks>
    internal static class MurmurHash3
    {
        private const ulong C1 = 0x87c37b91114253d5UL;
        private const ulong C2 = 0x4cf5ad432745937fUL;

        /// <summary>Hashes <paramref name="data"/> and returns the two 64-bit halves of the digest.</summary>
        /// <param name="data">The bytes to hash.</param>
        /// <param name="seed">The seed, applied to both halves as the reference implementation does.</param>
        public static (ulong H1, ulong H2) HashX64To128(ReadOnlySpan<byte> data, uint seed)
        {
            ulong h1 = seed;
            ulong h2 = seed;
            var blocks = data.Length / 16;

            for (var i = 0; i < blocks; i++)
            {
                var block = data.Slice(i * 16, 16);
                var k1 = BinaryPrimitives.ReadUInt64LittleEndian(block);
                var k2 = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);

                k1 *= C1; k1 = RotateLeft(k1, 31); k1 *= C2; h1 ^= k1;
                h1 = RotateLeft(h1, 27); h1 += h2; h1 = h1 * 5 + 0x52dce729;

                k2 *= C2; k2 = RotateLeft(k2, 33); k2 *= C1; h2 ^= k2;
                h2 = RotateLeft(h2, 31); h2 += h1; h2 = h2 * 5 + 0x38495ab5;
            }

            var tail = data[(blocks * 16)..];
            if (tail.Length > 8)
            {
                ulong k2 = 0;
                for (var i = tail.Length - 1; i >= 8; i--)
                {
                    k2 ^= (ulong)tail[i] << (8 * (i - 8));
                }

                k2 *= C2; k2 = RotateLeft(k2, 33); k2 *= C1; h2 ^= k2;
            }

            if (tail.Length > 0)
            {
                ulong k1 = 0;
                for (var i = Math.Min(tail.Length, 8) - 1; i >= 0; i--)
                {
                    k1 ^= (ulong)tail[i] << (8 * i);
                }

                k1 *= C1; k1 = RotateLeft(k1, 31); k1 *= C2; h1 ^= k1;
            }

            h1 ^= (ulong)data.Length;
            h2 ^= (ulong)data.Length;
            h1 += h2;
            h2 += h1;
            h1 = FinalMix(h1);
            h2 = FinalMix(h2);
            h1 += h2;
            h2 += h1;
            return (h1, h2);
        }

        private static ulong RotateLeft(ulong value, int bits) => (value << bits) | (value >> (64 - bits));

        private static ulong FinalMix(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccdUL;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53UL;
            k ^= k >> 33;
            return k;
        }
    }
}
