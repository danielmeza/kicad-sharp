using System.Security.Cryptography;
using System.Text;

using Google.Protobuf;

using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// Packs raw bytes into the <see cref="EmbeddedFile"/> KiCad's IPC API expects, and unpacks one
    /// back into bytes, checking its hash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The proto's <c>data</c> field is not the file. It is what KiCad keeps in memory for an
    /// embedded file: the zstd frame of the content, base64-encoded, as ASCII bytes. And
    /// <c>data_hash</c> is MurmurHash3 x64-128 of the <i>raw</i> content, seeded with
    /// <see cref="HashSeed"/>, written as two uppercase 16-digit hex words. KiCad validates the hash
    /// when it unpacks the message and rejects the whole request on a mismatch ("embedded file
    /// validation failed"), so a caller that builds an <see cref="EmbeddedFile"/> by hand has to get
    /// all three right. This is read from <c>common/embedded_files.cpp</c> and
    /// <c>pcbnew/api/api_pcb_utils.cpp</c> on KiCad master.
    /// </para>
    /// <para>
    /// <see cref="Unpack"/> also accepts a 64-character SHA-256 hash, which is what files embedded
    /// by older KiCad versions carry. It does not accept the "V1" MurmurHash3 that KiCad computes
    /// for files saved before its tail-padding fix; KiCad itself rewrites those hashes on load.
    /// </para>
    /// </remarks>
    public static class EmbeddedFileCodec
    {
        /// <summary>The seed KiCad hashes embedded files with (<c>EMBEDDED_FILES::Seed()</c>).</summary>
        public const uint HashSeed = 0xABBA2345;

        /// <summary>The zstd level KiCad compresses with. Any level decodes the same; this only matches KiCad's output size.</summary>
        public const int CompressionLevel = 15;

        /// <summary>Builds an <see cref="EmbeddedFile"/> from raw content.</summary>
        /// <param name="name">The file name KiCad will list it under; unique within a document.</param>
        /// <param name="content">The raw bytes of the file.</param>
        /// <param name="type">What KiCad should treat the file as. Defaults to "other".</param>
        /// <returns>A message ready to go in <c>AddEmbeddedFiles</c> or <c>SetEmbeddedFiles</c>.</returns>
        public static EmbeddedFile Pack(string name, ReadOnlySpan<byte> content, EmbeddedFileType type = EmbeddedFileType.EftOther)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            using var compressor = new ZstdSharp.Compressor(CompressionLevel);
            var compressed = compressor.Wrap(content);
            var encoded = Convert.ToBase64String(compressed);

            return new EmbeddedFile
            {
                Name = name,
                Type = type,
                Data = ByteString.CopyFrom(encoded, Encoding.ASCII),
                DataHash = ComputeHash(content),
            };
        }

        /// <summary>Decodes the raw content of an <see cref="EmbeddedFile"/>, verifying its hash.</summary>
        /// <param name="file">A message as KiCad returns it from <c>GetEmbeddedFiles</c>, or as <see cref="Pack"/> built it.</param>
        /// <returns>The raw bytes of the file.</returns>
        /// <exception cref="InvalidDataException">The payload is not base64, not a zstd frame, or its hash does not match.</exception>
        public static byte[] Unpack(EmbeddedFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            byte[] compressed;
            try
            {
                compressed = Convert.FromBase64String(file.Data.ToStringUtf8());
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Embedded file '{file.Name}': data is not base64.", exception);
            }

            byte[] content;
            try
            {
                using var decompressor = new ZstdSharp.Decompressor();
                content = decompressor.Unwrap(compressed).ToArray();
            }
            catch (ZstdSharp.ZstdException exception)
            {
                throw new InvalidDataException($"Embedded file '{file.Name}': data is not a zstd frame.", exception);
            }

            if (!HashMatches(file.DataHash, content))
            {
                throw new InvalidDataException($"Embedded file '{file.Name}': hash '{file.DataHash}' does not match its content.");
            }

            return content;
        }

        /// <summary>Same as <see cref="Unpack"/>, reporting failure instead of throwing.</summary>
        /// <param name="file">The message to decode.</param>
        /// <param name="content">The raw bytes, when decoding succeeds.</param>
        /// <returns>True when the payload decoded and its hash matched.</returns>
        public static bool TryUnpack(EmbeddedFile file, out byte[] content)
        {
            try
            {
                content = Unpack(file);
                return true;
            }
            catch (InvalidDataException)
            {
                content = [];
                return false;
            }
        }

        /// <summary>The hash KiCad expects in <c>data_hash</c> for this content.</summary>
        /// <param name="content">The raw bytes of the file.</param>
        /// <returns>32 uppercase hex digits: the two 64-bit halves of MurmurHash3 x64-128, seeded with <see cref="HashSeed"/>.</returns>
        public static string ComputeHash(ReadOnlySpan<byte> content)
        {
            var (h1, h2) = MurmurHash3.HashX64To128(content, HashSeed);
            return $"{h1:X16}{h2:X16}";
        }

        private static bool HashMatches(string hash, byte[] content)
        {
            if (hash.Length == 64)
            {
                // SHA-256, lower-case hex, from file formats before KiCad moved to MurmurHash3.
                return string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), hash, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(ComputeHash(content), hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
