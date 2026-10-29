using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tangent.Participants;

/// <summary>A stand-in for the public profile service on a loopback port, recording every request it receives. Under
/// <c>/answer/</c> it answers a profile for the DID asked; under <c>/stall/</c> it never answers; under
/// <c>/overflow/</c> it answers without end. Each is a base address for <c>Tangent:Profile:BaseAddress</c>.</summary>
public sealed class ProfileStub : IAsyncDisposable
{
    public const string DisplayName = "Ada Lovelace";
    public const string Avatar = "https://cdn.example/avatar/ada.jpg";

    private readonly WebApplication _application;
    private readonly ConcurrentQueue<string> _requests = new();

    private ProfileStub(WebApplication application) => _application = application;

    /// <summary>The handle the stub answers for a DID.</summary>
    public static string HandleOf(string did) => did.Split(':')[^1] + ".example";

    public Uri Address { get; private set; } = default!;

    public Uri Answering => new(Address, "answer/");

    public Uri Stalling => new(Address, "stall/");

    public Uri Overflowing => new(Address, "overflow/");

    /// <summary>The DIDs asked for, one per request, in arrival order.</summary>
    public IReadOnlyCollection<string> Requests => _requests;

    public static async Task<ProfileStub> StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var application = builder.Build();
        var stub = new ProfileStub(application);
        const string read = "xrpc/app.bsky.actor.getProfile";

        application.MapGet("/answer/" + read, (HttpContext http) =>
        {
            var did = stub.Record(http);
            return Results.Json(new { did, handle = HandleOf(did), displayName = DisplayName, description = "Writes things.", avatar = Avatar });
        });
        application.MapGet("/stall/" + read, async (HttpContext http) =>
        {
            stub.Record(http);
            await Task.Delay(Timeout.Infinite, http.RequestAborted);
        });
        application.MapGet("/overflow/" + read, async (HttpContext http) =>
        {
            stub.Record(http);
            http.Response.ContentType = "application/json";
            var chunk = new byte[16 * 1024];
            Array.Fill(chunk, (byte)' ');
            while (!http.RequestAborted.IsCancellationRequested) await http.Response.Body.WriteAsync(chunk, http.RequestAborted);
        });

        await application.StartAsync(ct);
        var address = application.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        stub.Address = new Uri(address + "/");
        return stub;
    }

    private string Record(HttpContext http)
    {
        var did = http.Request.Query["actor"].ToString();
        _requests.Enqueue(did);
        return did;
    }

    public async ValueTask DisposeAsync()
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await _application.StopAsync(stopping.Token);
        }
        finally
        {
            await _application.DisposeAsync();
        }
    }
}
