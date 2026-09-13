using CarpaNet.Identity;
using Koan.Core;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Web.Auth.Initialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using TangentSpace.Site;
using TangentSpace.Rooms;
using TangentSpace.AtProtocol;
using TangentSpace.AtProtocol.Verification;
using TangentSpace.Participation;
using TangentSpace.Conversation;
using TangentSpace.Communities;
using TangentSpace.Activity;
using TangentSpace.Mcp;
using TangentSpace.Mcp.Authentication;
using TangentSpace.Moderation;

namespace TangentSpace.Infrastructure;

[After(typeof(AuthModule))]
public sealed class TangentModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        Message.Lifecycle.BeforeUpsert(async context =>
        {
            var message = context.Current;
            if (string.IsNullOrWhiteSpace(Koan.Data.Core.EntityContext.Current?.Partition)
                && !message.Removed && message.OfMessageId is null && message.Facets is null)
                message.Facets = await MessageFacets.Effective(message.Content.Text, null, context.CancellationToken);
            return context.Proceed();
        });
        services.AddOptions<SiteOptions>().BindConfiguration(TangentConstants.SiteConfiguration)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name) && o.Name.Length <= 120, "Set Tangent:Site:Name to a name of 1–120 characters.")
            .Validate(o => string.IsNullOrWhiteSpace(o.OwnerDid) || IdentityResolver.IsValidDid(o.OwnerDid), "Tangent:Site:OwnerDid must be blank for first-login ownership, or a valid AT DID.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PolicyGate>();
        services.AddSingleton<Arrival>();
        services.AddSingleton<TangentSpace.Participants.ParticipantDirectory>();
        services.AddSingleton<TangentSpace.Participants.IAtprotoHandleSource, TangentSpace.Participants.AtprotoHandleResolver>();
        services.AddSingleton<TangentServer>();
        services.AddMemoryCache();
        services.AddSingleton<TangentSpace.Participants.ParticipantProfiles>();
        services.AddHostedService<TangentSpace.Participants.ProfileCapture>();
        services.AddSingleton<ServerGovernance>();
        services.AddOptions<SpacesOptions>().BindConfiguration(SpacesOptions.Configuration);
        services.AddOptions<TangentSpace.Conversation.ConversationOptions>().BindConfiguration(TangentSpace.Conversation.ConversationOptions.Configuration);
        services.AddSingleton<RoomGovernance>();
        services.AddSingleton<Rooms.Web.PublicConversationReader>();
        services.AddSingleton<TangentGovernance>();
        services.AddSingleton<CompanionGovernance>();
        services.AddSingleton<SpacesVerifier>();
        services.AddSingleton<SpacesService>();
        services.AddSingleton<ServiceAuthentication>();
        services.AddParticipation();
        services.AddTangentMcpAuthentication();
        services.AddTangentMcp();
        services.AddSingleton<ConversationService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<LiveSessions>();
        services.AddSingleton<Experience.ExperienceDigest>();
        services.AddSingleton<Experience.ExperienceService>();
        services.AddSingleton<ModerationCaseService>();
        services.AddSingleton<SourceNotifications>();
        services.AddSingleton<SourceReadiness>();
        var protection = services.AddDataProtection().SetApplicationName(nameof(TangentSpace));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        services.AddOptions<KeyManagementOptions>().Configure<IHostEnvironment, ILoggerFactory>((keyOptions, host, logger) =>
            keyOptions.XmlRepository = new FileSystemXmlRepository(
                new DirectoryInfo(Path.Combine(host.ContentRootPath, TangentConstants.KeyDirectory)), logger));
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
        => services.GetRequiredService<Arrival>().CheckConfiguration(ct);

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);
}
