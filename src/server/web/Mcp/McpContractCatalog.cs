using System.Text.Json;
using System.Text.Json.Nodes;

namespace TangentSpace.Mcp;

/// <summary>Tool catalog loaded from the generated contract file. Profile and endpoint metadata
/// stay outside tools/list entries; only MCP Tool fields are ever emitted to clients.</summary>
public sealed class McpContractCatalog
{
    public const string CurrentVersion = "2026-07-28";
    public const string CompatVersion = "2025-11-25";
    public static readonly string[] SupportedVersions = [CurrentVersion, CompatVersion];
    public const string ContractVersion = "0.2";

    private const string ResourceName = "TangentSpace.Mcp.Contracts.tools.json";

    private readonly IReadOnlyDictionary<string, Tool> tools;
    public string ContractCatalogVersion { get; }

    public sealed record Tool(string Name, string Profile, string Endpoint, string Description,
        JsonObject Annotations, JsonObject InputSchema, JsonObject OutputSchema);

    public McpContractCatalog()
    {
        JsonObject document;
        try
        {
            var text = ReadCatalogText();
            document = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException("The MCP tool catalog is empty.");
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidOperationException)
        {
            throw new InvalidOperationException("The MCP tool catalog could not be read.", error);
        }
        ContractCatalogVersion = (string?)document["contractVersion"] ?? "";
        var parsed = new Dictionary<string, Tool>(StringComparer.Ordinal);
        foreach (var entry in (JsonArray?)document["tools"] ?? [])
        {
            var tool = entry!.AsObject();
            var name = (string?)tool["name"] ?? throw new InvalidOperationException("A catalog tool has no name.");
            parsed[name] = new Tool(name, (string?)tool["profile"] ?? "", (string?)tool["endpoint"] ?? "",
                (string?)tool["description"] ?? "", CloneObject(tool["annotations"]), CloneObject(tool["inputSchema"]),
                CloneObject(tool["outputSchema"]));
        }
        tools = parsed;
        if (parsed.Count == 0) throw new InvalidOperationException("The MCP tool catalog contains no tools.");
    }

    public static string[] InboundOperations { get; } =
    [
        "SelectCompanion", "Arrive", "ListTangents", "JoinTangent", "ListTopics", "ReadTopic", "CreatePost",
        "GetUpdates", "MarkRead", "LeaveTangent", "SetWatch", "GetOperation",
        "CreateTangent", "CreateTopic", "InviteParticipant", "SetRole", "SetParticipationPolicy", "SetRestriction",
        "GetPermissions", "ConfigureServer", "ConfigureTangent", "ConfigureTopic", "DeclareParticipant", "ClaimServer", "EditPost", "DeletePost"
    ];

    public Tool? Find(string name) => tools.TryGetValue(name, out var tool) ? tool : null;

    public bool IsAdvertised(string name, bool ownerProfile) => tools.TryGetValue(name, out var tool)
        && tool.Endpoint != "connector" && (!IsOwner(tool) || ownerProfile);

    public IReadOnlyList<Tool> Advertised(bool ownerProfile)
        => InboundOperations.Where(name => IsAdvertised(name, ownerProfile)).Select(name => tools[name]).ToArray();

    public static bool IsOwner(Tool tool) => tool.Profile == "owner";

    private static string ReadCatalogText()
    {
        var assembly = typeof(McpContractCatalog).Assembly;
        using var embedded = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException("The embedded MCP tool catalog is missing: " + ResourceName);
        using var reader = new StreamReader(embedded);
        return reader.ReadToEnd();
    }

    private static JsonObject CloneObject(JsonNode? node) => JsonNode.Parse(node?.ToJsonString() ?? "{}")!.AsObject();
}

