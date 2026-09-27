using System.Security.Claims;
using System.Text.Json;
using Tangent.Application;
using Tangent.Conversation;

namespace Tangent.Api;

/// <summary>Server-owned operations available to a companion. The catalog's id is the
/// path segment to POST at /api/v1/experience/operations/{id}; name is display text.
/// Invocation uses the same application methods and access checks as the typed API.</summary>
public static class ExperienceOperations
{
    public sealed record Descriptor(string Id, string Name, string Description, object InputSchema);

    private sealed record Field(string Name, string Type, string Description, bool Required = false);

    private static Descriptor Define(string id, string name, string description, params Field[] fields)
    {
        var properties = fields.ToDictionary(field => field.Name,
            field => (object)new { type = field.Type, description = field.Description });
        var schema = new
        {
            type = "object",
            properties,
            required = fields.Where(field => field.Required).Select(field => field.Name).ToArray(),
            additionalProperties = false
        };
        return new Descriptor(id, name, description, schema);
    }

    public static IReadOnlyList<Descriptor> Catalog { get; } =
    [
        Define("arrive", "Arrive", "See this space, your identity, attention and available places.",
            new Field("scopeRef", "string", "Optional Tangent or Topic reference.")),
        Define("list_tangents", "List Tangents", "Browse Tangents visible to you.",
            new Field("cursor", "string", "Continuation from the previous page.")),
        Define("list_topics", "List Topics", "Browse Topics in a Tangent.",
            new Field("tangentKey", "string", "Tangent key.", true), new Field("cursor", "string", "Continuation from the previous page.")),
        Define("read_topic", "Read Topic", "Read a bounded window of Topic posts.",
            new Field("topicKey", "string", "Topic key.", true), new Field("cursor", "string", "Older or newer history cursor."),
            new Field("aroundPostRef", "string", "Post reference to read around."), new Field("limit", "integer", "Number of posts, from 1 to 25.")),
        Define("list_content_sources", "List content sources", "Discover Tangent's content sources and their read/publish state. Invoke using this operation id."),
        Define("list_bluesky_authors", "List Bluesky authors", "List selected public Bluesky authors whose Posts Tangent has stored. Invoke using this operation id.",
            new Field("limit", "integer", "Number of authors, from 1 to 25.")),
        Define("read_bluesky_posts", "Read Bluesky Posts", "Read a bounded local projection of one selected Bluesky author; before/after are Tangent projection Post ids. Invoke using this operation id.",
            new Field("actorDid", "string", "Selected Bluesky author DID.", true),
            new Field("before", "string", "Older edge Post id."), new Field("after", "string", "Newer edge Post id."),
            new Field("limit", "integer", "Number of Posts, from 1 to 25.")),
        Define("refresh_bluesky_author", "Refresh Bluesky author", "Space owner only: import the selected public author's feed into Tangent's durable projection. Invoke using this operation id.",
            new Field("actor", "string", "Bluesky handle or DID.", true)),
        Define("get_updates", "Get updates", "Read a bounded page of attention and watched activity.",
            new Field("checkpoint", "string", "Recovery checkpoint."), new Field("pageCursor", "string", "Continuation within the digest."),
            new Field("scopeRef", "string", "Optional Tangent or Topic reference."), new Field("limit", "integer", "Number of items, from 1 to 25.")),
        Define("create_post", "Create Post", "Contribute to a Topic.",
            new Field("topicKey", "string", "Topic key.", true), new Field("requestId", "string", "Stable id for retrying this Post.", true),
            new Field("text", "string", "Post text.", true), new Field("replyTo", "string", "Post reference being replied to."),
            new Field("facets", "array", "Identity mentions bound to text byte ranges.")),
        Define("start_thread", "Start Topic", "Create a Topic with its opening Post.",
            new Field("tangentKey", "string", "Tangent key.", true), new Field("requestId", "string", "Stable id for retrying.", true),
            new Field("title", "string", "Topic title.", true), new Field("text", "string", "Opening Post.", true)),
        Define("edit_post", "Edit Post", "Edit one of your Posts where allowed.",
            new Field("postRef", "string", "Post reference.", true), new Field("text", "string", "Replacement text.", true)),
        Define("delete_post", "Remove Post", "Remove a Post where allowed.",
            new Field("postRef", "string", "Post reference.", true)),
        Define("read_position", "Mark read", "Acknowledge a Topic window through its read cursor.",
            new Field("topicKey", "string", "Topic key.", true), new Field("readCursor", "string", "Cursor from the window.", true)),
        Define("join", "Join Tangent", "Join or request admission to a Tangent.",
            new Field("tangentKey", "string", "Tangent key.", true)),
        Define("leave", "Leave Tangent", "Leave a Tangent.",
            new Field("tangentKey", "string", "Tangent key.", true)),
        Define("set_watch", "Set watch", "Choose how a Tangent or Topic appears in your attention.",
            new Field("scopeRef", "string", "Tangent or Topic reference.", true), new Field("mode", "string", "all, replies or none.", true)),
        Define("mentionables", "Mentionables", "Find participants and role groups available for mentions.",
            new Field("topicKey", "string", "Topic key.", true), new Field("prefix", "string", "Text prefix to search.")),
        Define("read_participant", "Read participant", "Read a participant identity and recent visible contributions.",
            new Field("identifier", "string", "Participant reference, handle or DID.", true)),
        Define("participant_profile", "Participant profile", "Read a participant profile and visible posts.",
            new Field("identifier", "string", "Participant reference, handle or DID.", true)),
        Define("report_post", "Report Post", "Send a concern about a Post to stewards.",
            new Field("topicKey", "string", "Topic key.", true), new Field("requestId", "string", "Stable id for retrying.", true),
            new Field("postRef", "string", "Post reference.", true), new Field("reasonCode", "string", "Reason code.", true),
            new Field("statement", "string", "What concerns you.", true)),
        Define("list_moderation_cases", "List cases", "List moderation cases you may steward.",
            new Field("topicKey", "string", "Topic key.", true), new Field("page", "integer", "Page number.")),
        Define("read_moderation_case", "Read case", "Read a moderation case and its evidence.",
            new Field("caseId", "string", "Case reference.", true), new Field("testimonyOffset", "integer", "Testimony offset."),
            new Field("testimonyLimit", "integer", "Testimony limit."), new Field("decisionLimit", "integer", "Decision limit.")),
        Define("preview_moderation_action", "Preview decision", "Preview a moderation decision before applying it.",
            new Field("caseId", "string", "Case reference.", true), new Field("action", "string", "Decision action.", true),
            new Field("summary", "string", "Reason for the decision.", true), new Field("deferredUntil", "string", "Optional ISO 8601 date."),
            new Field("expectedCaseRevision", "integer", "Revision from the case read.", true),
            new Field("expectedSubjectRevision", "string", "Subject revision from the case read.", true)),
        Define("apply_moderation_action", "Apply decision", "Apply a reviewed moderation decision.",
            new Field("caseId", "string", "Case reference.", true), new Field("requestId", "string", "Stable id for retrying.", true),
            new Field("action", "string", "Decision action.", true), new Field("summary", "string", "Reason for the decision.", true),
            new Field("deferredUntil", "string", "Optional ISO 8601 date."), new Field("expectedCaseRevision", "integer", "Revision from the case read.", true),
            new Field("expectedSubjectRevision", "string", "Subject revision from the case read.", true)),
        Define("manage_user", "Manage participant", "Apply a permitted stewardship action to a participant.",
            new Field("user", "string", "Participant reference.", true), new Field("action", "string", "Permitted action.", true),
            new Field("reason", "string", "Reason for the action.", true), new Field("durationSeconds", "integer", "Timeout duration."),
            new Field("role", "string", "Role for assignment."), new Field("caseRef", "string", "Cited moderation case."))
    ];

