using Koan.Web.OpenGraph;
using Microsoft.AspNetCore.Components;

namespace Tangent.Render;

/// <summary>Which Razor component renders each resource type, with the title of its page, the version
/// its <c>ETag</c> carries, the frame the shell draws around it and the card a link to it previews. Registered once; the HTML formatter and the
/// ETag filter read it.</summary>
public sealed class ResourceComponents
{
    private readonly Dictionary<Type, ResourceComponent> _byResource = [];

    public ResourceComponents Add<TResource, TComponent>(Func<TResource, string> title, Func<TResource, long>? version = null,
        Func<TResource, Frame>? frame = null, Func<TResource, SocialCard?>? card = null)
        where TResource : notnull
        where TComponent : IComponent
    {
        _byResource[typeof(TResource)] = new ResourceComponent(
            typeof(TComponent),
            resource => title((TResource)resource),
            version is null ? null : resource => version((TResource)resource),
            frame is null ? null : resource => frame((TResource)resource),
            card is null ? null : resource => card((TResource)resource));
        return this;
    }

    /// <summary>A page record, drawn by the one page component from its parts (<see cref="Page"/>).</summary>
    public ResourceComponents Add<TPage>(Func<TPage, string> title, Func<TPage, long>? version = null, Func<TPage, Frame>? frame = null,
        Func<TPage, SocialCard?>? card = null)
        where TPage : notnull
        => Add<TPage, Page>(title, version, frame, card);

    /// <summary>Every page record registered, which the start checks against the parts; a refusal is drawn as its page.</summary>
    public IEnumerable<Type> Pages => _byResource.Where(pair => pair.Value.Component == typeof(Page))
        .Select(pair => pair.Key == typeof(Koan.Web.Controllers.Problem) ? typeof(RefusalPage) : pair.Key);

    public bool Has(Type resource) => _byResource.ContainsKey(resource);

    public ResourceComponent For(Type resource) => _byResource[resource];
}

/// <summary>The component for one resource type, the title of its page, the version of one of its values
/// where the resource has one, its frame where it names one, and its card where it may carry one.</summary>
public sealed record ResourceComponent(Type Component, Func<object, string> Title, Func<object, long>? Version, Func<object, Frame>? Frame,
    Func<object, SocialCard?>? Card);
