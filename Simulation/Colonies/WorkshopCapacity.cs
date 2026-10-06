namespace GodColony.Simulation.Colonies;

/// <summary>
/// Ce qu'un atelier à lots (four à pain, moulin) peut faire : ses postes de travail, la taille maximale d'un lot et la place de sa sortie. La capacité ne vient que de bâtiments
/// achevés — le bâtiment principal et ses extensions — jamais d'un chantier, et rien ici ne dépend du nombre d'habitants.
/// </summary>
public static class WorkshopCapacity
{
    /// <summary>Les extensions achevées du bâtiment principal.</summary>
    public static int CompletedExtensions(Colony colony, Building principal) =>
        principal.Id == 0 ? 0 : colony.Buildings.Count(b => b.IsExtension && b.ExtensionOfId == principal.Id && b.IsComplete);

    /// <summary>Répétitions maximales d'un lot : deux au départ, quatre dès la première extension achevée.</summary>
    public static int MaxBatch(Colony colony, Building principal) =>
        CompletedExtensions(colony, principal) >= 1 ? ScaleRules.ExtendedMaxBatch : ScaleRules.InitialMaxBatch;

    /// <summary>Postes de travail : un seul, deux avec la deuxième extension si chacun a sa case de travail accessible.</summary>
    public static int Slots(Colony colony, Building principal)
    {
        if (CompletedExtensions(colony, principal) < 2)
            return ScaleRules.InitialSlots;
        return SettlementServices.WorkCells(colony, principal).Length >= ScaleRules.ExtendedSlots ? ScaleRules.ExtendedSlots : ScaleRules.InitialSlots;
    }

    /// <summary>Les postes occupés : un lot engagé garde son poste du départ jusqu'à sa fin, pause comprise.</summary>
    public static IEnumerable<Activity> Crafts(Colony colony, Building workshop)
    {
        foreach (Colonist colonist in colony.PresentMembers)
        {
            if (colonist.Activity is { Kind: ActivityKind.Craft } active && active.Building == workshop && active.WorkshopSlotId >= 0)
                yield return active;
            if (colonist.PausedCraft is { Kind: ActivityKind.Craft } paused && paused.Building == workshop && paused.WorkshopSlotId >= 0 && paused != colonist.Activity)
                yield return paused;
        }
    }

    /// <summary>Le premier poste libre (-1 s'il n'y en a pas). <paramref name="ignoring"/> : l'activité qui se revalide ne s'oppose pas à elle-même.</summary>
    public static int FindFreeSlot(Colony colony, Building workshop, Activity? ignoring = null)
    {
        int slots = Slots(colony, workshop);
        for (int slot = 0; slot < slots; slot++)
            if (!Crafts(colony, workshop).Any(a => a != ignoring && a.WorkshopSlotId == slot))
                return slot;
        return -1;
    }

    /// <summary>Unités de sortie que l'atelier peut garder : deux lots maximaux par poste.</summary>
    public static int OutputCapacity(Colony colony, Building workshop, int unitsPerRepeat) =>
        Slots(colony, workshop) * ScaleRules.StoredOutputBatches * MaxBatch(colony, workshop) * unitsPerRepeat;

    /// <summary>Un atelier à lots a-t-il un poste libre et de la place de sortie pour une répétition de plus ? Les sélecteurs des chaînes s'y réfèrent tous.</summary>
    public static bool CanStartBatch(Colony colony, Building workshop, Recipe reference) =>
        FindFreeSlot(colony, workshop) >= 0
        && BatchProduction.CommittedOutput(colony, workshop) + workshop.OutputUnits(reference.Output) + reference.OutputAmount
            <= OutputCapacity(colony, workshop, reference.OutputAmount);

    // --- Mesure et décision d'extension ---

    /// <summary>Utilisation, coût et gain estimé d'une extension : un résultat temporaire, jamais sauvegardé.</summary>
    public sealed record ExtensionCase(ExtensionVerdict Verdict, double Utilization, double CostHours, double SavedHoursPerDay, double PaybackDays);

