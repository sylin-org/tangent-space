using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Identity;
using Tangent.Api;

namespace Tangent.Conversation;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ConversationMutationAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (!string.Equals(request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            context.Result = new StatusCodeResult(415);
        else if (!ParticipationAccess.UsesCredential(context.HttpContext.User)
            && (request.Headers.Origin.Count != 1 || !TopicMutationAttribute.IsSameOrigin(request.Headers.Origin[0], request.Scheme, request.Host.Value ?? "")))
            context.Result = new StatusCodeResult(403);
    }
}
