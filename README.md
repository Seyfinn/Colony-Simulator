# GodColony — jeu de dieu et de colonies

Un sandbox où l'on observe plusieurs peuples vivre, commercer et grandir, et où l'on intervient en dieu.
Le document de conception (GDD) vit dans un Claude Doc ; ce fichier explique seulement le code.

## Organisation

| Dossier | Rôle |
|---|---|
| `Simulation/` | Toute la simulation, en C# pur, sans Godot. C'est elle qui décide de tout. |
| `Simulation.Tests/` | Tests xUnit de la simulation (parties de plusieurs mois ou années à graine fixe). |
| `Game/` | Le projet Godot 4 (C#) : affichage, interface, caméra. Il ne fait que lire la simulation. |
| `Game/Assets/` | Réservé aux illustrations (le travail graphique se fait ici, jamais dans `Simulation/`). |

Règle d'or : **la simulation ne connaît pas l'affichage**. Godot lit `WorldState` et dessine.

## Dans `Simulation/`

- `WorldState` : l'horloge, les colonies, les caravanes. `Step()` avance d'un tick.
- `Map/LocalMap` : la carte locale d'une colonie (relief en couches, eau, rivières, canaux, retenues d'eau).
- `Generation/` : génération des cartes (relief, rivières).
- `Colonies/` — une colonie, c'est :
  - `ColonyBrain` : le cerveau (capteurs → pyramide des priorités → répartition de la main-d'œuvre → pensées) ;
  - `ColonistAI` : le comportement individuel (besoins, déplacements, travail) ;
  - la vie : `Needs`, `Personality`, `Skills`, `Relations`, `Lifecycle`, `Migration`, `Species` ;
  - l'économie du sol : `Farming`, `Irrigation`/`Canal`, `Hydrology` (barrages), `ToolChain` (fer), `FoodChain` (blé), `Crafting` ;
  - les échanges : `Economy` (valeurs), `Trade` (caravanes), `WorldMap` ;
  - les grandes décisions : `Prayers` (la colonie demande l'accord du joueur).

Les durées sont en « ticks » (voir `Time/TimeConstants`) : une année = 4 saisons × 5 jours = 20 jours,
un jour = 45 secondes à vitesse ×1.

## Lancer

```bash
dotnet test Simulation.Tests          # tous les tests (≈ 15 s)
dotnet build Game/GodColony.csproj    # à faire avant de lancer Godot : il charge la DLL compilée
```

Le jeu se lance depuis Godot 4.7 (version .NET) en ouvrant `Game/project.godot`.

### Options de développement (en ligne de commande, après `--`)

`--capture=chemin.png` (capture puis quitte), `--advance-hours=N`, `--speed=1|4|30`, `--zoom=N`,
`--colony=N` (colonie observée), `--open-world` (panneau économie), `--open-prayers`, `--demo-prayer`,
`--demo-dam`, `--auto-dam`, `--focus-dam`, `--demo-workshops`, `--demo-quarry`, `--select-first`,
`--focus-fields`, `--focus-quarry`. Les options sont lues dans l'ordre : `--auto-dam --advance-hours=1000 --focus-dam`.

## Conventions

- Le code et les commentaires sont en français, au plus près du vocabulaire du jeu.
- Chaque étape se termine par des tests verts et un commit.
- Les tests à graine fixe ne doivent pas mesurer un instant précis (on se réveille parfois très affamé) :
  utiliser `StarvationWatch` pour détecter une vraie famine.
- Ajouter un type d'énumération (`ResourceType`, `SkillType`, `BuildingType`…) décale les tirages au hasard :
  des tests dépendants de la graine peuvent bouger, il faut alors les rendre robustes plutôt que d'ajuster la graine.
