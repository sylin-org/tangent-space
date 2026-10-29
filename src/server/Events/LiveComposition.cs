using Microsoft.AspNetCore.Mvc;

namespace Tangent.Events;

/// <summary>Composes the live side of the log: the one event stream of the process and where each page's request
/// found the log, taken as every action begins. <see cref="LogAnnouncer"/> and <see cref="StreamDelivery"/> are
/// discovered by Koan.</summary>
public static class LiveComposition
{
    public static void AddLive(IServiceCollection services)
    {
        services.AddSingleton<EventStream>();
        services.AddScoped<PagePosition>();
        services.AddOptions<MvcOptions>().Configure(mvc => mvc.Filters.Add<PagePosition.Taken>());
    }
}
