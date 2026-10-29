using Koan.Core.BackgroundServices;
using Koan.Data.Core;
using Koan.Data.Core.Lexical;

namespace Tangent.Posts;

/// <summary>At start, asks Koan whether the Post index covers every Post and, when it does not (a folder restored from a
/// backup copy, whose rows may carry new row numbers), has Koan rebuild it. Runs once on Koan's background mechanism,
/// so a start does not wait for the rebuild.</summary>
[KoanBackgroundService]
public sealed class PostIndex(ILogger<PostIndex> logger, IConfiguration configuration) : KoanBackgroundServiceBase(logger, configuration)
{
    public override async Task ExecuteCore(CancellationToken cancellationToken)
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