    public const int UtilizationDays = 10;
    public const double MinUtilization = 0.70;
    public const int MaxPaybackDays = 40;
    public const int MaxExtensions = 2;

    /// <summary>Heures de travail possibles d'un poste par jour : la journée de travail des habitants, de 6 h à 17 h.</summary>
    public static double WorkdayTicks => 11 * Time.TimeConstants.TicksPerHour;

    /// <summary>
    /// Part des heures de travail possibles réellement employées à travailler (hors trajet et repas) sur les dix derniers jours complets. Null tant que la capacité
    /// actuelle de l'atelier n'a pas été observée dix jours entiers.
    /// </summary>
    public static double? Utilization(Colony colony, Building principal)
    {
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        ScaleDay[] window = ledger.Last(UtilizationDays).ToArray();
        if (window.Length < UtilizationDays || !ledger.CapacitySince.TryGetValue(principal.Id, out long since) || window[0].Day < since)
            return null;
        double worked = window.Sum(d => d.WorkTicks.GetValueOrDefault(principal.Id));
        return worked / (UtilizationDays * Slots(colony, principal) * WorkdayTicks);
    }

    /// <summary>Les dix journées de la capacité actuelle, pour le graphique ; aucune donnée avant une fenêtre complète.</summary>
    public static IReadOnlyList<double> DailyUtilization(Colony colony, Building principal) =>
        Utilization(colony, principal) is null ? [] : colony.LocalSettlement.ScaleLedger.Last(UtilizationDays)
            .Select(d => d.WorkTicks.GetValueOrDefault(principal.Id) / (Slots(colony, principal) * WorkdayTicks)).ToArray();

    /// <summary>Enregistre le début d'observation de chaque atelier à lots et la remet à zéro quand sa capacité change (une extension s'achève).</summary>
    internal static void ObserveCapacities(Colony colony, long day)
    {
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        foreach (Building principal in colony.Workshops(BuildingType.Oven).Concat(colony.Workshops(BuildingType.Mill)).Concat(colony.Workshops(BuildingType.Mint)))
        {
            int key = Slots(colony, principal) * 100 + MaxBatch(colony, principal);
            if (!ledger.CapacityKey.TryGetValue(principal.Id, out int known) || known != key)
            {
                ledger.CapacityKey[principal.Id] = key;
                ledger.CapacitySince[principal.Id] = day;
            }
        }
    }

    /// <summary>
    /// L'extension est-elle justifiée ? Utilisation au-delà de 70 % sur dix jours entiers, demande encore présente, intrants crédibles, survie assurée, et amortissement
    /// en 40 jours au plus par le travail réellement économisé (préparation et combustible des lots plus grands, plus l'attente devant un poste occupé). Le chantier ne compte
    /// jamais comme capacité : seule une extension achevée en donne.
    /// </summary>
    public static ExtensionCase Evaluate(Colony colony, Building principal)
    {
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        if (colony.Buildings.Any(b => b.ExtensionOfId == principal.Id && !b.IsComplete))
            return new(ExtensionVerdict.UnderConstruction, 0, 0, 0, double.PositiveInfinity);
        if (CompletedExtensions(colony, principal) >= MaxExtensions)
            return new(ExtensionVerdict.MaxReached, 0, 0, 0, double.PositiveInfinity);
        if (Utilization(colony, principal) is not { } use || use <= MinUtilization)
            return new(ExtensionVerdict.TooLittleUse, Utilization(colony, principal) ?? 0, 0, 0, double.PositiveInfinity);
        if (colony.Sensors is not { SurvivalAssured: true } || colony.Sensors.FoodDays < SettlementRules.ComfortFoodDays || colony.Sensors.WoodPressure > 70f)
            return new(ExtensionVerdict.SurvivalFirst, use, 0, 0, double.PositiveInfinity);
        Recipe reference = Crafting.RecipeFor(colony, principal.Type);
        if (BatchProduction.NetDemand(colony, reference.Output) <= 0)
            return new(ExtensionVerdict.NoDemand, use, 0, 0, double.PositiveInfinity);
        if (ToolChain.MissingInputs(colony, reference).Count > 0)
            return new(ExtensionVerdict.NoInputs, use, 0, 0, double.PositiveInfinity);

        var module = new Building(principal.Type, principal.X, principal.Y) { Width = 2, Height = 3, ExtensionOfId = principal.Id };
        double cost = module.WoodRequired * Economy.Cost(colony, ResourceType.Wood) + module.StoneRequired * Economy.Cost(colony, ResourceType.Stone)
            + module.WorkSeconds * ScaleRules.HoursPerSecond;
        double saved = SavedHoursPerDay(colony, principal, reference);
        double payback = saved <= 0 ? double.PositiveInfinity : cost / saved;
        return payback <= MaxPaybackDays
            ? new(ExtensionVerdict.Wanted, use, cost, saved, payback)
            : new(ExtensionVerdict.PaybackTooLong, use, cost, saved, payback);
    }

