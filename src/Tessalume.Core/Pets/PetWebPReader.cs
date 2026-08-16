using System.Buffers.Binary;
using System.Text;

namespace Tessalume.Core.Pets;

internal static class PetWebPReader
{
    private const int MaximumHeaderScanBytes = 1024 * 1024;

    public static async Task<PetWebPInfo> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 20)
        {
            throw new InvalidDataException("WebP 图集头不完整。");
        }

        var riffHeader = new byte[12];
        await stream.ReadExactlyAsync(riffHeader, cancellationToken);
        if (!riffHeader.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !riffHeader.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            throw new InvalidDataException("图集不是有效的 RIFF WebP 文件。");
        }

        var declaredLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(riffHeader.AsSpan(4, 4)) + 8;
        if (declaredLength > stream.Length || declaredLength < 20)
        {
            throw new InvalidDataException("WebP 图集声明了无效的 RIFF 长度。");
        }

        var sawAlphaChunk = false;
        while (stream.Position + 8 <= declaredLength && stream.Position <= MaximumHeaderScanBytes)
        {
            var chunkHeader = new byte[8];
            await stream.ReadExactlyAsync(chunkHeader, cancellationToken);
            var chunkName = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            var dataStart = stream.Position;
            var dataEnd = dataStart + chunkSize;
            if (dataEnd > declaredLength)
            {
                throw new InvalidDataException("WebP 图集包含越界的区块长度。");
            }

            if (chunkName == "ALPH")
            {
                sawAlphaChunk = true;
            }
            else if (chunkName == "VP8L")
            {
                if (chunkSize < 5)
                {
                    throw new InvalidDataException("WebP VP8L 图集头不完整。");
                }
                var losslessHeader = new byte[5];
                await stream.ReadExactlyAsync(losslessHeader, cancellationToken);
                if (losslessHeader[0] != 0x2f)
                {
                    throw new InvalidDataException("WebP VP8L 签名无效。");
                }
                var width = 1 + losslessHeader[1] + ((losslessHeader[2] & 0x3f) << 8);
                var height = 1 + ((losslessHeader[2] & 0xc0) >> 6) +
                             (losslessHeader[3] << 2) + ((losslessHeader[4] & 0x0f) << 10);
                var hasAlpha = (losslessHeader[4] & 0x10) != 0;
                var version = losslessHeader[4] >> 5;
                if (version != 0)
                {
                    throw new InvalidDataException("WebP VP8L 版本不受支持。");
                }
                return new PetWebPInfo(width, height, hasAlpha, "VP8L");
            }
            else if (chunkName == "VP8X")
            {
                if (chunkSize < 10)
                {
                    throw new InvalidDataException("WebP VP8X 图集头不完整。");
                }
                var extendedHeader = new byte[10];
                await stream.ReadExactlyAsync(extendedHeader, cancellationToken);
                var width = 1 + ReadUInt24LittleEndian(extendedHeader.AsSpan(4, 3));
                var height = 1 + ReadUInt24LittleEndian(extendedHeader.AsSpan(7, 3));
                var hasAlpha = (extendedHeader[0] & 0x10) != 0;
                return new PetWebPInfo(width, height, hasAlpha, "VP8X");
            }
            else if (chunkName == "VP8 ")
            {
                if (chunkSize < 10)
                {
                    throw new InvalidDataException("WebP VP8 图集头不完整。");
                }
                var lossyHeader = new byte[10];
                await stream.ReadExactlyAsync(lossyHeader, cancellationToken);
                if (lossyHeader[3] != 0x9d || lossyHeader[4] != 0x01 || lossyHeader[5] != 0x2a)
                {
                    throw new InvalidDataException("WebP VP8 帧签名无效。");
                }
                var width = BinaryPrimitives.ReadUInt16LittleEndian(lossyHeader.AsSpan(6, 2)) & 0x3fff;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(lossyHeader.AsSpan(8, 2)) & 0x3fff;
                return new PetWebPInfo(width, height, sawAlphaChunk, "VP8");
            }

            stream.Position = dataEnd + (chunkSize & 1);
        }

        throw new InvalidDataException("WebP 图集缺少受支持的图像区块。");
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
