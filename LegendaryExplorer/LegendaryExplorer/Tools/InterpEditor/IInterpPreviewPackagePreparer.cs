using System;
using System.Threading;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewPackagePreparer
{
    Task<InterpPreviewPrepareResult> PrepareAsync(string path, CancellationToken cancellationToken);
}

public enum InterpPreviewPrepareOutcome
{
    Prepared,
    Cancelled,
    Failed
}

public sealed class InterpPreviewPrepareResult
{
    private InterpPreviewPrepareResult(InterpPreviewPrepareOutcome outcome, InterpPreviewPreparedResource preparedResource, Exception error)
    {
        Outcome = outcome;
        PreparedResource = preparedResource;
        Error = error;
    }

    public InterpPreviewPrepareOutcome Outcome { get; }
    public InterpPreviewPreparedResource PreparedResource { get; }
    public Exception Error { get; }

    public static InterpPreviewPrepareResult Prepared(InterpPreviewPreparedResource preparedResource)
        => new(InterpPreviewPrepareOutcome.Prepared, preparedResource, null);

    public static InterpPreviewPrepareResult Cancelled()
        => new(InterpPreviewPrepareOutcome.Cancelled, null, null);

    public static InterpPreviewPrepareResult Failed(Exception error)
        => new(InterpPreviewPrepareOutcome.Failed, null, error);
}
