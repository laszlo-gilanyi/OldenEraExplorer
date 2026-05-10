// MODIFIED FOR UnityAssetReader: removed Oodle (proprietary, license-incompatible)
// and Zstd (unused for OEE bundles) decompression branches and their `using`
// imports + the static ZstdDecompressor field. EA bundles use only Lzma + LZ4.
// If a bundle hits these branches, throw NotSupportedException so the issue
// surfaces clearly rather than producing silent corruption.
// Modifications copyright (c) 2026 Laszlo Gilanyi (MIT). Upstream file is part
// of AssetStudioMod (MIT) - see ATTRIBUTIONS.md.

using BundleCompression.Lzma;
using System;
using System.IO;
using System.Text.RegularExpressions;
using K4os.Compression.LZ4;

namespace AssetStudio
{
    internal static class BundleDecompressionHelper
    {
        private static readonly string MsgPattern = @"\. ";

        public static MemoryStream DecompressLzmaStream(MemoryStream inStream)
        {
            return SevenZipLzma.DecompressStream(inStream);
        }

        public static long DecompressLzmaStream(Stream compressedStream, Stream decompressedStream, long compressedSize, long decompressedSize, ref string errorMsg)
        {
            var numWrite = -1L;
            try
            {
                numWrite = SevenZipLzma.DecompressStream(compressedStream, decompressedStream, compressedSize, decompressedSize);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                errorMsg = $"({Regex.Split(e.Message, MsgPattern, RegexOptions.CultureInvariant)[0]})";
            }
            return numWrite;
        }

        public static int DecompressBlock(CompressionType type, ReadOnlySpan<byte> srcBuffer, Span<byte> dstBuffer, ref string errorMsg)
        {
            var numWrite = -1;
            try
            {
                switch (type)
                {
                    case CompressionType.Lz4:
                    case CompressionType.Lz4HC:
                        numWrite = LZ4Codec.Decode(srcBuffer, dstBuffer);
                        break;
                    // MODIFIED FOR UnityAssetReader: Zstd decompression not vendored.
                    case CompressionType.Zstd:
                        throw new NotSupportedException("Zstd decompression is not vendored in UnityAssetReader.");
                    // MODIFIED FOR UnityAssetReader: Oodle decompression is proprietary and intentionally not vendored.
                    case CompressionType.Oodle:
                        throw new NotSupportedException("Oodle decompression is proprietary and not vendored in UnityAssetReader.");
                    default:
                        throw new NotSupportedException();
                }
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                errorMsg = $"({Regex.Split(e.Message, MsgPattern, RegexOptions.CultureInvariant)[0]})";
            }
            return numWrite;
        }
    }
}
