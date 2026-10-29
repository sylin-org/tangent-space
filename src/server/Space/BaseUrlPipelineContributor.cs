using Koan.Web.Hosting;
using Microsoft.Extensions.Options;

namespace Tangent.Space;

/// <summary>Serves the Tangent Space under its base URL's path and nothing outside it.</summary>
public sealed class BaseUrlPipelineContributor(IOptions<TangentSpaceOptions> options) : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public int Order => -10;

    public void Configure(IApplicationBuilder app)
    {
        var basePath = new PathString(options.Value.BasePath);
        if (!basePath.HasValue) return;
        app.UsePathBase(basePath);
        app.Use((context, next) =>
        {
            if (context.Request.PathBase.StartsWithSegments(basePath)) return next(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
    }
}
