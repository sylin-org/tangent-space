using System.Net;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Auth.Connector.Atproto.Options;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What participants need composed: the profile reader with its bounds, and the atproto connector's session
/// store inside the data folder, so a backup of the folder carries the sessions.</summary>
public static class ParticipantsComposition
{
    /// <summary>The atproto sessions' folder inside the data folder.</summary>
    public const string SessionFolder = "atproto";

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
        return services;
    }
}
