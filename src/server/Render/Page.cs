using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Tangent.Render;

/// <summary>The one page component: draws any page record from its slotted properties, in declaration order, each part
/// through <see cref="Draw"/> in the slot its property declares and the layout the record names (<see cref="PageAttribute"/>);
/// a null part is left out. It hands every part the reader's id and the day the page's times are written for. A page that
/// refreshes itself is the fragment island's host, unless it stands alone.</summary>
public sealed class Page : ComponentBase
{
    /// <summary>The cascading value that names the reader's id, null for a visitor.</summary>
    public const string ReaderValue = "Reader";

    /// <summary>The cascading value that names the UTC day a page's times are written for.</summary>
    public const string TodayValue = "Today";

    [Parameter, EditorRequired] public object Model { get; set; } = default!;

    /// <summary>The reader's id, null for a visitor.</summary>
    [Parameter] public string? Reader { get; set; }

    /// <summary>The address the page answers at, which a page that refreshes itself fetches again.</summary>
    [Parameter] public string? Address { get; set; }

    [CascadingParameter(Name = nameof(Shell.Standalone))] public bool Standalone { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var type = Model.GetType();
        var declared = type.GetCustomAttribute<PageAttribute>() ?? throw new InvalidOperationException($"{type.Name} declares no layout.");
        var page = Model is ILaidOut laid ? new PageAttribute(laid.Layout) { Refreshes = declared.Refreshes } : declared;
        var slots = PartComponents.Slots(type).Select(slot => (slot.Slot, Value: slot.Property.GetValue(Model))).Where(slot => slot.Value is not null).ToList();
        var head = slots.Where(slot => slot.Slot == Slot.Head).Select(slot => slot.Value).ToList();
        var rail = slots.Where(slot => slot.Slot == Slot.Rail).Select(slot => slot.Value).ToList();
        var column = slots.Where(slot => slot.Slot == Slot.Column).Select(slot => slot.Value).ToList();

        builder.OpenComponent<CascadingValue<string?>>(0);
        builder.AddComponentParameter(1, "Name", ReaderValue);
        builder.AddComponentParameter(2, "Value", Reader);
        builder.AddComponentParameter(3, "IsFixed", true);
        builder.AddComponentParameter(4, "ChildContent", (RenderFragment)(inner =>
        {
            inner.OpenComponent<CascadingValue<DateOnly>>(0);
            inner.AddComponentParameter(1, "Name", TodayValue);
            inner.AddComponentParameter(2, "Value", DateOnly.FromDateTime(DateTime.UtcNow));
            inner.AddComponentParameter(3, "IsFixed", true);
            inner.AddComponentParameter(4, "ChildContent", (RenderFragment)(content => Main(content, page, head, rail, column)));
            inner.CloseComponent();
        }));
        builder.CloseComponent();
    }

    private void Main(RenderTreeBuilder builder, PageAttribute page, List<object?> head, List<object?> rail, List<object?> column)
    {
        builder.OpenElement(0, "main");
        builder.AddAttribute(1, "class", page.Layout switch
        {
            Layout.Panel => "page sign-in post-form",
            Layout.Settings => "page layout layout-settings",
            Layout.Reading => "page layout layout-reading topic-layout",
            _ => "page frame-main"
        });
        if (page.Refreshes && !Standalone && Address is not null)
        {
            builder.AddAttribute(2, "data-island", "fragment");
            builder.AddAttribute(3, "data-props", JsonSerializer.Serialize(new { url = Address }));
        }

        switch (page.Layout)
        {
            case Layout.Panel:
                Panel(builder, head, column);
                break;
            case Layout.Settings:
            case Layout.Reading:
                Rail(builder, rail);
                builder.OpenElement(10, "div");
                builder.AddAttribute(11, "class", page.Layout == Layout.Reading ? "layout-main topic-main topic" : "layout-main");
                if (page.Layout == Layout.Settings) Panel(builder, head, column);
                else
                {
                    DrawAll(builder, head);
                    DrawAll(builder, column);
                }

                builder.CloseElement();
                break;
            default:
                DrawAll(builder, head);
                builder.OpenElement(20, "div");
                builder.AddAttribute(21, "class", "layout tangent-layout");
                Rail(builder, rail);
                builder.OpenElement(22, "div");
                builder.AddAttribute(23, "class", "layout-main tangent-main");
                DrawAll(builder, column);
                builder.CloseElement();
                builder.CloseElement();
                break;
        }

        builder.CloseElement();
    }

    /// <summary>One panel: the head's parts across its top, the column's inside its body.</summary>
    private static void Panel(RenderTreeBuilder builder, List<object?> head, List<object?> column)
    {
        builder.OpenElement(30, "section");
        builder.AddAttribute(31, "class", "sign-in-panel post-form-panel");
        DrawAll(builder, head);
        builder.OpenElement(32, "div");
        builder.AddAttribute(33, "class", "sign-in-body");
        DrawAll(builder, column);
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Rail(RenderTreeBuilder builder, List<object?> rail)
    {
        if (rail.Count == 0) return;
        builder.OpenElement(40, "aside");
        builder.AddAttribute(41, "class", "rail");
        DrawAll(builder, rail);
        builder.CloseElement();
    }

    private static void DrawAll(RenderTreeBuilder builder, List<object?> parts)
    {
        foreach (var part in parts)
        {
            builder.OpenRegion(50);
            builder.OpenComponent<Draw>(0);
            builder.AddComponentParameter(1, nameof(Draw.Part), part);
            builder.CloseComponent();
            builder.CloseRegion();
        }
    }
}
