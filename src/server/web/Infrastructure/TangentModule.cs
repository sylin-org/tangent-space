using CarpaNet.Identity;
using Koan.Core;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Web.Auth.Initialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Koan.Identity;
using Koan.Identity.Web.Initialization;
using Tangent.Application;
using Tangent.Access;
using Tangent.Spaces;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Identity;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Community;
using Tangent.Activity;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Api;

namespace Tangent.Infrastructure;

[After(typeof(AuthModule))]
[After(typeof(SecIdentityWebModule))]
public sealed class TangentModule : KoanModule
{
    /// <summary>Scopes every Data Protection payload, sign-in cookies and list cursors included, so a
    /// change here signs everyone out and expires every cursor. It names the assembly, which D14 leaves
    /// as TangentSpace while the namespaces become Tangent.*, and is held stable on purpose.</summary>
    private const string DataProtectionApplicationName = "TangentSpace";

    public override void Register(IServiceCollection services)
    {
        TangentOwnerRoleGuard.Register();
        Post.Lifecycle.BeforeUpsert(async context =>
        {
            var post = context.Current;
            if (string.IsNullOrWhiteSpace(Koan.Data.Core.EntityContext.Current?.Partition)
                && !post.Removed && post.OfMessageId is null && post.Facets is null)
                post.Facets = await PostFacets.Effective(post.Content.Text, null, context.CancellationToken);
            return context.Proceed();
        });
        services.AddOptions<SpaceOptions>().BindConfiguration(TangentConstants.SpaceConfiguration)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name) && o.Name.Length <= 120, "Set Tangent:Space:Name to a name of 1–120 characters.")
            .Validate(o => string.IsNullOrWhiteSpace(o.OwnerDid) || IdentityResolver.IsValidDid(o.OwnerDid), "Tangent:Space:OwnerDid must be blank for first-login ownership, or a valid AT DID.")
            .Validate(o => string.IsNullOrWhiteSpace(o.PublicOrigin) || SpaceOptions.IsCanonicalOrigin(o.PublicOrigin, out _),
                "Tangent:Space:PublicOrigin must be a canonical absolute origin like https://tangent.example.")
            .ValidateOnStart();
        services.AddOptions<EnrollmentOptions>().BindConfiguration(EnrollmentOptions.Configuration)
            .Validate(o => string.IsNullOrWhiteSpace(o.ProofAudience) || ProofAudience.IsAtprotoAudience(o.ProofAudience),
                "Tangent:Enrollment:ProofAudience must be blank, to derive it from Tangent:Space:PublicOrigin, or an atproto audience: a did:plc, or a did:web of a hostname with a port only for localhost.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddAntiforgery();
        services.AddSingleton<TangentIdentityActorAccessor>();
        services.Replace(ServiceDescriptor.Singleton<IIdentityActorAccessor>(provider => provider.GetRequiredService<TangentIdentityActorAccessor>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation, HostOwnerClaimsTransformation>());
        services.AddSingleton<TangentRoleAccess>();
        services.AddSingleton<PolicyGate>();
        services.AddSingleton<Arrival>();
        services.AddSingleton<ParticipantDirectory>();
        services.AddSingleton<IAtprotoHandleSource, AtprotoHandleResolver>();
        services.AddSingleton<TangentServer>();
        services.AddMemoryCache();
        services.AddSingleton<ParticipantProfiles>();
        services.AddHostedService<ProfileCapture>();
        services.AddSingleton<ServerGovernance>();
        services.AddSingleton<TopicGovernance>();
        services.AddSingleton<PublicConversationReader>();
        services.AddSingleton<TangentGovernance>();
        services.AddSingleton<ParticipantGovernance>();
        services.AddParticipation();
        services.AddTangentMcpAuthentication();
        services.AddSingleton<References>();
        services.AddSingleton<OperationReceipts>();
        services.AddSingleton<ConversationService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<LiveSessions>();
        services.AddSingleton<ExperienceDigest>();
        services.AddSingleton<ExperienceService>();
        services.AddSingleton<ModerationCaseService>();
        var protection = services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        services.AddOptions<KeyManagementOptions>().Configure<IHostEnvironment, ILoggerFactory>((keyOptions, host, logger) =>
            keyOptions.XmlRepository = new FileSystemXmlRepository(
                new DirectoryInfo(Path.Combine(host.ContentRootPath, TangentConstants.KeyDirectory)), logger));
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<Arrival>().CheckConfiguration(ct);
        await services.GetRequiredService<TangentRoleAccess>().Seed(
            await Space.Get(TangentConstants.SpaceId, ct), ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);
}
