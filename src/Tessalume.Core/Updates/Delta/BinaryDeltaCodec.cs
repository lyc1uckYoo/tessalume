using System.Security.Cryptography;
using Octodiff.Core;
using Octodiff.Diagnostics;

namespace Tessalume.Core.Updates.Delta;

public static class BinaryDeltaCodec
{
    private const short ChunkSize = 16 * 1024;
    private const int BufferSize = 128 * 1024;
    private static readonly NullProgressReporter ProgressReporter = new();

    public static Task CreateAsync(
        string basisPath,
        string targetPath,
        string deltaPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Create(
                Path.GetFullPath(basisPath),
                Path.GetFullPath(targetPath),
                Path.GetFullPath(deltaPath),
                cancellationToken),
            cancellationToken);

    public static Task ApplyAsync(
        string basisPath,
        string deltaPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Apply(
                Path.GetFullPath(basisPath),
                Path.GetFullPath(deltaPath),
                Path.GetFullPath(outputPath),
                cancellationToken),
            cancellationToken);

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void Create(
        string basisPath,
        string targetPath,
        string deltaPath,
        CancellationToken cancellationToken)
    {
        RequireDistinctRegularFiles(basisPath, targetPath);
        if (string.Equals(deltaPath, basisPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(deltaPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("差分包不能覆盖源 EXE 或目标 EXE。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(deltaPath)!);
        var partialPath = deltaPath + ".partial";
        File.Delete(partialPath);
        try
        {
            using var signature = new MemoryStream();
            using (var basis = OpenRead(basisPath))
            {
                new SignatureBuilder
                {
                    ChunkSize = ChunkSize,
                    ProgressReporter = ProgressReporter,
                }.Build(basis, new SignatureWriter(signature));
            }

            cancellationToken.ThrowIfCancellationRequested();
            signature.Position = 0;
            using (var target = OpenRead(targetPath))
            using (var delta = new FileStream(
                       partialPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       BufferSize,
                       FileOptions.SequentialScan))
            {
                new DeltaBuilder
                {
                    ProgressReporter = ProgressReporter,
                }.BuildDelta(
                    target,
                    new SignatureReader(signature, ProgressReporter),
                    new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(delta)));
                delta.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, deltaPath, overwrite: true);
        }
        catch
        {
            TryDeletePartialFile(partialPath);
            throw;
        }
    }

    private static void Apply(
        string basisPath,
        string deltaPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        RequireDistinctRegularFiles(basisPath, deltaPath);
        if (string.Equals(outputPath, basisPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outputPath, deltaPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("差分输出不能覆盖基线 EXE 或差分包。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Delete(outputPath);
        try
        {
            using var basis = OpenRead(basisPath);
            using var delta = OpenRead(deltaPath);
            using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.SequentialScan);
            new DeltaApplier { SkipHashCheck = false }.Apply(
                basis,
                new BinaryDeltaReader(delta, ProgressReporter),
                output);
            output.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            TryDeletePartialFile(outputPath);
            throw;
        }
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);

    private static void RequireDistinctRegularFiles(string firstPath, string secondPath)
    {
        if (string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("差分输入文件不能相同。");
        }
        if (!File.Exists(firstPath) || !File.Exists(secondPath))
        {
            throw new FileNotFoundException("差分输入文件不存在。");
        }
        if ((File.GetAttributes(firstPath) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(secondPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("差分输入不能使用链接或重解析点。");
        }
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
