using System.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Tangent.Render;

/// <summary>The one dispatcher: draws a part by its type, through the one component the parts folder has for it, in the shape
/// the part that holds it chooses where that component takes one; a list of parts one after another; nothing for null.</summary>
public sealed class Draw : ComponentBase
{
    /// <summary>The part, a list of parts, or null.</summary>
    [Parameter] public object? Part { get; set; }

    /// <summary>How the part is drawn, where its component takes a shape.</summary>
    [Parameter] public Shape As { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Part is IPart part) One(builder, part, As);
        else if (Part is IEnumerable list)
            foreach (var item in list.OfType<IPart>())
                One(builder, item, As);
    }

    /// <summary>One part into <paramref name="builder"/>.</summary>
    public static void One(RenderTreeBuilder builder, IPart part, Shape shape = Shape.Default)
    {
        var component = PartComponents.ComponentOf(part.GetType());
        builder.OpenRegion(0);
        builder.OpenComponent(0, component);
        builder.AddComponentParameter(1, PartComponents.Model, part);
        if (shape != Shape.Default && PartComponents.TakesShape(component)) builder.AddComponentParameter(2, PartComponents.As, shape);
        builder.CloseComponent();
        builder.CloseRegion();
    }
}
