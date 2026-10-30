using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
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

/// <summary>Composes the Tangent Space on Koan: what the operator configured, the one data folder, where the place
/// keeps the address it accepts, the HTML projection, first run's gate, the delegated agent door, and what every start
/// makes sure of: the roles, first run, the pre-registered client and the Offers of an address.</summary>
public sealed class TangentModule : KoanModule
{
    /// <summary>The key Koan reads a configured base URL from, which wins over the address the place accepted
    /// (<see cref="PlaceAddress"/>). A path under a host is configured here, since an accepted address is an origin.</summary>
    public const string BaseUrlKey = "Koan:Web:ApplicationUrl";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TangentSpaceOptions>(TangentSpaceOptions.Configuration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.DataFolder),
                "Tangent:DataFolder must name a folder.");

        // The address the place accepts lives on its row; Koan reads it before the host serves.
        services.AddSingleton(provider => new PlaceAddress(provider.GetRequiredService<WriteLoop>(), PlaceAddress.DefaultBound));
        services.AddSingleton<IApplicationUrlStore>(provider => provider.GetRequiredService<PlaceAddress>());
        services.AddSingleton<Offers.AddressOffers>();

        services.AddOptions<SqliteOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((sqlite, tangent, environment) =>
                sqlite.ConnectionString = "Data Source=" + tangent.Value.DatabasePath(environment.ContentRootPath));

        // The data folder is marked in use while the Tangent Space runs, so a restore refuses it.
        services.AddHostedService<RunningMark>();

        // The log's sequence: the writer continues it from the last Event stored.
        services.AddSingleton<HighWaterMark>(Event.HighWaterMark);

        // The controllers of this assembly, whichever assembly hosts the composition.
        services.AddKoanControllersFrom<TangentModule>();
        Participants.ParticipantsComposition.AddParticipants(services);
        Delegates.DelegatesComposition.AddDelegates(services);
        LiveComposition.AddLive(services);

        // The HTML projection: each resource type names its component; the formatter goes first so a request
        // without a preference reads a page, and every other type keeps Koan's JSON.
        services.AddScoped<HtmlRenderer>();
        services.AddSingleton(new ResourceComponents()
            .Add<Door, DoorView>(door => door.Name, Door.Version, DoorView.Frame)
            .Add<Problem, ProblemView>(problem => problem.Title)
            .Add<Participants.SignIn, Participants.SignInView>(signIn => "Sign in · " + signIn.Name, frame: Participants.SignInView.Frame)
            .Add<Tangents.TangentPage, Tangents.TangentView>(page => page.Name + " · " + page.Place, Tangents.TangentPage.Version, Tangents.TangentView.Frame)
            .Add<Posts.TopicPage, Posts.TopicView>(page => page.Title + " · " + page.Tangent.Name, Posts.TopicPage.Version, Posts.TopicView.Frame)
            .Add<Posts.NewTopicPage, Posts.NewTopicView>(page => (page.Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? "New article · " : "New topic · ") + page.TangentName, frame: Posts.NewTopicView.Frame)
            .Add<Posts.EditPage, Posts.EditView>(page => (page.Root ? "Edit topic · " : "Edit post · ") + page.TopicTitle, frame: Posts.EditView.Frame)
            .Add<Posts.TakeOnATangentPage, Posts.TakeOnATangentView>(page => "Take this on a tangent · " + page.Origin.Topic, frame: Posts.TakeOnATangentView.Frame)
            .Add<Posts.ReportPage, Posts.ReportView>(page => (page.Received ? "Report received · " : "Report · ") + page.Reported.Topic, frame: Posts.ReportView.Frame)
            .Add<FirstRun, FirstRunView>(firstRun => "Set up · " + firstRun.Name, frame: FirstRunView.Frame)
            .Add<Delegates.Consent, Delegates.ConsentView>(consent => "Approve an agent · " + consent.Place, frame: Delegates.ConsentView.Frame));
        services.AddSingleton<HtmlFormatter>();
        services.AddOptions<MvcOptions>().Configure<HtmlFormatter>((mvc, html) =>
        {
            mvc.OutputFormatters.Insert(0, html);
            mvc.RespectBrowserAcceptHeader = true;
            mvc.Filters.Add<ETagFilter>();
            mvc.Filters.Add<FirstRunGate>();
        });
        services.WithExceptionHandler();
        services.AddExceptionHandler(handler => handler.ExceptionHandler = ProblemExceptionHandler.Answer);
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        await Roles.Seed(services.GetRequiredService<RoleCollection>(), ct);
        await FirstRunStart.Run(services, ct);
        await Delegates.DelegateClient.Seed(services, ct);
        await services.GetRequiredService<Offers.AddressOffers>().Start(ct);
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => ReportStarted(services));
    }

    private static void ReportStarted(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        services.GetRequiredService<ILogger<TangentModule>>().LogInformation(
            "Tangent Space at {BaseUrl}; listening on {Addresses}; data folder {DataFolder}",
            services.GetRequiredService<KoanWebBaseUrl>().Current ?? "each request's own origin",
            string.Join(" ", addresses),
            options.DataFolderPath(contentRoot));
    }
}
