namespace Tangent.Render;

/// <summary>Marks a controller, or an action, whose pages manage the place: whatever it answers, a refusal included, leaves
/// out the owner layer, so no stylesheet of the place's can hide the way back. The HTML formatter reads it from the endpoint
/// that answered, and nothing else decides it.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class ManagingAttribute : Attribute;
