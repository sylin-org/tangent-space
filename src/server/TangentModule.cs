using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core;
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
/// keeps the address it accepts, the Offers of an address, the HTML projection, first run's gate, and what every start
/// makes sure of: the tables, the roles and first run.</summary>
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
        services.AddSingleton<Koan.Mcp.Hosting.IMcpInstructions, ToolDoor.Instructions>();

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

        // What every row of a Post, a Tangent and the place holds, judged wherever it is saved: a command, a seed, a snapshot.
        Posts.Post.Lifecycle.BeforeUpsert(Posts.Post.Holds);
        Tangents.Tangent.Lifecycle.BeforeUpsert(Tangents.Tangent.Holds);
        TangentSpace.Lifecycle.BeforeUpsert(TangentSpace.Holds);

        // The Doorway judges every route that declares its bit, before anything is bound, and a 401 names the proof door.
        services.AddScoped<Koan.Web.Authorization.IAuthorizationProvider, Access.Doorway>();
        services.AddSingleton<Koan.Web.Authorization.IChallenge, Access.ProofChallenge>();

        // The controllers of this assembly, whichever assembly hosts the composition.
        services.AddKoanControllersFrom<TangentModule>();
        Participants.ParticipantsComposition.AddParticipants(services);
        services.AddSingleton<Connections.AtprotoAdapter>();
        services.AddSingleton<Connections.Nudge>();
        services.AddSingleton<Connections.Readers>();
        // What the network says about a published article: one bounded answer per read, no redirect, no cookie.
        services.AddHttpClient<Connections.AppView>(client => client.MaxResponseContentBufferSize = Connections.AppView.MaximumResponseBytes)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        LiveComposition.AddLive(services);

        // The HTML projection: each resource type names its component; the formatter goes first so a request
        // without a preference reads a page, and every other type keeps Koan's JSON.
        services.AddScoped<HtmlRenderer>();
        services.AddSingleton(new ResourceComponents()
            .Add<Door>(door => door.Name, Door.Version, Door.Frame, Door.Card)
            .Add<DoorPast>(page => "Older for you · " + page.Name, frame: DoorPast.Frame)
            .Add<Koan.Web.Controllers.Problem, Page>(Render.Parts.Refused.Heading)
            .Add<Participants.SignIn>(signIn => "Sign in · " + signIn.Name, frame: Participants.SignIn.Frame)
            .Add<Tangents.TangentPage>(page => page.Name + " · " + page.Place, Tangents.TangentPage.Version, Tangents.TangentPage.Frame, Tangents.TangentPage.Card)
            .Add<Tangents.TangentsPage>(page => "Tangents · " + page.Place, frame: Tangents.TangentsPage.Frame)
            .Add<Tangents.TangentOutside>(page => page.Tangent.Name + " · " + page.Place, frame: Tangents.TangentOutside.Frame)
            .Add<Tangents.NewTangentPage>(page => "New Tangent · " + page.Place, frame: Tangents.NewTangentPage.Frame)
            .Add<Tangents.TangentSettingsPage>(page => "Settings · " + page.Name, frame: Tangents.TangentSettingsPage.Frame)
            .Add<Tangents.TangentClosed>(page => "Saved · " + page.Name, frame: Tangents.TangentClosed.Frame)
            .Add<Tangents.TangentAccessPage>(page => "Access · " + page.Name, frame: Tangents.TangentAccessPage.Frame)
            .Add<Tangents.TangentPeoplePage>(page => "People · " + page.Name, frame: Tangents.TangentPeoplePage.Frame)
            .Add<Posts.TopicPage>(page => page.Title + " · " + page.Tangent.Name, Posts.TopicPage.Version, Posts.TopicPage.Frame, Posts.TopicPage.Card)
            .Add<Posts.NewTopicPage>(page => page.Origin is { } origin ? "Take this on a tangent · " + origin.Topic : (page.Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? "New article · " : "New topic · ") + page.TangentName, frame: Posts.NewTopicPage.Frame)
            .Add<Posts.EditPage>(page => (!page.Root ? "Edit post · " : page.Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? "Edit article · " : "Edit topic · ") + page.TopicTitle, frame: Posts.EditPage.Frame)
            .Add<Posts.PostHistory>(page => "Earlier versions · " + page.Topic, frame: Posts.PostHistory.Frame)
            .Add<Posts.MoveTopicPage>(page => "Move · " + page.Title, frame: Posts.MoveTopicPage.Frame)
            .Add<Posts.ReportPage>(page => (page.Received ? "Report received · " : "Report · ") + page.Reported.Topic, frame: Posts.ReportPage.Frame)
            .Add<Participants.ProfilePage>(page => page.Name + " · " + page.Place, Participants.ProfilePage.Version, Participants.ProfilePage.Frame)
            .Add<FirstRun>(firstRun => "Set up · " + firstRun.Name, frame: FirstRun.Frame)
            .Add<TangentSpaceSettingsPage>(page => "Settings · " + page.Place, frame: TangentSpaceSettingsPage.Frame)
            .Add<AdmissionSettingsPage>(page => "Admission · " + page.Place, frame: AdmissionSettingsPage.Frame)
            .Add<AppearanceSettingsPage>(page => "Appearance · " + page.Place, frame: AppearanceSettingsPage.Frame)
            .Add<Participants.ApplicationsPage>(page => "Applications · " + page.Place, frame: Participants.ApplicationsPage.Frame)
            .Add<Participants.PeoplePage>(page => "People · " + page.Place, frame: Participants.PeoplePage.Frame)
            .Add<RolesPage>(page => "Roles · " + page.Place, frame: RolesPage.Frame)
            .Add<Lifecycle.ExportSettingsPage>(page => "Export · " + page.Place, frame: Lifecycle.ExportSettingsPage.Frame)
            .Add<Posts.SearchPage>(page => (page.Text is { } text ? text + " · Search · " : "Search · ") + page.Place, frame: Posts.SearchPage.Frame)
            .Add<Participants.BringYourAgent>(page => "Bring your agent · " + page.Place, frame: Participants.BringYourAgent.Frame)
            .Add<Connections.ConnectionsPage>(page => "Connections · " + page.Place, frame: Connections.ConnectionsPage.Frame)
#if TANGENT_CAPTURE
            // The capture host's gallery of every part in its states.
            .Add<Capture.Gallery>(_ => "Parts", frame: Capture.Gallery.Frame)
#endif
        );
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
        PartComponents.Check(services.GetRequiredService<ResourceComponents>().Pages);
        await TablesExist(services.GetRequiredService<IDataService>(), ct);
        await Roles.Seed(services.GetRequiredService<RoleCollection>(), ct);
        await FirstRunStart.Run(services, ct);
        await Tangents.TallyTopics.AtStart(services.GetRequiredService<ILogger<Tangents.TallyTopics>>(), ct);
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
        await data.GetRepository<Posts.FormerAddress, string>().EnsureReady(ct);
        await data.GetRepository<Participants.Participant, string>().EnsureReady(ct);
        await data.GetRepository<Event, string>().EnsureReady(ct);
        await data.GetRepository<Checkpoint, string>().EnsureReady(ct);
        await data.GetRepository<Offers.Offer, string>().EnsureReady(ct);
        await data.GetRepository<Connections.Connection, string>().EnsureReady(ct);
        await data.GetRepository<Connections.Receipt, string>().EnsureReady(ct);
        await data.GetRepository<Connections.Publication, string>().EnsureReady(ct);
        await data.GetRepository<Connections.Elsewhere, string>().EnsureReady(ct);
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