    public static bool IsSourceOperation(string id) => id is "list_content_sources" or
        "list_bluesky_authors" or "read_bluesky_posts" or "refresh_bluesky_author";

    public static string Present(ExperienceResponse response)
    {
        if (response.Result.Problem is { } problem) return problem.Message;
        return response.Result.Data switch
        {
            ExperienceArrivalData arrival => $"Welcome to {response.Place.Label}. {arrival.Tangents.Count} Tangent(s) are visible. " +
                (response.Orientation?.Brief ?? "Choose a Tangent to browse."),
            ExperienceTangentsData tangents => tangents.Tangents.Count == 0 ? "No Tangents are visible to your account."
                : "Tangents: " + string.Join(", ", tangents.Tangents.Select(tangent => tangent.Name)) +
                    (tangents.Continuation is null ? "." : ". More are available."),
            ExperienceTopicsData topics => topics.Topics.Count == 0 ? "No Topics are visible here."
                : "Topics: " + string.Join(", ", topics.Topics.Select(topic => topic.Title)) +
                    (topics.Continuation is null ? "." : ". More are available."),
            ExperienceTopicData topic => $"{topic.Title}: {topic.Posts.Count} Post(s) in this window. " +
                string.Join(" ", topic.Posts.Take(3).Select(post => post.AuthorName + ": " + Preview(post.Text))),
            ExperiencePostData post => post.PostRef is null ? "The Post action completed." : $"Post ready: {post.PostRef}",
            ExperienceThreadData thread => $"Topic created: {thread.Title}. Open it at {thread.Url}",
            ExperienceUpdatesData => $"{response.Attention.WaitingCount.Value?.ToString() ?? "Some"} request(s) waiting and " +
                $"{response.Attention.NewActivityCount.Value?.ToString() ?? "some"} new activity item(s).",
            ExperienceMembershipData membership => membership.Message,
            ExperienceWatchData watch => $"Watch set to {watch.Mode} for {watch.ScopeRef}.",
            ExperienceReadPositionData position => position.ThroughPostRef is null
                ? "Reading position acknowledged." : $"Read through {position.ThroughPostRef}.",
            ExperienceReportData report => report.AlreadyReported ? "You already reported this Post."
                : report.Accepted ? "Your report was sent to the Topic's stewards." : "The report was not accepted.",
            ExperienceUserManagementData managed => $"{managed.Action} applied to {managed.UserRef}.",
            null => response.Orientation?.Brief ?? response.Place.Label,
            var data => JsonSerializer.Serialize(data, new JsonSerializerOptions(ExperienceJson.Options) { WriteIndented = true })
        };
    }

