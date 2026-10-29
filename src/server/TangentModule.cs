using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Koan.Web.Options;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Events;
using Tangent.Lifecycle;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent;

/// <summary>Composes the Tangent Space on Koan: what the operator configured, the one data folder,
/// the HTML projection, and the first start.</summary>
public sealed class TangentModule : KoanModule
{
    /// <summary>The key Koan reads the base URL from. A Tangent Space always has one: its row records it and
    /// every absolute link derives from it, so Koan's request-derived posture is never enough here.</summary>
    public const string BaseUrlKey = "Koan:Web:ApplicationUrl";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TangentSpaceOptions>(TangentSpaceOptions.Configuration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.DataFolder),
                "Tangent:DataFolder must name a folder.");

        services.AddOptions<KoanWebOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApplicationUrl),
                $"{BaseUrlKey} must be set to the base URL the Tangent Space answers at: scheme, host and an optional path.");

        services.AddOptions<SqliteOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((sqlite, tangent, environment) =>
                sqlite.ConnectionString = "Data Source=" + tangent.Value.DatabasePath(environment.ContentRootPath));

        // The data folder is marked in use while the Tangent Space runs, so a restore refuses it.
        services.AddHostedService<RunningMark>();

        // The log's sequence: the writer continues it from the last Event stored.
        services.AddSingleton<HighWaterMark>(Event.HighWaterMark);

        // The controllers of this assembly, whichever assembly hosts the composition.
        services.AddKoanControllersFrom<TangentModule>();

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
        // The row records the base URL the operator configures; a start under another one moves it.
        var baseUrl = services.GetRequiredService<KoanWebBaseUrl>().Configured
            ?? throw new InvalidOperationException($"{BaseUrlKey} must be set to the base URL the Tangent Space answers at.");
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        if (space is null)
            await new TangentSpace { Name = "Tangent Space", BaseUrl = baseUrl }.Save(ct);
        else if (space.BaseUrl != baseUrl)
        {
            space.BaseUrl = baseUrl;
            space.Version++;
            await space.Save(ct);
        }

        await Roles.Seed(services.GetRequiredService<RoleCollection>(), ct);
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => ReportStarted(services));
    }

    private static void ReportStarted(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        services.GetRequiredService<ILogger<TangentModule>>().LogInformation(
            "Tangent Space at {BaseUrl}; listening on {Addresses}; data folder {DataFolder}",
            services.GetRequiredService<KoanWebBaseUrl>().Configured,
            string.Join(" ", addresses),
            options.DataFolderPath(contentRoot));
    }
}
