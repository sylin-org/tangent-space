using Koan.Core.BackgroundServices;
using TangentSpace.AtProtocol;

namespace TangentSpace.Conversation;

[KoanBackgroundService]
public sealed class ConversationSync(SourceNotifications notifications, ILogger<ConversationSync> logger, IConfiguration configuration)
    : KoanBackgroundServiceBase(logger, configuration)
{
    public override Task ExecuteCore(CancellationToken ct) => notifications.Run(ct);
}
