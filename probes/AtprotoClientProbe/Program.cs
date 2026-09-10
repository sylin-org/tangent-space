using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.Http;
using CarpaNet.Repo;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5180");
// Callback queries contain authorization codes; do not enable request logging in this probe.
builder.Logging.ClearProviders();
var stateDirectory = Path.Combine(builder.Environment.ContentRootPath, ".state");
var protection = builder.Services.AddDataProtection()
    .SetApplicationName("Tangent.AtprotoClientProbe")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(stateDirectory, "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddSingleton(sp => new ProtectedStore(sp.GetRequiredService<IDataProtectionProvider>(), stateDirectory));
builder.Services.AddSingleton<NativeProbe>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
    { context.Response.StatusCode = 403; return; }
    try { await next(context); }
    catch (ProbeRejected e) { await Results.BadRequest(new { error = e.Message }).ExecuteAsync(context); }
    catch (OAuthException e) { await Results.BadRequest(new { error = e.ErrorCode, type = e.GetType().Name }).ExecuteAsync(context); }
    catch (Exception e) { await Results.Json(new { error = "probe_failed", type = e.GetType().Name }, statusCode: 500).ExecuteAsync(context); }
});
app.MapGet("/", () => new { probe = "native-atproto", sdk = NativeProbe.Sdk, disposable = true });
app.MapPost("/oauth/start", async (StartRequest request, NativeProbe probe, CancellationToken ct) =>
    new { authorizationUrl = await probe.Start(request, ct) });
app.MapGet("/oauth/callback", async (HttpRequest request, NativeProbe probe, CancellationToken ct) =>
    await probe.Callback(request, ct));
app.MapGet("/sessions", (ProtectedStore store) => store.Describe());
app.MapPost("/sessions/refresh", async (RefreshRequest request, NativeProbe probe, CancellationToken ct) =>
    await probe.Refresh(request, ct));
app.MapPost("/xrpc", async (RpcRequest request, NativeProbe probe, CancellationToken ct) => await probe.Rpc(request, ct));
app.MapPost("/spaces/read", async (SpaceReadRequest request, NativeProbe probe, CancellationToken ct) => await probe.ReadSpace(request, ct));
app.Run();

public sealed record StartRequest(string Did, string Pds, string? Scope);
public sealed record DidRequest(string Did);
public sealed record RefreshRequest(string Did, bool ExpireForProbe = false);
public sealed record RpcRequest(string Did, string Nsid, string Method = "GET", Dictionary<string, string>? Parameters = null, JsonElement? Body = null);
public sealed record LoginBinding(string Did, string Pds, string Issuer, string Scope);
public sealed record SpaceReadRequest(string Did, string Space, string? Repo = null, string Nsid = "com.atproto.space.getRecord", Dictionary<string, string>? Parameters = null);
public sealed class ProbeRejected(string reason) : Exception(reason);

public sealed class NativeProbe(ProtectedStore store)
{
    public const string Sdk = "CarpaNet.OAuth/1.1.0-alpha.5";
    private const string Redirect = "http://127.0.0.1:5180/oauth/callback";
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly ConcurrentDictionary<string, ATProtoOAuthClient> clients = new();
    private static readonly string[] PdsHosts = ["http://localhost:2583", "http://localhost:2584"];
    private IdentityResolver Resolver() => new(http, plcDirectoryUrl: "http://localhost:2582");
    private AuthorizationServerDiscovery Discovery() => new(http, cacheTtl: TimeSpan.Zero);

    private OAuthSession Session(string scope) => new(new OAuthClientConfig
    {
        ClientId = "http://localhost?redirect_uri=" + Uri.EscapeDataString(Redirect) + "&scope=" + Uri.EscapeDataString(scope),
        RedirectUri = Redirect, Scope = scope, HttpClient = http,
        StateStore = store, SessionStore = store, IdentityResolver = Resolver()
    });

    private static string AllowedPds(string value)
    {
        if (!PdsHosts.Contains(value, StringComparer.Ordinal)) throw new ProbeRejected("test_pds_required");
        return value;
    }

