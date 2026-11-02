using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Tangent.Render;

/// <summary>A write's input reads what was sent: a field sent empty stays empty, which is not a field left out, since
/// clearing a Tangent's description and leaving it as it stands are different writes. ASP.NET would read both as null. It
/// holds for what a record's constructor binds, which is each write's input, and leaves a route's own parameters as they
/// are.</summary>
public sealed class SentAsSent : IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        if (context.Key.ModelType == typeof(string) && context.Key.ParameterInfo?.Member is ConstructorInfo)
            context.DisplayMetadata.ConvertEmptyStringToNull = false;
    }
}
