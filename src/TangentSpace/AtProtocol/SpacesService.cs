using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol.Verification;
using TangentSpace.Rooms;

namespace TangentSpace.AtProtocol;

public sealed class SpacesService(AtprotoSessions sessions, AtprotoHttp network, SpacesVerifier verifier,
    RoomGovernance governance, IOptions<SpacesOptions> configured)
{
    private readonly SpacesOptions options = configured.Value;

    public async Task<RoomAdministrationResult> Provision(string actorDid, string roomKey, CancellationToken ct)
    {
        var preparation = await governance.BeginProvisioning(actorDid, roomKey, ct);
        if (!preparation.Accepted) return preparation;
        var space = options.Space(roomKey);
        using var body = JsonContent.Create(new Dictionary<string, object>
        {
            ["type"] = SpacesOptions.SpaceType, ["skey"] = roomKey,
            ["policy"] = new Dictionary<string, object> { ["$type"] = "com.atproto.simplespace.defs#managingAppPolicy", ["managingApp"] = options.ManagingApp },
            ["appAccess"] = new Dictionary<string, object> { ["$type"] = "com.atproto.simplespace.defs#open" }
        });
        using var created = await Own(options.AuthorityDid, "com.atproto.simplespace.createSpace", HttpMethod.Post, null, body, ct);
        var result = await Json(created, "create-space", ct, "SpaceAlreadyExists");
        if (created.IsSuccessStatusCode && result.GetProperty("uri").GetString() != space)
            throw new InvalidDataException("Created Space does not match the requested authority, type and key.");
        using var described = await Own(options.AuthorityDid, "com.atproto.simplespace.getSpace", HttpMethod.Get, Parameters(("space", space)), null, ct);
        var actual = await Json(described, "verify-space", ct);
        if (actual.GetProperty("uri").GetString() != space
            || actual.GetProperty("policy").GetProperty("$type").GetString() != "com.atproto.simplespace.defs#managingAppPolicy"
            || actual.GetProperty("policy").GetProperty("managingApp").GetString() != options.ManagingApp
            || actual.GetProperty("appAccess").GetProperty("$type").GetString() != "com.atproto.simplespace.defs#open")
            throw new InvalidDataException("The existing Space has a different admission configuration.");
        var mapped = await governance.MapSpace(actorDid, roomKey, preparation.PolicyRevision, space, ct);
        if (mapped.Accepted) SourceNotifications.RequestMaintenance();
        return mapped;
    }

    public async Task<JsonElement> CreateRecord(string authorDid, string space, string recordKey, JsonElement record, CancellationToken ct)
    {
        using var content = JsonContent.Create(new { space, repo = authorDid, collection = SpacesOptions.Collection, rkey = recordKey, record });
        using var response = await Own(authorDid, "com.atproto.space.createRecord", HttpMethod.Post, null, content, ct);
        return await Json(response, "write", ct, "RecordAlreadyExists");
    }

    public async Task<VerifiedSpaceRepo> ReadRepo(string readerDid, string space, string authorDid, CancellationToken ct)
    {
        using var response = await Read(readerDid, space, authorDid, "com.atproto.space.getRepo", null, ct);
        if (!response.IsSuccessStatusCode) { await Json(response, "read-repo", ct); throw new UnreachableException(); }
        // This response is from the guarded resolved expected PDS, never an uploaded CAR.
        return await verifier.Verify(await response.Content.ReadAsByteArrayAsync(ct), space, authorDid, ct);
    }

    public async Task<JsonElement> ListRepos(string readerDid, string space, string? cursor, CancellationToken ct)
    {
        var parameters = Parameters(("limit", "8"));
        if (cursor is not null) parameters["cursor"] = cursor;
        using var response = await Read(readerDid, space, null, "com.atproto.space.listRepos", parameters, ct);
        return await Json(response, "list-repos", ct);
    }

    public async Task<DateTimeOffset> RegisterNotifications(string space, CancellationToken ct)
    {
        using var body = JsonContent.Create(new { space, service = options.NotificationService });
        using var response = await Read(options.AuthorityDid, space, null, "com.atproto.space.registerNotify", null, ct, HttpMethod.Post, body);
        var value = await Json(response, "register-notifications", ct);
        return value.GetProperty("expiresAt").GetDateTimeOffset();
    }

    private async Task<HttpResponseMessage> Read(string readerDid, string space, string? authorDid, string nsid,
        Dictionary<string, string>? parameters, CancellationToken ct, HttpMethod? method = null, HttpContent? content = null)
    {
        using var delegated = await Own(readerDid, "com.atproto.space.getDelegationToken", HttpMethod.Get, Parameters(("space", space)), null, ct);
        var token = (await Json(delegated, "delegate", ct)).GetProperty("token").GetString()!;
        var authorityPds = (await sessions.ResolveDid(options.AuthorityDid, ct)).PdsEndpoint ?? throw new InvalidDataException("Authority has no PDS.");
        using var key = await DPoPKeyPair.GenerateAsync();
        var exchangeUrl = authorityPds.TrimEnd('/') + "/xrpc/com.atproto.space.getSpaceCredential";
        using var exchange = new HttpRequestMessage(HttpMethod.Post, exchangeUrl);
        exchange.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        exchange.Headers.Add("DPoP", await key.CreateProofAsync("POST", exchangeUrl));
        exchange.Content = JsonContent.Create(new { space });
        using var issued = await network.Send(exchange, ct);
        var credential = (await Json(issued, "credential", ct)).GetProperty("credential").GetString()!;
        var targetPds = authorDid is null ? authorityPds : (await sessions.ResolveDid(authorDid, ct)).PdsEndpoint ?? throw new InvalidDataException("Author has no PDS.");
        var query = parameters is null ? new Dictionary<string, string>() : new(parameters);
        query["space"] = space;
        if (authorDid is not null) query["repo"] = authorDid;
        var readUrl = targetPds.TrimEnd('/') + "/xrpc/" + nsid;
        var verb = method ?? HttpMethod.Get;
        using var request = new HttpRequestMessage(verb, verb == HttpMethod.Get
            ? readUrl + "?" + string.Join('&', query.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))) : readUrl);
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credential);
        request.Headers.Add("DPoP", await key.CreateProofAsync(verb.Method, readUrl, accessToken: credential));
        return await network.Send(request, ct);
    }

    private async Task<HttpResponseMessage> Own(string did, string method, HttpMethod verb, Dictionary<string, string>? parameters, HttpContent? content, CancellationToken ct)
    {
        try { return await sessions.Send(did, method, verb, parameters, content, ct); }
        catch (InvalidOperationException) { throw new SpacesUnavailable("session", "reauthorization-required", 409); }
        catch (OAuthException) { throw new SpacesUnavailable("session", "reauthorization-required", 409); }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, string stage, CancellationToken ct, string? allowedError = null)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var json = JsonSerializer.Deserialize<JsonElement>(bytes);
        if (!response.IsSuccessStatusCode)
        {
            var code = json.TryGetProperty("error", out var error) ? error.GetString() ?? "provider-error" : "provider-error";
            if (code != allowedError) throw new SpacesUnavailable(stage, code, (int)response.StatusCode);
        }
        return json;
    }

    private static Dictionary<string, string> Parameters(params (string Name, string Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
}