    private async Task<(string Pds, string Issuer)> Resolve(string did, CancellationToken ct)
    {
        if (!did.StartsWith("did:plc:", StringComparison.Ordinal)) throw new ProbeRejected("test_plc_did_required");
        using var resolver = Resolver();
        var document = await resolver.ResolveAsync(did, ct);
        if (!string.Equals(document.Id, did, StringComparison.Ordinal)) throw new ProbeRejected("resolved_did_mismatch");
        var pds = AllowedPds(document.PdsEndpoint ?? "");
        using var discovery = Discovery();
        var issuer = AllowedPds(await discovery.DiscoverAuthorizationServerAsync(pds, ct));
        var metadata = await discovery.GetMetadataAsync(issuer, ct);
        if (!string.Equals(metadata.Issuer, issuer, StringComparison.Ordinal)) throw new ProbeRejected("metadata_issuer_mismatch");
        foreach (var endpoint in new[] { metadata.AuthorizationEndpoint, metadata.TokenEndpoint, metadata.PushedAuthorizationRequestEndpoint })
            if (string.IsNullOrEmpty(endpoint) || new Uri(endpoint).GetLeftPart(UriPartial.Authority) != issuer)
                throw new ProbeRejected("unexpected_oauth_endpoint");
        return (pds, issuer);
    }

    public async Task<string> Start(StartRequest request, CancellationToken ct)
    {
        var (pds, issuer) = await Resolve(request.Did, ct);
        if (pds != request.Pds) throw new ProbeRejected("requested_pds_mismatch");
        var scope = request.Scope ?? "atproto";
        if (!scope.Split(' ').Contains("atproto")) throw new ProbeRejected("atproto_scope_required");
        var binding = new LoginBinding(request.Did, pds, issuer, scope);
        using var oauth = Session(scope);
        return await oauth.AuthorizeAsync(request.Did, JsonSerializer.Serialize(binding), ct);
    }

    public async Task<object> Callback(HttpRequest request, CancellationToken ct)
    {
        if (request.Query["state"].Count != 1) throw new ProbeRejected("invalid_state_count");
        var state = request.Query["state"].ToString();
        var stored = store.Peek(state) ?? throw new ProbeRejected("invalid_or_expired_state");
        var binding = JsonSerializer.Deserialize<LoginBinding>(stored.AppState!)!;
        if (request.Query["iss"].Count != 1 || request.Query["iss"].ToString() != stored.Issuer || stored.Issuer != binding.Issuer)
        { await store.ConsumeAsync(state, ct); throw new ProbeRejected("callback_issuer_mismatch"); }
        var oauth = Session(binding.Scope);
        var client = await oauth.CallbackAsync(Redirect + request.QueryString, ct);
        try
        {
            var (pds, issuer) = await Resolve(client.Did, ct);
            if (client.Did != binding.Did || pds != binding.Pds || issuer != binding.Issuer || client.BaseUrl.ToString().TrimEnd('/') != pds)
                throw new ProbeRejected("authenticated_subject_binding_mismatch");
            clients[client.Did] = client;
            return new { authenticated = true, did = client.Did, pds, sdk = Sdk };
        }
        catch { await store.DeleteAsync(client.Did, ct); client.Dispose(); oauth.Dispose(); throw; }
    }

    private async Task<ATProtoOAuthClient> Client(string did, CancellationToken ct)
    {
        if (clients.TryGetValue(did, out var client)) return client;
        var stored = await store.GetAsync(did, ct) ?? throw new ProbeRejected("session_required");
        var (pds, issuer) = await Resolve(did, ct);
        if (stored.TokenSet.Sub != did || stored.TokenSet.Audience.TrimEnd('/') != pds || stored.TokenSet.Issuer != issuer)
            throw new ProbeRejected("restored_subject_binding_mismatch");
        client = await Session(stored.Scope ?? "atproto").RestoreSessionAsync(did, ct) ?? throw new ProbeRejected("session_restore_failed");
        clients[did] = client;
        return client;
    }