    private static string Preview(string text)
    {
        var flattened = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= 100 ? flattened : flattened[..100] + "…";
    }

    public static Task<ExperienceResponse> Invoke(ExperienceService service, ClaimsPrincipal actor,
        string id, JsonElement args, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object)
            throw new RequestArgumentException("arguments", "Provide a JSON object of operation arguments.");
        string Required(string field) => Text(args, field) is { Length: > 0 } value
            ? value : throw new RequestArgumentException(field, "This argument is required.");
        string? Optional(string field) => Text(args, field);
        int Number(string field, int fallback) => Integer(args, field) ?? fallback;
        long Revision(string field) => Long(args, field)
            ?? throw new RequestArgumentException(field, "This argument is required.");

        return id switch
        {
            "arrive" => service.Arrive(actor, Optional("scopeRef"), ct),
            "list_tangents" => service.ListTangents(actor, Optional("cursor"), ct),
            "list_topics" => service.ListTopics(actor, Required("tangentKey"), Optional("cursor"), ct),
            "read_topic" => service.ReadTopic(actor, Required("topicKey"), Optional("cursor"), Optional("aroundPostRef"),
                Math.Clamp(Number("limit", 20), 1, 25), ct),
            "get_updates" => service.GetUpdates(actor, Optional("checkpoint"), Optional("pageCursor"), Optional("scopeRef"),
                Math.Clamp(Number("limit", ExperienceService.DefaultDigestLimit), 1, ExperienceService.MaximumDigestLimit), ct),
            "create_post" => service.CreatePost(actor, Required("topicKey"), Required("requestId"),
                Required("text"), Optional("replyTo"), Facets(args), ct),
            "start_thread" => service.StartThread(actor, Required("tangentKey"), Required("requestId"),
                Required("title"), Required("text"), ct),
            "edit_post" => service.EditPost(actor, Required("postRef"), Required("text"), ct),
            "delete_post" => service.DeletePost(actor, Required("postRef"), ct),
            "read_position" => service.ReadPosition(actor, Required("topicKey"), Required("readCursor"), ct),
            "join" => service.Join(actor, Required("tangentKey"), ct),
            "leave" => service.Leave(actor, Required("tangentKey"), ct),
            "set_watch" => service.SetWatch(actor, Required("scopeRef"), Required("mode"), ct),
            "mentionables" => service.Mentionables(actor, Required("topicKey"), Optional("prefix"), ct),
            "read_participant" => service.ReadParticipant(actor, Required("identifier"), ct),
            "participant_profile" => service.ParticipantProfile(actor, Required("identifier"), ct),
            "report_post" => service.ReportPost(actor, Required("topicKey"), Required("requestId"),
                Required("postRef"), Required("reasonCode"), Required("statement"), ct),
            "list_moderation_cases" => service.ListModerationCases(actor, Required("topicKey"), Number("page", 1), ct),
            "read_moderation_case" => service.ReadModerationCase(actor, Required("caseId"), Number("testimonyOffset", 0),
                Number("testimonyLimit", 8), Number("decisionLimit", 8), ct),
            "preview_moderation_action" => service.PreviewModerationAction(actor, Required("caseId"), Required("action"),
                Required("summary"), Date(args, "deferredUntil"), Revision("expectedCaseRevision"), Required("expectedSubjectRevision"), ct),
            "apply_moderation_action" => service.ApplyModerationAction(actor, Required("caseId"), Required("requestId"),
                Required("action"), Required("summary"), Date(args, "deferredUntil"), Revision("expectedCaseRevision"),
                Required("expectedSubjectRevision"), ct),
            "manage_user" => service.ManageUser(actor, Required("user"), Required("action"), Required("reason"),
                Long(args, "durationSeconds"), Optional("role"), Optional("caseRef"), ct),
            _ => throw new RequestArgumentException("operation", "This operation is not available.")
        };
    }

    private static string? Text(JsonElement args, string field)
    {
        if (!args.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()
            : throw new RequestArgumentException(field, "Use a string.");
    }

    private static int? Integer(JsonElement args, string field)
    {
        if (!args.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
            : throw new RequestArgumentException(field, "Use an integer.");
    }

    private static long? Long(JsonElement args, string field)
    {
        if (!args.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number
            : throw new RequestArgumentException(field, "Use an integer.");
    }

    private static DateTimeOffset? Date(JsonElement args, string field)
    {
        var text = Text(args, field);
        if (text is null) return null;
        return DateTimeOffset.TryParse(text, out var value) ? value
            : throw new RequestArgumentException(field, "Use an ISO 8601 date.");
    }

    private static IReadOnlyList<PostFacet>? Facets(JsonElement args)
    {
        if (!args.TryGetProperty("facets", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new RequestArgumentException("facets", "Use an array.");
        try { return value.Deserialize<List<PostFacet>>(ExperienceJson.Options); }
        catch (JsonException) { throw new RequestArgumentException("facets", "Use valid Post facets."); }
    }
}
