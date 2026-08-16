using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tessalume.Core.Pets;

public sealed partial class PetPackageLoader
{
    private const long MaximumCatalogBytes = 256 * 1024;
    private const long MaximumManifestBytes = 64 * 1024;
    private const long MaximumSingleFileBytes = 32L * 1024 * 1024;
    private const long MaximumPackageBytes = 64L * 1024 * 1024;
    private const int MaximumFileCount = 24;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal)
    {
        PetPackageContract.ManifestRole,
        PetPackageContract.SpritesheetRole,
        PetPackageContract.PreviewRole,
    };
    private static readonly HashSet<string> AllowedPreviewKinds = new(StringComparer.Ordinal)
    {
        PetPackageContract.ActionPreviewKind,
        PetPackageContract.DirectionPreviewKind,
        PetPackageContract.ShowcasePreviewKind,
    };

    public async Task<PetLoadResult> LoadAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var validation = new PetValidationResult();
        var root = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(root))
        {
            validation.AddError("package.directory.missing", "宠物包目录不存在。", root);
            return new PetLoadResult(null, validation);
        }

        try
        {
            PetPathSafety.EnsureRegularDirectory(root, root);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            validation.AddError("package.directory.unsafe", exception.Message, root);
            return new PetLoadResult(null, validation);
        }

        var catalogPath = Path.Combine(root, PetPackageContract.CatalogFileName);
        var catalog = await ReadJsonAsync<PetCatalog>(
            catalogPath,
            MaximumCatalogBytes,
            "catalog",
            validation,
            cancellationToken);
        if (catalog is null)
        {
            return new PetLoadResult(null, validation);
        }
        catalog = NormalizeCatalog(catalog, validation);
        ValidateCatalog(catalog, validation);

        var filesByPath = new Dictionary<string, PetCatalogFile>(StringComparer.OrdinalIgnoreCase);
        var resolvedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        if (catalog.Files.Count > MaximumFileCount)
        {
            validation.AddError(
                "catalog.files.too-many",
                $"宠物包最多声明 {MaximumFileCount} 个文件。");
        }

        foreach (var file in catalog.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateCatalogFileMetadata(file, validation) ||
                !filesByPath.TryAdd(file.Path, file))
            {
                if (filesByPath.ContainsKey(file.Path))
                {
                    validation.AddError("catalog.file.duplicate", "宠物包重复声明了同一路径。", file.Path);
                }
                continue;
            }

            string fullPath;
            try
            {
                fullPath = PetPathSafety.ResolveContainedPath(root, file.Path);
                PetPathSafety.EnsureRegularFile(root, fullPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
            {
                validation.AddError("catalog.file.missing-or-unsafe", exception.Message, file.Path);
                continue;
            }

            var actualSize = new FileInfo(fullPath).Length;
            if (actualSize <= 0 || actualSize > MaximumSingleFileBytes)
            {
                validation.AddError(
                    "catalog.file.size.invalid",
                    "宠物文件为空或超过 32 MiB 安全限制。",
                    file.Path);
                continue;
            }
            if (actualSize != file.Size)
            {
                validation.AddError(
                    "catalog.file.size.mismatch",
                    "宠物文件大小与 catalog 声明不一致。",
                    file.Path);
                continue;
            }
            totalBytes += actualSize;
            var hash = await ComputeSha256Async(fullPath, cancellationToken);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                validation.AddError(
                    "catalog.file.hash.mismatch",
                    "宠物文件 SHA-256 与 catalog 声明不一致。",
                    file.Path);
                continue;
            }
            resolvedFiles[file.Path] = fullPath;
        }

        if (totalBytes > MaximumPackageBytes)
        {
            validation.AddError("catalog.files.total-too-large", "宠物包总大小不能超过 64 MiB。");
        }

        ValidateDeclaredFileSet(root, filesByPath.Keys, validation);
        ValidateFileRoles(catalog, filesByPath, validation);
        ValidatePreviews(catalog, filesByPath, validation);

        var previewInfos = new Dictionary<string, PetGifInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var preview in catalog.Previews)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resolvedFiles.TryGetValue(preview.Path, out var previewPath))
            {
                continue;
            }
            try
            {
                var info = await PetGifReader.ReadAsync(previewPath, cancellationToken);
                ValidateGifPreview(preview, info, validation);
                previewInfos[preview.Path] = info;
            }
            catch (InvalidDataException exception)
            {
                validation.AddError("preview.gif.invalid", exception.Message, preview.Path);
            }
        }

        var manifestPath = Path.Combine(root, PetPackageContract.ManifestFileName);
        var manifest = await ReadJsonAsync<PetManifest>(
            manifestPath,
            MaximumManifestBytes,
            "manifest",
            validation,
            cancellationToken);
        if (manifest is null)
        {
            return new PetLoadResult(null, validation);
        }
        manifest = NormalizeManifest(manifest);
        ValidateManifest(manifest, catalog, filesByPath, validation);

        PetWebPInfo? spritesheetInfo = null;
        if (PetPathSafety.IsSafeRelativePath(manifest.SpritesheetPath) &&
            resolvedFiles.TryGetValue(manifest.SpritesheetPath, out var spritesheetPath))
        {
            try
            {
                spritesheetInfo = await PetWebPReader.ReadAsync(spritesheetPath, cancellationToken);
                ValidateSpritesheet(spritesheetInfo, catalog.Protocol, validation, manifest.SpritesheetPath);
            }
            catch (InvalidDataException exception)
            {
                validation.AddError(
                    "spritesheet.webp.invalid",
                    exception.Message,
                    manifest.SpritesheetPath);
            }
        }

        if (!validation.IsValid || spritesheetInfo is null)
        {
            return new PetLoadResult(null, validation);
        }

        return new PetLoadResult(
            new PetPackage(
                root,
                catalogPath,
                manifestPath,
                catalog,
                manifest,
                resolvedFiles,
                spritesheetInfo,
                previewInfos),
            validation);
    }

    internal static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private async Task<T?> ReadJsonAsync<T>(
        string path,
        long maximumBytes,
        string kind,
        PetValidationResult validation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            validation.AddError($"{kind}.missing", $"宠物包缺少 {Path.GetFileName(path)}。", path);
            return null;
        }
        try
        {
            var root = Path.GetDirectoryName(path)!;
            PetPathSafety.EnsureRegularFile(root, path);
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maximumBytes)
            {
                validation.AddError($"{kind}.size.invalid", $"{Path.GetFileName(path)} 为空或过大。", path);
                return null;
            }
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
            if (result is null)
            {
                validation.AddError($"{kind}.empty", $"{Path.GetFileName(path)} 内容为空。", path);
            }
            return result;
        }
        catch (JsonException exception)
        {
            validation.AddError($"{kind}.invalid-json", exception.Message, path);
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            validation.AddError($"{kind}.unreadable", exception.Message, path);
            return null;
        }
    }

    private static PetCatalog NormalizeCatalog(PetCatalog catalog, PetValidationResult validation)
    {
        if (catalog.Protocol is null) validation.AddError("catalog.protocol.missing", "catalog.protocol 必须是对象。");
        if (catalog.Author is null) validation.AddError("catalog.author.missing", "catalog.author 必须是对象。");
        if (catalog.License is null) validation.AddError("catalog.license.missing", "catalog.license 必须是对象。");
        if (catalog.Rights is null) validation.AddError("catalog.rights.missing", "catalog.rights 必须是对象。");
        if (catalog.Files is null) validation.AddError("catalog.files.missing", "catalog.files 必须是数组。");
        if (catalog.Previews is null) validation.AddError("catalog.previews.missing", "catalog.previews 必须是数组。");
        if (catalog.RecommendedThemeIds is null)
        {
            validation.AddError("catalog.recommended-themes.missing", "recommendedThemeIds 必须是数组。");
        }

        var files = new List<PetCatalogFile>();
        foreach (var file in catalog.Files ?? [])
        {
            if (file is null)
            {
                validation.AddError("catalog.file.null", "catalog.files 不能包含 null。");
                continue;
            }
            files.Add(file with
            {
                Path = file.Path ?? string.Empty,
                Sha256 = file.Sha256 ?? string.Empty,
                Role = file.Role ?? string.Empty,
            });
        }
        var previews = new List<PetPreviewMetadata>();
        foreach (var preview in catalog.Previews ?? [])
        {
            if (preview is null)
            {
                validation.AddError("catalog.preview.null", "catalog.previews 不能包含 null。");
                continue;
            }
            previews.Add(preview with
            {
                Path = preview.Path ?? string.Empty,
                Kind = preview.Kind ?? string.Empty,
                MediaType = preview.MediaType ?? string.Empty,
                ActionKey = preview.ActionKey ?? string.Empty,
                Label = preview.Label ?? string.Empty,
                StateKey = preview.StateKey ?? string.Empty,
            });
        }
        var recommendedThemeIds = (catalog.RecommendedThemeIds ?? [])
            .Select(themeId => themeId ?? string.Empty)
            .ToArray();
        if ((catalog.RecommendedThemeIds ?? []).Any(themeId => themeId is null))
        {
            validation.AddError(
                "catalog.recommended-theme.null",
                "recommendedThemeIds 不能包含 null。");
        }

        return catalog with
        {
            Id = catalog.Id ?? string.Empty,
            DisplayName = catalog.DisplayName ?? string.Empty,
            Description = catalog.Description ?? string.Empty,
            ProductVersion = catalog.ProductVersion ?? string.Empty,
            Protocol = NormalizeProtocol(catalog.Protocol, validation),
            Author = catalog.Author ?? new PetAuthorMetadata(),
            License = catalog.License ?? new PetLicenseMetadata(),
            Rights = catalog.Rights ?? new PetRightsMetadata(),
            Files = files,
            Previews = previews,
            RecommendedThemeIds = recommendedThemeIds,
        };
    }

    private static PetProtocolMetadata NormalizeProtocol(
        PetProtocolMetadata? protocol,
        PetValidationResult validation)
    {
        var states = new List<PetProtocolState>();
        foreach (var state in protocol?.States ?? [])
        {
            if (state is null)
            {
                validation.AddError("catalog.protocol.state.null", "protocol.states 不能包含 null。");
            }
            else
            {
                states.Add(state with { Key = state.Key ?? string.Empty });
            }
        }
        return (protocol ?? new PetProtocolMetadata()) with { States = states };
    }

    private static PetManifest NormalizeManifest(PetManifest manifest) =>
        manifest with
        {
            Id = manifest.Id ?? string.Empty,
            DisplayName = manifest.DisplayName ?? string.Empty,
            Description = manifest.Description ?? string.Empty,
            SpritesheetPath = manifest.SpritesheetPath ?? string.Empty,
        };

    private static void ValidateCatalog(PetCatalog catalog, PetValidationResult validation)
    {
        if (catalog.SchemaVersion != PetPackageContract.CatalogSchemaVersion)
        {
            validation.AddError(
                "catalog.schema.unsupported",
                $"宠物 catalog schema {catalog.SchemaVersion} 不受支持。");
        }
        if (!PetIdRegex().IsMatch(catalog.Id))
        {
            validation.AddError("catalog.id.invalid", "宠物 ID 必须是 3-64 位小写字母、数字、点或连字符。");
        }
        if (string.IsNullOrWhiteSpace(catalog.DisplayName))
        {
            validation.AddError("catalog.display-name.missing", "宠物显示名称不能为空。");
        }
        if (!Version.TryParse(catalog.ProductVersion, out _))
        {
            validation.AddError("catalog.product-version.invalid", "产品版本必须是数字版本，例如 1.0.0。");
        }
        if (string.IsNullOrWhiteSpace(catalog.Author.Name))
        {
            validation.AddError("catalog.author.name.missing", "宠物作者名称不能为空。");
        }
        if (string.IsNullOrWhiteSpace(catalog.License.Kind))
        {
            validation.AddError("catalog.license.kind.missing", "宠物许可类型不能为空。");
        }
        if (string.IsNullOrWhiteSpace(catalog.Rights.Kind) || string.IsNullOrWhiteSpace(catalog.Rights.Notice))
        {
            validation.AddError("catalog.rights.missing", "宠物权利类型与声明不能为空。");
        }
        foreach (var themeId in catalog.RecommendedThemeIds)
        {
            if (!ThemeIdRegex().IsMatch(themeId ?? string.Empty))
            {
                validation.AddError("catalog.recommended-theme.invalid", "配套主题 ID 格式无效。", themeId);
            }
        }
        if (catalog.RecommendedThemeIds.Count !=
            catalog.RecommendedThemeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            validation.AddError("catalog.recommended-theme.duplicate", "配套主题 ID 不能重复。");
        }
        ValidateProtocol(catalog.Protocol, validation);
    }

    private static void ValidateProtocol(PetProtocolMetadata protocol, PetValidationResult validation)
    {
        if (protocol.SpriteVersionNumber != PetPackageContract.SpriteVersionNumber ||
            protocol.AtlasWidth != PetPackageContract.AtlasWidth ||
            protocol.AtlasHeight != PetPackageContract.AtlasHeight ||
            protocol.Columns != PetPackageContract.Columns ||
            protocol.Rows != PetPackageContract.Rows ||
            protocol.CellWidth != PetPackageContract.CellWidth ||
            protocol.CellHeight != PetPackageContract.CellHeight)
        {
            validation.AddError(
                "catalog.protocol.geometry.invalid",
                "宠物图集必须使用协议 2 的 1536×2288、8×11、192×208 固定布局。");
        }

        var declaredTotal = protocol.States.Sum(state => state.Frames);
        if (protocol.UsedFrameCount != PetPackageContract.UsedFrameCount ||
            declaredTotal != protocol.UsedFrameCount)
        {
            validation.AddError(
                "catalog.protocol.frame-count.invalid",
                $"宠物布局必须声明真实的 {PetPackageContract.UsedFrameCount} 个有效格，且逐行总数必须一致。");
        }
        if (protocol.States.Count != PetPackageContract.RequiredStates.Count)
        {
            validation.AddError("catalog.protocol.states.invalid", "宠物协议必须完整声明 11 行状态布局。");
            return;
        }
        for (var index = 0; index < PetPackageContract.RequiredStates.Count; index++)
        {
            var expected = PetPackageContract.RequiredStates[index];
            var actual = protocol.States[index];
            if (!string.Equals(actual.Key, expected.Key, StringComparison.Ordinal) ||
                actual.Row != expected.Row || actual.Frames != expected.Frames)
            {
                validation.AddError(
                    "catalog.protocol.state.invalid",
                    $"协议第 {index} 行必须是 {expected.Key}，使用 {expected.Frames} 格。",
                    actual.Key);
            }
        }
    }

    private static bool ValidateCatalogFileMetadata(PetCatalogFile file, PetValidationResult validation)
    {
        var valid = true;
        if (!PetPathSafety.IsSafeRelativePath(file.Path) ||
            file.Path.Contains('\\'))
        {
            validation.AddError(
                "catalog.file.path.invalid",
                "宠物文件路径必须是包内的正斜杠相对路径，不能是远程资源或越界路径。",
                file.Path);
            valid = false;
        }
        if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
        {
            validation.AddError("catalog.file.sha256.invalid", "宠物文件 SHA-256 格式无效。", file.Path);
            valid = false;
        }
        if (file.Size <= 0 || file.Size > MaximumSingleFileBytes)
        {
            validation.AddError("catalog.file.declared-size.invalid", "宠物文件声明大小无效。", file.Path);
            valid = false;
        }
        if (!AllowedRoles.Contains(file.Role))
        {
            validation.AddError("catalog.file.role.invalid", "宠物文件角色不受支持。", file.Path);
            valid = false;
        }
        return valid;
    }

    private static void ValidateDeclaredFileSet(
        string root,
        IEnumerable<string> declaredPaths,
        PetValidationResult validation)
    {
        var declared = declaredPaths
            .Append(PetPackageContract.CatalogFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                PetPathSafety.EnsureNoReparsePoints(root, directory);
            }
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                PetPathSafety.EnsureRegularFile(root, file);
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!declared.Contains(relative))
                {
                    validation.AddError(
                        "package.file.undeclared",
                        "宠物发布包包含 catalog 未声明的文件。",
                        relative);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            validation.AddError("package.enumeration.failed", exception.Message, root);
        }
    }

    private static void ValidateFileRoles(
        PetCatalog catalog,
        Dictionary<string, PetCatalogFile> filesByPath,
        PetValidationResult validation)
    {
        var manifests = catalog.Files.Where(file => file.Role == PetPackageContract.ManifestRole).ToArray();
        var spritesheets = catalog.Files.Where(file => file.Role == PetPackageContract.SpritesheetRole).ToArray();
        if (manifests.Length != 1 ||
            !string.Equals(manifests.FirstOrDefault()?.Path, PetPackageContract.ManifestFileName, StringComparison.Ordinal))
        {
            validation.AddError("catalog.manifest-file.invalid", "catalog 必须且只能把 pet.json 声明为 Codex manifest。");
        }
        if (spritesheets.Length != 1 || !filesByPath.ContainsKey(spritesheets[0].Path))
        {
            validation.AddError("catalog.spritesheet-file.invalid", "catalog 必须且只能声明一个 Codex spritesheet。");
        }
    }

    private static void ValidatePreviews(
        PetCatalog catalog,
        Dictionary<string, PetCatalogFile> filesByPath,
        PetValidationResult validation)
    {
        var previewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preview in catalog.Previews)
        {
            if (!PetPathSafety.IsSafeRelativePath(preview.Path) ||
                preview.Path.Contains('\\') ||
                !previewPaths.Add(preview.Path))
            {
                validation.AddError("catalog.preview.path.invalid", "宠物预览路径无效或重复。", preview.Path);
                continue;
            }
            if (!AllowedPreviewKinds.Contains(preview.Kind))
            {
                validation.AddError("catalog.preview.kind.invalid", "宠物动态预览类型无效。", preview.Path);
            }
            if (!string.Equals(preview.MediaType, PetPackageContract.GifMediaType, StringComparison.Ordinal))
            {
                validation.AddError("catalog.preview.media-type.invalid", "宠物动态预览必须声明 image/gif。", preview.Path);
            }
            if (!PreviewActionKeyRegex().IsMatch(preview.ActionKey) ||
                !actionKeys.Add(preview.ActionKey))
            {
                validation.AddError("catalog.preview.action-key.invalid", "宠物动作 key 无效或重复。", preview.Path);
            }
            if (!string.Equals(preview.StateKey, preview.ActionKey, StringComparison.Ordinal))
            {
                validation.AddError("catalog.preview.state-key.mismatch", "兼容状态 key 必须与动作 key 一致。", preview.Path);
            }
            if (string.IsNullOrWhiteSpace(preview.Label))
            {
                validation.AddError("catalog.preview.label.missing", "宠物动作标签不能为空。", preview.Path);
            }
            if (preview.ExpectedFrameCount is < 2 or > PetPackageContract.MaximumPreviewFrames)
            {
                validation.AddError("catalog.preview.frame-count.invalid", "宠物动态预览声明帧数无效。", preview.Path);
            }
            if (preview.Width is <= 0 or > PetPackageContract.MaximumPreviewWidth ||
                preview.Height is <= 0 or > PetPackageContract.MaximumPreviewHeight)
            {
                validation.AddError("catalog.preview.dimensions.invalid", "宠物动态预览声明尺寸越界。", preview.Path);
            }
            if (preview.RepresentativeFrame < 0 ||
                preview.RepresentativeFrame >= preview.ExpectedFrameCount)
            {
                validation.AddError("catalog.preview.representative-frame.invalid", "宠物代表帧索引越界。", preview.Path);
            }
            if (!preview.Loop)
            {
                validation.AddError("catalog.preview.loop.invalid", "内置宠物动态预览必须循环播放。", preview.Path);
            }
            if (!filesByPath.TryGetValue(preview.Path, out var file) ||
                file.Role != PetPackageContract.PreviewRole)
            {
                validation.AddError("catalog.preview.file.invalid", "每个预览必须对应 role=preview 的已声明文件。", preview.Path);
            }
            else if (file.Size > PetPackageContract.MaximumPreviewFileBytes)
            {
                validation.AddError("catalog.preview.file.too-large", "GIF 预览超过 8 MiB 安全限制。", preview.Path);
            }
            if (!string.Equals(Path.GetExtension(preview.Path), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                validation.AddError("catalog.preview.extension.invalid", "内置宠物动态预览必须是 GIF。", preview.Path);
            }
        }
        foreach (var file in catalog.Files.Where(file => file.Role == PetPackageContract.PreviewRole))
        {
            if (!previewPaths.Contains(file.Path))
            {
                validation.AddError("catalog.preview.metadata.missing", "预览文件缺少 previews 元数据。", file.Path);
            }
        }
        if (catalog.Previews.Count == 0)
        {
            validation.AddError("catalog.previews.empty", "宠物包至少需要一个产品预览。");
        }
    }

    private static void ValidateGifPreview(
        PetPreviewMetadata preview,
        PetGifInfo info,
        PetValidationResult validation)
    {
        if (info.Width != preview.Width || info.Height != preview.Height)
        {
            validation.AddError(
                "preview.gif.dimensions.mismatch",
                $"GIF 实际尺寸必须是 catalog 声明的 {preview.Width}×{preview.Height}。",
                preview.Path);
        }
        if (info.FrameCount != preview.ExpectedFrameCount)
        {
            validation.AddError(
                "preview.gif.frame-count.mismatch",
                $"GIF 实际帧数必须是 catalog 声明的 {preview.ExpectedFrameCount}。",
                preview.Path);
        }
        if (info.FrameDelaysMilliseconds.Any(delay =>
                delay < PetPackageContract.MinimumPreviewDelayMilliseconds ||
                delay > PetPackageContract.MaximumPreviewDelayMilliseconds))
        {
            validation.AddError(
                "preview.gif.delay.invalid",
                $"GIF 帧延时必须介于 {PetPackageContract.MinimumPreviewDelayMilliseconds}–{PetPackageContract.MaximumPreviewDelayMilliseconds} ms。",
                preview.Path);
        }
    }

    private static void ValidateManifest(
        PetManifest manifest,
        PetCatalog catalog,
        Dictionary<string, PetCatalogFile> filesByPath,
        PetValidationResult validation)
    {
        if (!PetIdRegex().IsMatch(manifest.Id))
        {
            validation.AddError("manifest.id.invalid", "pet.json 的宠物 ID 格式无效。");
        }
        if (!string.Equals(manifest.Id, catalog.Id, StringComparison.Ordinal))
        {
            validation.AddError("manifest.id.mismatch", "pet.json 与 catalog 的宠物 ID 不一致。");
        }
        if (!string.Equals(manifest.DisplayName, catalog.DisplayName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            validation.AddError("manifest.display-name.mismatch", "pet.json 与 catalog 的显示名称不一致。");
        }
        if (manifest.SpriteVersionNumber != PetPackageContract.SpriteVersionNumber ||
            manifest.SpriteVersionNumber != catalog.Protocol.SpriteVersionNumber)
        {
            validation.AddError("manifest.sprite-version.invalid", "spriteVersionNumber 必须是 Codex 协议号 2。");
        }
        if (!PetPathSafety.IsSafeRelativePath(manifest.SpritesheetPath) ||
            manifest.SpritesheetPath.Contains('\\'))
        {
            validation.AddError(
                "manifest.spritesheet-path.invalid",
                "spritesheetPath 必须是宠物目录内的本地相对路径。",
                manifest.SpritesheetPath);
        }
        else if (!filesByPath.TryGetValue(manifest.SpritesheetPath, out var file) ||
                 file.Role != PetPackageContract.SpritesheetRole)
        {
            validation.AddError(
                "manifest.spritesheet-file.mismatch",
                "spritesheetPath 必须指向 catalog 声明的 Codex spritesheet。",
                manifest.SpritesheetPath);
        }
    }

    private static void ValidateSpritesheet(
        PetWebPInfo info,
        PetProtocolMetadata protocol,
        PetValidationResult validation,
        string path)
    {
        if (info.Width != protocol.AtlasWidth || info.Height != protocol.AtlasHeight)
        {
            validation.AddError(
                "spritesheet.dimensions.mismatch",
                $"WebP 图集必须是 {protocol.AtlasWidth}×{protocol.AtlasHeight}。",
                path);
        }
        if (!info.HasAlpha)
        {
            validation.AddError("spritesheet.alpha.missing", "WebP 图集必须声明透明通道。", path);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PetIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PreviewActionKeyRegex();
}
