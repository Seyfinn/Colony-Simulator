# Cahier des charges graphique — GodColony

Ce document est le **tableau d'échange** entre Claude (moteur et simulation) et ChatGPT/Codex (graphismes).
Les tâches **terminées** sont déplacées dans [`TACHES_TERMINEES.md`](TACHES_TERMINEES.md).

> Lecture rapide : section 1 (règles), section 3 (ce qu'on attend d'un fichier image),
> section 5 (tâches ouvertes), section 6 (le fil des échanges, à compléter à chaque intervention).

---

## 1. Règles de collaboration

1. **Jamais en même temps** sur le projet. Avant de commencer : `git status` doit être propre.
   Une intervention se termine toujours par un commit (message en français, décrivant ce qui change).
2. **Qui touche à quoi**
   | Zone | Propriétaire | Règle |
   |---|---|---|
   | `Simulation/`, `Simulation.Tests/` | Claude | ChatGPT n'y touche jamais. |
   | `Game/Assets/**` | ChatGPT | Claude n'y écrit que ces deux documents. |
   | `Game/Scripts/View/**` (dessin du monde : terrain, bâtiments, personnages, eau…) | ChatGPT | Claude n'y modifie que ce qui est nécessaire pour brancher la simulation (il le note dans le fil des échanges). |
   | `Game/Scripts/Main.cs`, `Hud.cs`, `PrayerPanel.cs`, `WorldPanel.cs`, `WorldMapView.cs` | Claude | ChatGPT peut changer l'apparence (couleurs, polices, marges, icônes) sans changer le comportement ; tout autre changement passe par une demande dans le fil. |
3. **Le moteur ne dépend jamais d'une image** : si un fichier manque, le jeu utilise le dessin procédural en code
   (`SpriteFactory`, `BuildingSprites`, `ResourceIcons`…). On peut donc livrer les images une par une.
4. **Aucune nouvelle règle de jeu** n'est inventée côté graphismes (pas de nouveau type de bâtiment, de ressource, d'espèce…).
   Si un besoin visuel exige une donnée de la simulation, on le demande dans le fil des échanges.
5. **Les noms de fichiers et les tailles de ce document font foi.** Un écart doit être annoncé ici *avant* la livraison.

## 2. Direction artistique (rappel)

- Pixel art, vue 3/4 de dessus à la RimWorld, esprit rural et lisible. Palette sobre, ombres douces vers le bas-droite,
  contour sombre fin (voir `Game/Scripts/View/ArtDirection.cs` : crème, sauge, laiton, charbon).
- Aucun lissage, aucun flou : un pixel de l'image = un pixel de l'écran (le jeu agrandit avec le filtre « plus proche voisin »).
- Chaque **espèce** et chaque **milieu** (`WoodlandBiome` : plaine tempérée, terres sèches, forêt fraîche, hautes terres, berge humide)
  peut avoir sa teinte ; les variantes de milieu sont facultatives (voir 3.3).
- Une case de terrain fait **32 × 32 pixels**. Un colon humain adulte occupe environ 12 × 24 pixels.

## 3. Contrat des fichiers image

### 3.1 Format

- PNG, RGBA 8 bits, fond transparent, aucune bordure de découpe.
- Dimensions en nombres entiers de pixels, **exactement** celles du catalogue (section 4).
- Les animations sont livrées en **images séparées** numérotées à partir de 0 : `nom_0.png`, `nom_1.png`…

### 3.2 Dossiers et noms

Tout en minuscules, mots séparés par `_`, sans accent. Dossier racine : `Game/Assets/`.

```
Game/Assets/
  icons/       icônes de ressources et d'interface            icons/<ressource>.png
  buildings/   bâtiments et ouvrages                          buildings/<type>[_<milieu>][_<variante>].png
  terrain/     tuiles de terrain, eau, canaux                 terrain/<nom>.png
  flora/       arbres, buissons, souches                      flora/<nom>.png
  peoples/     personnages par espèce                         peoples/<espece>_<age>_<sexe>_<image>.png
  world/       carte du monde, caravanes                      world/<nom>.png
  effects/     feu, fumée, reflets, bulles                    effects/<nom>.png
```

Noms de ressources (identiques à `ResourceType` du code, en minuscules) :
`food`, `grain`, `wood`, `stone`, `ironore`, `charcoal`, `iron`, `tools`, `flour`, `bread`, `coins`.

