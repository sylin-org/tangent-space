using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Tangent.Render;

/// <summary>A part: a record that says what a thing on a page is — a person, a Tangent entry, a Topic row, the place's head,
/// a shelf, a list that holds others — and never how it looks. Each part type has exactly one component in
/// <c>Render/Parts</c>, whose <c>Model</c> is the part, and <see cref="Draw"/> draws any part by its type, so the same thing
/// looks the same wherever it stands. A part that holds others decides how each is drawn (<see cref="Shape"/>).</summary>
public interface IPart;

/// <summary>Where a page places a part: across its head, in its rail, or in its column. A panel page has a head and a
/// column.</summary>
public enum Slot
{
    Head,
    Rail,
    Column
}

/// <summary>The slot a page's property is drawn in, in declaration order; a property with none is a fact of the page's JSON
/// alone. The property holds a part, or a list of parts drawn one after another; a null one is left out.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SlotAttribute(Slot slot) : Attribute
{
    public Slot Slot { get; } = slot;
}

/// <summary>How a page lays its slots out: <see cref="Home"/>, a head over a rail and a column, as the door, a Tangent and a
/// profile stand; <see cref="Reading"/>, the same for a reading column; <see cref="Settings"/>, the settings rail and its
/// section; <see cref="Panel"/>, one panel in the middle of the frame, as a sign-in, a form or a refusal stands.</summary>
public enum Layout
{
    Home,
    Reading,
    Settings,
    Panel
}

/// <summary>A page whose layout is its own to choose, as a page that writes an article in the article's own layout and anything
/// else in a panel; its <see cref="PageAttribute"/> names the one it starts from.</summary>
public interface ILaidOut
{
    Layout Layout { get; }
}

/// <summary>A resource drawn as a page: its <see cref="Layout"/>, and whether it refreshes itself from its own address when the
/// log moves (<see cref="Refreshes"/>), as the door does.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PageAttribute(Layout layout) : Attribute
{
    public Layout Layout { get; } = layout;

    public bool Refreshes { get; init; }
}

/// <summary>How a part that holds another draws it, in LANGUAGE's shapes: a person is a <see cref="Chip"/> in a Post's head and
/// among who is around, a <see cref="Row"/> in a list of people; a Topic a <see cref="Row"/> of a table or a <see cref="Card"/>
/// among articles; a form opens in place as a <see cref="Row"/> of a list. <see cref="Name"/> is a person's name alone,
/// linked to their profile; <see cref="Inline"/> a sentence inside another's line; <see cref="Menu"/> an item of the
/// account's menu; <see cref="Lead"/> the first article, drawn large. A part draws its own first shape where it is given
/// none.</summary>
public enum Shape
{
    Default,
    Chip,
    Card,
    Row,
    Tile,
    Badge,
    Menu,
    Name,
    Inline,
    Lead
}

/// <summary>The version a page's <c>ETag</c> carries: the first eight bytes of the SHA-256 of the page as its JSON says it, as a
/// non-negative number, so what the page draws from its facts changes nothing the tag does not already hold.</summary>
public static class PageVersion
{
    public static long Of(object page)
        => BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(page))), 0)
           & long.MaxValue;
}

/// <summary>Which component draws each part type: the one component in <c>Tangent.Render.Parts</c> whose <c>Model</c> is
/// that type. Built once from the assembly; <see cref="Check"/>, run at start, stops the host on a part type with no
/// component or with two, and on a page whose slot holds anything but parts.</summary>
public static class PartComponents
{
    /// <summary>The parameter every part's component declares for the part it draws.</summary>
    public const string Model = "Model";

    /// <summary>The parameter a part's component declares where a part that holds it may choose its shape.</summary>
    public const string As = "As";

    private static readonly Lazy<(IReadOnlyDictionary<Type, Type> Components, IReadOnlyList<string> Faults)> Found = new(() => Find(typeof(PartComponents).Assembly.GetTypes()));

