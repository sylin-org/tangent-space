using System.Globalization;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Lifecycle;

/// <summary>What a visitor may read of the Tangent Space, rendered to a folder under its own URLs, so any static file host
/// serves it and the place's addresses keep working with no server: <c>{address}/index.html</c> for each page, a page's
/// continuations beside it as <c>page-{n}.html</c> or a Topic's windows as <c>{n}.html</c>, and each feed at its own address.
/// A Post's address forwards to its window. Every page reads as a visitor reads it live: the door and the profiles are
/// their own handlers' answers to a request with no reader, the rest <see cref="Export"/>'s walk with no reader, each read
/// bounded. No page offers anything, and each opens with the line that says the place is retired. A private place has
/// nothing to archive, and an applicant no page. What the folder holds is whole or absent (<see cref="FolderSink"/>).</summary>
public sealed class Archive
{
    private readonly Export _export;
    private readonly IServiceProvider _services;

    private Archive(Export export, IServiceProvider services)
    {
        _export = export;
        _services = services;
    }

    /// <summary>How many participants one read of them holds.</summary>
    private const int PeopleRead = 100;

    /// <summary>What a data folder's place renders to <paramref name="folder"/>, through a Koan host of its own that
    /// listens nowhere, and the base URL its pages name. A running Tangent Space keeps the folder to itself.</summary>
    public static async Task<string> Take(WebApplicationBuilder builder, string dataFolder, string folder)
    {
        var mark = RunningMark.Inspect(dataFolder);
        if (mark.InUse)
            throw new LifecycleRefusal($"The data folder {dataFolder} is in use by a running Tangent Space ({mark.Holder}). Stop it before archiving.");
        if (!File.Exists(Path.Combine(dataFolder, TangentSpaceOptions.DatabaseFile)))
            throw new LifecycleRefusal($"There is no Tangent Space in {dataFolder} to archive.");

        builder.Services.AddKoan();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IServer, Listening>();
        await using var application = builder.Build();
        await application.StartAsync();
        try
        {
            return await Write(folder, application.Services, application.Lifetime.ApplicationStopping);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    /// <summary>Renders the place to <paramref name="folder"/>, an empty or absent one, and answers the base URL its pages
    /// name. A place a visitor may not read is refused before anything is written, and a read that fails leaves
    /// nothing.</summary>
    public static async Task<string> Write(string folder, IServiceProvider services, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space)
            throw new LifecycleRefusal("This Tangent Space is not set up yet, so it has nothing to archive.");
        if (space.Audience == Audience.Private)
            throw new LifecycleRefusal("A private place has nothing to archive: a visitor reads none of it.");
        if (services.GetRequiredService<KoanWebBaseUrl>().Current is not { } baseAddress)
            throw new LifecycleRefusal($"This place has no address yet for its feeds and links to name: name it with --{TangentModule.BaseUrlKey}=<url>.");

        var files = FolderSink.Open(folder);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var line = "This place is retired. Its pages stay as they stood on "
                + DateTimeOffset.UtcNow.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + ".";
            var archive = new Archive(Export.Visitors(files, space, baseAddress, scope.ServiceProvider, line), scope.ServiceProvider);
            await archive._export.Assets(ct);
            await archive.Door(ct);
            await archive._export.Tangents(ct);
            await archive._export.Feed(Feeds.ArticlesPath.TrimStart('/'),
                await Feeds.OfArticles(await ViewableTangents.All(null, ct), space, baseAddress, ct), ct);
            await archive.Profiles(ct);
            files.Finish();
        }
        catch
        {
            files.Abandon();
            throw;
        }

        return baseAddress;
    }

    /// <summary>The door a visitor reads.</summary>
    private async Task Door(CancellationToken ct)
    {
        if (await As<DoorController>().Get(ct) is not OkObjectResult { Value: Door door })
            throw new InvalidOperationException("A visitor of a public place reads its door.");
        await _export.Page(Export.First, door with { Address = Export.First + ".html", Can = [], SignIn = null, Gate = null, Day = Export.Undated }, ".", null, ct);
    }

    /// <summary>Everyone admitted to the place, a bounded read at a time by Id, which never changes: each one's profile
    /// and its older Posts as a visitor reads them.</summary>
    private async Task Profiles(CancellationToken ct)
    {
        var after = "";
        while (true)
        {
            var query = new QueryDefinition
            {
                Filter = Filter.On(FieldPath.Of(nameof(Participant.Id)), FilterOperator.Gt, FilterValue.Of(after)),
                Page = 1,
                PageSize = PeopleRead
            }.WithSort<Participant>(sort => sort.OrderBy(participant => participant.Id));
            var rows = await Participant.Query(participant => participant.Standing == Standing.Admitted
                || participant.Standing == Standing.TimedOut || participant.Standing == Standing.Banned, query, ct);
            foreach (var row in rows) await Profile(row, ct);
            if (rows.Count < PeopleRead) return;
            after = rows[^1].Id;
        }
    }

    /// <summary>A participant's profile at its address, and their DID's address leading to it. A DID's colons name no file
    /// on Windows, so an archive rendered there leaves those addresses out.</summary>
    private async Task Profile(Participant row, CancellationToken ct)
    {
        var address = row.Handle.Length > 0 ? row.Handle : row.Id;
        if (!Nameable(address)) return;
        var folder = "p/" + address;
        string? before = null;
        var page = 1;
        do
        {
            if (await As<ProfileController>().Get(address, before, null, ct) is not OkObjectResult { Value: ProfilePage read }) return;
            before = read.Older is { } older ? Uri.UnescapeDataString(older[(older.IndexOf('=') + 1)..]) : null;
            await _export.Page(folder + "/" + Export.PagePath(page), read with
            {
                Address = Export.First + ".html",
                Can = [],
                Gate = null,
                Older = before is null ? null : Export.PagePath(page + 1) + ".html",
                Door = "../../" + Export.First + ".html",
                SignIn = null,
                Day = Export.Undated
            }, "../..", null, ct);
            page++;
        } while (before is not null);

        if (address != row.Id && Nameable(row.Id))
            await _export.Forward("p/" + row.Id, "../" + Uri.EscapeDataString(address) + "/" + Export.First + ".html", address, ct);
    }

    private static bool Nameable(string segment) => segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>A page's own handler, reading for no one in particular.</summary>
    private TController As<TController>() where TController : ControllerBase
    {
        var controller = ActivatorUtilities.CreateInstance<TController>(_services);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = _services } };
        return controller;
    }

    /// <summary>What a host takes for a server where nothing is to listen.</summary>
    private sealed class Listening : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken ct) where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
