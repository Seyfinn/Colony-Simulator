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
- `World/` : la carte du monde, une grille de 64 × 40 hexagones (`WorldGenerator`) : continents, climat du nord glacé au sud tropical, onze biomes, relief, fleuves. Chaque case donne sa carte locale à la colonie qui s'y installe (`MapStyle.For`).
- `Map/LocalMap` : la carte locale d'une colonie (relief en couches, eau, rivières, canaux, retenues d'eau).
- `Generation/` : génération des cartes. Chaque carte locale fait 200 × 200 cases (`MapGenerator.DefaultSize`) et se compose de grandes zones (un massif de montagne, de vastes forêts et plaines, un grand lac) ; les rivières naissent en ruisseau puis s'élargissent en fleuve de 3 à 8 cases (`Rivers`).
- `Colonies/` — une colonie, c'est :
  - `ColonyBrain` : le cerveau (capteurs → pyramide des priorités → répartition de la main-d'œuvre → pensées) ;
  - `ColonistAI` : le comportement individuel (besoins, déplacements, travail) ;
  - la vie : `Needs`, `Personality`, `Skills`, `Relations`, `Lifecycle`, `Migration`, `Species` ;
  - l'économie du sol : `Farming`, `Irrigation`/`Canal`, `Hydrology` (barrages), `ToolChain` (fer), `FoodChain` (blé), `Crafting` ;
  - les échanges : `Economy` (valeurs), `Trade` (caravanes), `WorldMap` (case de chaque colonie, routes des caravanes) ;
  - les grandes décisions : `Prayers` (la colonie demande l'accord du joueur).

Les durées sont en « ticks » (voir `Time/TimeConstants`) : une année = 4 saisons × 5 jours = 20 jours,
un jour = 45 secondes à vitesse ×1.

## Lancer

```bash
dotnet test Simulation.Tests          # tous les tests (≈ 15 s)
dotnet build Game/GodColony.csproj    # à faire avant de lancer Godot : il charge la DLL compilée
```

Le jeu se lance depuis Godot 4.7 (version .NET) en ouvrant `Game/project.godot`.

### Menus et fondation des colonies

Au lancement, l'accueil propose **Créer un monde**, **Charger une partie**, **Paramètres**, **Comment jouer** et **Quitter**.
Le formulaire de monde permet de choisir la graine, la taille des régions (128, 200 ou 256 cases de côté),
0 à 4 colonies initiales, leurs fondateurs, les migrations, le cycle de vie, le commerce et la vitesse.
Un monde vierge attend la fondation de sa première colonie.

En partie, **Menu** ou **Échap** ouvre le menu de pause ; il permet aussi de retourner à l'accueil et de reprendre
le monde courant. La création d'un autre monde demande confirmation avant de remplacer la partie.
Les préférences de plein écran, de synchronisation verticale, d'ambiance et de caméra sont enregistrées
dans `user://settings.cfg`.

**Sauvegarder la partie** propose trois emplacements avec le nom du monde, la date, les colonies et la population.
**Charger une partie** les retrouve depuis l'accueil ou le menu de pause. **F5** enregistre dans un quatrième emplacement
de sauvegarde rapide ; **F9** le charge, après confirmation si une partie est en cours. Les fichiers vivent dans
`user://saves` (sous Windows, `%APPDATA%/Godot/app_userdata/GodColony/saves`).
Le terrain modifié, les colonies, les habitants, les familles, les travaux, les stocks, les caravanes, les prières et le hasard
sont conservés, ainsi que la caméra, la vitesse et la sélection. Remplacer un emplacement conserve sa version précédente,
chargeable avec **Charger la version précédente**. Un fichier invalide est refusé avant de remplacer la partie courante.
Le format est versionné et lié au schéma de simulation ; une évolution de ce schéma nécessitera une migration.

**Fonder une colonie** ouvre le choix du nom, du peuple et de 5 à 20 fondateurs. Cliquez d'abord sur une case libre
de la carte du monde (son biome et son relief donnent le terrain de la région), puis sur une zone plate de 5 × 5 cases sur le terrain. Le contour vert indique un site valide,
le rouge un obstacle ; **Emplacement conseillé** choisit un camp proche des ressources. **Fonder la colonie** installe
les habitants à la case choisie, avec leurs provisions et 400 pièces. Le monde peut accueillir jusqu'à 16 colonies.
Chaque colonie possède sa propre carte locale ; les anciennes colonies restent à leur emplacement lors d'une fondation.
La simulation est suspendue pendant les menus et la fondation ; annuler conserve la vitesse et la partie précédentes.

Pour vérifier le parcours des contrôles Godot après compilation :

```bash
godot --path Game --fixed-fps 60 -- --smoke-menu
godot --path Game --fixed-fps 60 -- --smoke-saves
```

Le scénario vérifie l'accueil, une graine invalide, un monde vierge, la fondation, les paramètres persistants,
la pause, l'annulation, la reprise et le remplacement du monde. Le second scénario vérifie les sauvegardes, leur restauration,
les confirmations, les copies de secours, les fichiers endommagés et les raccourcis. Ils fonctionnent aussi avec `--headless`.
`--smoke-captures=chemin` enregistre les étapes dans un dossier existant lorsque le rendu est activé.

### Options de développement (en ligne de commande, après `--`)

`--capture=chemin.png` (capture puis quitte), `--advance-hours=N`, `--speed=1|4|30`, `--zoom=N`,
`--colony=N` (colonie observée), `--open-world` (panneau économie), `--open-map` (carte du monde), `--open-prayers`, `--demo-prayer`,
`--demo-dam`, `--auto-dam`, `--focus-dam`, `--demo-workshops`, `--demo-quarry`, `--select-first`,
`--focus-fields`, `--focus-quarry`. Les options sont lues dans l'ordre : `--auto-dam --advance-hours=1000 --focus-dam`.

Sans option, le jeu ouvre l'accueil. Les outils habituels démarrent directement la partie de démonstration à quatre colonies.
`--menu`, `--menu-world` et `--menu-settings` affichent les écrans de menu pour une capture ;
`--empty-world` démarre un monde vierge et `--demo-foundation` affiche un aperçu de camp prêt à confirmer.

`--perf=N` mesure N images puis affiche le temps par image (moyenne, centiles, pire image) et la part de la simulation,
avant de quitter. Pour comparer deux versions sur la même machine :
`godot --path Game --fixed-fps 60 -- --advance-hours=480 --speed=30 --perf=1800`.

## Conventions

- Le code et les commentaires sont en français, au plus près du vocabulaire du jeu.
- Chaque étape se termine par des tests verts et un commit.
- Les tests à graine fixe ne doivent pas mesurer un instant précis (on se réveille parfois très affamé) :
  utiliser `StarvationWatch` pour détecter une vraie famine.
- Ajouter un type d'énumération (`ResourceType`, `SkillType`, `BuildingType`…) décale les tirages au hasard :
  des tests dépendants de la graine peuvent bouger, il faut alors les rendre robustes plutôt que d'ajuster la graine.
