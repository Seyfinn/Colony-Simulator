namespace GodColony.Simulation.Colonies;

public enum ColonistLocationKind { Settlement = 0, Travel = 1 }
/// <summary>Position lue depuis le déplacement réel ; les coordonnées locales restent sur le colon.</summary>
public readonly record struct ColonistLocation(ColonistLocationKind Kind, int Id, float X, float Y);
