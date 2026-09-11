using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TangentSpace.Participation;

namespace TangentSpace.Mcp;

/// <summary>
/// The inbound MCP endpoint. Bearer-credential only: a browser cookie can never become MCP identity.
/// Responses carry the contract envelope in structuredContent and the deterministic BBS screen in text.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ParticipationConstants.Scheme)]
[Route("mcp")]
[RequestSizeLimit(McpOptions.DefaultRequestLimitBytes)]
public sealed class McpController(
    McpOperationDispatcher dispatcher,
    McpContractCatalog catalog,
    IOptions<McpOptions> options) : ControllerBase
{
    private readonly McpOptions settings = options.Value;

    public const string UnsupportedVersion = "This server supports MCP protocol versions: "
        + McpContractCatalog.CurrentVersion + ", " + McpContractCatalog.CompatVersion + ".";

    private const string Instructions = "Copy references and cursors from responses; never construct them. " +
        "Every response shows identity, place, result, nearby activity and safe next steps.";

    [HttpGet]
    [HttpDelete]
    public IActionResult MethodNotAllowed()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Allow = "POST";
        return StatusCode(StatusCodes.Status405MethodNotAllowed, new { error = "server_push_streams_not_supported" });
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] JObject? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || !ParticipationAccess.UsesCredential(User))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "mcp_requires_a_participant_credential" });
        if (!string.Equals(Request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { error = "use_application_json" });
        if (Request.ContentLength is { } declared && declared > settings.RequestLimitBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge, new { error = "request_too_large" });
        if (!CheckOrigin()) return StatusCode(StatusCodes.Status403Forbidden, new { error = "cross_origin_mcp_denied" });
        if (request is null)
            return Rpc(null, Error(-32700, "The request body must be a JSON-RPC request object."));
        if (request.Type == JTokenType.Array)
            return Rpc(null, Error(-32600, "Batch requests are not supported."));
        var method = request["method"]?.Type == JTokenType.String ? (string?)request["method"] : null;
        var id = RequestId(request["id"]);
        if (method is null) return Rpc(id, Error(-32600, "A JSON-RPC method string is required."));

        var validation = McpWire.Validate(request, Request.Headers);
        if (!validation.Valid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            var failure = (JObject)Error(validation.ErrorCode!.Value, validation.Error!);
            if (validation.Data is not null) failure["data"] = validation.Data;
            return Rpc(id, failure);
        }
        if (method == "notifications/initialized")
        {
            // A stateless server accepts notifications without side effects or a response body.
            Response.StatusCode = StatusCodes.Status202Accepted;
            return new EmptyResult();
        }

        var version = validation.Version;

        return method switch
        {
            "ping" => Ok(id, []),
            "initialize" => Initialize(request, id),
            "server/discover" => Discover(version, id),
            "tools/list" => await ToolsList(version, id, ct),
            "tools/call" => await ToolsCall(request, id, ct),
            _ => MissingMethod(id)
        };
    }

    private static JToken? RequestId(JToken? token)
        => token is null || token.Type == JTokenType.Null ? null : token;

    private static JToken Error(int code, string message) => new JObject { ["code"] = code, ["message"] = message };

    private IActionResult Rpc(JToken? id, JToken error)
        => Content(McpJson.Serialize(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Node(id),
            ["error"] = Node(error)
        }), "application/json");

    private IActionResult Ok(JToken? id, JsonObject result)
    {
        result["resultType"] = "complete";
        result["_meta"] = new JsonObject { ["io.modelcontextprotocol/serverInfo"] = ServerInfo() };
        return Content(McpJson.Serialize(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = Node(id), ["result"] = result }), "application/json");
    }

    private IActionResult MissingMethod(JToken? id)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Rpc(id, Error(-32601, "This RPC method is not supported."));
    }

    private static JsonNode? Node(JToken? token)
        => token is null ? null : JsonNode.Parse(token.ToString(Newtonsoft.Json.Formatting.None));

    private IActionResult Initialize(JObject request, JToken? id)
    {
        var requested = request["params"]?["protocolVersion"]?.Type == JTokenType.String
            ? (string?)request["params"]?["protocolVersion"] : null;
        if (requested != McpContractCatalog.CompatVersion)
            return Rpc(id, Error(-32020,
                "The stateless-compatible profile negotiates " + McpContractCatalog.CompatVersion + ". " + UnsupportedVersion));
        return Ok(id, new JsonObject
        {
            ["protocolVersion"] = McpContractCatalog.CompatVersion,
            ["serverInfo"] = ServerInfo(),
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["instructions"] = Instructions
        });
    }

    private IActionResult Discover(string version, JToken? id)
        => Ok(id, new JsonObject
        {
            ["supportedVersions"] = McpJson.StringArray(McpContractCatalog.SupportedVersions),
            ["ttlMs"] = 300000,
            ["cacheScope"] = "private",
            ["protocolVersion"] = version,
            ["serverInfo"] = ServerInfo(),
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["profiles"] = new JsonArray(
                Profile("daily", ["SelectCompanion", "Arrive", "ListTangents", "JoinTangent", "ListTopics", "ReadTopic", "CreatePost", "GetUpdates", "MarkRead", "CreateTangent", "CreateTopic", "EditPost", "DeletePost", "GetPermissions", "DeclareParticipant"]),
                Profile("control", ["LeaveTangent", "SetWatch", "GetOperation"]),
                Profile("owner", ["ConfigureServer", "ClaimServer", "ConfigureTangent", "ConfigureTopic", "InviteParticipant", "SetRole", "SetParticipationPolicy", "SetRestriction"])),
            ["authentication"] = new JsonObject
            {
                ["profile"] = "atproto_service_proof_exchange",
                ["tokenEndpoint"] = McpOptions.TokenEndpoint,
                ["discovery"] = McpOptions.WellKnownEndpoint,
                ["note"] = "Transport credentials are never tool arguments."
            },
            ["instructions"] = Instructions
        });

    private static JsonNode Profile(string name, string[] operations) => new JsonObject
    {
        ["profile"] = name,
        ["endpoint"] = "/mcp",
        ["operations"] = McpJson.StringArray(operations)
    };

    private static JsonObject ServerInfo() => new()
    {
        ["name"] = "Tangent inbound MCP",
        ["version"] = McpContractCatalog.ContractVersion,
        ["contractVersion"] = McpContractCatalog.ContractVersion
    };

    private async Task<IActionResult> ToolsList(string version, JToken? id, CancellationToken ct)
    {
        var owner = User.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage);
        var tools = new JsonArray();
        foreach (var tool in catalog.Advertised(owner).Where(tool => dispatcher.IsCallable(tool.Name, User)))
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.ToJsonString()),
                ["outputSchema"] = JsonNode.Parse(tool.OutputSchema.ToJsonString()),
                ["annotations"] = JsonNode.Parse(tool.Annotations.ToJsonString())
            });
        }
        var result = new JsonObject { ["tools"] = tools };
        if (version == McpContractCatalog.CurrentVersion)
        {
            result["ttlMs"] = 300000;
            result["cacheScope"] = "private";
        }
        return Ok(id, result);
    }

    private async Task<IActionResult> ToolsCall(JObject request, JToken? id, CancellationToken ct)
    {
        var parameters = request["params"] as JObject;
        var name = parameters?["name"]?.Type == JTokenType.String ? (string?)parameters?["name"] : null;
        if (name is null) return Rpc(id, Error(-32602, "tools/call requires a tool name string."));
        var arguments = parameters?["arguments"] as JObject;
        if (parameters?["arguments"] is { } supplied && arguments is null)
            return Rpc(id, Error(-32602, "arguments must be an object."));
        McpOperationDispatcher.ToolResult outcome;
        try
        {
            outcome = await dispatcher.Call(User, name, arguments, ct);
        }
        catch (McpUnknownToolException unknown)
        {
            return Rpc(id, Error(-32602, $"Unknown tool for this endpoint and authorization: {unknown.Name}."));
        }
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = outcome.Screen }),
            ["structuredContent"] = JsonNode.Parse(outcome.Json),
            ["isError"] = outcome.IsError,
            ["resultType"] = "complete"
        };
        return Ok(id, result);
    }

    /// <summary>The only accepted Origin is this server's canonical public origin.</summary>
    private bool CheckOrigin()
    {
        var origin = Request.Headers.Origin;
        if (origin.Count == 0) return true;
        if (!McpOptionsValidation.IsCanonicalOrigin(settings.PublicBaseUrl, out var canonical)) return false;
        return origin.Count == 1 && string.Equals(origin[0], canonical, StringComparison.OrdinalIgnoreCase);
    }
}
