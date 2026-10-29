using System.Net;
using Koan.Core;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent;

/// <summary>Composes the Tangent Space on Koan: what the operator configured, the one data folder,
/// the request pipeline, and the first start.</summary>
public sealed class TangentModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddOptions<TangentSpaceOptions>()
            .BindConfiguration(TangentSpaceOptions.Configuration)
            .Validate(options => TangentSpaceOptions.TryParseBaseUrl(options.BaseUrl, out _),
                "Tangent:BaseUrl must be an absolute http or https URL: scheme, host and an optional path.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.DataFolder),
                "Tangent:DataFolder must name a folder.")
            .Validate(options => options.TrustedProxy is null || IPAddress.TryParse(options.TrustedProxy, out _),
                "Tangent:TrustedProxy must be the one IP address of the immediate reverse proxy.")
            .Validate<IConfiguration>((options, configuration) => options.TrustedProxy is null
                || (TangentSpaceOptions.TryParseBaseUrl(options.BaseUrl, out var baseUrl)
                    && baseUrl.Scheme == Uri.UriSchemeHttps
                    && string.Equals(configuration["AllowedHosts"], baseUrl.Host, StringComparison.OrdinalIgnoreCase)),
                "A trusted proxy requires an https Tangent:BaseUrl and AllowedHosts equal to its host name.")
            .ValidateOnStart();

        services.AddOptions<SqliteOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((sqlite, tangent, environment) =>
                sqlite.ConnectionString = "Data Source=" + tangent.Value.DatabasePath(environment.ContentRootPath));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TrustedProxyPipelineContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, BaseUrlPipelineContributor>());
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is null)
            await new TangentSpace { Name = "Tangent Space", BaseUrl = options.BaseUrl }.Save(ct);

        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => ReportStarted(services));
    }

    private static void ReportStarted(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        services.GetRequiredService<ILogger<TangentModule>>().LogInformation(
            "Tangent Space at {BaseUrl}; listening on {Addresses}; data folder {DataFolder}; SQLite journal {Journal}",
            options.BaseUrl,
            string.Join(" ", addresses),
            options.DataFolderPath(contentRoot),
            SqliteJournal(options.DatabasePath(contentRoot)));
    }

    /// <summary>The persistent journal mode SQLite recorded in header bytes 18 and 19: 1 is rollback, 2 is write-ahead.</summary>
    private static string SqliteJournal(string databasePath)
    {
        if (!File.Exists(databasePath)) return "no database file";
        Span<byte> header = stackalloc byte[20];
        using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Read(header) < header.Length) return "header too short";
        return (header[18], header[19]) switch
        {
            (1, 1) => "rollback (header 1 1)",
            (2, 2) => "wal (header 2 2)",
            var (read, write) => $"unknown (header {read} {write})"
        };
    }
}
