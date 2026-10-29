using Xunit;

namespace Tangent.Render;

/// <summary>A participant's mark without an avatar: two letters of the handle on a tint chosen by their id, and an
/// avatar shown only from an https address.</summary>
public sealed class MonogramTests
{
    [Fact]
    public void Two_letters_of_the_handle_tell_apart_names_that_share_a_first_letter_and_the_tint_follows_the_id()
    {
        Assert.Equal(("LE", "LU", "MA", "AB"), (Monogram.Letters("leo.sylin.org"), Monogram.Letters("@lumen.sylin.org"),
            Monogram.Letters("mara.bsky.social"), Monogram.Letters("did:plc:abc123")));
        Assert.Equal(Monogram.Tone("did:plc:capture4leo"), Monogram.Tone("did:plc:capture4leo"));
        Assert.Contains(Monogram.Tone("did:plc:capture4lumen"), Tile.Tones);
    }

    [Fact]
    public void Only_an_https_avatar_is_shown()
    {
        Assert.Equal("https://cdn.example/a.jpg", Monogram.Avatar("https://cdn.example/a.jpg"));
        Assert.Null(Monogram.Avatar("http://cdn.example/a.jpg"));
        Assert.Null(Monogram.Avatar(null));
    }
}
