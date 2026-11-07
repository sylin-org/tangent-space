using Microsoft.AspNetCore.Components;
using Tangent.Render.Parts;
using Xunit;

namespace Tangent.Render
{
    public sealed class PartsTests
    {
        [Fact]
        public void The_start_refuses_a_part_no_component_draws_a_part_two_draw_and_a_component_that_draws_no_part_and_the_tree_holds()
        {
            var faults = PartComponents.Faults([typeof(Orphan), typeof(Twice), typeof(TwiceOne), typeof(TwiceTwo), typeof(Bespoke)]);

            Assert.Contains("No component draws Orphan.", faults);
            Assert.Contains("Twice is drawn by TwiceOne and TwiceTwo.", faults);
            Assert.Contains("Bespoke is in the parts folder but draws no part.", faults);
            Assert.Equal(3, faults.Count);
            Assert.Empty(PartComponents.Faults(typeof(PartComponents).Assembly.GetTypes()));
        }
    }
}

namespace Tangent.Render.Parts
{
    public sealed record Orphan : IPart;

    public sealed record Twice : IPart;

    public sealed class TwiceOne : ComponentBase
    {
        [Parameter] public Twice Model { get; set; } = default!;
    }

    public sealed class TwiceTwo : ComponentBase
    {
        [Parameter] public Twice Model { get; set; } = default!;
    }

    public sealed class Bespoke : ComponentBase
    {
        [Parameter] public string Model { get; set; } = "";
    }
}
