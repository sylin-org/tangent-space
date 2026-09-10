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

namespace TangentSpace.Infrastructure;

[After(typeof(AuthModule))]
public sealed class TangentModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddOptions<SiteOptions>().BindConfiguration(TangentConstants.SiteConfiguration)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name) && o.Name.Length <= 120, "Set Tangent:Site:Name to a name of 1–120 characters.")
            .Validate(o => IdentityResolver.IsValidDid(o.OwnerDid), "Set Tangent:Site:OwnerDid to the AT DID that must sign in to establish this site.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PolicyGate>();
        services.AddSingleton<Arrival>();
        services.AddOptions<SpacesOptions>().BindConfiguration(SpacesOptions.Configuration);
        services.AddSingleton<RoomGovernance>();
        services.AddSingleton<TangentGovernance>();
        services.AddSingleton<SpacesVerifier>();
        services.AddSingleton<SpacesService>();
        services.AddSingleton<ServiceAuthentication>();
        services.AddParticipation();
        services.AddSingleton<ConversationService>();
        services.AddSingleton<ActivityService>();
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
