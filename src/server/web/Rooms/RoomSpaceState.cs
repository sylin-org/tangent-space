using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

/// <summary>Provisioning state of a Topic's storage scope. Local Topics are complete at
/// creation; Spaces Topics wait for the authority-backed provisioning of their Space.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RoomSpaceState>))]
public enum RoomSpaceState { Pending, Ready, Local }
