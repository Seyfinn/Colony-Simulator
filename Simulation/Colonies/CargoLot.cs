namespace GodColony.Simulation.Colonies;

/// <summary>Un lot effectivement retiré d'un stock ; la viande conserve son âge pendant le transport.</summary>
public sealed record CargoLot(ResourceType Resource, int Amount, int AgeDays = 0);
