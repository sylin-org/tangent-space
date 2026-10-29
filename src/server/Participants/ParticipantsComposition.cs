using System.Net;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Auth.Connector.Atproto.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What participants need composed: the profile reader with its bounds; the atproto connector's session
/// store and ASP.NET's data-protection key ring inside the data folder, so sessions and cookies survive a new
/// binary or container and a backup of the folder carries them; and antiforgery for the forms a page posts.</summary>
public static class ParticipantsComposition
{
    /// <summary>The atproto sessions' folder inside the data folder.</summary>
    public const string SessionFolder = "atproto";

    /// <summary>The data-protection key ring's folder inside the data folder.</summary>
    public const string KeyFolder = "keys";

    /// <summary>The name data protection isolates this application's keys by, the same wherever the binary runs.</summary>
    public const string ProtectionName = "Tangent Space";

    public static IServiceCollection AddParticipants(this IServiceCollection services)
    {
        services.AddKoanOptions<ProfileReaderOptions>(ProfileReaderOptions.Configuration)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true, Scheme: "https" or "http" },
                $"{ProfileReaderOptions.Configuration}:BaseAddress must be an absolute http or https address.")
            .Validate(options => options.Timeout > TimeSpan.Zero && options.MaximumResponseBytes > 0,
                $"{ProfileReaderOptions.Configuration}:Timeout and MaximumResponseBytes must be positive.");

        // One request per read: no redirect is followed and no cookie is kept.
        services.AddHttpClient<ProfileReader>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None
            });

        services.AddOptions<AtprotoOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((atproto, tangent, environment) =>
                atproto.SessionDirectory = Path.Combine(tangent.Value.DataFolderPath(environment.ContentRootPath), SessionFolder));

        services.AddDataProtection().SetApplicationName(ProtectionName);
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<TangentSpaceOptions>, IHostEnvironment, ILoggerFactory>((keys, tangent, environment, logging) =>
                keys.XmlRepository = new FileSystemXmlRepository(
                    Directory.CreateDirectory(Path.Combine(tangent.Value.DataFolderPath(environment.ContentRootPath), KeyFolder)),
                    logging));

        services.AddAntiforgery();
        return services;
    }
}
