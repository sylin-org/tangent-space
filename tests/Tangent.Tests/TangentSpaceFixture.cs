using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent;

/// <summary>One Tangent Space under test: the application's own composition — <c>AddKoan()</c> over the
/// server's appsettings.json, so <see cref="TangentModule"/>, its options and its pipeline contributors
/// are the ones that run — in the Production environment the server runs in, on Kestrel at a free
/// loopback port, with a new temp folder as its content root, so the data folder and the database land
/// inside that folder. A test names any setting it needs; disposing the fixture stops the application
/// and deletes the folder.</summary>
/// <remarks>Koan keeps one process-default host, and a test process runs several Tangent Spaces at once.
/// Starting a fixture therefore binds Koan's ambient host to it for the calling test's flow
/// (<see cref="AppHost.PushScope"/>), and every request it serves runs bound to it the same way, so no
/// fixture reads or writes another's database.</remarks>
public sealed class TangentSpaceFixture : IAsyncDisposable
{
    private readonly WebApplication _application;
    private bool _stopped;
    private bool _disposed;

    private TangentSpaceFixture(WebApplication application, string folder, Uri address)
    {
        _application = application;
        Folder = folder;
        Client = new HttpClient { BaseAddress = address };
    }

    /// <summary>Sends requests to the loopback address the Tangent Space listens on.</summary>
    public HttpClient Client { get; }

    /// <summary>The temp folder that is the content root; everything the Tangent Space writes is inside it.</summary>
    public string Folder { get; }

    public IServiceProvider Services => _application.Services;

    public TangentSpaceOptions Options => Services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;

    public string DataFolder => Options.DataFolderPath(Folder);

    public string DatabasePath => Options.DatabasePath(Folder);

    /// <summary>Builds and starts a Tangent Space. <paramref name="settings"/> override the server's
    /// appsettings.json (<c>Tangent:BaseUrl</c> above all); <paramref name="services"/> adds registrations
    /// after the application's own. Not an async method, so the binding it makes stays on the caller's flow.</summary>
    public static Task<TangentSpaceFixture> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null,
        CancellationToken ct = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tangent-fixture-" + Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(folder);
        WebApplication application;
        try
        {
            application = Build(folder, settings, services);
        }
        catch
        {
            Directory.Delete(folder, recursive: true);
            throw;
        }

        // Undone when the calling test method completes; a later fixture in the same test rebinds.
        AppHost.PushScope(application.Services);
        return StartAsync(application, folder, ct);
    }

    private static WebApplication Build(string folder, IReadOnlyDictionary<string, string?>? settings, Action<IServiceCollection>? services)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = folder,
            EnvironmentName = Environments.Production
        });
        builder.Configuration
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false)
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // Registered before Koan's own startup filter, so the binding wraps the whole pipeline.
        builder.Services.AddSingleton<IStartupFilter, RequestBinding>();
        builder.Services.AddKoan();
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    private static async Task<TangentSpaceFixture> StartAsync(WebApplication application, string folder, CancellationToken ct)
    {
        try
        {
            await application.StartAsync(ct);
        }
        catch (Exception startFailure)
        {
            try
            {
                await application.DisposeAsync();
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("The Tangent Space failed to start, and so did its cleanup.", startFailure, cleanupFailure);
            }

            throw;
        }

        var address = application.Services.GetRequiredService<IServer>().Features
            .GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return new TangentSpaceFixture(application, folder, new Uri(address));
    }

    /// <summary>Stops the application and keeps the folder, so a test can read what it left behind; disposing
    /// still deletes the folder.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_stopped) return;
        _stopped = true;
        await _application.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Client.Dispose();
        try
        {
            await StopAsync(CancellationToken.None);
        }
        finally
        {
            await _application.DisposeAsync();
            Directory.Delete(Folder, recursive: true);
        }
    }

    /// <summary>Runs each request bound to the Tangent Space that serves it.</summary>
    private sealed class RequestBinding : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var services = app.ApplicationServices;
            app.Use(async (context, inner) =>
            {
                using (AppHost.PushScope(services)) await inner(context);
            });
            next(app);
        };
    }
}