    public async Task<object> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var client = await Client(request.Did, ct);
        var provider = (DPoPTokenProvider)client.TokenProvider;
        var previousToken = provider.AccessToken;
        if (request.ExpireForProbe) store.ExpireForProbe(request.Did);
        await provider.RefreshAsync(ct);
        return new { refreshed = previousToken != provider.AccessToken, did = request.Did };
    }

    public async Task<object> Rpc(RpcRequest request, CancellationToken ct)
    {
        if (!(request.Nsid.StartsWith("com.atproto.space.", StringComparison.Ordinal) || request.Nsid.StartsWith("com.atproto.simplespace.", StringComparison.Ordinal)))
            throw new ProbeRejected("space_xrpc_required");
        if (request.Method is not ("GET" or "POST")) throw new ProbeRejected("invalid_method");
        var client = await Client(request.Did, ct);
        using var transport = new HttpClient(new ATProtoDPoPAuthHandler((DPoPTokenProvider)client.TokenProvider, new HttpClientHandler { AllowAutoRedirect = false }));
        var url = client.BaseUrl.ToString().TrimEnd('/') + "/xrpc/" + request.Nsid;
        if (request.Parameters is { Count: > 0 }) url += "?" + string.Join("&", request.Parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), url);
        if (request.Body is { } body) message.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        using var response = await transport.SendAsync(message, ct);
        return await DescribeResponse(response, ct);
    }

    public async Task<object> ReadSpace(SpaceReadRequest input, CancellationToken ct)
    {
        if (!input.Space.StartsWith("at://", StringComparison.Ordinal) || !input.Space.Contains("/space/local.tangent.room/", StringComparison.Ordinal))
            throw new ProbeRejected("test_space_required");
        if (input.Nsid is not ("com.atproto.space.getRecord" or "com.atproto.space.listRecords" or "com.atproto.space.listRepos" or "com.atproto.space.getRepo"))
            throw new ProbeRejected("unsupported_space_read");
        var authorityDid = input.Space[5..].Split('/')[0];
        var (authorityPds, _) = await Resolve(authorityDid, ct);
        var client = await Client(input.Did, ct);
        // The SDK owns OAuth refresh, DPoP key generation, ES256 signing, and ath hashing.
        // The current experimental Spaces exchange is application-level request composition.
        using var oauthTransport = new HttpClient(new ATProtoDPoPAuthHandler((DPoPTokenProvider)client.TokenProvider, new HttpClientHandler { AllowAutoRedirect = false }));
        var delegationUrl = client.BaseUrl.ToString().TrimEnd('/') + "/xrpc/com.atproto.space.getDelegationToken?space=" + Uri.EscapeDataString(input.Space);
        using var delegation = await oauthTransport.GetAsync(delegationUrl, ct);
        if (!delegation.IsSuccessStatusCode) return new { stage = "delegation", response = await DescribeResponse(delegation, ct) };
        using var delegationBody = JsonDocument.Parse(await delegation.Content.ReadAsByteArrayAsync(ct));
        var token = delegationBody.RootElement.GetProperty("token").GetString()!;
        using var key = await DPoPKeyPair.GenerateAsync();
        var exchangeUrl = authorityPds + "/xrpc/com.atproto.space.getSpaceCredential";
        using var exchange = new HttpRequestMessage(HttpMethod.Post, exchangeUrl);
        exchange.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        exchange.Headers.Add("DPoP", await key.CreateProofAsync("POST", exchangeUrl));
        exchange.Content = JsonContent.Create(new { space = input.Space });
        using var credentialResponse = await http.SendAsync(exchange, ct);
        if (!credentialResponse.IsSuccessStatusCode) return new { stage = "credential", response = await DescribeResponse(credentialResponse, ct) };
        using var credentialBody = JsonDocument.Parse(await credentialResponse.Content.ReadAsByteArrayAsync(ct));
        var credential = credentialBody.RootElement.GetProperty("credential").GetString()!;
        var targetPds = input.Repo is { } repo ? (await Resolve(repo, ct)).Pds : authorityPds;
        var parameters = input.Parameters is null ? new Dictionary<string, string>() : new(input.Parameters);
        parameters["space"] = input.Space;
        if (input.Repo is not null) parameters["repo"] = input.Repo;
        var readUrl = targetPds + "/xrpc/" + input.Nsid;
        using var read = new HttpRequestMessage(HttpMethod.Get, readUrl + "?" + string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
        read.Headers.Authorization = new AuthenticationHeaderValue("DPoP", credential);
        read.Headers.Add("DPoP", await key.CreateProofAsync("GET", readUrl, accessToken: credential));
        using var result = await http.SendAsync(read, ct);
        return new { stage = "read", credentialIssued = true, response = await DescribeResponse(result, ct) };
    }

    private static async Task<object> DescribeResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == "application/json")
        {
            var body = JsonSerializer.Deserialize<JsonElement>(bytes);
            if (body.TryGetProperty("token", out _) || body.TryGetProperty("credential", out _))
                return new { status = (int)response.StatusCode, credentialPresent = true };
            return new { status = (int)response.StatusCode, body };
        }
        if (mediaType == "application/vnd.ipld.car" && response.IsSuccessStatusCode)
        {
            // Parsing is evidence of format compatibility only; this SDK has no Spaces verifier.
            using var car = new CarReader(bytes);
            var rootCount = car.Header.Roots.Count;
            var repository = Repository.Load(bytes);
            return new
            {
                status = (int)response.StatusCode, mediaType, bytes = bytes.Length,
                carRoots = rootCount, blocks = repository.BlockCount,
                sdkPublicRepositoryVersion = repository.Version,
                sdkPublicRepositoryRecords = repository.Records.Count,
                verified = false, gap = "SDK public-repo MST parser does not verify Spaces commitment and index"
            };
        }
        return new { status = (int)response.StatusCode, mediaType, bytes = bytes.Length };
    }
}
