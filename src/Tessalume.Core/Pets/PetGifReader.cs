using System.Buffers.Binary;

namespace Tessalume.Core.Pets;

internal static class PetGifReader
{
    private const int DefaultDelayMilliseconds = 100;

    public static async Task<PetGifInfo> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > PetPackageContract.MaximumPreviewFileBytes)
        {
            throw new InvalidDataException("GIF 预览为空或超过 8 MiB 安全限制。");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)info.Length));
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return Parse(bytes);
    }

    internal static PetGifInfo Parse(ReadOnlySpan<byte> bytes)
    {
        var cursor = new GifCursor(bytes);
        var signature = cursor.ReadSpan(6, "GIF 签名不完整。");
        if (!signature.SequenceEqual("GIF87a"u8) && !signature.SequenceEqual("GIF89a"u8))
        {
            throw new InvalidDataException("预览不是有效的 GIF87a/GIF89a 文件。");
        }

        var width = cursor.ReadUInt16("GIF 逻辑画布宽度缺失。");
        var height = cursor.ReadUInt16("GIF 逻辑画布高度缺失。");
        if (width == 0 || height == 0 ||
            width > PetPackageContract.MaximumPreviewWidth ||
            height > PetPackageContract.MaximumPreviewHeight)
        {
            throw new InvalidDataException(
                $"GIF 逻辑画布必须介于 1×1 与 {PetPackageContract.MaximumPreviewWidth}×{PetPackageContract.MaximumPreviewHeight} 之间。");
        }

        var logicalPacked = cursor.ReadByte("GIF 逻辑画布描述符不完整。");
        cursor.Skip(2, "GIF 逻辑画布描述符不完整。");
        if ((logicalPacked & 0x80) != 0)
        {
            cursor.Skip(ColorTableBytes(logicalPacked), "GIF 全局颜色表越界。");
        }

        var frameDelays = new List<int>();
        var pendingDelay = DefaultDelayMilliseconds;
        var foundTrailer = false;
        while (!cursor.End)
        {
            switch (cursor.ReadByte("GIF 数据流意外结束。"))
            {
                case 0x21:
                    ReadExtension(ref cursor, ref pendingDelay);
                    break;
                case 0x2c:
                    ReadImage(ref cursor, width, height);
                    frameDelays.Add(pendingDelay);
                    pendingDelay = DefaultDelayMilliseconds;
                    if (frameDelays.Count > PetPackageContract.MaximumPreviewFrames)
                    {
                        throw new InvalidDataException(
                            $"GIF 帧数超过 {PetPackageContract.MaximumPreviewFrames} 帧安全限制。");
                    }
                    break;
                case 0x3b:
                    foundTrailer = true;
                    if (!cursor.End)
                    {
                        throw new InvalidDataException("GIF 结束标记后包含未声明数据。");
                    }
                    break;
                default:
                    throw new InvalidDataException("GIF 包含不受支持或损坏的数据块。");
            }

            if (foundTrailer)
            {
                break;
            }
        }

        if (!foundTrailer)
        {
            throw new InvalidDataException("GIF 缺少结束标记。");
        }
        if (frameDelays.Count < 2)
        {
            throw new InvalidDataException("动态预览至少需要 2 帧。");
        }

        return new PetGifInfo(width, height, frameDelays.Count, frameDelays);
    }

    private static void ReadExtension(ref GifCursor cursor, ref int pendingDelay)
    {
        var label = cursor.ReadByte("GIF 扩展块缺少类型。");
        if (label != 0xf9)
        {
            cursor.SkipSubBlocks();
            return;
        }

        if (cursor.ReadByte("GIF 图形控制扩展不完整。") != 4)
        {
            throw new InvalidDataException("GIF 图形控制扩展长度无效。");
        }
        cursor.Skip(1, "GIF 图形控制扩展不完整。");
        var delayUnits = cursor.ReadUInt16("GIF 图形控制扩展缺少帧延时。");
        cursor.Skip(1, "GIF 图形控制扩展不完整。");
        if (cursor.ReadByte("GIF 图形控制扩展缺少结束标记。") != 0)
        {
            throw new InvalidDataException("GIF 图形控制扩展结束标记无效。");
        }

        pendingDelay = delayUnits == 0
            ? DefaultDelayMilliseconds
            : checked(delayUnits * 10);
    }

    private static void ReadImage(ref GifCursor cursor, int canvasWidth, int canvasHeight)
    {
        var left = cursor.ReadUInt16("GIF 图像描述符不完整。");
        var top = cursor.ReadUInt16("GIF 图像描述符不完整。");
        var width = cursor.ReadUInt16("GIF 图像描述符不完整。");
        var height = cursor.ReadUInt16("GIF 图像描述符不完整。");
        var packed = cursor.ReadByte("GIF 图像描述符不完整。");
        if (width == 0 || height == 0 ||
            left + width > canvasWidth || top + height > canvasHeight)
        {
            throw new InvalidDataException("GIF 帧超出逻辑画布边界。");
        }
        if ((packed & 0x80) != 0)
        {
            cursor.Skip(ColorTableBytes(packed), "GIF 局部颜色表越界。");
        }

        var minimumCodeSize = cursor.ReadByte("GIF 图像数据缺少 LZW 码宽。");
        if (minimumCodeSize is < 2 or > 8)
        {
            throw new InvalidDataException("GIF LZW 最小码宽无效。");
        }
        cursor.SkipSubBlocks();
    }

    private static int ColorTableBytes(byte packed) =>
        checked(3 * (1 << ((packed & 0x07) + 1)));

    private ref struct GifCursor(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;

        public bool End => _position == _bytes.Length;

        public byte ReadByte(string message)
        {
            if (_position >= _bytes.Length)
            {
                throw new InvalidDataException(message);
            }
            return _bytes[_position++];
        }

        public ushort ReadUInt16(string message)
        {
            var span = ReadSpan(2, message);
            return BinaryPrimitives.ReadUInt16LittleEndian(span);
        }

        public ReadOnlySpan<byte> ReadSpan(int count, string message)
        {
            if (count < 0 || _position > _bytes.Length - count)
            {
                throw new InvalidDataException(message);
            }
            var result = _bytes.Slice(_position, count);
            _position += count;
            return result;
        }

        public void Skip(int count, string message) => ReadSpan(count, message);

        public void SkipSubBlocks()
        {
            while (true)
            {
                var length = ReadByte("GIF 子数据块长度缺失。");
                if (length == 0)
                {
                    return;
                }
                Skip(length, "GIF 子数据块越界。");
            }
        }
    }
}
