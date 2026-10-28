using SkiaSharp;
namespace Tangent.Tests.ExperienceIntegration;
internal static class TestImage {
 internal static byte[] Png(){using var bitmap=new SKBitmap(8,8);using(var canvas=new SKCanvas(bitmap))canvas.Clear(SKColors.CornflowerBlue);using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);return encoded.ToArray();}
}
