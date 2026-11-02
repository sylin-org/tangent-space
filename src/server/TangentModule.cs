using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Media.Web.Routing;
using Koan.Storage.Connector.Local;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Events;
using Tangent.Lifecycle;
using Tangent.Render;
using Tangent.Space;

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
        services.AddSingleton<IApplicationUrlStore, PlaceAddress>();
        services.AddSingleton<Offers.AddressOffers>();
        services.AddSingleton<ICommandWatcher>(provider => provider.GetRequiredService<Offers.AddressOffers>());

        services.AddOptions<SqliteOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((sqlite, tangent, environment) =>
                sqlite.ConnectionString = "Data Source=" + tangent.Value.DatabasePath(environment.ContentRootPath));

        // Koan's media and its renders live in the data folder, and its routes serve what reaches the reader.
        services.AddOptions<LocalStorageOptions>()
            .PostConfigure<IOptions<TangentSpaceOptions>, IHostEnvironment>((storage, tangent, environment) =>
                storage.BasePath = tangent.Value.DataFolderPath(environment.ContentRootPath));
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IMediaSource, Posts.GatedMedia>());

        // The data folder is marked in use while the Tangent Space runs, so a restore refuses it.
        services.AddHostedService<RunningMark>();

        // The controllers of this assembly, whichever assembly hosts the composition.
        services.AddKoanControllersFrom<TangentModule>();
        Participants.ParticipantsComposition.AddParticipants(services);
        Delegates.DelegatesComposition.AddDelegates(services);
        LiveComposition.AddLive(services);

        // The HTML projection: each resource type names its component; the formatter goes first so a request
        // without a preference reads a page, and every other type keeps Koan's JSON.
        services.AddScoped<HtmlRenderer>();
        services.AddSingleton(new ResourceComponents()
            .Add<Door, DoorView>(door => door.Name, Door.Version, DoorView.Frame, Door.Card)
            .Add<Problem, ProblemView>(problem => problem.Title)
            .Add<Participants.SignIn, Participants.SignInView>(signIn => "Sign in · " + signIn.Name, frame: Participants.SignInView.Frame)
            .Add<Tangents.TangentPage, Tangents.TangentView>(page => page.Name + " · " + page.Place, Tangents.TangentPage.Version, Tangents.TangentView.Frame, Tangents.TangentPage.Card)
            .Add<Tangents.TangentsPage, Tangents.TangentsView>(page => "Tangents · " + page.Place, frame: Tangents.TangentsView.Frame)
            .Add<Tangents.NewTangentPage, Tangents.NewTangentView>(page => "New Tangent · " + page.Place, frame: Tangents.NewTangentView.Frame)
            .Add<Tangents.TangentSettingsPage, Tangents.TangentSettingsView>(page => "Settings · " + page.Name, frame: Tangents.TangentSettingsView.Frame)
            .Add<Tangents.TangentClosed, Tangents.TangentClosedView>(page => "Saved · " + page.Name, frame: Tangents.TangentClosedView.Frame)
            .Add<Tangents.TangentAccessPage, Tangents.TangentAccessView>(page => "Access · " + page.Name, frame: Tangents.TangentAccessView.Frame)
            .Add<Posts.TopicPage, Posts.TopicView>(page => page.Title + " · " + page.Tangent.Name, Posts.TopicPage.Version, Posts.TopicView.Frame, Posts.TopicPage.Card)
            .Add<Posts.NewTopicPage, Posts.NewTopicView>(page => (page.Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? "New article · " : "New topic · ") + page.TangentName, frame: Posts.NewTopicView.Frame)
            .Add<Posts.EditPage, Posts.EditView>(page => (!page.Root ? "Edit post · " : page.Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? "Edit article · " : "Edit topic · ") + page.TopicTitle, frame: Posts.EditView.Frame)
            .Add<Posts.TakeOnATangentPage, Posts.TakeOnATangentView>(page => "Take this on a tangent · " + page.Origin.Topic, frame: Posts.TakeOnATangentView.Frame)
            .Add<Posts.ReportPage, Posts.ReportView>(page => (page.Received ? "Report received · " : "Report · ") + page.Reported.Topic, frame: Posts.ReportView.Frame)
            .Add<Participants.ProfilePage, Participants.ProfileView>(page => page.Name + " · " + page.Place, Participants.ProfilePage.Version, Participants.ProfileView.Frame)
            .Add<FirstRun, FirstRunView>(firstRun => "Set up · " + firstRun.Name, frame: FirstRunView.Frame)
            .Add<TangentSpaceSettingsPage, TangentSpaceSettingsView>(page => "Settings · " + page.Place, frame: TangentSpaceSettingsView.Frame)
            .Add<AdmissionSettingsPage, AdmissionSettingsView>(page => "Admission · " + page.Place, frame: AdmissionSettingsView.Frame)
            .Add<AppearanceSettingsPage, AppearanceSettingsView>(page => "Appearance · " + page.Place, frame: AppearanceSettingsView.Frame)
            .Add<Participants.ApplicationsPage, Participants.ApplicationsView>(page => "Applications · " + page.Place, frame: Participants.ApplicationsView.Frame)
            .Add<Participants.PeoplePage, Participants.PeopleView>(page => "People · " + page.Place, frame: Participants.PeopleView.Frame)
            .Add<RolesPage, RolesView>(page => "Roles · " + page.Place, frame: RolesView.Frame)
            .Add<Lifecycle.ExportSettingsPage, Lifecycle.ExportSettingsView>(page => "Export · " + page.Place, frame: Lifecycle.ExportSettingsView.Frame)
            .Add<NotificationsPage, NotificationsView>(page => "Notifications · " + page.Place, frame: NotificationsView.Frame)
            .Add<Posts.SearchPage, Posts.SearchView>(page => (page.Text is { } text ? text + " · Search · " : "Search · ") + page.Place, frame: Posts.SearchView.Frame)
            .Add<Delegates.Consent, Delegates.ConsentView>(consent => "Approve an agent · " + consent.Place, frame: Delegates.ConsentView.Frame)
            .Add<Delegates.BringYourAgent, Delegates.BringYourAgentView>(page => "Bring your agent · " + page.Place, frame: Delegates.BringYourAgentView.Frame));
        services.AddSingleton<HtmlFormatter>();
        services.AddOptions<MvcOptions>().Configure<HtmlFormatter>((mvc, html) =>
        {
            mvc.OutputFormatters.Insert(0, html);
            mvc.RespectBrowserAcceptHeader = true;
            mvc.ModelMetadataDetailsProviders.Add(new SentAsSent());
            mvc.Filters.Add<ETagFilter>();
            mvc.Filters.Add<FirstRunGate>();
            mvc.Filters.Add<PrivatePlaceGate>();
        });
        services.WithExceptionHandler();
        services.AddExceptionHandler(handler => handler.ExceptionHandler = ProblemExceptionHandler.Answer);
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        await TablesExist(services.GetRequiredService<IDataService>(), ct);
        await Roles.Seed(services.GetRequiredService<RoleCollection>(), ct);
        await FirstRunStart.Run(services, ct);
        await Delegates.DelegateClient.Seed(services, ct);
        await services.GetRequiredService<Offers.AddressOffers>().Start(ct);
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => ReportStarted(services));
    }

    /// <summary>Makes every table a command writes exist at boot, the audit partition's among them, since a table first
    /// used inside a hold makes every other flow that wants it wait for that hold (DATA-0166).</summary>
    private static async Task TablesExist(IDataService data, CancellationToken ct)
    {
        await data.GetRepository<TangentSpace, string>().EnsureReady(ct);
        await data.GetRepository<Tangents.Tangent, string>().EnsureReady(ct);
        await data.GetRepository<Posts.Post, string>().EnsureReady(ct);
        await data.GetRepository<Posts.MediaUse, string>().EnsureReady(ct);
        await data.GetRepository<Participants.Participant, string>().EnsureReady(ct);
        await data.GetRepository<Event, string>().EnsureReady(ct);
        await data.GetRepository<Checkpoint, string>().EnsureReady(ct);
        await data.GetRepository<Offers.Offer, string>().EnsureReady(ct);
        using (EntityContext.Partition(Posts.AuditPartition.Name)) await data.GetRepository<Posts.Post, string>().EnsureReady(ct);
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
