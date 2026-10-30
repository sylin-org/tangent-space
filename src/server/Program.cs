using Koan.Core;
using Tangent.Lifecycle;

if (LifecycleCommand.Parse(args) is { } lifecycle) return lifecycle.Run(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
if (builder.Configuration["urls"] is null
    && builder.Configuration["HTTP_PORTS"] is null
    && builder.Configuration["HTTPS_PORTS"] is null)
{
    builder.WebHost.UseUrls("http://localhost:5220");
}
if (Retirement.Compose(builder) is { } retired)
{
    await retired.RunAsync();
    return 0;
}

builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
return 0;
