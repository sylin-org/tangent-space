using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
namespace Tangent.Api;
[ApiController, AllowAnonymous, Route("api/search")]
public sealed class SearchController(TangentServer hub, ParticipantDirectory participants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q="", [FromQuery] int page=1, CancellationToken ct=default)
    {
        Response.Headers.CacheControl="private,no-store";
        Response.Headers.Vary="Cookie, Authorization";
        q=q.Trim();
        if(q.Length is <2 or >100 || page is <1 or >10000)return BadRequest(new {message="Use 2 to 100 characters and a valid page."});
        string? actor=null;
        if(User.Identity?.IsAuthenticated==true) {
            try {actor=ParticipationAccess.Require(User,ParticipationGrants.Read);}catch(UnauthorizedAccessException){return Unauthorized();}
        }
        using var fresh=EntityContext.NoCache();
        var scopes=new Dictionary<string,TopicDescription>();
        var limited=false;
        for(var directoryPage=1;directoryPage<=4;directoryPage++) {
            var directory=await hub.Tangents.ListTopicDirectory(actor,null,directoryPage,ct);
            foreach(var topic in directory.Topics) { if(scopes.Count==100){limited=true;break;} if(topic.CanRead)scopes[topic.Key]=topic; }
            if(scopes.Count==100){limited |= directory.NextPage is not null;break;}
            if(directory.NextPage is null)break;
            if(directoryPage==4)limited=true;
        }
        if(scopes.Count==0)return Ok(new {items=Array.Empty<object>(),more=false,scopeLimited=limited});
        var titleMatches = scopes.Values.Where(topic => topic.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || topic.Topic.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(topic => topic.Key).ToArray();
        var textMatch = new FieldFilter(FieldPath.Of(nameof(Post.SearchText)), FilterOperator.Contains, FilterValue.Of(q.ToLowerInvariant()));
        var match = titleMatches.Length == 0 ? (Filter)textMatch : Filter.Any(textMatch,
            Filter.All(Filter.In(nameof(Post.TopicKey), titleMatches), Filter.Eq(nameof(Post.Sequence), 1L)));
        var filter=Filter.All(Filter.In(nameof(Post.TopicKey),scopes.Keys.ToArray()),
            LinqFilterCompiler.Compile<Post>(post=>post.Sequence>0 && post.OfPostId==null && !post.Removed), match);
        var query=new QueryDefinition {Sort=SortSpecParser.ParseStrict<Post>("-AcceptedAt,-Id")};
        var posts=await Post.Query(post=>true,query.Where(filter).WithPagination(page,25),ct);
        var items=new List<object>();
        foreach(var post in posts) {
            var topic=await hub.Topics.Describe(actor,post.TopicKey,ct);
            if(topic?.CanRead!=true)continue;
            var author=await hub.Profiles.Read(post.AuthorParticipantId,ct);
            var participant=await Participant.Get(post.AuthorParticipantId,ct);
            var text=PostMarkup.PlainText(post.Content);
            var index=text.IndexOf(q,StringComparison.OrdinalIgnoreCase);
            var start=Math.Max(0,index-60);var excerpt=text.Substring(start,Math.Min(240,text.Length-start));
            items.Add(new {post.Id,topic.Title,url=$"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(post.Id)}",excerpt=(start>0?"…":"")+excerpt+(start+excerpt.Length<text.Length?"…":""),author=new DirectoryParticipant(post.AuthorParticipantId,author.DisplayName??author.Handle??"Participant",author.Handle,author.Avatar,participant?.Classification.ToString()??"Undeclared","/u/"+Uri.EscapeDataString(await participants.PerennialValue(post.AuthorParticipantId,ct)))});
        }
        return Ok(new {items,more=posts.Count==25,scopeLimited=limited});
    }
}
