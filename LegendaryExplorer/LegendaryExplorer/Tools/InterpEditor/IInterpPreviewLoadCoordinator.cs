using System;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewLoadCoordinator : IDisposable
{
    Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace);
    void CancelPendingLoad();
}

public enum InterpPreviewLoadOutcome
{
    Loaded,
    Duplicate,
    Cancelled,
    Failed
}

public sealed class InterpPreviewLoadResult
{
    private InterpPreviewLoadResult(InterpPreviewLoadOutcome outcome, InterpPreviewLoadedLevel loadedLevel, Exception error)
    {
        Outcome = outcome;
        LoadedLevel = loadedLevel;
        Error = error;
    }

    public InterpPreviewLoadOutcome Outcome { get; }
    public InterpPreviewLoadedLevel LoadedLevel { get; }
    public Exception Error { get; }

    public static InterpPreviewLoadResult Loaded(InterpPreviewLoadedLevel loadedLevel) => new(InterpPreviewLoadOutcome.Loaded, loadedLevel, null);
    public static InterpPreviewLoadResult Duplicate() => new(InterpPreviewLoadOutcome.Duplicate, null, null);
    public static InterpPreviewLoadResult Cancelled() => new(InterpPreviewLoadOutcome.Cancelled, null, null);
    public static InterpPreviewLoadResult Failed(Exception error) => new(InterpPreviewLoadOutcome.Failed, null, error);
}
