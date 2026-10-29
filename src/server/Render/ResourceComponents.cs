using Microsoft.AspNetCore.Components;

namespace Tangent.Render;

/// <summary>Which Razor component renders each resource type, with the title of its page, the version
/// its <c>ETag</c> carries and the frame the shell draws around it. Registered once; the HTML formatter and the
/// ETag filter read it.</summary>
public sealed class ResourceComponents
{
    private readonly Dictionary<Type, ResourceComponent> _byResource = [];

    public ResourceComponents Add<TResource, TComponent>(Func<TResource, string> title, Func<TResource, long>? version = null,
        Func<TResource, Frame>? frame = null)
        where TResource : notnull
        where TComponent : IComponent
    {
        _byResource[typeof(TResource)] = new ResourceComponent(
            typeof(TComponent),
            resource => title((TResource)resource),
            version is null ? null : resource => version((TResource)resource),
            frame is null ? null : resource => frame((TResource)resource));
        return this;
    }

    public bool Has(Type resource) => _byResource.ContainsKey(resource);

    public ResourceComponent For(Type resource) => _byResource[resource];
}

/// <summary>The component for one resource type, the title of its page, the version of one of its values
/// where the resource has one, and its frame where it names one.</summary>
public sealed record ResourceComponent(Type Component, Func<object, string> Title, Func<object, long>? Version, Func<object, Frame>? Frame);
