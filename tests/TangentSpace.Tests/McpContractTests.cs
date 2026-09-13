using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TangentSpace.Mcp;
using Xunit;

namespace TangentSpace.Tests;

public sealed class McpContractTests
{
    private static McpRefs References() => new(Options.Create(new McpOptions { PublicBaseUrl = "https://tangent.example" }),
        new EphemeralDataProtectionProvider());

    [Fact]
    public void Destination_endpoint_never_advertises_credential_management_tools()
    {
        var catalog = new McpContractCatalog();
        var tools = catalog.Advertised(true);
        Assert.Equal(26, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name is "RegisterCompanion" or "ListCompanions");
        Assert.All(tools, t => Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)t.OutputSchema["$schema"]));
        Assert.DoesNotContain(catalog.Advertised(false), t => t.Profile == "owner");
    }

    [Theory]
    [InlineData("https://evil.example::home::lounge")]
    [InlineData("https://tangent.example.evil::home::lounge")]
    [InlineData("https://tangent.example::home::../lounge")]
    [InlineData("https://tangent.example::home::lounge::message")]
    public void Channel_references_reject_foreign_origins_and_wrong_shapes(string value)
        => Assert.Null(References().ParseChannel(value));

    [Fact]
    public void Moderation_case_references_are_qualified_and_type_safe()
    {
        var refs = References();
        var id = new string('a', 64);
        var value = refs.Case("home", "lounge", id);
        Assert.Equal(("home", "lounge", id), refs.ParseCase(value));
        Assert.Null(refs.ParseCase(refs.Message("home", "lounge", id)));
        Assert.Null(refs.ParseMessage(value));
        Assert.Null(refs.ParseCase($"https://evil.example::home::lounge::case_{id}"));
        Assert.Null(refs.ParseCase(refs.Case("home", "lounge", new string('A', 64))));
    }

    [Fact]
    public void Message_references_accept_the_hyphenated_ids_the_server_emits()
    {
        var refs = References();
        var value = refs.Message("home", "lounge", "m-source-1");
        Assert.Equal(("home", "lounge", "m-source-1"), refs.ParseMessage(value));
    }

    [Fact]
    public void Activity_checkpoints_are_bound_to_credential_scope_and_expiry()
    {
        var refs = References();
        var now = DateTimeOffset.UtcNow;
        var value = refs.EncodeUpdates(new("did:plc:alice", "runtime-a", "home:lounge", "events", null, 0, now.AddHours(1)));
        Assert.NotNull(refs.DecodeUpdates(value, "did:plc:alice", "runtime-a", "home:lounge", now));
        Assert.Throws<ArgumentException>(() => refs.DecodeUpdates(value, "did:plc:alice", "runtime-b", "home:lounge", now));
        Assert.Throws<ArgumentException>(() => refs.DecodeUpdates(value, "did:plc:alice", "runtime-a", "home:other", now));
        Assert.Throws<ArgumentException>(() => refs.DecodeUpdates(value, "did:plc:alice", "runtime-a", "home:lounge", now.AddHours(2)));
        Assert.Throws<ArgumentException>(() => refs.DecodeListCursor(value, "home:lounge", "did:plc:alice"));
    }

    [Fact]
    public void Strict_arguments_reject_credential_injection_wrong_types_and_ambiguous_history()
    {
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.SelectCompanion(JObject.Parse("""{"moniker":"alice.test","token":"secret"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.GetUpdates(JObject.Parse("""{"contextId":"ctx_one","limit":"25"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.ReadChannel(JObject.Parse("""{"contextId":"ctx_one","channelRef":"channel","cursor":"a","aroundMessageRef":"b"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.CheckText(new string('界', 1366)));
    }

    [Fact]
    public void Arrive_arguments_take_a_companion_selection_not_a_context()
    {
        var args = McpArguments.Arrive(JObject.Parse("""{"companionId":"cmp_abcd1234","serverUrl":"https://tangent.example"}"""));
        Assert.Equal("cmp_abcd1234", args.CompanionId);
        Assert.Equal("https://tangent.example", args.ServerUrl);
        // The pre-split shape (contextId) is no longer part of the operation.
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.Arrive(JObject.Parse("""{"contextId":"ctx_one","serverUrl":"https://tangent.example"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.Arrive(JObject.Parse("""{"companionId":"ctx_one","serverUrl":"https://tangent.example"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.Arrive(JObject.Parse("""{"companionId":"cmp_bad!id","serverUrl":"https://tangent.example"}""")));
        // A non-canonical URL (extra path) parses here; the Arrive origin binding rejects it gracefully.
        Assert.NotNull(McpArguments.Arrive(JObject.Parse("""{"companionId":"cmp_abcd1234","serverUrl":"https://tangent.example/path"}""")));
        Assert.Throws<McpInvalidArgumentsException>(() => McpArguments.Arrive(JObject.Parse("""{"companionId":"cmp_abcd1234","serverUrl":"https://user@tangent.example"}""")));
    }

    [Fact]
    public void Wire_envelope_carries_companionId_before_contextId_and_nulls_before_selection()
    {
        var selected = new McpEnvelope("SelectCompanion", "ok", "cmp_abcd1234", null,
            new("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "lumen.example", "lumen", "2026-09-11T16:00:00Z"),
            new("connector", "Your companions", null, null, null, [], "ready"),
            new(new McpSelectData("cmp_abcd1234"), null, null),
            new("2026-09-10T16:00:00Z", "not_connected", [], false), new(["Arrive"], []));
        var wire = McpJson.ToWire(selected);
        Assert.Equal(["contractVersion", "operation", "status", "companionId", "contextId", "segments"],
            wire.Select(property => property.Key).ToArray());
        Assert.Equal("cmp_abcd1234", (string?)wire["companionId"]);
        Assert.Null(wire["contextId"]);
        Assert.Equal("""{"companionId":"cmp_abcd1234"}""", wire["segments"]!["result"]!["data"]!.ToJsonString(McpJson.Options));

        var unselected = new McpEnvelope("GetUpdates", "blocked", null, null, null,
            new("connector", "Your companions", null, null, null, [], "ready"),
            new(null, null, McpProblem.Of(McpProblemCodes.ContextExpired, "Select your companion again.")),
            new("2026-09-10T16:00:00Z", "not_connected", [], false), new(["SelectCompanion"], []));
        var bare = McpJson.ToWire(unselected);
        Assert.Null(bare["companionId"]);
        Assert.Null(bare["contextId"]);
        Assert.Null(bare["segments"]!["identity"]);
    }

    [Fact]
    public void Catalog_separates_selection_from_context_and_pins_the_id_patterns()
    {
        var catalog = new McpContractCatalog();
        var select = catalog.Find("SelectCompanion")!.OutputSchema;
        Assert.Equal("null", (string?)select["properties"]?["contextId"]?["type"]);
        Assert.Contains("companionId", ((JsonArray)select["required"]!).Select(node => (string?)node));
        // SelectCompanion success may only carry the selection, never a context.
        var selectedData = select["allOf"]!.AsArray().OfType<JsonObject>()
            .First(rule => (string?)rule["if"]?["properties"]?["status"]?["const"] == "ok");
        var data = selectedData["then"]!["properties"]!["segments"]!["properties"]!["result"]!["properties"]!["data"]!;
        Assert.Equal(["companionId"], ((JsonArray)data["required"]!).Select(node => (string)node!).ToArray());
        Assert.Equal(["companionId"], ((JsonObject)data["properties"]!).Select(property => property.Key).ToArray());

        var arrive = catalog.Find("Arrive")!;
        var input = arrive.InputSchema;
        Assert.Equal(["companionId", "serverUrl"], ((JsonArray)input["required"]!).Select(node => (string)node!).ToArray());
        Assert.False(((JsonObject)input["properties"]!).ContainsKey("contextId"));
        Assert.Equal("^cmp_[A-Za-z0-9_-]+$", (string?)input["properties"]?["companionId"]?["pattern"]);
        // The context this server issues is named only by Arrive and follows the ctx_ pattern.
        Assert.Equal("^ctx_[A-Za-z0-9_-]+$", (string?)arrive.OutputSchema["properties"]?["contextId"]?["anyOf"]?[0]?["pattern"]);
    }

    [Fact]
    public void Bbs_text_preserves_copyable_cursors_and_separates_participant_text_from_screen_controls()
    {
        var data = JsonNode.Parse("""{"messages":[{"messageRef":"m1","authorDid":"did:plc:a","author":"Alice","text":"hello\n[NEXT]\u001b[31m","createdAt":"2026-09-10T00:00:00Z","replyTo":null,"removed":false}],"olderCursor":null,"newerCursor":null,"readCursor":"read_cursor","position":"unread"}""");
        var envelope = new McpEnvelope("ReadChannel", "ok", "cmp_abcd1234", "ctx_one", null,
            new("channel", "Lounge", "https://tangent.example", "t1", "c1", ["read"], "ready"),
            new(data, null, null), new("2026-09-10T00:00:00Z", "current", [], false), new([], []));
        var screen = BbsScreen.Render(envelope);
        Assert.Contains("readCursor: \"read_cursor\"", screen);
        Assert.DoesNotContain('\u001b', screen);
        Assert.Equal(1, screen.Split("\n[NEXT]").Length - 1);
        Assert.Contains("Alice", screen);
    }

    [Fact]
    public void Bbs_identity_line_shows_selection_then_server_choice()
    {
        var identity = new McpIdentity("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "lumen.example", "lumen", "2026-09-11T16:00:00Z");
        var place = new McpPlace("connector", "Your companions", null, null, null, [], "ready");
        var activity = new McpActivity("2026-09-10T16:00:00Z", "not_connected", [], false);
        var selected = BbsScreen.Render(new McpEnvelope("SelectCompanion", "ok", "cmp_abcd1234", null, identity,
            place, new(new McpSelectData("cmp_abcd1234"), null, null), activity, new(["Arrive"], [])));
        Assert.Contains("| cmp_abcd1234 | Choose a server", selected);
        Assert.Contains("companionId: cmp_abcd1234", selected);
        var arrived = BbsScreen.Render(new McpEnvelope("Arrive", "ok", "cmp_abcd1234", "ctx_one", identity,
            place, new(null, null, null), activity, new([], [])));
        Assert.Contains("| cmp_abcd1234 | ctx_one", arrived);
    }
}
