using System;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewActorRealizer
{
    Task<InterpPreviewRealizeResult> RealizeAsync(
        InterpPreviewPreparedResource preparedResource,
        IActorEditorContext actorEditorContext,
        Func<bool> canContinue,
        CancellationToken cancellationToken);
}

public interface IInterpPreviewActorProxyFactory
{
    ActorProxy Create(
        IActorEditorContext actorEditorContext,
        IMEPackage package,
        InterpPreviewActorBuildDescriptor descriptor);
}

public enum InterpPreviewRealizeOutcome
{
    Loaded,
    Cancelled,
    Superseded,
    Failed
}

public sealed class InterpPreviewRealizeResult
{
    private InterpPreviewRealizeResult(InterpPreviewRealizeOutcome outcome, InterpPreviewLoadedLevel loadedLevel, Exception error)
    {
        Outcome = outcome;
        LoadedLevel = loadedLevel;
        Error = error;
    }

    public InterpPreviewRealizeOutcome Outcome { get; }
    public InterpPreviewLoadedLevel LoadedLevel { get; }
    public Exception Error { get; }

    public static InterpPreviewRealizeResult Loaded(InterpPreviewLoadedLevel loadedLevel)
        => new(InterpPreviewRealizeOutcome.Loaded, loadedLevel, null);

    public static InterpPreviewRealizeResult Cancelled()
        => new(InterpPreviewRealizeOutcome.Cancelled, null, null);

    public static InterpPreviewRealizeResult Superseded()
        => new(InterpPreviewRealizeOutcome.Superseded, null, null);

    public static InterpPreviewRealizeResult Failed(Exception error)
        => new(InterpPreviewRealizeOutcome.Failed, null, error);
}
