using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Bénédictions : appliquées une seule fois à une cible valide, bornées, sans cumul, et lues par la moisson et le combat.</summary>
public sealed class DivinePowersTests
{
    private static (WorldState World, Colony Colony, Field Field) Village()
    {
        var world = new WorldState(11, startingColonists: 18, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Field field = colony.Fields.First();
        foreach (FieldPlot p in field.Plots) { p.Stage = CropStage.Growing; p.PlantedTicks = 100; p.LastHarvestTicks = 0; }
        return (world, colony, field);
    }

    private static DivineWish Wish(Colony colony, WishTargetKind kind, int target, int settlement = -1)
    {
        var wish = new DivineWish
        {
            Id = colony.NextWishId(), SourceColonyId = colony.Id, SettlementId = settlement < 0 ? colony.PrimarySettlementId : settlement,
            Kind = kind == WishTargetKind.Field ? DivineWishKind.CropBlessing : DivineWishKind.ChampionBlessing, TargetKind = kind, TargetId = target,
            Status = DivineWishStatus.AwaitingResponse, CreatedTicks = colony.Clock.Ticks,
        };
        colony.Wishes.Add(wish);
        return wish;
    }

    private static DivineEffect Accept(Colony colony, DivineWish wish)
    {
        wish.AcceptedTicks = colony.Clock.Ticks;
        return DivinePowers.ApplyAccepted(colony, wish)!;
    }

    [Fact]
    public void Un_accord_applique_l_effet_une_seule_fois_et_un_second_appel_n_ajoute_rien()
    {
        var (_, colony, field) = Village();
        DivineWish wish = Wish(colony, WishTargetKind.Field, field.Id);
        DivineEffect effect = Accept(colony, wish);
        Assert.Equal(DivineWishStatus.Fulfilled, wish.Status);
        Assert.Equal(field.Plots.Count, effect.Plots.Count);
        Assert.Same(effect, DivinePowers.ApplyAccepted(colony, wish));
        Assert.Single(colony.DivineEffects);
        Assert.Equal(wish.Id, effect.WishId);
        Assert.Equal(colony.Id, effect.SourceColonyId);
    }

    [Fact]
    public void Sans_accord_rien_ne_s_applique_et_une_cible_deja_benie_termine_le_souhait_sans_effet_de_plus()
    {
        var (_, colony, field) = Village();
        DivineWish refused = Wish(colony, WishTargetKind.Field, field.Id);
        Assert.Null(DivinePowers.ApplyAccepted(colony, refused)); // pas d'accord
        Assert.Empty(colony.DivineEffects);

        Accept(colony, Wish(colony, WishTargetKind.Field, field.Id));
        DivineWish again = Wish(colony, WishTargetKind.Field, field.Id);
        again.AcceptedTicks = colony.Clock.Ticks;
        Assert.Null(DivinePowers.ApplyAccepted(colony, again));
        Assert.Equal(DivineWishStatus.AlreadyBlessed, again.Status);
        Assert.NotEmpty(again.Outcome);
        Assert.Single(colony.DivineEffects);
    }

    [Fact]
    public void Une_cible_disparue_avant_l_accord_invalide_le_souhait()
    {
        var (_, colony, field) = Village();
        foreach (FieldPlot p in field.Plots) p.Stage = CropStage.Fallow; // récolte perdue avant la réponse
        DivineWish wish = Wish(colony, WishTargetKind.Field, field.Id);
        wish.AcceptedTicks = colony.Clock.Ticks;
        Assert.Null(DivinePowers.ApplyAccepted(colony, wish));
        Assert.Equal(DivineWishStatus.TargetInvalid, wish.Status);
        Assert.Empty(colony.DivineEffects);
    }

    [Fact]
    public void La_moisson_beneficie_d_un_quart_une_seule_fois_par_parcelle_et_pas_aux_cycles_suivants()
    {
        var (_, colony, field) = Village();
        DivineEffect effect = Accept(colony, Wish(colony, WishTargetKind.Field, field.Id));
        FieldPlot plot = field.Plots[0];
        Assert.Equal(5, DivinePowers.HarvestBonus(colony, plot.X, plot.Y, 20, 100, 0)); // 20 → 25
        Assert.Equal(0, DivinePowers.HarvestBonus(colony, plot.X, plot.Y, 20, 100, 0)); // consommé
        FieldPlot other = field.Plots[1];
        Assert.Equal(0, DivinePowers.HarvestBonus(colony, other.X, other.Y, 20, 999, 0)); // autre cycle (nouvelle semaille)
        Assert.Equal(1, DivinePowers.HarvestBonus(colony, other.X, other.Y, 3, 100, 0)); // 3,75 → 4 : arrondi au plus proche
        FieldPlot third = field.Plots[2];
        Assert.Equal(0, DivinePowers.HarvestBonus(colony, third.X, third.Y, 1, 100, 0)); // 1,25 → 1 : gain nul, sans minimum artificiel
        Assert.Equal(DivineEffectStatus.Active, effect.Status);
        foreach (FieldPlot p in field.Plots.Skip(3)) DivinePowers.HarvestBonus(colony, p.X, p.Y, 8, 100, 0);
        Assert.Equal(DivineEffectStatus.Consumed, effect.Status);
    }

    [Fact]
    public void L_expiration_est_exacte_et_un_cycle_perdu_invalide_la_parcelle()
    {
        var (world, colony, field) = Village();
        DivineEffect effect = Accept(colony, Wish(colony, WishTargetKind.Field, field.Id));
        long end = effect.ExpiresTicks;
        Assert.Equal(DivinePowers.HarvestSeasons * TimeConstants.DaysPerSeason * (long)TimeConstants.TicksPerDay, end - effect.StartTicks);

        field.Plots[0].Stage = CropStage.Fallow; // le gel emporte une parcelle
        DivinePowers.Daily(colony);
        Assert.Equal(BlessedPlotState.Invalid, effect.Plots[0].State);

        while (world.Clock.Ticks < end - 1) world.Clock.Advance();
        Assert.True(colony.Clock.Ticks < effect.ExpiresTicks);
        FieldPlot p = field.Plots[1];
        Assert.True(DivinePowers.HarvestBonus(colony, p.X, p.Y, 8, 100, 0) > 0); // encore valide un tick avant la fin
        world.Clock.Advance();
        Assert.Equal(0, DivinePowers.HarvestBonus(colony, field.Plots[2].X, field.Plots[2].Y, 8, 100, 0)); // now >= fin : aucun bonus
        DivinePowers.Daily(colony);
        Assert.Equal(DivineEffectStatus.Expired, effect.Status);
    }

    [Fact]
    public void Seul_le_champion_present_parmi_les_combattants_reels_recoit_son_bonus_et_il_s_eteint_a_sa_mort()
    {
        var (world, colony, _) = Village();
        Colonist champion = colony.Members.First(m => m.Stage == LifeStage.Adult), other = colony.Members.First(m => m != champion && m.Stage == LifeStage.Adult);
        DivineEffect effect = Accept(colony, Wish(colony, WishTargetKind.Colonist, champion.Id));
        Assert.Equal(1.0f, effect.ExpiresTicks - effect.StartTicks == TimeConstants.TicksPerYear ? 1.0f : 0f);
        Assert.Equal(0.25f, DivinePowers.ChampionStrengthBonus(colony, [champion, other], 1f), 4);
        Assert.Equal(0f, DivinePowers.ChampionStrengthBonus(colony, [other], 1f)); // bande sans son champion
        Assert.Equal(0.15f, DivinePowers.ChampionStrengthBonus(colony, [champion], 0.6f), 4); // un quart de la force de base d'un défenseur

        Lifecycle.Die(world, champion, "accident de test");
        Assert.Equal(DivineEffectStatus.TargetInvalid, effect.Status);
        Assert.Equal(0f, DivinePowers.ChampionStrengthBonus(colony, [champion], 1f));
        Assert.Equal(DivineWishStatus.Fulfilled, colony.Wishes[0].Status); // l'historique reste
    }

    [Fact]
    public void La_defense_et_l_attaque_lisent_le_meme_bonus_que_la_bataille()
    {
        var (world, colony, _) = Village();
        float before = Warfare.DefenseEstimate(world, colony, null);
        Colonist champion = colony.PresentMembers.First(m => m.Stage == LifeStage.Adult && m.Ailment == Ailment.None);
        Accept(colony, Wish(colony, WishTargetKind.Colonist, champion.Id));
        Assert.Equal(before + 0.15f, Warfare.DefenseEstimate(world, colony, null), 3);
    }

    [Fact]
    public void Les_effets_survivent_a_la_sauvegarde_et_un_effet_partiellement_consomme_continue_pareil()
    {
        var (world, colony, field) = Village();
        DivineEffect effect = Accept(colony, Wish(colony, WishTargetKind.Field, field.Id));
        FieldPlot plot = field.Plots[0];
        DivinePowers.HarvestBonus(colony, plot.X, plot.Y, 20, 100, 0);
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-pouvoirs-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            DivineEffect restored = Assert.Single(loaded.Colonies[0].DivineEffects);
            Assert.Equal((effect.Id, effect.WishId, effect.ExpiresTicks), (restored.Id, restored.WishId, restored.ExpiresTicks));
            Assert.Equal(BlessedPlotState.Consumed, restored.Plots[0].State);
            Assert.Equal(5, restored.Plots[0].Gained);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}

public sealed class ScaleSnapshotTests
{
    [Fact]
    public void L_instantane_lit_le_registre_sans_rien_faire_avancer()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Building oven = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Oven)!;
        oven.StoreOutput(ResourceType.Bread, 4, 2);
        colony.LocalSettlement.ScaleLedger.Today.Produced[ResourceType.Bread] = 9;
        colony.LocalSettlement.ScaleLedger.Close(1);
        ScaleSnapshot snapshot = ScaleSnapshot.Of(colony.PrimarySettlement);
        WorkshopView view = Assert.Single(snapshot.Workshops);
        Assert.Equal((oven.Id, 1, 2, 4), (view.BuildingId, view.Slots, view.MaxBatch, view.OutputWaiting));
        Assert.Equal(9, snapshot.Produced[ResourceType.Bread]);
        Assert.Equal(4, snapshot.Services.Count);
        Assert.Equal(oven.OutputUnits(ResourceType.Bread), 4);
    }
}
