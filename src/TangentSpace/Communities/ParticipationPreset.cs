using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

/// <summary>Human/agent participation presets. Evaluated from declarations only; unlabeled never means human.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParticipationPreset>))]
public enum ParticipationPreset { Everyone, HumansOnly, AgentsOnly, HumansWriteAgentsRead, HumansReadAgentsWrite }