    /// <summary>Heures de travail économisées chaque jour par une extension, d'après la fenêtre mesurée : préparation et combustible partagés par de plus gros lots, attente évitée.</summary>
    private static double SavedHoursPerDay(Colony colony, Building principal, Recipe reference)
    {
        ScaleDay[] window = colony.LocalSettlement.ScaleLedger.Last(UtilizationDays).ToArray();
        double repeatsPerDay = window.Sum(d => d.Produced.GetValueOrDefault(reference.Output)) / (double)UtilizationDays / reference.OutputAmount;
        int now = MaxBatch(colony, principal), next = CompletedExtensions(colony, principal) == 0 ? ScaleRules.ExtendedMaxBatch : now;
        double preparationSeconds = ScaleRules.BatchPreparationShare * reference.Seconds * repeatsPerDay * (1.0 / now - 1.0 / next);
        double fuelUnits = 0;
        if (reference.Fuel is { } fuel && reference.Inputs.FirstOrDefault(i => i.Type == fuel).Amount is var perRepeat and > 0)
            fuelUnits = repeatsPerDay * perRepeat * ((FuelPerRepeat(now, perRepeat)) - FuelPerRepeat(next, perRepeat));
        double waiting = window.Sum(d => d.SlotRefusals.GetValueOrDefault(principal.Id)) / (double)UtilizationDays * ColonistAI.ThinkIntervalTicks / Time.TimeConstants.TicksPerHour;
        return preparationSeconds * ScaleRules.HoursPerSecond + fuelUnits * (reference.Fuel is { } f ? Economy.Cost(colony, f) : 0) + waiting;
    }

    /// <summary>Combustible par répétition d'un lot de <paramref name="count"/> répétitions : la part fixe est partagée.</summary>
    private static double FuelPerRepeat(int count, int perRepeat) =>
        Math.Ceiling(ScaleRules.BatchFuelFixedShare * perRepeat + count * (1 - ScaleRules.BatchFuelFixedShare) * perRepeat - 1e-9) / count;

    /// <summary>
    /// Chaque jour : met à jour les verdicts d'extension des ateliers à lots de l'établissement. Aucune recherche de site ici : la pensée horaire l'exécute plus tard, selon la pyramide.
    /// </summary>
    internal static void ReviewDaily(Colony colony, long day)
    {
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        ObserveCapacities(colony, day);
        foreach (Building principal in colony.Workshops(BuildingType.Oven).Concat(colony.Workshops(BuildingType.Mill)).ToArray())
            ledger.Verdicts[principal.Id] = Evaluate(colony, principal).Verdict;
        foreach (int id in ledger.Verdicts.Keys.Where(id => colony.BuildingById(id) is null).ToArray())
            ledger.Verdicts.Remove(id);
    }
}
