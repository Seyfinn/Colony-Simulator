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
- `World/` : la carte du monde, une grille de 64 × 40 hexagones (`WorldGenerator`) : continents, climat du nord glacé au sud tropical, onze biomes, relief, fleuves. Les fleuves sont rares : deux à trois grands fleuves traversent le continent de la montagne à la mer, rejoints par quelques rivières ; seule une case traversée par l'un d'eux a une rivière sur sa carte locale, les autres régions n'ont presque pas d'eau, et une colonie y vit sans (les colons ne boivent pas). Chaque case donne sa carte locale à la colonie qui s'y installe (`MapStyle.For`).
- `Map/LocalMap` : la carte locale d'une colonie (relief en couches, eau, rivières, canaux, retenues d'eau).
- `Generation/` : génération des cartes. Chaque carte locale fait 200 × 200 cases (`MapGenerator.DefaultSize`) et se compose de grandes zones (un massif de montagne, de vastes forêts et plaines, un grand lac) ; les rivières naissent en ruisseau puis s'élargissent en fleuve de 3 à 8 cases (`Rivers`).
- `Colonies/` — une colonie, c'est :
  - `ColonyBrain` : le cerveau (capteurs → pyramide des priorités → répartition de la main-d'œuvre → pensées) ;
  - `ColonistAI` : le comportement individuel (besoins, déplacements, travail) ;
  - la vie : `Needs`, `Personality`, `Skills`, `Relations`, `Lifecycle`, `Migration`, `Species` ;
  - l'économie du sol : `Farming`, `Irrigation`/`Canal`, `Hydrology` (barrages), `ToolChain` (fer), `FoodChain` (blé), `Crafting` ;
  - l'élevage et le textile : `Husbandry` (un enclos de poules et de moutons ; œufs, laine, métier à tisser, vêtements) ;
  - les échanges : `Economy` (valeurs), `Trade` (caravanes), `Specialties` (sel, épices et bois dur selon le biome, marché, négoce),
    `WorldMap` (case de chaque colonie, routes des caravanes) ;
  - la santé et le climat : `Health` (fièvres, blessures, soins), `Climate` (hivers rigoureux des biomes froids, sécheresses des biomes arides) ;
  - la cuisine : `Cuisine` (gâteau et ragoût) ;
  - la vie du village : `Civic` (enclos, puits, entrepôt, infirmerie, marché, taverne, école : ce que la colonie bâtit ensuite, et ce que chacun apporte) ;
  - les événements et les objectifs : `Events` (incendie, pillards, colporteur) et `Milestones` (jalons que chaque colonie atteint) ;
  - les grandes décisions : `Prayers` (la colonie demande l'accord du joueur).

### Ce que la colonie bâtit

Après les huttes et les ateliers du fer et du blé, une colonie dont la survie est assurée bâtit, dans cet ordre et un chantier à la fois
(`Civic.NextToBuild`) : un **enclos** (dès 6 habitants et des champs), un **puits** (8 habitants), un **métier à tisser** (quand la laine s'accumule),
un **entrepôt**, une **infirmerie** (après deux fièvres ou 12 habitants), un **marché** (après une caravane ou 14 habitants), une **taverne** (12) et
une **école** (3 enfants ou 16 habitants).

- **Élevage** : les poules pondent, les vaches donnent du lait, les moutons de la laine ; les bêtes paissent hors de l'hiver, mais l'hiver
  (et pendant une sécheresse) elles mangent du grain (une vache mange pour deux), faute de quoi l'une d'elles dépérit chaque jour. Les œufs et le lait se mangent
  (le lait tourne vite sans entrepôt, et ils ne se mangent qu'en dernier recours : on les garde pour la vente) ; les vaches tirent la charrue, jusqu'à +24 % de vitesse aux champs avec quatre vaches. Un enclos abrite 8 poules, 8 moutons et 4 vaches.
  Deux laines tissent un vêtement ; un vêtement dure environ trois ans et protège du froid et des fièvres.
- **Bêtes rares et abondantes** : chaque espèce est plus ou moins répandue selon le biome (`Husbandry.Abundance`) : les poules abondent en prairie, en forêt tempérée
  et en jungle, les moutons dans la steppe, la toundra et la taïga, les vaches en prairie ; le désert, la glace, la jungle et les marais manquent de vaches et de moutons.
  Là où une espèce est rare, le troupeau de départ est maigre ou absent (pas de vache dans le désert) et il se multiplie lentement.
  **Les bêtes coûtent une fortune** (prix de base : poule 100, mouton 250, vache 600 pièces, jusqu'à trois fois plus là où l'espèce est rare ; voir
  `Economy.BaselineCost` et `Husbandry.CostFactor`) et une colonie n'y songe que si elle a en caisse deux fois et demie leur prix
  (`Husbandry.LivestockWealthFactor`) : seule une colonie très prospère en achète, par caravane ou chez le colporteur, et l'enclos accueille la bête le lendemain.
  Une colonie dont l'enclos est plein garde les petits (jusqu'à 4 par espèce) pour ces rares acheteurs.
  Ce que les colonies s'échangent surtout, ce sont les **produits** : œufs, lait, laine et vêtements, que l'éleveur ramasse tant que son stock
  n'atteint pas un plafond, et que la région sans vaches ou sans moutons achète à sa voisine. L'écran Économie indique les bêtes abondantes ou rares de la région.
- **Reproduction** : une espèce ne se multiplie qu'à partir d'une paire, et le troupeau croît avec le nombre de paires (hors de l'hiver ; les moutons et
  les vaches seulement au printemps et en été). Enclos plein : les petits sont gardés en réserve (4 par espèce au plus) pour la vente.
- **Abattage et viande** : une bête abattue donne énormément de viande (poule 6 repas, mouton 20, vache 60) mais ne pond plus, ne donne plus ni lait ni laine
  et ne se reproduit plus : c'est un coût d'opportunité, que la colonie prend en compte. Elle ne décide d'abattre (`Husbandry.PlanSlaughter`, chaque matin) que dans trois cas :
  1. un **surplus** : l'enclos est plein et les quatre petits en réserve ne trouvent pas preneur (leur croissance serait perdue) ;
  2. l'**hiver qui vient** : à l'automne, le grain ne suffira pas à nourrir tout le troupeau (on abat d'abord les bouches les moins chères à remplacer) ;
  3. un **vrai besoin** : il reste moins de deux jours de vivres (on vise quatre jours, et l'on abat la bête qui rapporte le plus de viande pour ce qu'elle vaut).

  Dans tous les cas, la paire de reproducteurs de chaque espèce est protégée, les petits en réserve partent avant le troupeau, et la viande doit pouvoir servir
  (mangée pendant le délai où elle se garde, 3 jours ou 7 avec un entrepôt, ou salée) : on n'abat pas pour la laisser pourrir. Une bête qu'on ne peut plus nourrir l'hiver est elle aussi abattue plutôt que de dépérir.
  Les colons de l'agriculture (et ceux de la cueillette en cas de besoin) font l'abattage à l'enclos, à l'enclos le plus proche.
  La **viande fraîche** se mange avant le reste (la plus vieille d'abord) et **commence à se gâter après 3 jours, ou 7 jours si la colonie a un entrepôt** : passé ce délai, elle perd la moitié de son stock chaque jour. **Le sel la conserve** : chaque matin, la viande fraîche est salée (une unité de sel pour huit de
  viande) en **viande salée**, qui ne se gâte jamais, se mange après les céréales (c'est la réserve de longue durée) et s'échange comme une marchandise. Le sel, denrée de région,
  gagne ainsi un usage de plus.
- **Plusieurs enclos** : une colonie n'est pas limitée à un enclos. Quand les premiers débordent (enclos plein et quatre petits en réserve que personne
  n'achète), qu'elle compte au moins huit habitants par enclos, que ses vivres sont confortables (4 jours) et que son grain peut nourrir un troupeau plus grand l'hiver,
  elle en bâtit un autre (`Husbandry.WantsAnotherPen`), jusqu'à six. Chaque enclos ajoute 8 poules, 8 moutons et 4 vaches de capacité, et les bêtes sont soignées
  et abattues à l'enclos le plus proche du colon.
- **Bière** : elle se brasse dans un **fût**, un petit bâtiment de bois (6 bois) que la colonie construit dès qu'elle a une taverne (un fût pour vingt colons, quatre au plus).
  Un colon de l'artisanat y **verse 50 céréales** ; le fût **fermente cinq jours**, puis livre **40 chopes**, disponibles à la taverne (sans taverne, elles attendent dans le fût).
  On ne remplit un fût que si la survie est assurée, si le stock de bière (fûts en fermentation compris) est sous deux chopes par habitant et si la colonie garde trois jours de repas
  en céréales en plus des 50 versées. Le coût de la fournée (céréales et travail) est enregistré : une chope revient à environ une heure de travail (0,7 à 1 h mesurées en simulation). Un colon qui se détend à la taverne en boit une,
  et **son humeur monte de 15 % pendant cinq jours**
  (une saison entière), jusqu'à ce que l'effet retombe sous la moitié et qu'il en reprenne une. Les enfants n'en boivent pas. La bière est une marchandise,
  qu'une colonie qui a une taverne valorise (une chope par habitant). Le brassage et le ragoût passent avant les autres chaînes d'artisanat quand leur stock tombe sous le tiers
  de l'objectif, après elles sinon.
- **Plats de fête** : le **gâteau** (2 œufs, 2 laits, 2 farines, au four ; priorité sur le pain) fait six parts qui rassasient entièrement et remontent le moral de qui les mange
  (+20 % d'humeur qui retombe en deux jours). Le **ragoût** (2 viandes, fraîches ou salées, 1 œuf, 2 céréales ; à la taverne si elle existe, sinon au four) donne 4 bols qui rassasient,
  remontent le moral (+12 %) et donnent **+25 % de vitesse de travail pendant deux jours**. On les mange avant tout le reste et on ne les cuisine que si la survie est assurée,
  jusqu'à un gâteau pour huit habitants et un bol de ragoût par habitant.
- **Commerce** : chaque biome produit une denrée (sel : toundra, steppe, désert ; épices : prairie, savane, marais ; bois dur : forêts et jungle) et manque
  des deux autres. Le marché troque la denrée locale avec les nomades (compétence de négoce), augmente d'un tiers la charge des caravanes, et les marchands
  expérimentés rabattent le coût des voyages. Le sel conserve les vivres, les épices relèvent le moral, une bûche de bois dur brûle comme trois de bois.
  Toutes ces marchandises, avec la laine et les vêtements, apparaissent dans l'écran Économie.
- **Santé** : en hiver surtout, ou à jeun, ou à la belle étoile, un colon peut tomber malade ; un geste de travail peut le blesser. Il garde le lit
  (à l'infirmerie, il guérit deux fois plus vite) et peut succomber s'il n'a pas été soigné par un guérisseur (compétence de médecine). Le puits divise le
  risque de fièvre par deux. Les biomes froids brûlent plus de bois et connaissent des vagues de froid ; les biomes arides connaissent des sécheresses l'été.
- **Entrepôt** : au-delà d'une capacité de stockage, le grain, la farine, le pain et les œufs pourrissent en partie.
- **Événements** : un incendie détruit un bâtiment (le puits l'éteint à temps), des pillards attaquent une colonie qui a de quoi les attirer
  (des défenseurs adultes et des outils les repoussent), un colporteur propose des denrées lointaines ou achète un surplus.
- **Objectifs** : `Milestones.All` liste les jalons (population, premier pain, premier outil de fer, barrage, enclos, caravane, village…) ; chacun est célébré
  dans le journal et réjouit un peu tout le monde. L'écran Économie en donne le compte.

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

L'interface adapte les stocks à la largeur de la fenêtre : une rangée sur grand écran, deux rangées sur une fenêtre
plus petite. Le bandeau indique les jours de repas en réserve ; la nourriture est signalée en rouge sous deux jours.
La case **Nourriture** additionne la valeur nutritive des baies, du poisson, des céréales et du pain :
100 points de faim valent 1 nourriture (0,6 par baie, poisson ou céréale ; 0,85 par pain).
Un clic ouvre le détail sous la case, avec les quantités et leur contribution. La farine y figure avec une valeur nulle
tant qu'elle n'est pas cuite en pain. Un second clic, un clic ailleurs ou Échap replie le détail.
L'économie et les prières disposent de panneaux défilants ; l'actualisation de l'économie conserve la position de lecture.
Les panneaux se replient lorsqu'une carte ou les commandes occupent leur emplacement.

**C** ou **Recentrer** rejoint le camp, ou l'habitant sélectionné. **Tab** passe à la colonie suivante.
**M** ouvre la carte du monde, **E** l'économie, **P** les prières, **J** le journal et **H** les commandes.
Ces raccourcis sont suspendus pendant la saisie d'un nom. **Échap** annule le renommage en conservant la fiche,
puis ferme les panneaux avant d'ouvrir le menu de pause. Les déplacements de caméra restent limités au terrain.

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
le rouge un obstacle ; **Emplacement conseillé** propose, sur la carte du monde comme sur le terrain, jusqu'à 5 choix numérotés (régions agréables pour le peuple, camps proches des ressources) : chaque clic passe au suivant. **Fonder la colonie** installe
les habitants à la case choisie, avec leurs provisions et 400 pièces. Le monde peut accueillir jusqu'à 16 colonies.
Chaque colonie possède sa propre carte locale ; les anciennes colonies restent à leur emplacement lors d'une fondation.
La simulation est suspendue pendant les menus et la fondation ; annuler conserve la vitesse et la partie précédentes.

Pour vérifier le parcours des contrôles Godot après compilation :

```bash
godot --path Game --fixed-fps 60 -- --smoke-menu
godot --path Game --fixed-fps 60 -- --smoke-saves
```

Le scénario vérifie l'accueil, une graine invalide, un monde vierge, la fondation, les paramètres persistants,
la pause, l'annulation, la reprise, le remplacement du monde, les raccourcis, la saisie, les limites de caméra et le défilement des panneaux.
Le second scénario vérifie les sauvegardes, leur restauration,
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
- Le hasard des maladies, du climat et des événements passe par `WorldState.Chance`, à part de `WorldState.Random` : régler ces aléas ne décale
  pas le reste de l'histoire d'une graine. Les quatre derniers métiers (`SkillType.Husbandry` et suivants) sont dérivés des premiers sans tirage,
  pour la même raison. La monnaie reste conservée, aux pièces près que les événements prennent ou apportent (`WorldState.CoinsLostToEvents`).
- Enrichir la colonie (nouvelles nourritures, plats, bière, élevage) la rend plus attrayante aux voyageurs et accélère sa croissance : `GrowthTests` garde la cadence
  (doublement en 2 à 5 ans). Le réglage à ajuster est `Migration.MaxTravelerChancePerDay` (0,11 depuis la viande, les enclos multiples et la bière ; 0,16 à l'origine), pas la natalité,
  qui pèse peu devant les arrivées.
- Le format de sauvegarde est lié au schéma des données : les sauvegardes d'avant l'élevage, la santé et les bâtiments de village sont refusées
  (« version incompatible »).
- Ajouter un type d'énumération (`ResourceType`, `SkillType`, `BuildingType`…) décale les tirages au hasard :
  des tests dépendants de la graine peuvent bouger, il faut alors les rendre robustes plutôt que d'ajuster la graine.
