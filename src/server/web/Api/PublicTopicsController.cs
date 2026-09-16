using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Community;
using Tangent.Infrastructure;
using Tangent.Application;

namespace Tangent.Api;

/// <summary>An unlisted direct-read mount. Koan constrains the row; this controller only narrows its route and projection.</summary>
[ApiController, AllowAnonymous, Route("api/v1/public/tangents/{tangentId}/topics")]
public sealed class PublicTopicsController(PublicConversationReader reader, PolicyGate policyGate) : EntityController<Topic, string>
{
    protected override QueryOptions BuildOptions()
    {
        var options = base.BuildOptions();
        var tangentKey = RouteData.Values["tangentId"]?.ToString() ?? "";
        options.AddPredicate<Topic>(topic => topic.TangentKey == tangentKey);
        return options;
    }

    protected override ObjectResult PrepareResponse(object? content)
        => base.PrepareResponse(content is Topic topic ? PublicTopicDescription.From(topic) : content);

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var response = await base.GetById(id, ct);
        if (response is NotFoundResult || response is ObjectResult { StatusCode: StatusCodes.Status404NotFound })
            return HiddenNotFound();
        if (response is not ObjectResult { Value: PublicTopicDescription topic }) return response;
        using var fresh = EntityContext.NoCache();
        return await Community.Tangent.Get(topic.TangentKey, ct) is null ? HiddenNotFound() : response;
    }

    [HttpGet("{id}/posts")]
    public async Task<IActionResult> GetPosts(string id, [FromQuery] long? before = null, [FromQuery] long? after = null,
        [FromQuery] string? around = null, [FromQuery] int limit = PublicConversationReader.MaximumPosts,
        CancellationToken ct = default)
    {
        await policyGate.Enter(ct);
        try
        {
            // Resolve through Koan first: failed credentials must win before caller query validation or data work.
            var topicResponse = await GetById(id, ct);
            if (topicResponse is not ObjectResult { Value: PublicTopicDescription topic }) return topicResponse;
            try
            {
                var window = await reader.ReadPostsUnderGate(topic, before, after, around, limit, ct);
                return window is null ? HiddenNotFound() : Ok(window);
            }
            catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
        }
        finally { policyGate.Exit(); }
    }

    [HttpGet("")]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpPost("query")]
    public override Task<IActionResult> Query(object body, CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpGet("new")]
    public override Task<IActionResult> GetNew(CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpPost("")]
    public override Task<IActionResult> Upsert(Topic model, CancellationToken ct) => ReadOnly();

    [HttpPut("{id}")]
    public override Task<IActionResult> Put(string id, CancellationToken ct) => ReadOnly();

    [HttpPost("bulk")]
    public override Task<IActionResult> UpsertMany(IEnumerable<Topic> models, CancellationToken ct) => ReadOnly();

    [HttpDelete("{id}")]
    public override Task<IActionResult> Delete(string id, CancellationToken ct) => ReadOnly();

    [HttpDelete("bulk")]
    public override Task<IActionResult> DeleteMany(IEnumerable<string> ids, CancellationToken ct) => ReadOnly();

    [HttpDelete("")]
    public override Task<IActionResult> DeleteByQuery(string? q, CancellationToken ct) => ReadOnly();

    [HttpDelete("all")]
    public override Task<IActionResult> DeleteAll(CancellationToken ct) => ReadOnly();

    [HttpPatch("{id}")]
    public override Task<IActionResult> Patch(string id, CancellationToken ct) => ReadOnly();

    private Task<IActionResult> ReadOnly()
        => Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status405MethodNotAllowed));

    private IActionResult HiddenNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }
}
