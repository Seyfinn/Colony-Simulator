# Spécification technique : nature sauvage, transport, royaumes

Destinataire : l'agent chargé du code (backend `Simulation/`, puis affichage `Game/`).
Conception validée : [Plan-transport-faune-royaumes.md](Plan-transport-faune-royaumes.md). Règles du dépôt : [AGENTS.md](../AGENTS.md).
Ce document fixe **quoi** construire et **où**. Les constantes sont des valeurs de départ, à régler par mesure.

---

## 0. Cadre commun

### 0.1 Principes non négociables
- **Autonomie** : aucune nouvelle commande joueur. Les habitants décident (chasse, apprivoisement, ponts, caravanes, élections).
- **Simulation pure** : rien dans `Simulation/` ne dépend de Godot ni des images. `Game/` lit l'état et l'affiche, avec un rendu procédural de secours.
- **Conservation** : aucun bien ni pièce ne naît ou ne disparaît hors des voies existantes (production, consommation, pertes comptées). Biens en transit et pertes passent par `Stockpile.Transport` et la comptabilité actuelle ; pièces perdues → `WorldState.CoinsLostToEvents`.
- **Déterminisme** : tout hasard passe par un générateur nommé. Ajouter **un quatrième générateur** `WorldState.Nature` (graine `seed * 53 + 0x4A7E`) pour la faune, la chasse, la prédation et l'interception. Politique (élections, conquête, sécession) → `WorldState.Politics`. Ne jamais toucher `Random`/`Chance` pour ces systèmes.
- **Énumérations** : ajouter toujours **à la fin, avec valeur explicite** (comme `ResourceType.MineralCoal = 27`). Un `Enum.GetValues` parcouru avec tirage (ex. compétences de départ) décale les histoires : renforcer les tests, ne pas changer leur graine.
- **Sauvegarde** : chaque nouveau type persistant s'ajoute à la liste blanche de `Persistence/StateGraph.cs` ; incrémenter `WorldSave.Version`. **Aucune migration**, aucun test d'ancien format. Seul l'aller-retour `WorldSave`/`StateGraph` du format courant doit passer.
- **Langue** : code et commentaires en français, identifiants dans le style existant (anglais pour les noms de types et membres, `///` en français).

