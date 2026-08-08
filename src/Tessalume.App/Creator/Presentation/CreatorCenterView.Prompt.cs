using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Tessalume.App.Creator;

public partial class CreatorCenterView
{
    private CreatorPromptView PromptView => WorkspacePage.PromptView;

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (!CreatorPromptComposer.CanCopy(_promptDraft)) return;
        try
        {
            Clipboard.SetText(PromptView.CreatorPromptText.Text);
            _showToast?.Invoke("提示词已复制");
        }
        catch (ExternalException)
        {
            _showToast?.Invoke("剪贴板正忙，请再点一次");
        }
    }

    private void TogglePromptEditor_Click(object sender, RoutedEventArgs e)
    {
        _promptEditorExpanded = !_promptEditorExpanded;
        PromptView.CreatorPromptEditor.Visibility = _promptEditorExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        PromptView.TogglePromptEditorButton.Content = _promptEditorExpanded
            ? "收起定制"
            : "定制提示词";
    }

    private void PromptField_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingPrompt) return;
        _promptDraft = ReadPromptDraft();
        RenderPromptDraft();
        _promptDraftDirty = true;
        _promptSaveTimer.Stop();
        _promptSaveTimer.Start();
    }

    private void ResetPrompt_Click(object sender, RoutedEventArgs e)
    {
        LoadPromptDraft(new CreatorPromptDraft());
        _promptDraftDirty = true;
        _promptSaveTimer.Stop();
        _promptSaveTimer.Start();
        _showToast?.Invoke("已恢复提示词示例");
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
            await _savePromptDraftAsync(saving);
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
            await _savePromptDraftAsync(_promptDraft.Normalize());
            _promptDraftDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