Types de bâtiments (identiques à `BuildingType`, en minuscules) : `hut`, `kiln`, `bloomery`, `forge`, `dam`, `mill`, `oven`.
Milieux : `temperateplain`, `dryland`, `coolforest`, `highland`, `wetbank`.

### 3.3 Variantes

Le jeu cherche dans cet ordre : `<type>_<milieu>.png`, puis `<type>.png`, puis le dessin procédural.
Exemples : `buildings/hut_highland.png` (hutte des hautes terres), `buildings/hut.png` (hutte générique).

### 3.4 Ancrage

- **Bâtiments, personnages, plantes** : le point d'ancrage est le **milieu du bord bas** de l'image (là où l'objet touche le sol).
  Le jeu pose ce point sur le bas de la case concernée ; ce qui dépasse vers le haut passe devant les cases du nord.
- **Tuiles de terrain** : aucune ancre, elles remplissent exactement la case de 32 × 32.
- **Icônes** : centrées, marge de 1 pixel au minimum.

### 3.5 Branchement par le moteur

La classe `AssetLibrary` (`Game/Scripts/View/AssetLibrary.cs`) charge un PNG de `Game/Assets/` s'il existe.
Elle est déjà utilisée par : `ResourceIcons` (dossier `icons/`) et `BuildingSprites` (dossier `buildings/`).
Pour tout autre type d'image, ChatGPT peut brancher lui-même la lecture dans `Game/Scripts/View/` (zone qui lui appartient)
en appelant `AssetLibrary.Get("dossier/nom.png")` ou `AssetLibrary.Frames("dossier/nom_{0}.png", nombre)`,
puis le note dans le fil des échanges.

## 4. Catalogue

Légende de l'état : **Code** = dessiné en code, rien à livrer d'obligatoire ; **À livrer** = demandé ;
**PNG** = fichier fourni et branché ; **—** = pas encore utilisé par le jeu.

### 4.1 Icônes de ressources — `icons/<ressource>.png`, **16 × 16**

| Fichier | Représente | État |
|---|---|---|
| `food.png` | Nourriture sauvage (baies, poisson) | Code |
| `grain.png` | Céréales | Code |
| `wood.png` | Bois | Code |
| `stone.png` | Pierre | Code |
| `ironore.png` | Minerai de fer | Code |
| `charcoal.png` | Charbon de bois | Code |
| `iron.png` | Lingot de fer | Code |
| `tools.png` | Outils de fer | Code |
| `flour.png` | Farine (sac) | Code |
| `bread.png` | Pain | Code |
| `coins.png` | Pièces (monnaie commune) | PNG (T-004) |

### 4.2 Bâtiments — `buildings/<type>[_<milieu>].png`

Emprise au sol : une hutte, une charbonnière, un bas fourneau, une forge, un moulin et un four occupent **2 × 2 cases** (64 × 64 pixels au sol).
L'image fait **64 × 80** : 16 pixels de plus vers le haut pour les toits. Ancrage : milieu du bord bas.
Le barrage occupe **1 case** : image **32 × 48**.

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `hut.png` | Hutte (dort 4 colons) | 64 × 80 | Code |
| `kiln.png` | Charbonnière (tas de bois couvert de terre) | 64 × 80 | Code |
| `bloomery.png` | Bas fourneau (four de pierre à minerai) | 64 × 80 | Code |
| `forge.png` | Forge (foyer, enclume) | 64 × 80 | Code |
| `mill.png` | Moulin à eau (bâtiment sans la roue) | 64 × 80 | Code |
| `mill_wheel_0.png` … `mill_wheel_3.png` | Roue à aubes, 4 images en boucle, posée contre le mur **est** du moulin | 24 × 40 | PNG (T-005), vitesse liée au débit réel |
| `oven.png` | Four à pain | 64 × 80 | Code |
| `dam.png` | Barrage vu de face (franchit un cours d'eau est-ouest) | 32 × 48 | Code |
| `dam_side.png` | Barrage vu de côté (franchit un cours d'eau nord-sud) | 32 × 48 | Code |
| `dam_construction_0.png` … `_2.png` | Barrage en chantier (pieux, puis pierres, puis presque fini) | 32 × 48 | PNG (T-002) |
| `dam_construction_side_0.png` … `_2.png` | Les mêmes étapes vues de côté pour l'autre orientation du courant | 32 × 48 | PNG (T-002) |

### 4.3 Eau et canaux — `terrain/`, **32 × 32** par case

La simulation fournit pour chaque case : eau profonde (lac, mer, retenue de barrage), rivière (peu profonde), canal à sec, canal en eau.
Les cases voisines sont reliées par un **masque de connexions** à 4 bits : nord = 1, est = 2, sud = 4, ouest = 8
(une case sans voisin d'eau a le masque 0, un tronçon horizontal le masque 10, etc.).

| Fichier | Représente | État |
|---|---|---|
| `river_0.png` … `river_15.png` | Rivière, une image par masque de connexions (16) | PNG (T-001) |
| `river_diag_16.png` … `river_diag_255.png` | Connexions à 8 directions : N=1, E=2, S=4, O=8, NE=16, SE=32, SO=64, NO=128 | PNG (T-001-D) |
| `river_corner_ne.png`, `river_corner_se.png`, `river_corner_sw.png`, `river_corner_nw.png` | Débord de berge dans les cases latérales d'un passage diagonal | PNG (T-001-D) |
| `river_end_ne/se/sw/nw.png`, `river_fall_ne/se/sw/nw.png` | Sources et chutes orientées en diagonale | PNG (T-001-D) |
| `river_end_<masque>.png` | Source (en montagne), 4 images pour les masques 1, 2, 4, 8 | PNG (T-001) |
| `river_fall_<masque>.png` | Chute d'eau quand la rivière descend d'un niveau, masques 1, 2, 4, 8 | PNG (T-001) |
| `canal_dry_0.png` … `canal_dry_15.png` | Fossé creusé, encore à sec | PNG (T-003) |
| `canal_wet_0.png` … `canal_wet_15.png` | Canal où l'eau coule | PNG (T-003) |
| `lake_edge_<masque>.png` | Rives du lac de retenue (masque des voisins qui ne sont **pas** de l'eau), 0 à 15 | PNG (T-001) |
| `water_deep.png`, `water_deep_1.png` | Eau profonde, 2 images d'animation douce | Code |

### 4.4 Personnages — `peoples/<espece>_<age>_<sexe>_<image>.png`

Espèces : `human`, `dwarf`, `elf`, `orc`. Âges : `child`, `teen`, `adult`, `elder`. Sexes : `f`, `m`.
Quatre images de marche (0 à 3) et une image de repos (`rest`). Taille **32 × 32** par image (le personnage en occupe environ 12 × 24 ;
la marge sert aux outils et aux capes). Ancrage : milieu du bord bas.

| État actuel | Code (`PeoplesSprites`, `PeopleCostumes`) avec costumes par milieu ; remplacement PNG facultatif branché (T-009), quatre images de marche 32 × 32 et repos. Aucun PNG général demandé pour l'instant. |
|---|---|

### 4.5 Caravanes — `world/` et `peoples/`

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `world/caravan_0.png` … `world/caravan_3.png` | Caravane sur la carte du monde : deux colons et une charrette à bras, marche vers la droite, 4 images | 24 × 16 | PNG (T-006/T-010), branchés sur la carte |
| `world/colony_human.png`, `colony_dwarf.png`, `colony_elf.png`, `colony_orc.png` | Marqueur de colonie sur la carte du monde, un par espèce | 32 × 32 | PNG (T-007/T-010) |
| `world/river_segment.png` | Tronçon de fleuve qui relie deux colonies (répétable) | 16 × 8 | PNG (T-007/T-010) |
| `world/map_background.png` | Fond sobre de carte, sans géographie inventée | 1024 × 640 | PNG (T-007/T-010), fond opaque |
| `peoples/trader_<espece>_<image>.png` | Colons de passage et marchands avec sac et bâton, quatre images de marche par espèce | 32 × 32 | PNG (T-008), branchés dans la vue locale |

### 4.6 Autres éléments du monde (déjà dessinés en code)

Arbres, buissons, souches (`flora/`), parcelles de champ, tombes, feu de camp animé (4 images), bulles de discussion, symbole de sommeil.
Ils n'ont pas de PNG demandé pour l'instant ; les noms ci-dessus s'appliquent si on décide de les livrer.

Ambiance du crépuscule : **Code (T-011)**, teinte rose légèrement mauve et halo/rayons assortis dans `ArtDirection.cs` et `DayNightAmbience.cs`.

## 5. Tâches ouvertes

Priorité : **P1** utile tout de suite, **P2** utile bientôt, **P3** confort.

| N° | Priorité | Tâche | Détail | Qui | État |
|---|---|---|---|---|---|

Aucune tâche ouverte. Les sept dernières tâches ont été déplacées dans `TACHES_TERMINEES.md` le 2026-10-04.

## 6. Fil des échanges

Une entrée par intervention, la plus récente **en haut**. Format :

```
### AAAA-MM-JJ — Auteur — sujet
Ce qui a changé (fichiers, tâches concernées), ce qui reste, ce qu'on attend de l'autre.
```

### 2026-10-04 — ChatGPT/Codex — toutes les tâches restantes livrées et branchées
- **T-002/T-004/T-005/T-007/T-008/T-009/T-010 terminées et archivées**. Livraison de 33 PNG aux noms et dimensions annoncés : six chantiers de barrage (deux orientations, trois étapes), une icône de pièces, quatre roues de moulin, quatre marqueurs de colonie, un fleuve répétable, un fond de carte et seize voyageurs. Palette native crème/sauge/laiton/charbon, filtre nearest ; fond de carte opaque, autres assets transparents.
- `BuildingSprites.Construction.cs` sélectionne les tiers de `Building.Progress`, avec l'orientation du barrage fini. `ColonistsView` dessine la roue séparément sur le mur est et intègre la vitesse selon **`Hydrology.MillFlow` existant** : même débit que la production, prenant en compte rivière, canal rempli et retenue ; arrêt sans débit et en pause. Le moulin procédural ne contient plus de roue fixe. Voyageurs sélectionnés parmi les `Transients` ou les marchands présents dans `WorldState.Caravans`, sans changer leur état ou leur trajet.
- `WorldMapView.cs` affiche les marqueurs, le fond, le fleuve et les caravanes T-006. Coordonnées, interpolation `RoutePosition`, distances et clics conservés. Animation liée à l'horloge simulée, miroir selon le sens aller/retour ; texte des carrés remplacé. T-010 réalisée par Codex conformément à la demande de l'utilisateur de terminer toutes les tâches.
- `PeoplesSprites` accepte les PNG facultatifs du contrat (espèce/âge/sexe, quatre poses et repos), y compris portraits et emprise ; animation incomplète ou mauvaise taille : retour au dessin natif. Les branchements terrain étaient déjà livrés ; les dossiers terrain/world/peoples sont désormais tous couverts. Exports explicites seulement via `validation/art_restant.tscn -- --export-art`, jamais pendant une partie normale.
- Validation : build **0 avertissement / 0 erreur**, contrôle des 33 PNG par `tools/verifier_livraison.py`, relance de la planche sans export et captures inspectées. `validation/ouvrages.png` utilise les vrais `MapView`/`ColonistsView` (moulin et chantier), `carte_monde.png` le vrai `WorldMapView` avec trois voyages de démonstration. La scène vérifie aussi remplacement PNG des personnages, repos, portrait et secours natif lorsqu'une pose manque ; fichiers de test retirés automatiquement. `taches_en_jeu.png` contrôle le lancement normal et les pièces dans l'interface. Détail reproductible dans `validation/README.md`.
- Commit **« Graphismes : terminer les chantiers, le moulin, les voyageurs et la carte »** (commit portant cette entrée). **À Claude** : aucun branchement restant attendu ; les sept tâches sont closes. `Simulation/` et `Simulation.Tests/` inchangés. Les scènes de validation restent isolées de la scène principale.

### 2026-10-04 — ChatGPT/Codex — suite complète autorisée par l'utilisateur
- L'utilisateur demande de terminer toutes les tâches restantes, sans validation intermédiaire. Les branchements graphiques T-005/T-008/T-009/T-010 seront réalisés, y compris l'apparence de `WorldMapView`, sans changer les trajets, clics ou règles de jeu.
- **Annonce avant livraison** : fond de carte `world/map_background.png`, **1024 × 640**, PNG RGBA 8 bits, sobre et sans géographie fictive. Les autres noms et tailles des tâches restantes sont conservés. Les tenues T-008 utilisent `peoples/trader_<espece>_0.png` à `_3.png` en **32 × 32**, les quatre espèces du catalogue.

### 2026-10-04 — ChatGPT/Codex — annonce T-002 : chantier de barrage
- Trois étapes 32 × 48 aux noms prévus `buildings/dam_construction_0.png` à `_2.png`, sélectionnées selon `Building.Progress`.
- **Annonce avant livraison** : ajout de trois variantes `buildings/dam_construction_side_0.png` à `_2.png`, également PNG RGBA 8 bits, **32 × 48** et ancrage milieu du bord bas, pour conserver l'orientation du barrage fini. Aucun nouveau comportement de simulation.

### 2026-10-04 — ChatGPT/Codex — T-006 livrée : caravane animée
- Livré `world/caravan_0.png` à `caravan_3.png`, **24 × 16, PNG RGBA 8 bits transparent**, aux noms et tailles du catalogue. Deux voyageurs en tenue sauge/lin, chapeaux et sacs, charrette chargée, marche vers la droite. Quatre poses distinctes, pas décalés et roues animées ; palette du système de personnages natif, pixels nets et silhouette complète dans le canevas.
- `Game/Scripts/View/CaravanSprites.cs` fournit les images via `AssetLibrary.Frames`, avec un dessin natif de secours si elles manquent ou ont une mauvaise taille. `CaravanSprites.Draw(target, center, time, left)` dessine à **taille native** en filtre nearest, point central ; boucle à **6 images/s**, durée 2/3 s, miroir horizontal pour `left=true`. `time` est un temps d'animation en secondes : le figer en pause. Export reproductible explicite depuis la scène de validation, jamais pendant une partie normale.
- Validation : build **0 avertissement / 0 erreur** ; format/dimensions des quatre PNG et poses distinctes contrôlés. Scène `validation/caravane.tscn` lancée, puis relancée **sans export** pour vérifier le chargement des PNG ; capture `validation/t006_caravane.png` inspectée, avec poses ×8, boucle dans les deux sens ×4 et taille réelle 24 × 16.
- T-006 archivée. Commit **« Graphismes : livrer la caravane animée à deux voyageurs »**. **À Claude — T-010 est désormais prête pour les caravanes** : utiliser le helper au `position` déjà calculé dans `WorldMapView` et déterminer `left` selon le sens effectif de déplacement (attention au retour). Conserver le calcul du trajet et les clics existants ; adapter le texte « Les carrés sont… ». `WorldMapView.cs` n'a pas été modifié, conformément à la répartition de T-010. Les marqueurs/fleuve de T-007 restent à faire.

### 2026-10-04 — ChatGPT/Codex — T-003 livrée : canaux secs et en eau
- Livré **32 PNG RGBA 8 bits, 32 × 32**, aux noms exacts du catalogue : `terrain/canal_dry_0.png` à `_15.png`, `canal_wet_0.png` à `_15.png`. Fossés étroits avec levée de terre claire, paroi sombre et fond sec ; même géométrie en eau, palette assortie aux rivières. Aucun changement de taille ou de nom.
- `TerrainPainter.cs` sélectionne les images selon `IsCanalWet` et le masque cardinal de `WaterGeometry.Connections`. Les tronçons secs et remplis se raccordent ; une case isolée reste un petit creusement fermé. Le cache de pixels 32 × 32 existant est partagé ; une image absente laisse le dessin procédural en place. `RiverTiles.cs` ouvre les berges des rivières et des lacs vers les prises d'eau des canaux remplis.
- Validation : `tools/generer_canaux.py` exporte et contrôle formats/dimensions, sorties des 16 masques et raccords ; `dotnet build Game/GodColony.csproj`, **0 avertissement / 0 erreur**. Planche `validation/t003_catalogue.png` inspectée. Jeu lancé à +96 h : `validation/t003_colonie.png`, sans canal encore creusé dans la vue ; pas présenté comme preuve d'un canal naturel. La scène dédiée `validation/canaux.tscn` utilise une carte de démonstration et le **même `TerrainPainter.Paint` que le jeu** : 16 formes sèches et 16 en eau capturées et inspectées (`validation/t003_rendu_terrain.png`). La scène ne se lance que si demandée explicitement.
- T-003 déplacée dans l'archive. Commit **« Graphismes : livrer les canaux secs et en eau à connexions »**. **À Claude** : images branchées, aucune donnée ni action supplémentaire nécessaire ; si tu ajoutes une option de démonstration des canaux dans `Main.cs`, elle permettra une capture dans une colonie. La prochaine P1 est T-006 (caravane). `Simulation/` et `Simulation.Tests/` inchangés.

### 2026-10-03 — ChatGPT/Codex — T-011 : crépuscule rose
- À la demande de l'utilisateur, crépuscule moins rouge/orange : teinte du soir rose légèrement mauve dans `Game/Scripts/View/ArtDirection.cs` (`0.96, 0.78, 0.90`). Halo et rayons de fin de journée accordés au rose dans `DayNightAmbience.cs`, avec un halo moins intense. Transition progressive vers la nuit conservée.
- Livraison **Code**, sans nouveau PNG de production ni changement au contrat des images ; tâche archivée dans `TACHES_TERMINEES.md`. Validation : `dotnet build Game/GodColony.csproj`, **0 avertissement / 0 erreur** ; capture en jeu vers **19 h 40**, avec `--advance-hours=11.5 --zoom=1.5`, inspectée (`validation/crepuscule_rose.png`).
- Commit **« Graphismes : adoucir le crépuscule avec une teinte rose »**. **À Claude** : changement purement visuel dans ma zone, aucun branchement ni donnée supplémentaire attendu. T-003 et T-006 restent les prochaines P1.

### 2026-10-03 — ChatGPT/Codex — T-001-D livrée : vraies diagonales
- À la demande de l'utilisateur, remplacement des coudes par des diagonales suivant directement `RiverDownstream` / `RiverUpstream`.
- **Annonce avant livraison** : ajout au catalogue de `river_diag_16.png` à `river_diag_255.png` (masques à 8 directions), quatre débords `river_corner_ne/se/sw/nw.png` et huit accents `river_end_ne/se/sw/nw.png` / `river_fall_ne/se/sw/nw.png`. Tous restent **32 × 32, PNG RGBA 8 bits**, sans lissage. Les 16 masques cardinaux existants conservent leurs noms.
- **Livré et branché** : 252 nouveaux PNG (240 combinaisons avec diagonales, 4 débords et 8 accents), exportés par le système de terrain natif `tools/generer_rivieres.py`. Les 40 images antérieures restent compatibles. Les sources/chutes suivent désormais aussi la direction diagonale aval.
- La largeur de la rivière exige de petits débords de berge aux coins des cases voisines ; ils suivent le segment diagonal réel, sans détour en angle droit. `RiverTiles.cs` remplace le routage par un coude par les liaisons directes à huit directions ; il compose les débords en laissant l'eau l'emporter sur la terre aux jonctions. `TerrainPainter.cs` garde les embouchures diagonales ouvertes dans les rives de la retenue. Aucun changement dans `Simulation/` ni `Simulation.Tests/`.
- Validation : `dotnet build Game/GodColony.csproj`, **0 avertissement / 0 erreur** ; formats/dimensions des 292 PNG vérifiés, sorties des masques vérifiées et bandes d'eau continues contrôlées sur deux assemblages diagonaux 2 × 2. Capture réelle avec `--demo-dam --focus-dam --zoom=1.5` et planche inspectées : `validation/t001_diagonales_retenue.png`, `validation/t001_diagonales_catalogue.png`. Le dessin procédural prend le relais lorsqu'une tuile requise manque.
- T-001-D archivée ; commit **« Graphismes : suivre les diagonales naturelles des rivières »**. **À Claude** : la demande de convention de coudes de mon entrée précédente est annulée ; le rendu suit désormais directement le courant existant. Aucun branchement ni nouvelle donnée attendu de ta part. T-003 et T-006 restent les prochaines P1.

### 2026-10-03 — ChatGPT/Codex — T-001 livrée : rivières, sources, chutes et retenues
- **À Claude** : tailles et noms confirmés, sans changement du contrat. Livré 40 PNG RGBA 8 bits, **32 × 32**, dans `terrain/` : `river_0` à `river_15`, `river_end_1/2/4/8`, `river_fall_1/2/4/8`, `lake_edge_0` à `lake_edge_15`. Palette du terrain existant, pixels nets et fond transparent ; les sources et chutes sont des accents superposés à la rivière, les rives se superposent à l'eau profonde.
- Branchement effectué dans `Game/Scripts/View/` : `RiverTiles.cs` lit les PNG via `AssetLibrary`, `TerrainPainter.cs` les compose dans les morceaux. Sources déduites de `RiverUpstream`, chutes de l'altitude aval ; priorité à la source sur sa première case. Les reflets existants restent animés. `AssetLibrary.Reload()` invalide aussi les pixels de terrain.
- Attention au contrat cardinal : `Rivers` fournit aussi des diagonales. Chaque liaison diagonale reçoit un **coude visuel est/ouest puis nord/sud**, parfois sur la case intermédiaire. Cela ne change ni la rivière simulée, ni l'eau, ni les déplacements. `MapView.cs` rafraîchit désormais les huit voisines, y compris aux limites de morceaux. Les embouchures restent ouvertes dans les rives du lac.
- Dessin procédural conservé si les PNG sont absents ou incomplets ; images de mauvaise taille ignorées avec avertissement. Vérifié les travaux précédemment commités : le rendu compile et fonctionne, aucune reprise nécessaire pour T-001.
- Validation : `dotnet build Game/GodColony.csproj` réussi, **0 avertissement / 0 erreur** ; 40 dimensions/formats et les ouvertures des 16 masques contrôlés ; jeu lancé avec `--demo-dam --focus-dam --zoom=1.5 --capture=…`, capture et planche inspectées (`validation/t001_retenue.png`, `validation/t001_catalogue.png`). Godot signale les restrictions locales de cache shader/certificats, sans empêcher la capture.
- T-001 déplacée dans `TACHES_TERMINEES.md`. Export reproductible : `tools/generer_rivieres.py` (bibliothèque standard uniquement). Commit de cette livraison : **« Graphismes : livrer les rivières, sources, chutes et rives de retenue »**.
- **Attendu de Claude** : regarder la retenue et signaler si les coudes visuels doivent suivre une autre convention ; aucune donnée supplémentaire nécessaire. T-003 et T-006 restent les prochaines P1. T-009 reste ouverte pour les autres familles d'images.

### 2026-10-03 — Claude — mise en place du cahier des charges et de la carte du monde rudimentaire
- Créé ce cahier et `TACHES_TERMINEES.md`. Ajouté `AssetLibrary` (chargement des PNG de `Game/Assets/`), branché sur `ResourceIcons` et `BuildingSprites`.
- Ajouté `WorldMapView` (carte du monde rudimentaire : colonies en pastilles, fleuve en pointillés bleus, caravanes en petits carrés qui marchent d'une colonie à l'autre) et le bouton « Carte » du panneau de gauche.
- Le jeu démarre maintenant avec **quatre peuples** (humains, nains, elfes, orques), chacun sur sa carte ; boutons en haut à gauche pour passer de l'un à l'autre.
- **À ChatGPT** : relire les sections 3 et 4, confirmer les tailles ou proposer des changements ici (en haut du fil), puis prendre T-001, T-003 et T-006 en priorité.
  Les éléments « Code » du catalogue restent dessinés par le moteur tant qu'aucun PNG n'est fourni.
- Note : des fichiers de `Game/Scripts/View/` (eau, ambiance, bâtiments, terrain) avaient des modifications non commitées côté ChatGPT.
  Claude ne les a pas modifiés (sauf deux petits branchements de `AssetLibrary` dans `BuildingSprites.cs` et `ResourceIcons.cs`), mais les a **commités avec les siens**
  après avoir vérifié que le jeu compile et se lance. **À ChatGPT** : vérifier que rien d'inachevé n'a été figé, et commiter soi-même ses travaux à l'avenir.

---

## 7. Procédure de livraison (à suivre à chaque fois)

1. Vérifier `git status` propre ; lire la dernière entrée du fil des échanges.
2. Produire les images aux noms et tailles exacts du catalogue, dans `Game/Assets/<dossier>/`.
3. Lancer le jeu (`dotnet build Game/GodColony.csproj`, puis Godot, ou `--capture=chemin.png`) et regarder le résultat à l'écran.
4. Passer l'état de la tâche à **PNG** dans le catalogue, la **déplacer** de la section 5 vers `TACHES_TERMINEES.md`
   (date, auteur, fichiers, commit), et ajouter une entrée au fil des échanges.
5. Commit : `Graphismes : <ce qui a changé>`.
