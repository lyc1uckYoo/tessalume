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

        var lines = new List<string>
        {
            $"请使用 $author-tessalume-theme 为《{work}》的{character}制作一套 Tessalume 主题。",
        };
        lines.AddRange(details);
        lines.AddRange([
            string.Empty,
            "严格按以下顺序执行：",
            "1. 先完成角色研究，提交角色身份卡和完整的 11 张素材规划；等我明确确认后再生成图片。",
            "2. 只从 Skill 的 canonical Template 1.0 脚手架创建主题，不复制已发布主题，也不修改冻结几何。",
            "3. 生成 11 张完成度足够的原始素材；首页横幅、左栏图片和聊天背景必须分别提供亮色与暗色原图，不依赖 CSS 修复错误构图。",
            "4. 在 artwork-defaults.json 中完成 hero/sidebar/chat × light/dark 六槽推荐值。每槽必须引用 manifest 中同名原始素材；构图、滤镜、透明度、叠色、渐变遮罩、暗角、混合模式和可读性保护只能存在于这里。",
            "5. skin.css 禁止引用六张可调原图，也禁止在对应图层写 background-size/position、filter、opacity、transform、混合模式、遮罩或图片动画。图片呼吸/漂移只能写为相对最终构图的 motion delta；角色 DOM、SVG、卡片纹样和独立装饰动效继续由 CSS/主题脚本负责。",
            "6. 调整主题推荐值时递增 defaultsVersion；不得写入用户个性化配置，不得把当前用户覆盖当成主题默认值。",
            "7. 完成亮暗皮肤、消息框、输入框、三张卡、记忆、同步组件与角色挂件后，运行 Skill 的几何和契约校验，再在实际 Codex 页面检查首页、任务页和亮暗切换。",
            string.Empty,
            "最终将可直接导入 Tessalume 的完整主题交付到 themes/<主题目录>，并分别说明静态契约、运行验证和仍需人工确认的视觉项。",
        ]);
        return string.Join(Environment.NewLine, lines);
    }
}
