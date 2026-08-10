using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    private CreatorPromptView PromptView => WorkspacePage.PromptView;

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        TryCopyPrompt();
    }

    private bool TryCopyPrompt()
    {
        if (!CreatorPromptComposer.CanCopy(_promptDraft)) return false;
        try
        {
            Clipboard.SetText(PromptView.CreatorPromptText.Text);
            _showToast?.Invoke("提示词已复制");
            UpdatePromptGuidanceState(copied: true);
            return true;
        }
        catch (ExternalException)
        {
            _showToast?.Invoke("剪贴板正忙，请再点一次");
            return false;
        }
    }

    private void TogglePromptEditor_Click(object sender, RoutedEventArgs e)
    {
        SetPromptEditorExpanded(!_promptEditorExpanded);
    }

    private void ExpandPromptEditor() => SetPromptEditorExpanded(true);

    private void SetPromptEditorExpanded(bool expanded)
    {
        _promptEditorExpanded = expanded;
        PromptView.CreatorPromptEditor.Visibility = _promptEditorExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        PromptView.TogglePromptEditorButton.Content = _promptEditorExpanded
            ? "收起需求编辑"
            : "编辑创作需求";
    }

    private void PromptField_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingPrompt) return;
        _promptDraft = ReadPromptDraft();
        RenderPromptDraft();
        UpdatePromptGuidanceState(copied: false);
        _promptDraftDirty = true;
        _promptSaveTimer.Stop();
        _promptSaveTimer.Start();
    }

    private void ResetPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (!ProductDialogWindow.Confirm(
                GetOwner(),
                "清空当前创作需求？",
                "将清空作品名称、角色名称、视觉方向和其他要求。工作区与已经生成的主题不会改变。",
                "清空内容",
                "取消",
                dangerous: false,
                darkMode: IsDarkMode())) return;
        LoadPromptDraft(new CreatorPromptDraft());
        _promptDraftDirty = true;
        _promptSaveTimer.Stop();
        _promptSaveTimer.Start();
        _showToast?.Invoke("创作需求已清空");
        UpdatePromptGuidanceState(copied: false);
    }

    private void LoadPromptDraft(CreatorPromptDraft draft)
    {
        _promptDraft = draft.Normalize();
        _updatingPrompt = true;
        try
        {
            PromptView.PromptWorkNameBox.Text = _promptDraft.WorkName;
            PromptView.PromptCharacterNameBox.Text = _promptDraft.CharacterName;
            PromptView.PromptVisualDirectionBox.Text = _promptDraft.VisualDirection;
            PromptView.PromptSpecialRequirementsBox.Text = _promptDraft.SpecialRequirements;
            PromptView.PromptReferenceCheckBox.IsChecked = _promptDraft.UsesReferenceImages;
        }
        finally
        {
            _updatingPrompt = false;
        }
        RenderPromptDraft();
        UpdatePromptGuidanceState(copied: false);
    }

    private CreatorPromptDraft ReadPromptDraft() => new()
    {
        WorkName = PromptView.PromptWorkNameBox.Text,
        CharacterName = PromptView.PromptCharacterNameBox.Text,
        VisualDirection = PromptView.PromptVisualDirectionBox.Text,
        SpecialRequirements = PromptView.PromptSpecialRequirementsBox.Text,
        UsesReferenceImages = PromptView.PromptReferenceCheckBox.IsChecked == true,
    };

    private void RenderPromptDraft()
    {
        _promptDraft = _promptDraft.Normalize();
        PromptView.CreatorPromptText.Text = CreatorPromptComposer.Compose(_promptDraft);
        var canCopy = CreatorPromptComposer.CanCopy(_promptDraft);
        PromptView.CopyPromptButton.IsEnabled = canCopy;
        PromptView.CreatorPromptStatusText.Text = canCopy
            ? "已包含角色确认、11 张素材计划、亮暗覆盖与最终校验"
            : "请先填写作品名称和角色名称";
        PromptView.CreatorPromptStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            canCopy ? "Teal" : "Amber");
    }

    private async void PromptSaveTimer_Tick(object? sender, EventArgs e)
    {
        _promptSaveTimer.Stop();
        if (_savePromptDraftAsync is null) return;
        var saving = _promptDraft.Normalize();
        try
        {
            await _savePromptDraftAsync(_promptWorkspacePath, saving);
            if (saving == _promptDraft.Normalize()) _promptDraftDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _showToast?.Invoke("提示词草稿暂时无法保存");
        }
    }

    internal async Task FlushPendingPromptDraftAsync()
    {
        _promptSaveTimer.Stop();
        if (!_promptDraftDirty || _savePromptDraftAsync is null) return;
        try
        {
            await _savePromptDraftAsync(_promptWorkspacePath, _promptDraft.Normalize());
            _promptDraftDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task SwitchPromptContextAsync(
        string? workspacePath,
        CreatorPromptDraft? seed = null)
    {
        if (string.Equals(
                _promptWorkspacePath,
                workspacePath,
                StringComparison.OrdinalIgnoreCase) && seed is null) return;

        await FlushPendingPromptDraftAsync();
        _promptWorkspacePath = workspacePath;
        var draft = (seed ?? _loadPromptDraft?.Invoke(workspacePath) ?? new CreatorPromptDraft()).Normalize();
        if (seed is not null && _savePromptDraftAsync is not null)
        {
            await _savePromptDraftAsync(workspacePath, draft);
        }
        LoadPromptDraft(draft);
    }

    private void UpdatePromptGuidanceState(bool copied) =>
        _viewModel?.SetPromptState(
            CreatorPromptComposer.CanCopy(_promptDraft),
            copied,
            _promptWorkspacePath is null);
}
