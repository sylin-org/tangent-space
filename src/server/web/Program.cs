using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
var storageConfigurationPath = builder.Configuration["Tangent:Storage:ConfigurationPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "storage.json");
builder.Configuration.AddJsonFile(storageConfigurationPath, optional: true, reloadOnChange: false);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
