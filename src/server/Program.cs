using Koan.Core;

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
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
