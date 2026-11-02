using Koan.Core.BackgroundServices;
using Koan.Data.Core;
using Koan.Data.Core.Lexical;

namespace Tangent.Posts;

/// <summary>At start, asks Koan whether the Post index covers every Post and, when it does not (a folder restored from a
/// backup copy, whose rows may carry new row numbers), has Koan rebuild it. Runs on Koan's background mechanism, so a start
/// does not wait for the rebuild. What fails is logged as an error and tried again, each wait longer than the last up to
/// <see cref="LongestWait"/>, until it is done or the host stops, so an index that is not current never goes unsaid.</summary>
[KoanBackgroundService]
public sealed class PostIndex(ILogger<PostIndex> logger, IConfiguration configuration) : KoanBackgroundServiceBase(logger, configuration)
{
    /// <summary>How long the first retry waits.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);

    /// <summary>How long any retry waits at most.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(1);

    public override async Task ExecuteCore(CancellationToken cancellationToken)
    {
        for (var wait = FirstWait; ; wait = wait * 2 < LongestWait ? wait * 2 : LongestWait)
        {
            try
            {
                await BringUpToDate(cancellationToken);
                return;
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Logger.LogError(failure, "The Post index could not be brought up to date, so search may miss Posts; trying again in {Wait}", wait);
            }

            try
            {
                await Task.Delay(wait, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task BringUpToDate(CancellationToken cancellationToken)
    {
        var status = await Post.Lexical.Status(cancellationToken);
        if (status.Current)
        {
            Logger.LogInformation("The Post index covers all {Rows} Posts.", status.Rows);
            return;
        }

        Logger.LogInformation("The Post index covers {Indexed} of {Rows} Posts; rebuilding it.", status.Indexed, status.Rows);
        var indexed = await Post.Lexical.Rebuild(cancellationToken);
        Logger.LogInformation("The Post index was rebuilt and covers {Indexed} Posts.", indexed);
    }
}
