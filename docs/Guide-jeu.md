# Guide du jeu — GodColony

Un sandbox où l'on observe plusieurs peuples vivre, commercer et grandir, et où l'on intervient en dieu.
Référence détaillée à consulter selon le besoin. Vérifier les comportements et paramètres dans le code actuel ; ce guide ne constitue pas une mémoire de session.

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
  - le progrès : `Knowledge` (savoirs, âges, étude, savoirs qui circulent avec les caravanes) ;
  - les relations entre colonies : `Diplomacy` (opinions, querelles, présents, alliances, guerre et paix), `Warfare` (bandes de guerriers, batailles, butin),
    `Schism` (un groupe part fonder une colonie sœur) ;
  - les grandes décisions : `Prayers` (la colonie demande l'accord du joueur : barrage, alliance, guerre, paix, schisme).

### Agriculture

En climat doux, les cultures demandent **8,75 jours de pousse** (maturité à la neuvième mise à jour quotidienne), soit 25 % de plus que le cycle de référence de 7 jours. Leur rendement est augmenté du même facteur : **5 céréales par parcelle ordinaire au lieu de 4**. La fertilité du sol, les berges, l'irrigation et l'assolement sont inclus avant l'arrondi final en céréales entières. Le froid et les sécheresses ralentissent toujours la pousse ; les cultures non moissonnées gèlent au début de l'hiver.

### Ce que la colonie bâtit

Après les huttes et les ateliers du fer et du blé, une colonie dont la survie est assurée bâtit, dans cet ordre et un chantier à la fois
(`Civic.NextToBuild`) : un **enclos** (dès 6 habitants et des champs), un **puits** (8 habitants), un **métier à tisser** (quand la laine s'accumule),
un **entrepôt**, une **infirmerie** (après deux fièvres ou 12 habitants), un **marché** (après une caravane ou 14 habitants), une **taverne** (12) et
une **école** (3 enfants ou 16 habitants). Elle passe ceux dont elle ignore encore le savoir (voir ci-dessous) et les étudie en attendant.

### Savoirs et âges

Les emprises sont différenciées : puits et fût **1 × 1**, huttes et petits ateliers **2 × 2**, forge et services **3 × 2**, moulin **3 × 3**,
enclos, marché et entrepôt **4 × 3**. Les matériaux et le travail des constructions agrandies suivent leur surface.
Une **mine de 6 × 4** demande **120 bois, 100 pierres et 260 secondes de travail** : elle permet d'exploiter cuivre, or, charbon minéral et gemmes.
Le fer de surface et les carrières restent accessibles sans mine. Le **barrage de 5 × 3** (ou 3 × 5 selon le courant) coûte **100 bois, 240 pierres et
480 secondes de travail** ; il ferme jusqu'à trois cases de rivière et retient jusqu'à **160 cases d'eau**, avec pêche, berges fertiles et irrigation.
Les habitants choisissent les parcelles, les portes et les chemins selon ces emprises ; la demande de barrage conserve son passage par une prière.

Rien ne se bâtit sans le savoir qui va avec (`Knowledge.Required`) : l'**élevage** (enclos), la **métallurgie** (charbonnière, bas fourneau, forge),
la **meunerie** (moulin, four), la **maçonnerie** (puits, entrepôt), l'**irrigation** (canaux), puis le **tissage**, la **médecine**, la **brasserie**
(taverne, fûts), le **commerce** (marché), l'**hydraulique** (barrage), l'**écriture** (école) et la **diplomatie** (alliances). Les savoirs du bourg
apportent des avantages : **assolement** (bonus de référence de +1 céréale, augmenté avec le cycle agricole et arrondi dans le rendement total), **fortifications** (défense ×1,5), **art de la guerre** (attaque ×1,4),
**pharmacopée** (fièvres −40 %), **monnaie frappée** (caravanes +25 %, voyages −15 %), **philosophie** (savoir +30 %).

- Chaque peuple part avec ses savoirs : humains (agriculture, élevage, maçonnerie), nains (agriculture, métallurgie, maçonnerie),
  elfes (agriculture, médecine, irrigation), orques (agriculture, élevage, métallurgie). Une colonie sœur hérite de ceux de sa mère.
- Chaque jour, la colonie accumule des points de savoir : 0,1 par adulte (plus s'il est curieux), 0,25 par ancien, 0,04 par adolescent ;
  ×1,6 avec une école. Un savoir coûte 6, 20 ou 50 points selon son rang, et 12 % de plus pour chaque savoir connu au-delà des quatre premiers.
- La colonie étudie d'abord ce qui lui manque pour bâtir ce qu'elle veut (`Knowledge.Wish`), en remontant ce qu'il demande ; sinon le savoir le plus proche.
- Une caravane rapporte 15 % du coût de chaque savoir de son hôte (30 % entre alliés), et l'hôte apprend de même.
- Les âges : **bois**, **fer** (métallurgie), **village** (trois savoirs du village), **bourg** (trois savoirs du bourg). Mesuré sur quatre peuples :
  âge du fer en 1 à 3 ans, du village vers 5 ou 6 ans, du bourg vers 10 ans.

### Relations entre colonies

Chaque colonie a une opinion des autres, de −100 à +100, qui rejoint chaque jour de 3 points la somme de ses raisons (`Diplomacy.OpinionFactors`) :
affinité des peuples (même peuple +15, elfes et orques −25…), tempérament (les belliqueux aiment moins), caravanes échangées (+5 chacune, +25 au plus),
rancunes (−25 par point : barrage en amont, querelle de frontière, attaque), terres voisines (jusqu'à −25), envie d'un voisin riche, tentation d'un voisin
faible pour un peuple belliqueux, alliance (+25), guerre (−40), trêve (−10), diplomatie (+8), racines communes après un schisme (+25).

- **Querelles de frontière** entre colonies à moins de 5 cases : une rancune naît, surtout entre belliqueux. Les colonies pacifiques ou diplomates
  envoient des **présents** (jusqu'à 60 pièces) à une voisine qui les boude.
- **Alliance** : deux colonies qui s'estiment (50 et plus), dont l'une connaît la diplomatie, peuvent s'allier par une prière. Entre alliés, une caravane
  n'a pas besoin de rapporter plus que le voyage ne coûte, les savoirs circulent deux fois plus vite, et chacun envoie des renforts (0,15 par adulte valide)
  à l'autre attaqué. L'alliance se rompt quand l'opinion tombe sous 15.
- **Guerre** : une colonie hostile (−50 et moins), d'au moins 10 habitants, sans autre guerre et qui se croit au moins aussi forte, prie pour déclarer la guerre.
  Tous les quatre jours au plus, l'agresseur (et le défenseur s'il s'en sent la force) envoie une **bande** d'un adulte valide sur trois (2 à 8), armés de ses outils :
  elle marche sur la carte du monde, livre bataille (guerriers 1, armés 1,6 ; défenseurs 0,6 plus leurs outils ; fortifications, art de la guerre, renforts),
  pille un quart des pièces et des vivres si elle l'emporte, tue ou blesse des défenseurs, ou laisse ses morts sur le terrain. Plus de commerce entre ennemis.
- **Paix** : une colonie lasse ou battue prie pour la paix ; l'ennemi accepte s'il est las, s'il ne gagne pas ou si la guerre dure depuis 15 jours.
  Une trêve de 20 jours (30 avec la diplomatie) suit.
- **Schisme** : dans un village de 30 habitants (20 s'il est à l'étroit ou morose), un adulte ambitieux et peu enraciné rêve de sa propre colonie.
  Ses amis, son conjoint et leurs enfants le suivraient (le tiers du village au plus), avec leur part des réserves. Accordé par une prière, le groupe fonde
  une colonie sœur sur la région libre la plus agréable à moins de 8 cases, qui garde les savoirs et l'affection de sa mère.
  Un établissement secondaire d'au moins dix habitants, malheureux et mené par un ambitieux, peut aussi demander à se détacher : l'établissement entier devient la colonie sœur.
- **Offrandes et souhaits** : une colonie prospère dresse d'elle-même un autel ou une statue avec des matériaux réellement livrés (les pierres rares viennent des échanges), puis adresse au joueur un souhait
  (bénédiction de récolte ou faveur pour un guerrier). Le joueur est libre de l'ignorer, de le refuser ou de l'accepter ; aucun pouvoir n'est encore appliqué. Voir [Monnaie-offrandes.md](Monnaie-offrandes.md).
- **Monnaie frappée** : avec la monnaie frappée, un atelier de frappe transforme un peu d'or en pièces, dans la limite d'un quota annuel mondial partagé entre les colonies.

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
  elle agrandit d'abord l'enclos existant (`Husbandry.WantsAnotherPen`). Chaque extension accolée de **2 × 3 cases** coûte **15 bois**, demande **21 secondes de travail**
  et ajoute **4 poules, 4 moutons et 2 vaches** de capacité une fois achevée. Le bâtiment principal reste utilisable pendant le chantier.
  Un enclos accepte trois extensions ; faute de place ou après ces trois ajouts, le village peut en ouvrir un autre, dans la limite d'une capacité totale de six enclos ordinaires.
  Les bêtes sont soignées et abattues à l'enclos le plus proche du colon.
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
  des deux autres. Le marché troque la denrée locale avec les nomades (compétence de négoce), augmente de moitié la charge des caravanes, et les marchands
  expérimentés rabattent le coût des voyages. Le sel conserve les vivres, les épices relèvent le moral, une bûche de bois dur brûle comme trois de bois.
  Toutes ces marchandises, avec la laine et les vêtements, apparaissent dans l'écran Économie.
  Quand les échanges se développent, le marché gagne jusqu'à **trois extensions** de **2 × 3 cases** : **21 bois, 9 pierres et 36 secondes de travail** chacune.
  Les ajouts sont décidés progressivement à partir de **20 habitants**, puis **26** et **32**, après les besoins essentiels du village.
  Chaque extension achevée prépare **9 unités de charge supplémentaires** par caravane ; c'est le plus grand marché des deux partenaires qui fixe ce bonus, avant l'effet de la monnaie frappée.
- **Santé** : en hiver surtout, ou à jeun, ou à la belle étoile, un colon peut tomber malade ; un geste de travail peut le blesser. Il garde le lit
  (à l'infirmerie, il guérit deux fois plus vite) et peut succomber s'il n'a pas été soigné par un guérisseur (compétence de médecine). Le puits divise le
  risque de fièvre par deux. Les biomes froids brûlent plus de bois et connaissent des vagues de froid ; les biomes arides connaissent des sécheresses l'été.
- **Entrepôt** : au-delà d'une capacité de stockage, le grain, la farine, le pain et les œufs pourrissent en partie.
- **Événements** : un incendie détruit un bâtiment (le puits l'éteint à temps), des pillards attaquent une colonie qui a de quoi les attirer
  (des défenseurs adultes et des outils les repoussent), un colporteur propose des denrées lointaines ou achète un surplus.
- **Objectifs** : `Milestones.All` liste les jalons (population, premier pain, premier outil de fer, barrage, enclos, caravane, village…) ; chacun est célébré
  dans le journal et réjouit un peu tout le monde. L'écran Économie en donne le compte.

Les durées sont en « ticks » (voir `Simulation/Time/GameClock.cs`) : une année = 4 saisons × 5 jours = 20 jours,
un jour = 45 secondes à vitesse ×1.

## Lancer

```bash
dotnet test Simulation.Tests          # tests de simulation
dotnet build Game/GodColony.csproj    # à faire avant de lancer Godot : il charge la DLL compilée
```

Le jeu se lance depuis Godot .NET (version du SDK dans `Game/GodColony.csproj`) en ouvrant `Game/project.godot`.

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
**M** ouvre la carte du monde, **E** l'économie, **R** les savoirs et relations, **P** les prières, **J** le journal et **H** les commandes.
Le panneau **Savoirs et relations** montre l'âge de la colonie, le savoir à l'étude et l'arbre des savoirs, puis son opinion de chaque autre colonie
(avec ses raisons), ses pactes, ses batailles et les bandes de guerriers en marche. La carte du monde trace les alliances (vert), les guerres (rouge)
et les trêves (pointillé clair), et les bandes de guerriers y avancent sous leur fanion.
Ces raccourcis sont suspendus pendant la saisie d'un nom. **Échap** annule le renommage en conservant la fiche,
puis ferme les panneaux avant d'ouvrir le menu de pause. Les déplacements de caméra restent limités au terrain.

**Sauvegarder la partie** propose trois emplacements avec le nom du monde, la date, les colonies et la population.
**Charger une partie** les retrouve depuis l'accueil ou le menu de pause. **F5** enregistre dans un quatrième emplacement
de sauvegarde rapide ; **F9** le charge, après confirmation si une partie est en cours. Les fichiers vivent dans
`user://saves` (sous Windows, `%APPDATA%/Godot/app_userdata/GodColony/saves`).
Le terrain modifié, les colonies, les habitants, les familles, les travaux, les stocks, les caravanes, les prières et le hasard
sont conservés, ainsi que la caméra, la vitesse et la sélection. Remplacer un emplacement conserve sa version précédente,
chargeable avec **Charger la version précédente**. Un fichier invalide est refusé avant de remplacer la partie courante.
Le format courant est v10 ; les schémas figés v1/v3/v4/v5/v6 sont migrés au chargement. Voir `Simulation/Persistence/WorldSave.cs`, `SchemaV1.txt` et les tests de migration avant de modifier la persistance.

**Fonder une colonie** ouvre le choix du nom, du peuple et de 5 à 20 fondateurs. Cliquez d'abord sur une case libre
de la carte du monde (son biome et son relief donnent le terrain de la région), puis sur une zone plate de 5 × 5 cases sur le terrain. Le contour vert indique un site valide,
le rouge un obstacle ; **Emplacement conseillé** propose, sur la carte du monde comme sur le terrain, jusqu'à 5 choix numérotés (régions agréables pour le peuple, camps proches des ressources) : chaque clic passe au suivant. **Fonder la colonie** installe
les habitants à la case choisie, avec leurs provisions et 400 pièces. Le monde peut accueillir jusqu'à 16 colonies.
Chaque colonie possède sa propre carte locale ; les anciennes colonies restent à leur emplacement lors d'une fondation.
La simulation est suspendue pendant les menus et la fondation ; annuler conserve la vitesse et la partie précédentes.

### Graphiques des ressources

**E → Graphiques** affiche les flux de la colonie observée, ressource par ressource : **production**, **utilisation**,
**achats** et **ventes**, sur une même échelle en unités par jour. Choisissez les 20, 60 ou 240 derniers jours ;
le survol donne les quatre valeurs et la date exacte, et la légende permet de masquer chaque courbe.
Les moyennes de la période et le stock actuel complètent le graphe. Le panneau reste accessible à toutes les vitesses.

Les provisions initiales, les cargaisons en voyage et les retours de matériaux ne comptent pas comme production.
Les achats et ventes sont relevés au moment où l'échange est conclu, y compris ceux des colporteurs ; les pertes
(pourriture, pillage, incendie) sont séparées de l'utilisation. Celle-ci comprend les matériaux affectés aux chantiers
et aux recettes. Un relevé est pris chaque jour, même si le panneau est fermé ; les 240 derniers sont conservés
sans réduire les pics commerciaux. Comme les autres courbes, l'historique repart au chargement d'une partie,
sans changer le format des sauvegardes.

### Vue chiffrée (×200)

Les vitesses sont ×1, ×4 et ×30 (touches **1**, **2**, **3**) et **×200** (touche **4**), qui fait passer une année en 4,5 secondes.
À ×200, la carte et les habitants ne sont plus dessinés : leurs vues sont libérées, textures comprises, et la simulation reçoit
jusqu'à 25 ms par image au lieu de 10. À leur place, la **vue chiffrée** (`StatsPanel`) montre le monde (population et sa courbe,
colonies habitées, caravanes, tombes et leurs causes, vitesse réellement atteinte, temps écoulé) puis une carte par colonie :
habitants, humeur, réserves, pièces, bâtiments, bêtes, malades, tombes, jalons, alertes (réserves basses, froid, sécheresse,
prière en attente) et dernière pensée. Les courbes suivent au choix les habitants, les réserves, l'humeur ou les pièces ; sous les cartes,
la comparaison met les colonies sur la même échelle (les huit premières, chacune avec sa teinte, reprise par le liseré de sa carte).
Le survol d'une courbe en donne les relevés.

Les relevés (un par jour de jeu ; au-delà de 480, un sur deux) sont tenus par l'affichage (`View/ColonyHistory`) dès le début
de la partie, quelle que soit la vitesse, et repartent de zéro au chargement. La pause garde la vue chiffrée ; **1**, **2** ou **3**,
**Observer** sur une colonie (ou son bouton dans la barre des colonies) rendent la carte, repeinte telle que les années l'ont changée,
et la vitesse d'avant (en restant en pause si le jeu l'était). **Tab** désigne la colonie à retrouver, et celle de l'écran Économie.
Une partie enregistrée en vue chiffrée s'y recharge sans peindre la carte. Mesuré sur un Ryzen 5 7600X (version Debug du jeu,
quatre colonies) : ×200 tenu après dix ans de partie, la simulation prenant environ 6 ms par image.

Pour vérifier le parcours des contrôles Godot après compilation :

```bash
godot --path Game --fixed-fps 60 -- --smoke-menu
godot --path Game --fixed-fps 60 -- --smoke-saves
```

Le scénario vérifie l'accueil, une graine invalide, un monde vierge, la fondation, les paramètres persistants,
la pause, l'annulation, la reprise, le remplacement du monde, les raccourcis, la saisie, les limites de caméra, le défilement des panneaux
et la vue chiffrée (carte libérée, pause, choix des courbes, retour à la carte).
Le second scénario vérifie les sauvegardes, leur restauration,
les confirmations, les copies de secours, les fichiers endommagés, les raccourcis et le rechargement en vue chiffrée. Ils fonctionnent aussi avec `--headless`.
`--smoke-captures=chemin` enregistre les étapes dans un dossier existant lorsque le rendu est activé.

### Options de développement (en ligne de commande, après `--`)

`--capture=chemin.png` (capture puis quitte), `--advance-hours=N`, `--speed=1|4|30|200`, `--zoom=N`,
`--colony=N` (colonie observée), `--open-world` (panneau économie), `--open-graphs` (graphiques des ressources), `--open-map` (carte du monde), `--open-prayers`, `--demo-prayer`,
`--demo-dam`, `--auto-dam`, `--focus-dam`, `--demo-workshops`, `--demo-quarry`, `--select-first`,
`--focus-fields`, `--focus-quarry`, `--open-knowledge`, `--open-relations`, `--demo-war` (la colonie suivante déclare la guerre à la colonie observée
et envoie ses guerriers ; une troisième s'allie à l'observée). Les options sont lues dans l'ordre : `--auto-dam --advance-hours=1000 --focus-dam`.

Sans option, le jeu ouvre l'accueil. Les outils habituels démarrent directement la partie de démonstration à quatre colonies.
`--menu`, `--menu-world` et `--menu-settings` affichent les écrans de menu pour une capture ;
`--empty-world` démarre un monde vierge et `--demo-foundation` affiche un aperçu de camp prêt à confirmer.

`--perf=N` mesure N images puis affiche le temps par image (moyenne, centiles, pire image) et la part de la simulation,
avant de quitter ; en vue chiffrée, il donne aussi la vitesse réellement atteinte. Pour comparer deux versions sur la même machine :
`godot --path Game --fixed-fps 60 -- --advance-hours=480 --speed=30 --perf=1800`.
