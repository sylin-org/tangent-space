using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TangentSpace.Mcp.Authentication;

namespace TangentSpace.Mcp;

/// <summary>
/// Registers the inbound Tangent MCP transport: the contract catalog, the durable participation
/// context store, the request registry, qualified references and the operation dispatcher. The
/// controller is discovered through ordinary Koan MVC discovery. Call after the domain and
/// authentication registrations (AddParticipation, AddTangentMcpAuthentication) are present.
/// </summary>
public static class McpRegistration
{
    public static IServiceCollection AddTangentMcp(this IServiceCollection services)
    {
        services.AddOptions<McpOptions>().BindConfiguration(McpOptions.Configuration)
            .Validate(options => McpOptionsValidation.IsCanonicalOrigin(options.PublicBaseUrl, out _),
                "Set Tangent:Mcp:PublicBaseUrl to a canonical absolute origin like https://tangent.example.")
            .Validate(options => options.RequestLimitBytes is >= 4096 and <= McpOptions.DefaultRequestLimitBytes,
                "Tangent:Mcp:RequestLimitBytes must be between 4 and 64 KiB.");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<McpContractCatalog>();
        services.TryAddSingleton<McpContexts>();
        services.TryAddSingleton<McpRequests>();
        services.TryAddSingleton<McpRefs>();
        services.TryAddSingleton<McpOperationDispatcher>();
        return services;
    }
}
