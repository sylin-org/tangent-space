using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tangent.Events;

/// <summary>Composes the live side of the log: the one event stream of the process, where each page's request
/// found the log, taken as every action begins, and the clock a page's reading of what reaches its reader is measured
/// by (<see cref="Readable.Clock"/>). <see cref="LogAnnouncer"/> and <see cref="StreamDelivery"/> are discovered by
/// Koan.</summary>
public static class LiveComposition
{
    public static void AddLive(IServiceCollection services)
    {
        services.TryAddKeyedSingleton(Readable.Clock, TimeProvider.System);
        services.AddSingleton<EventStream>();
        services.AddScoped<PagePosition>();
        services.AddOptions<MvcOptions>().Configure(mvc => mvc.Filters.Add<PagePosition.Taken>());
    }
}
