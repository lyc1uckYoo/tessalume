using System.Buffers.Binary;
using System.Text;

namespace Tessalume.Core.Pets;

internal sealed record PetPngInfo(int Width, int Height, bool HasAlpha, bool IsAnimated);

internal static class PetPngReader
{
    private static ReadOnlySpan<byte> Signature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async Task<PetPngInfo> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 33)
        {
            throw new InvalidDataException("PNG 图集头不完整。");
        }

        var signature = new byte[8];
        await stream.ReadExactlyAsync(signature, cancellationToken);
        if (!signature.AsSpan().SequenceEqual(Signature))
        {
            throw new InvalidDataException("图集不是有效的 PNG 文件。");
        }

        var header = new byte[8];
        var ihdr = new byte[13];
        var sawHeader = false;
        var sawEnd = false;
        var hasAlpha = false;
        var isAnimated = false;
        var width = 0;
        var height = 0;

        while (stream.Position < stream.Length)
        {
            await stream.ReadExactlyAsync(header, cancellationToken);
            var dataLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var chunkType = Encoding.ASCII.GetString(header, 4, 4);
            if (dataLength > int.MaxValue || stream.Length - stream.Position < dataLength + 4L)
            {
                throw new InvalidDataException("PNG 图集包含越界或未写完整的区块。");
            }
            if (!sawHeader && !chunkType.Equals("IHDR", StringComparison.Ordinal))
            {
                throw new InvalidDataException("PNG 图集的首个区块必须是 IHDR。");
            }

            if (chunkType.Equals("IHDR", StringComparison.Ordinal))
            {
                if (sawHeader || dataLength != ihdr.Length)
                {
                    throw new InvalidDataException("PNG 图集的 IHDR 区块无效。");
                }
                await stream.ReadExactlyAsync(ihdr, cancellationToken);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(0, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4, 4)));
                var colorType = ihdr[9];
                if (width <= 0 || height <= 0 || colorType is not (0 or 2 or 3 or 4 or 6))
                {
                    throw new InvalidDataException("PNG 图集声明了无效的尺寸或颜色类型。");
                }
                hasAlpha = colorType is 4 or 6;
                sawHeader = true;
            }
            else
            {
                if (chunkType.Equals("tRNS", StringComparison.Ordinal))
                {
                    hasAlpha = true;
                }
                else if (chunkType.Equals("acTL", StringComparison.Ordinal))
                {
                    isAnimated = true;
                }
                stream.Seek(dataLength, SeekOrigin.Current);
            }

            stream.Seek(4, SeekOrigin.Current); // CRC
            if (chunkType.Equals("IEND", StringComparison.Ordinal))
            {
                if (dataLength != 0)
                {
                    throw new InvalidDataException("PNG 图集的 IEND 区块无效。");
                }
                sawEnd = true;
                break;
            }
        }

        if (!sawHeader || !sawEnd)
        {
            throw new InvalidDataException("PNG 图集尚未完整写入。");
        }
        return new PetPngInfo(width, height, hasAlpha, isAnimated);
    }
}
