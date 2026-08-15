using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal readonly record struct ArtworkHistoryStatus(
    int UndoCount,
    int RedoCount,
    bool GestureActive)
{
    public bool CanUndo => UndoCount > 0;

    public bool CanRedo => RedoCount > 0;
}

internal sealed class ArtworkHistoryService
{
    public const int DefaultCapacity = 64;

    private readonly int _capacity;
    private readonly Dictionary<string, ThemeHistory> _histories =
        new(StringComparer.OrdinalIgnoreCase);

    public ArtworkHistoryService(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "History capacity must be positive.");
        }
        _capacity = capacity;
    }

    public bool BeginGesture(string themeId, ThemeVisualSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var history = GetHistory(themeId);
        if (history.GestureActive) return false;
        history.GestureStart = current.Normalize();
        history.GestureActive = true;
        return true;
    }

    public bool EndGesture(string themeId, ThemeVisualSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var history = GetHistory(themeId);
        if (!history.GestureActive || history.GestureStart is not { } start) return false;
        history.GestureStart = null;
        history.GestureActive = false;
        return Commit(history, start, current);
    }

    public bool CancelGesture(string themeId)
    {
        var history = GetHistory(themeId);
        if (!history.GestureActive) return false;
        history.GestureStart = null;
        history.GestureActive = false;
        return true;
    }

    public bool RecordDiscrete(
        string themeId,
        ThemeVisualSettings before,
        ThemeVisualSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var history = GetHistory(themeId);
        if (history.GestureActive)
        {
            _ = EndGesture(themeId, before);
        }
        return Commit(history, before, after);
    }

    public bool TryUndo(
        string themeId,
        ThemeVisualSettings current,
        out ThemeVisualSettings restored)
    {
        ArgumentNullException.ThrowIfNull(current);
        var history = GetHistory(themeId);
        if (history.GestureActive)
        {
            _ = EndGesture(themeId, current);
        }

        var normalizedCurrent = current.Normalize();
        restored = normalizedCurrent;
        if (history.Undo.Count == 0) return false;
        restored = history.Undo[^1];
        history.Undo.RemoveAt(history.Undo.Count - 1);
        Push(history.Redo, normalizedCurrent);
        return true;
    }

    public bool TryRedo(
        string themeId,
        ThemeVisualSettings current,
        out ThemeVisualSettings restored)
    {
        ArgumentNullException.ThrowIfNull(current);
        var history = GetHistory(themeId);
        if (history.GestureActive)
        {
            _ = EndGesture(themeId, current);
        }

        var normalizedCurrent = current.Normalize();
        restored = normalizedCurrent;
        if (history.Redo.Count == 0) return false;
        restored = history.Redo[^1];
        history.Redo.RemoveAt(history.Redo.Count - 1);
        Push(history.Undo, normalizedCurrent);
        return true;
    }

    public ArtworkHistoryStatus GetStatus(string themeId)
    {
        var history = GetHistory(themeId);
        return new ArtworkHistoryStatus(
            history.Undo.Count,
            history.Redo.Count,
            history.GestureActive);
    }

    public void Clear(string themeId) => _histories.Remove(NormalizeThemeId(themeId));

    public void ClearAll() => _histories.Clear();

    private bool Commit(
        ThemeHistory history,
        ThemeVisualSettings before,
        ThemeVisualSettings after)
    {
        var normalizedBefore = before.Normalize();
        var normalizedAfter = after.Normalize();
        if (ThemeVisualSettingsSemanticComparer.Instance.Equals(
                normalizedBefore,
                normalizedAfter)) return false;
        Push(history.Undo, normalizedBefore);
        history.Redo.Clear();
        return true;
    }

    private void Push(List<ThemeVisualSettings> stack, ThemeVisualSettings settings)
    {
        if (stack.Count == 0 ||
            !ThemeVisualSettingsSemanticComparer.Instance.Equals(stack[^1], settings))
        {
            stack.Add(settings);
        }
        if (stack.Count > _capacity)
        {
            stack.RemoveRange(0, stack.Count - _capacity);
        }
    }

    private ThemeHistory GetHistory(string themeId)
    {
        var key = NormalizeThemeId(themeId);
        if (_histories.TryGetValue(key, out var history)) return history;
        history = new ThemeHistory();
        _histories[key] = history;
        return history;
    }

    private static string NormalizeThemeId(string themeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        return themeId.Trim();
    }

    private sealed class ThemeHistory
    {
        public List<ThemeVisualSettings> Undo { get; } = [];

        public List<ThemeVisualSettings> Redo { get; } = [];

        public bool GestureActive { get; set; }

        public ThemeVisualSettings? GestureStart { get; set; }
    }
}
