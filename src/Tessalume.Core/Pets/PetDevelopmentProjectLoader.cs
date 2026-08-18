using System.Text.Json;

namespace Tessalume.Core.Pets;

public sealed class PetDevelopmentProjectLoader
{
    private const long MaximumProjectManifestBytes = 256 * 1024;
    private const long MaximumPetManifestBytes = 64 * 1024;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PetDevelopmentLoadResult> LoadAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        var validation = new PetValidationResult();
        var root = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(root))
        {
            validation.AddError("project.directory.missing", "宠物开发项目目录不存在。", root);
            return new PetDevelopmentLoadResult(null, validation);
        }

        try
        {
            PetPathSafety.EnsureRegularDirectory(root, root);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            validation.AddError("project.directory.unsafe", exception.Message, root);
            return new PetDevelopmentLoadResult(null, validation);
        }

        var projectManifestPath = Path.Combine(
            root,
            PetDevelopmentProjectContract.ManifestFileName);
        var manifest = await ReadJsonAsync<PetDevelopmentProjectManifest>(
            root,
            projectManifestPath,
            MaximumProjectManifestBytes,
            "project",
            validation,
            cancellationToken);
        if (manifest is null)
        {
            return new PetDevelopmentLoadResult(null, validation);
        }

        manifest = Normalize(manifest, validation);
        ValidateManifest(manifest, validation);

        string previewRoot;
        try
        {
            previewRoot = PetPathSafety.ResolveContainedPath(
                root,
                manifest.PreviewOutputDirectory);
            if (Directory.Exists(previewRoot))
            {
                PetPathSafety.EnsureRegularDirectory(root, previewRoot);
            }
            else
            {
                validation.AddError(
                    "project.preview-output.missing",
                    "开发预览尚未生成；请让 Codex 生成候选动画后重试。",
                    manifest.PreviewOutputDirectory);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or NotSupportedException)
        {
            validation.AddError(
                "project.preview-output.unsafe",
                exception.Message,
                manifest.PreviewOutputDirectory);
            previewRoot = root;
        }

        PetManifest? petManifest = null;
        if (PetPathSafety.IsSafeRelativePath(manifest.PetManifestPath))
        {
            try
            {
                var path = PetPathSafety.ResolveContainedPath(root, manifest.PetManifestPath);
                petManifest = await ReadJsonAsync<PetManifest>(
                    root,
                    path,
                    MaximumPetManifestBytes,
                    "pet-manifest",
                    validation,
                    cancellationToken);
                if (petManifest is not null)
                {
                    ValidatePetManifest(manifest, petManifest, validation);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
            {
                validation.AddError(
                    "project.pet-manifest.unreadable",
                    exception.Message,
                    manifest.PetManifestPath);
            }
        }

        var resolvedPreviews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previewInfos = new Dictionary<string, PetGifInfo>(StringComparer.OrdinalIgnoreCase);
        var latestWrite = File.GetLastWriteTimeUtc(projectManifestPath);
        foreach (var preview in manifest.Previews)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidatePreviewMetadata(preview, validation))
            {
                continue;
            }

            try
            {
                var fullPath = PetPathSafety.ResolveContainedPath(previewRoot, preview.Path);
                PetPathSafety.EnsureRegularFile(root, fullPath);
                var info = await PetGifReader.ReadAsync(fullPath, cancellationToken);
                ValidatePreviewFile(preview, info, validation);
                resolvedPreviews[preview.Path] = fullPath;
                previewInfos[preview.Path] = info;
                var writeTime = File.GetLastWriteTimeUtc(fullPath);
                if (writeTime > latestWrite)
                {
                    latestWrite = writeTime;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
            {
                validation.AddError("project.preview.unavailable", exception.Message, preview.Path);
            }
        }

        ValidatePreviewSet(manifest.Previews, validation);
        return new PetDevelopmentLoadResult(
            new PetDevelopmentProject(
                root,
                projectManifestPath,
                previewRoot,
                manifest,
                petManifest,
                resolvedPreviews,
                previewInfos,
                new DateTimeOffset(DateTime.SpecifyKind(latestWrite, DateTimeKind.Utc))),
            validation);
    }

    private async Task<T?> ReadJsonAsync<T>(
        string root,
        string path,
        long maximumBytes,
        string kind,
        PetValidationResult validation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            validation.AddError($"{kind}.missing", $"开发项目缺少 {Path.GetFileName(path)}。", path);
            return null;
        }

        try
        {
            PetPathSafety.EnsureRegularFile(root, path);
            var length = new FileInfo(path).Length;
            if (length <= 0 || length > maximumBytes)
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
            var value = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
            if (value is null)
            {
                validation.AddError($"{kind}.empty", $"{Path.GetFileName(path)} 内容为空。", path);
            }
            return value;
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

    private static PetDevelopmentProjectManifest Normalize(
        PetDevelopmentProjectManifest manifest,
        PetValidationResult validation)
    {
        if (manifest.Protocol is null) validation.AddError("project.protocol.missing", "protocol 必须是对象。");
        if (manifest.Author is null) validation.AddError("project.author.missing", "author 必须是对象。");
        if (manifest.License is null) validation.AddError("project.license.missing", "license 必须是对象。");
        if (manifest.Rights is null) validation.AddError("project.rights.missing", "rights 必须是对象。");
        if (manifest.Previews is null) validation.AddError("project.previews.missing", "previews 必须是数组。");
        if (manifest.RecommendedThemeIds is null)
        {
            validation.AddError("project.recommended-themes.missing", "recommendedThemeIds 必须是数组。");
        }

        var previews = new List<PetPreviewMetadata>();
        foreach (var preview in manifest.Previews ?? [])
        {
            if (preview is null)
            {
                validation.AddError("project.preview.null", "previews 不能包含 null。");
                continue;
            }
            previews.Add(preview with
            {
                Path = preview.Path ?? string.Empty,
                Kind = preview.Kind ?? string.Empty,
                MediaType = preview.MediaType ?? string.Empty,
                ActionKey = preview.ActionKey ?? string.Empty,
                StateKey = preview.StateKey ?? string.Empty,
                Label = preview.Label ?? string.Empty,
            });
        }

        var states = (manifest.Protocol?.States ?? [])
            .Where(state => state is not null)
            .Select(state => state with { Key = state.Key ?? string.Empty })
            .ToArray();
        return manifest with
        {
            Id = manifest.Id ?? string.Empty,
            DisplayName = manifest.DisplayName ?? string.Empty,
            Description = manifest.Description ?? string.Empty,
            ProjectVersion = manifest.ProjectVersion ?? string.Empty,
            PetManifestPath = manifest.PetManifestPath ?? string.Empty,
            PreviewOutputDirectory = manifest.PreviewOutputDirectory ?? string.Empty,
            Protocol = (manifest.Protocol ?? new PetProtocolMetadata()) with { States = states },
            Author = manifest.Author ?? new PetAuthorMetadata(),
            License = manifest.License ?? new PetLicenseMetadata(),
            Rights = manifest.Rights ?? new PetRightsMetadata(),
            Previews = previews,
            RecommendedThemeIds = (manifest.RecommendedThemeIds ?? [])
                .Select(themeId => themeId ?? string.Empty)
                .ToArray(),
        };
    }

    private static void ValidateManifest(
        PetDevelopmentProjectManifest manifest,
        PetValidationResult validation)
    {
        if (manifest.SchemaVersion != PetDevelopmentProjectContract.SchemaVersion)
        {
            validation.AddError(
                "project.schema.unsupported",
                $"宠物开发项目 schema {manifest.SchemaVersion} 不受支持。");
        }
        if (!PetPathSafety.IsValidPetId(manifest.Id))
        {
            validation.AddError("project.id.invalid", "项目宠物 ID 格式无效。");
        }
        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            validation.AddError("project.display-name.missing", "项目宠物名称不能为空。");
        }
        if (!Version.TryParse(manifest.ProjectVersion, out _))
        {
            validation.AddError("project.version.invalid", "开发版本必须是数字版本，例如 1.1.0。");
        }
        if (!PetPathSafety.IsSafeRelativePath(manifest.PetManifestPath) ||
            !PetPathSafety.IsSafeRelativePath(manifest.PreviewOutputDirectory))
        {
            validation.AddError(
                "project.paths.invalid",
                "开发项目只能引用项目目录内的本地相对路径。");
        }
        if (string.IsNullOrWhiteSpace(manifest.Author.Name) ||
            string.IsNullOrWhiteSpace(manifest.License.Kind))
        {
            validation.AddError("project.credits.invalid", "项目必须声明作者和许可类型。");
        }

        ValidateProtocol(manifest.Protocol, validation);
    }

    private static void ValidateProtocol(
        PetProtocolMetadata protocol,
        PetValidationResult validation)
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
                "project.protocol.geometry.invalid",
                "开发预览必须使用 Codex Pets 协议 2 的固定图集布局。");
        }

        if (protocol.UsedFrameCount != PetPackageContract.UsedFrameCount ||
            protocol.States.Sum(state => state.Frames) != PetPackageContract.UsedFrameCount ||
            protocol.States.Count != PetPackageContract.RequiredStates.Count)
        {
            validation.AddError(
                "project.protocol.states.invalid",
                "开发项目必须完整声明 11 行、74 个有效格的协议布局。");
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
                    "project.protocol.state.invalid",
                    $"协议第 {index} 行必须是 {expected.Key}，使用 {expected.Frames} 格。",
                    actual.Key);
            }
        }
    }

    private static bool ValidatePreviewMetadata(
        PetPreviewMetadata preview,
        PetValidationResult validation)
    {
        var valid = true;
        if (!PetPathSafety.IsSafeRelativePath(preview.Path) ||
            preview.Path.Contains('\\') ||
            !string.Equals(Path.GetExtension(preview.Path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            validation.AddError("project.preview.path.invalid", "预览必须是候选目录内的 GIF 相对路径。", preview.Path);
            valid = false;
        }
        if (!PetDevelopmentProjectContract.RequiredPreviewActionKeys.Contains(
                preview.ActionKey,
                StringComparer.Ordinal))
        {
            validation.AddError("project.preview.action.invalid", "开发预览动作 key 不受支持。", preview.ActionKey);
            valid = false;
        }
        if (string.IsNullOrWhiteSpace(preview.Label) ||
            !string.Equals(preview.StateKey, preview.ActionKey, StringComparison.Ordinal) ||
            !string.Equals(preview.MediaType, PetPackageContract.GifMediaType, StringComparison.Ordinal))
        {
            validation.AddError("project.preview.metadata.invalid", "开发预览标签、状态或媒体类型无效。", preview.Path);
            valid = false;
        }
        if (preview.ExpectedFrameCount is < 2 or > PetPackageContract.MaximumPreviewFrames ||
            preview.Width is <= 0 or > PetPackageContract.MaximumPreviewWidth ||
            preview.Height is <= 0 or > PetPackageContract.MaximumPreviewHeight ||
            preview.RepresentativeFrame < 0 ||
            preview.RepresentativeFrame >= preview.ExpectedFrameCount)
        {
            validation.AddError("project.preview.bounds.invalid", "开发预览帧数、尺寸或代表帧越界。", preview.Path);
            valid = false;
        }
        return valid;
    }

    private static void ValidatePreviewFile(
        PetPreviewMetadata preview,
        PetGifInfo info,
        PetValidationResult validation)
    {
        if (info.Width != preview.Width || info.Height != preview.Height)
        {
            validation.AddError(
                "project.preview.dimensions.mismatch",
                $"GIF 实际尺寸必须是 {preview.Width}×{preview.Height}。",
                preview.Path);
        }
        if (info.FrameCount != preview.ExpectedFrameCount)
        {
            validation.AddError(
                "project.preview.frames.mismatch",
                $"GIF 实际帧数必须是 {preview.ExpectedFrameCount}。",
                preview.Path);
        }
        if (info.FrameDelaysMilliseconds.Any(delay =>
                delay < PetPackageContract.MinimumPreviewDelayMilliseconds ||
                delay > PetPackageContract.MaximumPreviewDelayMilliseconds))
        {
            validation.AddError("project.preview.delay.invalid", "GIF 帧延时超出安全范围。", preview.Path);
        }
    }

    private static void ValidatePreviewSet(
        IReadOnlyList<PetPreviewMetadata> previews,
        PetValidationResult validation)
    {
        var actual = previews.Select(preview => preview.ActionKey).ToArray();
        if (actual.Length != actual.Distinct(StringComparer.Ordinal).Count())
        {
            validation.AddError("project.preview.duplicate", "开发项目不能重复声明动作预览。");
        }
        var missing = PetDevelopmentProjectContract.RequiredPreviewActionKeys
            .Except(actual, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            validation.AddError(
                "project.preview.missing",
                $"开发项目缺少完整动作预览：{string.Join("、", missing)}。");
        }
    }

    private static void ValidatePetManifest(
        PetDevelopmentProjectManifest project,
        PetManifest manifest,
        PetValidationResult validation)
    {
        if (!string.Equals(project.Id, manifest.Id, StringComparison.Ordinal) ||
            !string.Equals(project.DisplayName, manifest.DisplayName, StringComparison.Ordinal))
        {
            validation.AddError("project.pet-manifest.mismatch", "pet.json 与开发项目的宠物身份不一致。");
        }
        if (manifest.SpriteVersionNumber != PetPackageContract.SpriteVersionNumber ||
            !PetPathSafety.IsSafeRelativePath(manifest.SpritesheetPath))
        {
            validation.AddError("project.pet-manifest.protocol.invalid", "pet.json 的图集协议或路径无效。");
        }
    }
}