### 0.2 Existant sur lequel s'appuyer (vérifié)
| Besoin | Point d'ancrage |
|---|---|
| Boucle de jeu | `WorldState.Step()` : bloc *jour* (par établissement actif), bloc *heure*, puis `ColonistAI.Tick` par colon. |
| Cerveau | `ColonyBrain.Think` (horaire) → `Sense` → pyramide `PlanConstruction / PlanSpareRoom / PlanWorkshop / PlanCanal / PlanCivic` → `DecideShares` (parts par `WorkSector`). `ColonyBrain.OnDayStart` (quotidien). |
| Stock | Un `Stockpile` par `Settlement` (`colony.Stock` sous `colony.UseSettlement`). Accès physique par le point de service le plus proche : `SettlementServices.Nearest(colony, ServiceUse.Stock, x, y)`. **L'accès selon la distance au dépôt existe déjà**. |
| Porteurs | `Colonist.Carrying` (type, quantité) et `Colonist.CarryingTo`. |
| Ravitaillement interne | `LogisticsPlanner` + `TerritorialTravel` (missions `Supply`, `Evacuation`…). |
| Caravanes | `Caravan`, `Trade.Daily/Hourly`, `Trade.Routing` (avance de segment en segment, interruption sans retour au stock). |
| Routes | Locales : `RoadLayer`, `RoadWorks`, `RoadDevelopment`. Monde : `WorldRoadNetwork` (niveaux d'arête), `TerritorialTravel` mission `RoadWork`. |
| Élevage | `Husbandry` ; troupeau dans `Settlement.Chickens/Sheep/Cows` ; `OnPenBuilt` remplit l'enclos avec `StartingHerd`. |
| Santé | `Health.Fall(colony, colonist, Ailment.Injured, durée, clock, …)` ; `Health.MaybeInjure`. |
| Saisons, climat | `GameClock.Season` (4 × 5 jours), `Climate` (vagues de froid, sécheresse). |
| Ressources sauvages | `LocalMap.GetBerries/GetFish`, repousse dans `LocalMap.DailyUpdate`. |
| Diplomatie, guerre | `Diplomacy` (opinions, `Pact`), `Warfare` (bandes, pillage), décisions graves par `Prayers` (`DecisionKind.Alliance/War/Peace`). |
| Territoire | `RegionState.OwnerColonyId`, `Settlement.RegionTileIndex`, `WorldState.Territories`. |
| Filiation | `Schism` crée une fille avec `Colony.Parent = mère`. |
| Personnalité | `Personality` (axes `Piete, Sociabilite, Ardeur, Audace, Ambition, Curiosite, Attachement, Temperament`). |

### 0.3 Ordre des lots
1. **Lot A, Nature sauvage** (prérequis de B : bêtes de trait, prédateurs sur les routes).
2. **Lot B, Transport**.
3. **Lot C, Politique** (utilise le prestige gagné en A, les caravanes et routes de B).

Chaque sous-lot se livre compilé, testé, avec `dotnet test Simulation.Tests` vert. Mesures de croissance : lot entier = 15 parties hors `GrowthTests` (ne lire que le résumé).

### 0.4 Décision D1 (tranchée par l'utilisateur)
Alliance, guerre et paix **restent des prières au joueur** : c'est lui qui décide. Le chef élu ne fait que moduler les seuils qui déclenchent la prière. Ne pas remplacer `colony.Prayers.Ask(...)`. Une sécession qui mène à la guerre passe aussi par la prière.

---

## Lot A : Nature sauvage

### A.0 Règles du jeu
1. Chaque région porte une **population sauvage** par espèce, qui dépend du biome, de la saison et de la présence humaine.
2. Dans une région habitée, la faune apparaît sous forme de **hardes** visibles qui bougent sur la carte locale.
3. **Tous les animaux sont sauvages au départ.** L'enclos achevé est vide : il faut **capturer** puis **apprivoiser**.
4. Les habitants **chassent** (viande, peaux) quand c'est rentable ; la surchasse fait fuir et décroître le gibier.
5. Les **prédateurs** attaquent troupeaux, voyageurs et travailleurs isolés : en général une blessure, rarement la mort. Ils sont plus pressants l'hiver et quand le gibier manque.
6. Les **herbivores** abîment les champs proches.
7. Les jeunes s'apprivoisent mieux. Un **loup apprivoisé devient un chien** : il réduit les attaques et aide à la chasse.
8. Les générations nées en enclos deviennent **plus dociles et productives** (lignée propre à la colonie).
9. **Ressources sauvages épuisables** : ruches (miel, cire), champignons, plantes médicinales, en plus des baies et poissons existants.
10. Rarement, un **alpha** (grand ours, loup meneur) terrorise la région ; une **grande chasse** collective l'abat et rapporte du prestige.
11. Défrichement et chasse **repoussent** la faune ; une région laissée tranquille **se repeuple**. L'hiver, le gibier **migre** vers les régions plus douces.
12. L'équilibre ressource/menace dépend du biome (forêts : gibier et prédateurs ; prairies : herbivores, peu de prédateurs ; toundra : rare et dangereux…).

### A.1 Nouveaux fichiers : dossier `Simulation/Nature/`
| Fichier | Contenu |
|---|---|
| `WildSpecies.cs` | `enum WildSpecies : byte { Rabbit, Deer, Boar, Wolf, Bear, Junglefowl, Mouflon, Aurochs, Horse }` + `static class WildSpeciesInfo` : rôle (`Prey`/`Predator`), taille de harde, viande, peaux, danger, vitesse, `DomesticForm` (`Junglefowl→Chickens`, `Mouflon→Sheep`, `Aurochs→Cows`, `Horse→Horses`, `Wolf→Dogs`, autres → `null`), table d'abondance par `Biome`. |
| `WildHerd.cs` | `sealed class WildHerd` : `Id`, `Species`, `Count`, `Young` (nombre de petits), `X`, `Y` (float), `TargetX/TargetY`, `State` (`enum HerdState { Grazing, Roaming, Fleeing, Stalking, Attacking }`), `IsAlpha`, `AlphaName`, `Hunger` (prédateurs), `ReservedBy` (id du chasseur, 0 sinon). |
| `RegionWildlife.cs` | `sealed class RegionWildlife` : `Dictionary<WildSpecies,int> Population`, `Disturbance` (0..1), `Hives`, `LastAlphaTicks`. Porté par `RegionState.Wildlife` (nouvelle propriété). |
| `Wildlife.cs` | `static class Wildlife` : capacité d'accueil, croissance logistique, migrations, apparition et disparition des hardes, déplacements. |
| `Hunting.cs` | `static class Hunting` : choix de la proie, résolution d'une chasse, grande chasse. |
| `Taming.cs` | `static class Taming` : capture, apprivoisement, lignées ; `sealed class TamingAnimal { ResourceType Species; bool IsYoung; float Progress; int Required; long CapturedTicks; }` ; `sealed class LivestockLine { ResourceType Species; float Docility; float Yield; int Generations; string Name; }` (nom de race généré depuis la colonie). |
| `Predation.cs` | `static class Predation` : attaques des enclos, des colons, des champs. |
| `WildResources.cs` | `static class WildResources` : ruches, champignons, plantes. |

### A.2 Modifications des types existants
- `WorldState` : `public Random Nature { get; }` ; `Step()` appelle les crochets (A.4).
- `RegionState` : `public RegionWildlife Wildlife { get; internal set; }` (créée à la génération de la région, depuis le biome).
- `Settlement` : `public List<WildHerd> Herds { get; } = [];` ; `public List<TamingAnimal> Taming { get; } = [];` ; `public Dictionary<ResourceType, LivestockLine> Lines { get; } = [];` ; `public int Dogs` (ou `Stock.Get(ResourceType.Dogs)`, voir ci-dessous) ; `public bool PenReinforced`.
- `ResourceType` (à la fin) : `Horses = 47, Oxen = 48, Dogs = 49, Honey = 50, Wax = 51, Mushrooms = 52, Herbs = 53`. Les `Hides` existent déjà (peaux).
- `ActivityKind` (à la fin) : `Hunt = 29, Capture = 30, Tame = 31, Gather = 32, GreatHunt = 33`. `Activity` reçoit `public int HerdId { get; init; }`. Compléter `IsHarvest`, `Skill`, `ReservesTarget`.
- `SkillType` (à la fin) : `Hunting`. `WorkSectors.Skills(Food)` → `[Foraging, Fishing, Hunting]`.
- `ColonySensors` : `GameAbundance` (0..100), `PredatorPressure` (0..100), `CropRaidPressure`, `TamingOpportunity` (bool).
- `Colony` : `public int Prestige { get; internal set; }`. `Colonist` : `public float Renown { get; internal set; }` (servira à l'élection, lot C).
- `Husbandry` : ajouter `Horses` et `Oxen` aux espèces d'enclos (capacité réduite comme les vaches) ; **`OnPenBuilt` ne remplit plus l'enclos** (supprimer l'appel à `StartingHerd`, garder `Abundance` pour d'autres usages ou le remplacer par la table de `WildSpeciesInfo`).
- `Health` : exposer `internal static void Injure(WorldState world, Colonist c, string cause, float deathChance)`, qui encapsule `Fall(..., Ailment.Injured, ...)` avec une chance de mort propre (prédateur : **0,01**, contre 0,04 pour un accident non soigné).
- `Field` / `FieldPlot` : `public float Trampled` (0..1) réduit la récolte.
- Nouveau `BuildingType` (à la fin) : `Apiary = 22` (rucher, optionnel au début), `HuntingLodge = 23` (pavillon de chasse : stockage des peaux, bonus de chasse ; optionnel). **Pas de palissade** en v1 : l'enclos renforcé suffit.

### A.3 Données et constantes de départ
- Capacité d'accueil d'une région : `Cap(species) = BaseCap(biome, species) × (1 − 0,6 × Disturbance) × SaisonFactor`.
- `Disturbance` = part de cases défrichées, champs et bâtiments + 0,05 par colon présent (plafonnée à 1). Recalculée chaque jour pour les régions habitées ; décroît de 2 %/jour sinon.
- Croissance : `N += r × N × (1 − N/Cap)`, avec r ≈ 0,03/jour pour les proies, 0,01 pour les prédateurs ; les prédateurs mangent `0,02 × Pred × Proies/CapProies` proies par jour.
- Hardes visibles : au plus **8 hardes** par établissement actif ; une harde représente `min(Population, TailleHarde)` bêtes prises dans `RegionWildlife.Population` (pas de double compte : la harde **emprunte** ses bêtes à la population régionale et les rend en disparaissant).
- Apparition : à plus de `R = 12 + 30 × Disturbance` cases du camp (la faune recule avec le village).
- Alpha : chance `0,002/jour` par région habitée dont la population de prédateurs dépasse 60 % de sa capacité ; pas plus d'un alpha par région, ni plus d'un par an.

### A.4 Intégration dans `WorldState.Step()`
```
bloc JOUR, par établissement actif (après colony.Map.DailyUpdate) :
    Wildlife.DailySettlement(world, settlement)      // perturbation, croissance locale, rendre/créer les hardes
    Predation.DailyRaids(world, colony)              // enclos et champs, la nuit passée
    Taming.Daily(world, colony)                      // progrès d'apprivoisement, naissances de lignée
    WildResources.Daily(colony)                      // repousse des ruches, champignons, plantes

bloc JOUR, une fois pour le monde (à l'heure de Diplomacy.PlanningHour, avant Diplomacy.Daily) :
    Wildlife.WorldDaily(world)                       // régions inhabitées : croissance, migrations saisonnières, alphas

bloc HEURE, par établissement actif (après ColonyBrain.Think) :
    Wildlife.HourlyMove(world, settlement)           // les hardes bougent de quelques cases
    Predation.HourlyEncounters(world, colony)        // colons isolés hors du village

ColonistAI.Tick : nouvelles activités Hunt / Capture / Tame / Gather / GreatHunt
```
Les hardes bougent **à l'heure**, pas au tick (performances). L'affichage interpole entre `PrevX/PrevY` et `X/Y` s'il le souhaite (ajouter ces deux champs à `WildHerd`).

### A.5 Pseudo-code

```text
Wildlife.DailySettlement(world, s):
    region = world.Regions[s.RegionTileIndex].Wildlife
    region.Disturbance = clamp(clearedShare(s.Map) + 0.05 × s.PresentColonists.Count, 0, 1)
    pour chaque espèce e:
        cap = Cap(biome, e, saison, region.Disturbance)
        n = region.Population[e] + bêtesEmpruntéesParHardes(s, e)
        n = croissanceLogistique(n, cap, r(e)) − prédation(e)
        reporter n (moins les bêtes des hardes) dans region.Population[e]
    // hardes
    retirer les hardes vides ; rendre à la population celles trop loin ou en surnombre
    tant que s.Herds.Count < MaxHerds et une espèce a assez de population:
        e = tirage pondéré par population (world.Nature)
        position = case marchable à distance ≥ R du camp, dans un biome compatible
        créer WildHerd(e, count = min(pop, TailleHarde(e)), young = part saisonnière)
    si alpha tiré : marquer une harde de prédateurs IsAlpha = true, nom généré, pensée de la colonie

Wildlife.HourlyMove(world, s):
    pour chaque harde h (ordre par Id):
        si h.State == Fleeing et loin des colons → Grazing
        proies : marcher vers une case herbeuse, s'éloigner des colons à moins de 6 cases (Fleeing)
        prédateurs : si Hunger > seuil → Stalking vers la cible la plus proche
                     (harde de proies, enclos non renforcé, colon isolé) ; sinon Roaming
        un pas de 1 à Vitesse(e) cases via LocalMap.IsWalkable (pas de Pathfinder : marche gloutonne)

Wildlife.WorldDaily(world):
    pour chaque région SANS établissement actif (ordre par index):
        Disturbance ×= 0.98 ; croissance logistique ; prédation
    si saison == Hiver et biome froid (Tundra, BorealForest, IceSheet) :
        20 % des proies migrent vers la région voisine la plus douce ; retour au printemps
```

```text
Hunting.PickHunt(colonist, colony) → Activity?        // appelé par ColonistAI quand secteur = Food
    si colony.Sensors.GameAbundance < 10 → null
    candidates = hardes de proies non réservées, à portée (distance de marche ≤ 40 cases)
    coûtHeures = trajet + DuréeChasse(e) / habileté
    repas = Viande(e) × probaRéussite
    comparer repas/heure avec cueillette et pêche (même logique que le LaborLedger actuel)
    si meilleur → Activity(Hunt, h.X, h.Y) { HerdId = h.Id }, h.ReservedBy = colonist.Id

Hunting.Resolve(world, colonist, herd):
    p = 0.35 + 0.03 × Skill(Hunting) + 0.15 × (chiens présents > 0) + (outils de fer ? 0.1 : 0)
    si world.Nature < p :
        tuées = 1 (2 si sanglier ou cerf et habileté > 10)
        herd.Count −= tuées
        colonist.Carrying = (Meat, Viande(e) × tuées) ; dépôt ultérieur : Hides += Peaux(e) × tuées
        Skills.Practice(Hunting)
    sinon herd.State = Fleeing
    si e == Boar ou Bear : Health.Injure avec chance faible (0.03), mort 0.01
    herd.ReservedBy = 0

Hunting.PlanGreatHunt(colony)                         // appelé par ColonyBrain.Think, étage survie
    si un alpha est présent et PredatorPressure > 60 et pas de grande chasse en cours :
        désigner 4 à 8 adultes valides, les plus téméraires (Personality.Audace) puis les meilleurs chasseurs
        ils reçoivent Activity(GreatHunt) vers l'alpha
    à l'arrivée de tous (ou après 1 jour) :
        force = Σ(1 + 0.05 × Hunting) × (outils ? 1.4 : 1) × (chiens ? 1.2 : 1)
        victoire si world.Nature < force / (force + DangerAlpha)
        victoire : alpha tué, viande et peaux, colony.Prestige += 10, participants Renown += 5 (tueur +10), pensée
        défaite  : 1 à 2 blessés (Health.Injure, mort 0.01), l'alpha reste
```

```text
Taming.PickCapture(colonist, colony) → Activity?      // secteur Farm, étage confort
    si pas d'enclos achevé ou enclos plein pour l'espèce → null
    cible = harde dont DomesticForm != null, priorité aux jeunes (Young > 0), espèce absente de l'enclos d'abord
    → Activity(Capture, h.X, h.Y) { HerdId }

Taming.ResolveCapture(world, colonist, herd):
    p = 0.25 + 0.02 × Skill(Husbandry) + (jeune ? 0.25 : 0)
    réussite → herd.Count−−, (Young−− si jeune) ;
               s.Taming.Add(TamingAnimal { Species = DomesticForm, IsYoung, Progress = 0, Required = jeune ? 3 : 8 jours })
    échec → herd.State = Fleeing ; 5 % de blessure pour Aurochs/Boar/Horse/Wolf

Taming.Daily(world, colony):
    pour chaque animal en apprivoisement :
        si un colon a fait Tend/Tame ce jour et qu'il reste de quoi le nourrir (grain ou fourrage) : Progress += 1
        sinon Progress −= 0.5 ; 10 % de fuite s'il tombe à 0 (l'animal retourne à la population régionale)
        si Progress ≥ Required :
            Wolf → Stock.Add(Dogs, 1)
            sinon → entrer à l'enclos (Husbandry.SetCount) ou Stock.Add(espèce) si l'enclos est plein
    LivestockLine : à chaque naissance en enclos, Docility += 0.01 × (1 − Docility), Yield += 0.005 (plafond 1.5)
        → Husbandry multiplie œufs, lait et laine par Yield ; la capture et la reproduction sont plus faciles avec Docility
```

```text
Predation.DailyRaids(world, colony):
    pression = Σ prédateurs proches × faim × (hiver ? 1.5 : 1) × (gibier rare ? 1.5 : 1)
    colony.Sensors.PredatorPressure = normaliser(pression)
    chance d'attaque d'enclos = pression × 0.02 × (PenReinforced ? 0.3 : 1) × (Dogs > 0 ? 0.5 : 1)
    si world.Nature < chance : perdre 1 à 3 bêtes (les plus nombreuses d'abord, jamais la dernière paire) ; pensée
    herbivores à moins de 8 cases d'un champ : chaque parcelle mûrissante voisine reçoit Trampled += 0.1 × hardes

Predation.HourlyEncounters(world, colony):
    pour chaque colon présent à moins de 3 cases d'une harde de prédateurs en Stalking :
        protégé si ≥ 2 colons à moins de 4 cases, ou un chien l'accompagne (chasseur)
        sinon chance = DangerEspèce × (alpha ? 3 : 1) × 0.1
        si world.Nature < chance → Health.Injure(world, colon, "attaqué par un loup", deathChance: 0.01) ; harde nourrie
```

```text
WildResources : sur LocalMap, couches comme _berries
    _hives (forêts tempérées, tropicales), _mushrooms (forêts, automne), _herbs (prairies, steppes)
    Gather(Product = Honey | Mushrooms | Herbs) : récolte de 1 à 3, couche −1 ; repousse lente (une ruche : 1 rayon tous les 4 jours)
    Honey et Mushrooms comptent dans FoodUnits ; Herbs et Wax sont des biens (Herbs améliore Medicine dans Health)
```

### A.6 Pyramide des priorités (`ColonyBrain`)
- `Sense` calcule `GameAbundance`, `PredatorPressure`, `CropRaidPressure`, `TamingOpportunity`.
- **Étage 1 (survie)** : le secteur `Food` répartit ses bras entre cueillette, pêche et **chasse** selon le rendement repas/heure (déjà la logique du `LaborLedger`). Si `PredatorPressure > 60` avec un alpha → `PlanGreatHunt` passe avant la construction.
- **Étage 3 (réserves)** : si `PredatorPressure > 40` et un enclos existe sans renfort → chantier « enclos renforcé » (bois 15, pierre 10) via `PlanCivic`.
- **Étage 4 (confort)** : si `TamingOpportunity` et la survie assurée → part du secteur `Farm` réservée à `Capture`/`Tame` (au plus 15 %). L'enclos se construit dès qu'une harde domesticable est visible (modifier la condition de `PlanCivic` pour `Pen`).
- Pensées (`Narrate`) : alpha repéré, grande chasse, première bête apprivoisée, premier chien, enclos attaqué.

### A.7 Affichage (`Game/`)
- Hardes : sprite par `WildSpecies`, sinon forme procédurale colorée (rond brun pour les proies, rouge pour les prédateurs, contour doré pour un alpha).
- Infobulle de harde : espèce, nombre (dont petits), état (« broute », « rôde », « traque »), nom de l'alpha.
- Enclos : animaux en apprivoisement distincts (icône de longe) ; chiens près des chasseurs.

### A.8 Tests (xUnit, `Simulation.Tests/`)
- `WildlifeTests` : croissance bornée par la capacité ; la perturbation fait baisser la capacité ; une région abandonnée se repeuple ; aucune bête créée par les hardes (somme population + hardes constante hors naissances et morts).
- `HuntingTests` : une chasse réussie ajoute viande et peaux ; la surchasse fait baisser la population.
- `TamingTests` : enclos achevé = vide ; une capture puis N jours de soins donnent une bête à l'enclos ; un loup donne un chien ; un jeune s'apprivoise plus vite.
- `PredationTests` : enclos renforcé et chiens réduisent les pertes ; la dernière paire n'est jamais tuée ; une attaque de prédateur blesse, la mort reste rare (statistique sur 10 000 tirages avec une graine fixe).
- `GrowthTests` (9 graines) restent verts. **Risque majeur** : sans troupeau de départ, les débuts peuvent manquer de nourriture. Mesurer 15 parties ; si la survie baisse, augmenter le gibier précoce plutôt que de rendre un troupeau gratuit.
- `StateGraph` : aller-retour d'un monde avec hardes, apprivoisements et lignées.

---

## Lot B : Transport et logistique

### B.0 Règles du jeu
1. Le **stock reste commun** à l'établissement ; on y accède au dépôt achevé **le plus proche** (déjà en place). La distance coûte du temps, pas de la matière.
2. Porteurs, charrettes et caravanes sont **visibles** ; au survol, on voit **contenu et destination**.
3. **Charrettes locales** : quand le dépôt est loin et la charge lourde, un porteur sur route prend une charrette (plus de charge, vitesse réduite hors route).
4. **Ponts** : traverser une rivière à gué est lent ; les habitants bâtissent un pont là où l'on passe souvent.
5. **Caravanes améliorables** par la colonie qui a moyens et besoin : porteurs → charrette de bois → charrette renforcée de fer → bêtes de trait (bœufs ou chevaux). Plus de capacité et de vitesse.
6. **Interception très rare** selon le risque de la route : sauvage et peuplée de prédateurs = plus risquée ; route entretenue et proche d'une colonie = sûre. Une interception fait perdre **une partie** de la cargaison ; les pertes sont comptées.

### B.1 Visibilité et infobulles
Pas de nouveau modèle : `Colonist.Carrying`, `Colonist.CarryingTo`, `Activity`, `Caravan.Cargo/Coins`, `Caravan` destination existent déjà.
- Ajouter dans `Simulation` une méthode de lecture pure : `public static string TransportLabel(Colonist c)` dans un nouveau `Colonies/TransportView.cs` (et `TransportLabel(Caravan)`), qui produit « 6 bois → entrepôt nord », « 12 sel, 30 pièces → Valmont (retour) ». Le texte vient des données, `Game/` ne fait que l'afficher.
- `Game/` : infobulle au survol d'un porteur, d'une charrette ou d'une caravane.

### B.2 Charrettes locales
- `ResourceType.Carts = 54` : un bien fabriqué (recette : 10 bois + 1 fer) dans l'atelier de construction existant le plus proche (à confirmer dans `Crafting`, sinon à la forge).
- `Colonist.UsingCart` (bool) ; capacité de port ×4, vitesse ×1 sur `RoadLayer`, ×0,6 hors route.
- `ColonistAI` : au moment de rapporter une récolte (`Deliver`), si `distance au dépôt > 20 cases` et `charge potentielle ≥ 2 × charge normale` et `Stock.Get(Carts) > nombre de charrettes en service` → le colon prend une charrette au dépôt (réservation via `StockReservation`), la ramène au dépôt à la fin.
- `ColonyBrain` (étage prospérité) : commander une charrette si `Farming.AverageDepotRoundTripSeconds` dépasse un seuil et qu'aucune n'est libre.
- `LogisticsPlanner` : la charge d'un voyage de ravitaillement est multipliée par la monture de caravane de la colonie (B.4).

### B.3 Gués et ponts
**Local (carte d'un établissement)**
- Aujourd'hui une rivière est marchable sans surcoût (`LocalMap.IsWalkable` exclut seulement l'eau dormante). Ajouter un **coût de gué** dans `Pathfinding/TraversalCost.cs` : case `IsRiver` ×3, `IsWideRiver` ×6, sauf case de pont.
- `RoadLayer` : nouvelle valeur de couche `Bridge` (dans l'énumération des niveaux de route existante, ajoutée à la fin).
- `RoadDevelopment.OnDayStart` : pour un tracé dont les passages dépassent le seuil et qui coupe des cases de rivière → `DevelopmentProject` « pont » (bois 8 + pierre 6 par case de rivière, habileté Construction). Une fois achevé, ces cases deviennent `Bridge`.
- **Effet d'équilibrage** : le gué ralentit des colonies déjà installées près d'un fleuve. Mesurer ; si besoin, commencer avec ×2 et ×4.

**Monde**
- `WorldRoadNetwork` : une arête qui traverse un grand fleuve a un coût de marche majoré au niveau 0. Le passage au niveau ≥ 2 de cette arête représente le **pont** (matériaux ×1,5). Vérifier où le coût fluvial d'une arête est calculé avant de coder ; s'il n'existe pas, l'ajouter dans le calcul d'étape de `Trade.Routing`.

### B.4 Caravanes améliorables
- `enum CaravanGear : byte { Porters = 0, WoodCart = 1, IronCart = 2, Draft = 3 }` dans `Colonies/Trade.cs`.
- `Colony.CaravanGear` (niveau atteint) ; `Caravan.Gear` (figé au départ) ; `Caravan.DraftAnimals` (`ResourceType`, nombre) si `Draft`.
- Effets (départ) : capacité ×1 / ×2 / ×3 / ×4 ; vitesse ×1 / ×0,9 / ×1 / ×1,4 (bœufs ×1,2, chevaux ×1,5).
- Coûts d'amélioration, prélevés sur le stock du village principal :
  - `WoodCart` : 30 bois, Construction ≥ 5 chez un habitant ;
  - `IronCart` : 10 fer + 2 outils ;
  - `Draft` : 2 bœufs ou 2 chevaux apprivoisés **qui partent avec la caravane** (ils mangent du grain en route : 1 unité/bête/jour, ajouté aux provisions existantes).
- Décision dans `Trade.Daily` (heure `PlanningHour`, village principal) :
```text
Trade.ConsiderGearUpgrade(world, colony):
    si colony.Sensors.SurvivalAssured est faux → rien
    besoin = moyenne(gain en heures des 3 derniers TradeRecord) et part des voyages limités par la capacité
    si besoin < seuil(niveau suivant) → rien
    si le stock couvre le coût sans entamer les réserves de survie (règles LogisticsPlanner) :
        prélever, colony.CaravanGear++, pensée « La colonie équipe ses caravanes de … »
```
- `Trade` : le calcul de `TradePlan` (charge maximale, durée `TripDays`) lit `Gear`. Les bêtes de trait reviennent avec la caravane ; perdues si la caravane est interceptée gravement.

### B.5 Risque de route et interception
```text
Trade.Routing.RouteRisk(world, edge) → float             // par jour de marche sur cette arête
    sauvage = 1 − min(1, distance à l'établissement le plus proche ≤ 2 cases monde ? 0.7 : 0)
    prédateurs = population de prédateurs de la région / capacité
    route = 1 − 0.25 × niveau de l'arête
    retourner BaseRisk(0.004) × sauvage × (0.5 + prédateurs) × route

Trade.Routing.CheckInterception(world, caravan) (chaque fois qu'une arête est franchie):
    si world.Nature < RouteRisk × jours passés sur l'arête :
        perte = 10 % à 40 % de la cargaison (tirage) ; pièces : 0 à 25 %
        biens retirés via la voie de perte de Stockpile.Transport (jamais remis au stock)
        pièces → world.CoinsLostToEvents += n
        un porteur sur deux : Health.Injure (mort 0.01)
        Draft : 20 % de chance de perdre une bête
        RecentEvent / pensée : « Des loups ont attaqué la caravane de … », ou « des brigands … » en terrain non forestier
        la caravane CONTINUE avec ce qui lui reste
```
Cible d'équilibrage : **moins de 2 %** des voyages interceptés sur une partie standard.

### B.6 Tests
- `TransportViewTests` : libellés exacts pour un porteur, une caravane aller et retour.
- `CartTests` : charrette prise et rendue, capacité ×4, aucun bien créé.
- `BridgeTests` : coût de gué appliqué ; un pont achevé le supprime ; le projet consomme ses matériaux.
- `CaravanGearTests` : montée de niveau consomme le coût ; capacité et durée modifiées ; bêtes de trait nourries et ramenées.
- `InterceptionTests` : conservation (cargaison avant = après + perte comptée) ; fréquence < 2 % sur un grand nombre de voyages simulés.

---

## Lot C : Politique et territoires

### C.0 Règles du jeu
1. Chaque colonie a un **chef élu par tous ses adultes**. Il est réélu périodiquement ou après sa mort, son départ ou une crise grave.
2. La **personnalité du chef** infléchit les décisions habituelles (prudence, construction, guerre, commerce). **Pas de lois, pas de taxes.**
3. Le **territoire** d'une colonie, ce sont ses régions possédées ; les **frontières** se dessinent entre royaumes.
4. Entre colonies : **alliances et échanges** seulement, sans mariage ni fusion culturelle.
5. Un **royaume** réunit des colonies par **filiation** (une colonie fille naît dans le royaume de sa mère, sans rien lui devoir) ou par **conquête**.
6. Le **roi** est élu par les chefs des colonies du royaume.
7. **Conquête** : rare et coûteuse. Une partie des habitants meurt, une partie fuit, le reste change d'allégeance. La colonie garde son nom, son peuple et ses habitudes.
8. **Influence** : elle baisse avec la distance à la capitale et la taille du royaume. Entretenir un grand royaume coûte cher ; une colonie peu attachée finit par **faire sécession**.

### C.1 Chef et élection

**Données**
- `Colony` : `ChiefId` (int, 0 = aucun), `ChiefSinceTicks`, `NextElectionTicks`, `ElectionCount`.
- `Colonist.Renown` (créé au lot A) : gagné par grande chasse, victoire de guerre, chantier d'offrande, maîtrise d'une compétence (≥ 15), âge adulte ; perd 1 %/jour.
- Nouveau fichier `Colonies/Leadership.cs` : `static class Leadership`.

**Pseudo-code**
```text
Leadership.Daily(world, colony)                     // appelé à Diplomacy.PlanningHour, avant Diplomacy.Daily
    chef = colonist(ChiefId)
    crise = moral moyen < 0.3 pendant 3 jours, ou famine (StarvationWatch), ou défaite de guerre récente
    si chef absent, mort, parti, ou Ticks ≥ NextElectionTicks, ou crise (au plus une fois par an) :
        Elect(world, colony)

Leadership.Elect(world, colony):
    électeurs = citoyens Adult ou Elder (ordre par Id)
    candidats = 3 électeurs au plus fort Renown (à égalité, l'âge, puis l'Id) ; chef sortant inclus s'il est éligible
    pour chaque électeur v :
        score(c) = c.Renown
                 + 10 si ami, −15 si rival
                 + 8 × Personality.Compatibility(v, c)
                 + 5 si parent (Colonist.AreKin)
                 + bruit world.Politics ∈ [−3, 3]
        voix pour argmax(score) ; à égalité, l'Id le plus bas
    élu = candidat aux plus de voix ; ChiefId = élu.Id ; NextElectionTicks = now + 2 ans
    pensée : « X est élu chef avec N voix sur M. »

Leadership.Stance(colony) → ChiefStance              // lecture pure, pas de sauvegarde
    à partir des axes du chef : Prudence = Attachement − Audace ; Bellicisme = Audace + Temperament ;
    Commerce = Sociabilite + Curiosite ; Bâtisseur = Ardeur + Ambition   (chacun ∈ [−1, 1])
```
**Influence sur les décisions (modulations bornées, ±20 % au plus) :**
- `Diplomacy` : seuil d'opinion pour proposer la guerre −10 × Bellicisme ; pour l'alliance −10 × Commerce.
- `Trade` : gain minimal pour lancer une caravane ×(1 − 0,15 × Commerce).
- `ExpansionPlanner` : délai entre fondations ×(1 − 0,15 × Bâtisseur).
- `ColonyBrain` : jours de vivres visés ×(1 + 0,15 × Prudence).

**D1** (voir 0.4) : les prières `Alliance/War/Peace` restent ; le joueur décide.

### C.2 Royaumes

**Données**
- Nouveau `Colonies/Realm.cs` :
```text
sealed class Realm
    Id, Name, ColorIndex (stable, pour l'affichage)
    CapitalColonyId
    KingColonistId (un chef de colonie membre)
    List<int> MemberColonyIds (ordre d'entrée)
    FoundedTicks, NextKingElectionTicks
```
- `WorldState.Realms : List<Realm>` ; `Colony.RealmId` (0 = indépendante) ; `Colony.Loyalty` (0..1) ; `Colony.LowLoyaltyDays` ; `Colony.ConqueredTicks` (long?, null si jamais conquise).
- Nouveau `Colonies/Realms.cs` : `static class Realms` (formation, roi, influence, entretien, sécession).

**Formation par filiation (`Schism`)**
```text
dans Schism, après la création de la fille :
    si mère.RealmId == 0 : créer Realm(capitale = mère, nom = « Royaume de » + mère.Name), mère.RealmId = realm.Id
    fille.RealmId = mère.RealmId ; fille.Loyalty = 0.9
```
Note : les **camps et hameaux** (`Settlement` d'une même colonie) ne sont pas des colonies : ils restent dans leur colonie.

**Effets d'appartenance (aucun dû)**
- `Diplomacy` : deux membres d'un même royaume sont traités comme alliés (`AreAllied` vrai, pas de guerre entre eux, renforts).
- `Trade.Routing` : passage toujours permis entre membres.
- Rien d'autre : pas de tribut, pas de stock partagé, pas de mariages.

**Roi**
```text
Realms.ElectKing(world, realm):                    // à la formation, à la mort du roi, tous les 4 ans
    votants = chefs des colonies membres (une voix chacun)
    candidats = ces mêmes chefs
    score(votant, c) = Renown(c) + 0.5 × taille de la colonie de c + opinion(colonie votant → colonie c)/5 + bruit Politics
    roi = majorité ; à égalité, chef de la capitale ; CapitalColonyId = colonie du roi
```

### C.3 Conquête
Branchée dans `Warfare` à l'issue d'une bataille gagnée par l'attaquant.
```text
Conquest.TryConquer(world, attacker, defender, battle):   // nouveau Colonies/Conquest.cs
    conditions (toutes) :
        victoire nette : force attaque ≥ 2 × force défense
        défenseurs valides restants ≤ 40 % des adultes
        l'attaquant a gagné ≥ 2 batailles contre ce défenseur dans cette guerre
        l'attaquant a les moyens : vivres ≥ 10 jours et ne dépasse pas sa capacité d'entretien (C.4)
        defender n'est pas la capitale d'un royaume de plus de 3 membres (trop rare, géré par les batailles normales)
    sinon → pillage habituel (inchangé)

    résolution (tirages world.Politics, ordre par Id) :
        morts  = 10 % à 20 % des habitants (adultes surtout) → Lifecycle mort « tombé lors de la conquête »
        fuite  = 20 % à 30 % → partent en Migration (départ) vers une colonie indépendante proche ;
                 si ≥ 6 adultes fuient ensemble et qu'un site est libre : ils fondent une nouvelle colonie (TryFoundColony/AddColony)
        restent = le reste ; leurs opinions des vainqueurs −30
    defender.RealmId = royaume de l'attaquant (créé si besoin, capitale = attaquant)
    defender.Loyalty = 0.3 ; defender.ConqueredTicks = now
    fin de la guerre entre les deux (Pact retiré) ; le pillage de cette bataille reste acquis
    chef du conquis : réélection immédiate (Leadership.Elect)
    attacker.Prestige += 20 ; guerriers Renown += 5
```
Conservation : les biens des morts restent au stock de la colonie conquise ; ceux des fuyards partent avec eux (charge portable), comme l'évacuation existante.

### C.4 Influence, entretien et sécession
```text
Realms.Daily(world)                                   // Diplomacy.PlanningHour, après Leadership, avant Diplomacy.Daily
    pour chaque royaume (ordre par Id), pour chaque membre m ≠ capitale :
        d = distance monde (cases) entre m et la capitale
        cible = 0.95
              − 0.03 × d
              − 0.04 × (nombre de membres − 1)
              − (m conquise depuis < 5 ans ? 0.25 : 0)
              + opinion(m → capitale) / 400
              + (même peuple ? 0.05 : 0)
              + (entretien payé hier ? 0.10 : −0.10)
        m.Loyalty += (cible − m.Loyalty) × 0.05
        si m.Loyalty < 0.25 : LowLoyaltyDays++ sinon LowLoyaltyDays = 0
        si LowLoyaltyDays ≥ 10 : Secede(world, m)

    entretien (capitale) : chaque jour, pour chaque membre, consommer 0.2 × d unités de vivres
        + 0.1 × d bois (messagers, fêtes, garnisons) ;
        ce sont des CONSOMMATIONS comptées comme les repas, jamais un transfert vers le membre
        payé seulement si la capitale garde ses réserves de survie ; sinon « entretien non payé »

Realms.Secede(world, m):
    m quitte le royaume ; m.RealmId = 0 ; pensée « m proclame son indépendance »
    si m a été conquise : déclaration de guerre possible contre l'ancienne capitale (passe par Diplomacy et D1)
    sinon : opinion mutuelle −20, pas de guerre automatique
    si d'autres membres voisins de m ont aussi Loyalty < 0.35 : ils peuvent rejoindre m dans un nouveau royaume (capitale = m)
    si le royaume n'a plus que sa capitale : le dissoudre (RealmId = 0)
```
**Pourquoi les empires restent rares** : la loyauté chute avec la distance et le nombre de membres, et l'entretien grandit comme Σ distances. Un très grand royaume ne tient que si la capitale est riche **et** centrale.

### C.5 Territoires et frontières
- Rien de neuf dans la simulation : territoire d'une colonie = `RegionState` avec `OwnerColonyId` = elle + régions de ses `Settlement`. Exposer une lecture pure `WorldState.RealmOfTile(int tile) → Realm?` (dans `WorldState.Territories.cs`).
- `Game/` : sur la carte du monde, remplir chaque région possédée de la couleur `Realm.ColorIndex` (ou de la couleur de la colonie si indépendante) ; tracer une ligne là où deux cases voisines appartiennent à deux royaumes différents.
- Fiche au survol d'une colonie : chef (nom, âge, traits notables via `Personality.NotableTraits`, renom, depuis quand), royaume, roi, loyauté (en mots : « fidèle », « hésitante », « au bord de la sécession »).

### C.6 Tests
- `LeadershipTests` : seuls les adultes votent ; résultat déterministe pour une graine ; réélection à la mort du chef ; la stance module les seuils dans les bornes.
- `RealmTests` : une fille de schisme entre dans le royaume de sa mère ; membres traités comme alliés ; élection du roi par les chefs ; la loyauté baisse avec la distance et la taille ; sécession après 10 jours sous le seuil ; dissolution d'un royaume réduit à sa capitale.
- `ConquestTests` : conditions non remplies = pillage seul ; conquête = morts, fuyards et restants dans les fourchettes ; conservation des biens ; la colonie conquise garde nom et peuple.
- Mesure longue (15 parties, 10 ans de jeu) : nombre et taille des royaumes. Cible : la plupart des royaumes ont 1 à 3 membres, un royaume de plus de 6 membres est exceptionnel.
- Aller-retour `StateGraph` avec royaumes, chef et loyautés.

---

## Annexe : récapitulatif des ajouts

| Type | Ajouts |
|---|---|
| `WorldState` | `Nature` (Random), `Realms` |
| `RegionState` | `Wildlife` |
| `Settlement` | `Herds`, `Taming`, `Lines`, `PenReinforced` |
| `Colony` | `Prestige`, `CaravanGear`, `ChiefId`, `ChiefSinceTicks`, `NextElectionTicks`, `ElectionCount`, `RealmId`, `Loyalty`, `LowLoyaltyDays`, `ConqueredTicks` |
| `Colonist` | `Renown`, `UsingCart` |
| `Caravan` | `Gear`, `DraftAnimals` |
| `Field`/`FieldPlot` | `Trampled` |
| `ColonySensors` | `GameAbundance`, `PredatorPressure`, `CropRaidPressure`, `TamingOpportunity` |
| `ResourceType` | `Horses = 47` … `Herbs = 53`, `Carts = 54` |
| `ActivityKind` | `Hunt = 29` … `GreatHunt = 33` |
| `SkillType` | `Hunting` (fin) |
| `BuildingType` | `Apiary = 22`, `HuntingLodge = 23` (optionnels) |
| Nouvelles énumérations | `WildSpecies`, `HerdState`, `CaravanGear` |
| Nouvelles classes | `Nature/` : `WildHerd`, `RegionWildlife`, `TamingAnimal`, `LivestockLine`, `Wildlife`, `Hunting`, `Taming`, `Predation`, `WildResources` ; `Colonies/` : `TransportView`, `Leadership`, `Realm`, `Realms`, `Conquest` |
| `StateGraph` | liste blanche : `WildHerd`, `RegionWildlife`, `TamingAnimal`, `LivestockLine`, `Realm` ; `WorldSave.Version` +1 |
