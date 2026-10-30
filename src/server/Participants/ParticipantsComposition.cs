using System.Net;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Options;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What participants need composed: the profile reader with its bounds; the data-protection key ring, through
/// Koan's <c>Koan:Web:KeyRingDirectory</c>, inside the data folder, so cookies and the atproto sessions Koan keeps as rows
/// in the store survive a new binary or container and a backup of the folder carries them; and antiforgery for the
/// forms a page posts.</summary>
public static class ParticipantsComposition
{
    /// <summary>The data-protection key ring's folder inside the data folder.</summary>
    public const string KeyFolder = "keys";

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

        // Koan persists the ring there, remembers the application's name in it and creates it at start.
        services.AddOptions<KoanWebOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((web, tangent, environment) =>
                web.KeyRingDirectory = Path.Combine(tangent.Value.DataFolderPath(environment.ContentRootPath), KeyFolder));

        services.AddAntiforgery();
        return services;
    }
}
