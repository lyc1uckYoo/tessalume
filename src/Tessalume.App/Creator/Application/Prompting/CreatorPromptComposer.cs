namespace Tessalume.App.Creator;

internal sealed record CreatorPromptDraft
{
    public string WorkName { get; init; } = string.Empty;

    public string CharacterName { get; init; } = string.Empty;

    public string VisualDirection { get; init; } = string.Empty;

    public string SpecialRequirements { get; init; } = string.Empty;

    public bool UsesReferenceImages { get; init; }

    public CreatorPromptDraft Normalize() => this with
    {
        WorkName = NormalizeText(WorkName, 80),
        CharacterName = NormalizeText(CharacterName, 80),
        VisualDirection = NormalizeText(VisualDirection, 220),
        SpecialRequirements = NormalizeText(SpecialRequirements, 500),
    };

    private static string NormalizeText(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}

internal static class CreatorPromptComposer
{
    public static bool CanCopy(CreatorPromptDraft draft)
    {
        draft = draft.Normalize();
        return !string.IsNullOrWhiteSpace(draft.WorkName) &&
            !string.IsNullOrWhiteSpace(draft.CharacterName);
    }

    public static string Compose(CreatorPromptDraft draft)
    {
        draft = draft.Normalize();
        var work = string.IsNullOrWhiteSpace(draft.WorkName) ? "作品名" : draft.WorkName;
        var character = string.IsNullOrWhiteSpace(draft.CharacterName) ? "角色名" : draft.CharacterName;
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(draft.VisualDirection))
        {
            details.Add($"视觉方向：{draft.VisualDirection}。");
        }
        if (draft.UsesReferenceImages)
        {
            details.Add("我会附上参考图片，请先核对图片中的角色身份、服装形态与关键特征。");
        }
        if (!string.IsNullOrWhiteSpace(draft.SpecialRequirements))
        {
            details.Add($"其他要求：{draft.SpecialRequirements}。");
        }

        var optionalDetails = details.Count == 0
            ? string.Empty
            : $"\n{string.Join("\n", details)}";
        return
            $"请使用 $author-tessalume-theme 为《{work}》的{character}制作一套 Tessalume 主题。" +
            optionalDetails +
            "\n请先完成角色研究，向我提交一次角色身份卡和完整的 11 张素材规划，等我确认后再开始生成。" +
            "亮色与暗色必须完整覆盖，并保留 Template 1.0 冻结几何、角色专属组件和动效。" +
            "完成后运行契约校验与实际截图检查，最终把可直接导入 Tessalume 的主题交付到 themes/<主题目录>。";
    }
}
