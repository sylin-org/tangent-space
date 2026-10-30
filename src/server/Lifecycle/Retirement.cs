using Microsoft.Extensions.FileProviders;
using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>A retired Tangent Space: where the operator names an archive (<see cref="TangentSpaceOptions.Archive"/>), the
/// server is a static file host of that folder and nothing else, as any other host of it would be, so it takes no write by
/// having nowhere to take one: no Koan, no database, no writer, and the data folder untouched, so taking the setting away
/// brings the place back as it was. An address that holds a page answers it, its closing slash added as a static host
/// adds it, which its relative links count on; any other read answers not found, and any write that the place is
/// retired. Its path under a host is served as the base URL configures it.</summary>
public static class Retirement
{
    /// <summary>What answers that the process lives.</summary>
    public const string Health = "/health/live";

    /// <summary>The retired server, or null where no archive is named. An archive named and not there is no start at all,
    /// rather than a place that answers nothing.</summary>
    public static WebApplication? Compose(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(TangentSpaceOptions.Configuration).Get<TangentSpaceOptions>();
        if (string.IsNullOrWhiteSpace(options?.Archive)) return null;
        var folder = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.Archive));
        if (!File.Exists(Path.Combine(folder, "index.html")))
            throw new InvalidOperationException($"Tangent:Archive names {folder}, which holds no archive: its door, index.html, is not there.");

        var application = builder.Build();
        if (Uri.TryCreate(builder.Configuration[TangentModule.BaseUrlKey], UriKind.Absolute, out var address) && address.AbsolutePath.Length > 1)
            application.UsePathBase(address.AbsolutePath.TrimEnd('/'));
        var files = new PhysicalFileProvider(folder);
        application.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        application.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        application.Run(Answer);
        return application;
    }

    private static Task Answer(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = "GET, HEAD";
            response.ContentType = "application/problem+json";
            return response.WriteAsync("""{"status":405,"title":"This place is retired","detail":"It takes no writes; its pages stay readable at their addresses."}""");
        }

        if (request.Path.Equals(Health, StringComparison.Ordinal)) return Task.CompletedTask;
        response.StatusCode = StatusCodes.Status404NotFound;
        response.ContentType = "text/plain; charset=utf-8";
        return response.WriteAsync("This place is retired, and nothing is kept at this address.");
    }
}