    /// <summary>The component that draws <paramref name="part"/>.</summary>
    public static Type ComponentOf(Type part)
        => Found.Value.Components.TryGetValue(part, out var component)
            ? component
            : throw new InvalidOperationException($"No part component draws {part.Name}.");

    /// <summary>Whether <paramref name="component"/> lets a part that holds it choose its shape.</summary>
    public static bool TakesShape(Type component) => component.GetProperty(As) is { } shape && shape.IsDefined(typeof(ParameterAttribute));

    /// <summary>Every part type, by its component.</summary>
    public static IReadOnlyDictionary<Type, Type> All => Found.Value.Components;

    /// <summary>Stops the start on a part type that has no component or two, on a component of the parts folder that draws no
    /// part, and on a page whose slot holds something that is not a part.</summary>
    public static void Check(IEnumerable<Type> pages)
    {
        var faults = Found.Value.Faults.Concat(pages.SelectMany(PageFaults)).ToList();
        if (faults.Count > 0) throw new InvalidOperationException("The parts do not hold: " + string.Join(" ", faults));
    }

    /// <summary>A page's slotted properties in declaration order, with their slots.</summary>
    public static IReadOnlyList<(PropertyInfo Property, Slot Slot)> Slots(Type page)
        => page.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Slot: property.GetCustomAttribute<SlotAttribute>()))
            .Where(pair => pair.Slot is not null)
            .OrderBy(pair => pair.Property.MetadataToken)
            .Select(pair => (pair.Property, pair.Slot!.Slot))
            .ToList();

    /// <summary>The part type a slotted property holds: the property's type, or the type of its list's items.</summary>
    public static Type? PartOf(Type property)
    {
        if (typeof(IPart).IsAssignableFrom(property)) return property;
        var item = property.IsGenericType && property.GetGenericArguments() is [var argument]
                   && typeof(IEnumerable<>).MakeGenericType(argument).IsAssignableFrom(property)
            ? argument
            : null;
        return item is not null && typeof(IPart).IsAssignableFrom(item) ? item : null;
    }

    private static IEnumerable<string> PageFaults(Type page)
    {
        if (page.GetCustomAttribute<PageAttribute>() is null) yield return $"{page.Name} declares no layout.";
        foreach (var (property, _) in Slots(page))
            if (PartOf(property.PropertyType) is null) yield return $"{page.Name}.{property.Name} is in a slot but holds no part.";
    }

    /// <summary>What does not hold among <paramref name="types"/>: a part type with no component or two, and a component of
    /// the parts folder that draws no part.</summary>
    public static IReadOnlyList<string> Faults(IReadOnlyCollection<Type> types) => Find(types).Faults;

    private static (IReadOnlyDictionary<Type, Type> Components, IReadOnlyList<string> Faults) Find(IReadOnlyCollection<Type> types)
    {
        var faults = new List<string>();
        var drawn = new Dictionary<Type, List<Type>>();
        foreach (var component in types.Where(type => type.Namespace == typeof(PartComponents).Namespace + ".Parts" && typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract))
        {
            var model = component.GetProperty(Model);
            if (model is null || !model.IsDefined(typeof(ParameterAttribute)) || !typeof(IPart).IsAssignableFrom(model.PropertyType))
            {
                faults.Add($"{component.Name} is in the parts folder but draws no part.");
                continue;
            }

            (drawn.TryGetValue(model.PropertyType, out var list) ? list : drawn[model.PropertyType] = []).Add(component);
        }

        foreach (var part in types.Where(type => typeof(IPart).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false }))
        {
            if (!drawn.TryGetValue(part, out var components)) faults.Add($"No component draws {part.Name}.");
            else if (components.Count > 1) faults.Add($"{part.Name} is drawn by {string.Join(" and ", components.Select(component => component.Name))}.");
        }

        return (drawn.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0]), faults);
    }
}
