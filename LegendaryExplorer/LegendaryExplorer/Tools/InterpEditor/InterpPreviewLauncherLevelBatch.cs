using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

internal enum InterpPreviewLauncherBatchTerminalOutcome
{
    None,
    Cancelled,
    Superseded,
    RejectedClosing,
    Failed
}

internal sealed class InterpPreviewCommittedLevelResult
{
    public InterpPreviewCommittedLevelResult(string path, InterpPreviewOperationResult operationResult)
    {
        Path = path;
        OperationResult = operationResult;
    }

    public string Path { get; }
    public InterpPreviewOperationResult OperationResult { get; }
}

internal sealed class InterpPreviewLauncherLevelBatchResult
{
    public int LoadedCount { get; set; }
    public bool HadFailure { get; set; }
    public InterpPreviewLauncherBatchTerminalOutcome TerminalOutcome { get; set; }
    public string TerminalMessage { get; set; }
    public IReadOnlyList<InterpPreviewCommittedLevelResult> CommittedLevels { get; set; } = [];
}

internal static class InterpPreviewLauncherLevelBatch
{
    internal static bool TryNormalizeDistinctLevelPaths(IReadOnlyList<string> levelPaths, out IReadOnlyList<string> normalizedPaths)
    {
        normalizedPaths = [];
        if (levelPaths is null || levelPaths.Count == 0)
        {
            return true;
        }

        var distinct = new List<string>(levelPaths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < levelPaths.Count; i++)
        {
            string path = levelPaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string canonicalPath;
            try
            {
                canonicalPath = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            if (seen.Add(canonicalPath))
            {
                distinct.Add(canonicalPath);
            }
        }

        normalizedPaths = distinct;
        return true;
    }

    public static async Task<InterpPreviewLauncherLevelBatchResult> ExecuteTransactionalReplaceAsync(
        IInterpPreviewRuntime runtime,
        IReadOnlyList<string> levelPaths,
        IActorEditorContext actorEditorContext,
        Func<bool> isRequestCurrent)
    {
        var result = new InterpPreviewLauncherLevelBatchResult();
        var staged = new List<(string path, InterpPreviewLoadedLevel loadedLevel)>();
        var committed = new List<InterpPreviewCommittedLevelResult>();
        try
        {
            result.CommittedLevels = committed;
            if (!TryNormalizeDistinctLevelPaths(levelPaths, out IReadOnlyList<string> normalizedPaths))
            {
                result.HadFailure = true;
                result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Failed;
                result.TerminalMessage = "Launcher request contained an invalid level path.";
                return result;
            }

            int totalCount = normalizedPaths.Count;
            for (int i = 0; i < totalCount; i++)
            {
                if (!isRequestCurrent())
                {
                    result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Superseded;
                    result.TerminalMessage = "Launcher request was superseded.";
                    return result;
                }

                string path = normalizedPaths[i];

                InterpPreviewLoadResult loadResult = await runtime.LoadLevelAsync(path, replace: false, actorEditorContext, onReplace: null).ConfigureAwait(true);
                switch (loadResult.Outcome)
                {
                    case InterpPreviewLoadOutcome.Loaded:
                        if (loadResult.LoadedLevel is not null)
                        {
                            staged.Add((path, loadResult.LoadedLevel));
                        }
                        break;
                    case InterpPreviewLoadOutcome.Cancelled:
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Cancelled;
                        result.TerminalMessage = "Launcher request was cancelled.";
                        return result;
                    case InterpPreviewLoadOutcome.Superseded:
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Superseded;
                        result.TerminalMessage = "Launcher request was superseded.";
                        return result;
                    case InterpPreviewLoadOutcome.Duplicate:
                    case InterpPreviewLoadOutcome.Failed:
                        result.HadFailure = true;
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Failed;
                        result.TerminalMessage = "Launcher request could not stage all replacement levels.";
                        return result;
                }
            }

            bool replace = true;
            foreach ((string path, InterpPreviewLoadedLevel loadedLevel) in staged)
            {
                if (!isRequestCurrent())
                {
                    result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Superseded;
                    result.TerminalMessage = "Launcher request was superseded.";
                    return result;
                }

                InterpPreviewOperationResult commitResult = await runtime.ExecuteCommitOperationAsync(loadedLevel, replace).ConfigureAwait(true);
                replace = false;
                switch (commitResult.Outcome)
                {
                    case InterpPreviewOperationOutcome.Committed:
                        committed.Add(new InterpPreviewCommittedLevelResult(path, commitResult));
                        result.LoadedCount++;
                        break;
                    case InterpPreviewOperationOutcome.NoChange:
                        result.LoadedCount++;
                        break;
                    case InterpPreviewOperationOutcome.Cancelled:
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Cancelled;
                        result.TerminalMessage = "Launcher request was cancelled.";
                        return result;
                    case InterpPreviewOperationOutcome.Superseded:
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Superseded;
                        result.TerminalMessage = "Launcher request was superseded.";
                        return result;
                    case InterpPreviewOperationOutcome.RejectedClosing:
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.RejectedClosing;
                        result.TerminalMessage = "Preview is closing.";
                        return result;
                    case InterpPreviewOperationOutcome.Failed:
                        result.HadFailure = true;
                        result.TerminalOutcome = InterpPreviewLauncherBatchTerminalOutcome.Failed;
                        result.TerminalMessage = "Launcher request failed while integrating staged levels.";
                        return result;
                }
            }

            return result;
        }
        finally
        {
            foreach ((_, InterpPreviewLoadedLevel loadedLevel) in staged)
            {
                if (loadedLevel is not null && !loadedLevel.IsTransferred && !loadedLevel.IsDisposed)
                {
                    loadedLevel.Dispose();
                }
            }
        }
    }
}