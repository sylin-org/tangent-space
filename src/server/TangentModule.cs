using System.Net;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tangent.Render;
using Tangent.Space;

namespace Tangent;

/// <summary>Composes the Tangent Space on Koan: what the operator configured, the one data folder,
/// the request pipeline, the HTML projection, and the first start.</summary>
public sealed class TangentModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TangentSpaceOptions>(TangentSpaceOptions.Configuration)
            .PostConfigure(options =>
            {
                if (TangentSpaceOptions.TryNormalizeBaseUrl(options.BaseUrl, out var baseUrl)) options.BaseUrl = baseUrl;
            })
            .Validate(options => TangentSpaceOptions.TryParseBaseUrl(options.BaseUrl, out _),
                "Tangent:BaseUrl must be an absolute http or https URL: scheme, host and an optional path.")
            .Validate(options => !TangentSpaceOptions.EncodesSeparator(options.BaseUrl),
                "Tangent:BaseUrl must not encode a path separator (%2F): write the path as plain segments.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.DataFolder),
                "Tangent:DataFolder must name a folder.")
            .Validate(options => options.TrustedProxy is null || IPAddress.TryParse(options.TrustedProxy, out _),
                "Tangent:TrustedProxy must be the one IP address of the immediate reverse proxy.")
            .Validate<IConfiguration>((options, configuration) => options.TrustedProxy is null
                || (TangentSpaceOptions.TryParseBaseUrl(options.BaseUrl, out var baseUrl)
                    && baseUrl.Scheme == Uri.UriSchemeHttps
                    && string.Equals(configuration["AllowedHosts"], baseUrl.Host, StringComparison.OrdinalIgnoreCase)),
                "A trusted proxy requires an https Tangent:BaseUrl and AllowedHosts equal to its host name.");

        services.AddOptions<SqliteOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((sqlite, tangent, environment) =>
                sqlite.ConnectionString = "Data Source=" + tangent.Value.DatabasePath(environment.ContentRootPath));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TrustedProxyPipelineContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, BaseUrlPipelineContributor>());

        // The HTML projection: each resource type names its component; the formatter goes first so a request
        // without a preference reads a page, and every other type keeps Koan's JSON.
        services.AddScoped<HtmlRenderer>();
        services.AddSingleton(new ResourceComponents()
            .Add<Door, DoorView>(door => door.Name, door => door.Version)
            .Add<Problem, ProblemView>(problem => problem.Title));
        services.AddSingleton<HtmlFormatter>();
        services.AddOptions<MvcOptions>().Configure<HtmlFormatter>((mvc, html) =>
        {
            mvc.OutputFormatters.Insert(0, html);
            mvc.RespectBrowserAcceptHeader = true;
            mvc.Filters.Add<ETagFilter>();
        });
        services.WithExceptionHandler();
        services.AddExceptionHandler(handler => handler.ExceptionHandler = ProblemExceptionHandler.Answer);
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
