namespace Tessalume.App.Features.Pets;

internal readonly record struct PetCodexAtlasFrame(
    int Row,
    int Column,
    int DurationMilliseconds);

internal sealed record PetCodexMotionTrack(
    string Key,
    IReadOnlyList<PetCodexAtlasFrame> Frames,
    int LoopStartIndex,
    int StartDelayMilliseconds = 0);

internal sealed record PetCodexMotionSequence(
    string Key,
    IReadOnlyList<PetCodexMotionTrack> Tracks,
    bool MatchesCodexState,
    int ActionCycleCount,
    bool IsShowcase = false)
{
    public IReadOnlyList<PetCodexAtlasFrame> Frames => Tracks[0].Frames;

    public int LoopStartIndex => Tracks[0].LoopStartIndex;

    public int TotalFrameCount => Tracks.Sum(track => track.Frames.Count);

    public bool ReturnsToIdle => !IsShowcase && LoopStartIndex > 0;
}

/// <summary>
/// Current Codex desktop v2 pet clock. These values are intentionally kept in
/// one testable compatibility layer so the Tessalume preview does not inherit
/// timing from presentation GIFs.
/// </summary>
internal static class PetCodexMotionSchedule
{
    internal const string RuntimeContractId = "codex-desktop-v2-2026-08-19";
    internal const int ActionCycleCount = 3;
    private const int IdleSlowdown = 6;

    private static readonly int[] IdleDurations = [280, 110, 110, 140, 140, 320];

    public static bool TryCreate(string key, out PetCodexMotionSequence sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (key.Equals("showcase", StringComparison.OrdinalIgnoreCase))
        {
            sequence = CreateShowcase();
            return true;
        }

        if (key.Equals("idle", StringComparison.OrdinalIgnoreCase))
        {
            var idle = CreateIdleFrames();
            sequence = CreateSingleSequence("idle", idle, 0, true, 0);
            return true;
        }

        if (key.Equals("gaze-clockwise", StringComparison.OrdinalIgnoreCase))
        {
            var directions = new List<PetCodexAtlasFrame>(16);
            for (var column = 0; column < 8; column++)
            {
                directions.Add(new PetCodexAtlasFrame(9, column, 170));
            }
            for (var column = 0; column < 8; column++)
            {
                directions.Add(new PetCodexAtlasFrame(10, column, 170));
            }
            sequence = CreateSingleSequence(
                "gaze-clockwise",
                directions,
                0,
                false,
                0);
            return true;
        }

        var state = key.ToLowerInvariant() switch
        {
            "move-right" => new ActionState(1, 8, 120, 220),
            "move-left" => new ActionState(2, 8, 120, 220),
            "wave-touch" => new ActionState(3, 4, 140, 280),
            "jump" => new ActionState(4, 5, 140, 280),
            "blocked" => new ActionState(5, 8, 140, 240),
            "needs-input" => new ActionState(6, 6, 150, 260),
            "running" => new ActionState(7, 6, 120, 220),
            "ready" => new ActionState(8, 6, 150, 280),
            _ => null,
        };
        if (state is null)
        {
            sequence = null!;
            return false;
        }

        var actionFrames = Enumerable.Range(0, state.FrameCount)
            .Select(column => new PetCodexAtlasFrame(
                state.Row,
                column,
                column == state.FrameCount - 1
                    ? state.LastDurationMilliseconds
                    : state.FrameDurationMilliseconds))
            .ToArray();
        var frames = new List<PetCodexAtlasFrame>(
            actionFrames.Length * ActionCycleCount + IdleDurations.Length);
        for (var cycle = 0; cycle < ActionCycleCount; cycle++)
        {
            frames.AddRange(actionFrames);
        }
        var loopStartIndex = frames.Count;
        frames.AddRange(CreateIdleFrames());
        sequence = CreateSingleSequence(
            key.ToLowerInvariant(),
            frames,
            loopStartIndex,
            true,
            ActionCycleCount);
        return true;
    }

    private static PetCodexMotionSequence CreateShowcase()
    {
        string[] keys =
        [
            "idle",
            "move-right",
            "move-left",
            "wave-touch",
            "jump",
            "blocked",
            "needs-input",
            "running",
            "ready",
        ];
        var tracks = keys
            .Select((key, index) =>
            {
                if (!TryCreate(key, out var source))
                {
                    throw new InvalidOperationException($"无法建立宠物动作轨道：{key}。");
                }

                var sourceTrack = source.Tracks[0];
                var frames = key.Equals("idle", StringComparison.Ordinal)
                    ? sourceTrack.Frames
                    : sourceTrack.Frames
                        .Take(sourceTrack.LoopStartIndex / ActionCycleCount)
                        .ToArray();
                return new PetCodexMotionTrack(
                    key,
                    frames,
                    LoopStartIndex: 0,
                    StartDelayMilliseconds: index * 90);
            })
            .ToArray();
        return new PetCodexMotionSequence(
            "showcase",
            tracks,
            MatchesCodexState: false,
            ActionCycleCount,
            IsShowcase: true);
    }

    private static PetCodexMotionSequence CreateSingleSequence(
        string key,
        IReadOnlyList<PetCodexAtlasFrame> frames,
        int loopStartIndex,
        bool matchesCodexState,
        int actionCycleCount) =>
        new(
            key,
            [new PetCodexMotionTrack(key, frames, loopStartIndex)],
            matchesCodexState,
            actionCycleCount);

    private static PetCodexAtlasFrame[] CreateIdleFrames() =>
        IdleDurations
            .Select((duration, column) => new PetCodexAtlasFrame(
                0,
                column,
                checked(duration * IdleSlowdown)))
            .ToArray();

    private sealed record ActionState(
        int Row,
        int FrameCount,
        int FrameDurationMilliseconds,
        int LastDurationMilliseconds);
}
