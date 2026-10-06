# Catalogue graphique — référence à la demande

Contrats généraux : [CAHIER_DES_CHARGES.md](CAHIER_DES_CHARGES.md). Lire seulement les éléments concernés et vérifier les fichiers présents pour leur état actuel.

## 4. Catalogue

Légende de l'état : **Code** = dessiné en code, rien à livrer d'obligatoire ; **À livrer** = demandé ;
**PNG** = fichier fourni et branché ; **—** = pas encore utilisé par le jeu.

### 4.1 Icônes de ressources — `icons/<ressource>.png`, **16 × 16**

| Fichier | Représente | État |
|---|---|---|
| `food.png` | Nourriture sauvage (baies, poisson) | PNG (T-024) |
| `grain.png` | Céréales | PNG (T-024) |
| `wood.png` | Bois | PNG (T-024) |
| `stone.png` | Pierre | PNG (T-024) |
| `ironore.png` | Minerai de fer | PNG (T-024) |
| `charcoal.png` | Charbon de bois | PNG (T-024) |
| `iron.png` | Lingot de fer | PNG (T-024) |
| `tools.png` | Outils de fer | PNG (T-024) |
| `flour.png` | Farine (sac) | PNG (T-024) |
| `bread.png` | Pain | PNG (T-024) |
| `coins.png` | Pièces (monnaie commune) | PNG (T-004) |
| `fish.png` | Poisson | PNG (T-014) |
| `eggs.png` | Œufs de l'enclos | PNG (T-014) |
| `milk.png` | Lait des vaches | PNG (T-014) |
| `meat.png` | Viande fraîche (se gâte vite) | PNG (T-014) |
| `saltedmeat.png` | Viande salée (se garde) : à distinguer de la fraîche | PNG (T-014) |
| `cake.png` | Gâteau de fête (six parts) | PNG (T-014) |
| `stew.png` | Ragoût (quatre bols) | PNG (T-014) |
| `chickens.png`, `sheep.png`, `cows.png` | Bêtes vivantes en réserve ou en vente (pas de la nourriture) | PNG (T-014) |
| `wool.png` | Laine | PNG (T-014) |
| `clothes.png` | Vêtements de laine | PNG (T-014) |
| `salt.png` | Sel (denrée de région) : à distinguer de la farine | PNG (T-014) |
| `spices.png` | Épices (denrée de région) | PNG (T-014) |
| `hardwood.png` | Bois dur (denrée de région) | PNG (T-014) |
| `beer.png` | Bière (chopes tirées du fût, servies à la taverne) | PNG (T-023) |

### 4.2 Bâtiments — `buildings/<type>[_<milieu>].png`

Les emprises suivent `Building.FootprintOf` : équipements 1 × 1 ; huttes et petits ateliers 2 × 2 ; services et forge 3 × 2 ; moulin 3 × 3 ;
enclos, marché et entrepôt 4 × 3 ; mine 6 × 4 ; barrage 5 × 3 ou 3 × 5 selon le courant. L'image ajoute 16 pixels vers le haut. Ancrage : milieu du bord bas.

| Variante actuelle | Taille | Source de secours |
|---|---|---|
| `well_small.png`, `cask_small.png` | 32 × 48 | `BuildingSprites.Large.cs` |
| `forge_large.png`, `infirmary_large.png`, `tavern_large.png`, `school_large.png` | 96 × 80 | Aile ajoutée au noyau du PNG original |
| `mill_body_<milieu>_large.png` | 96 × 112 | Bâtiment et aile sans coursier fixe ; roue orientée indépendante |
| `pen_large.png`, `market_large.png`, `storehouse_large.png` | 128 × 112 | Cour clôturée, trois étals ou grand hangar |
| `pen_extension.png`, `market_extension.png` | 64 × 112 | Parcelle avec mangeoire et abreuvoir ; deux étals avec auvents |
| `oven_<milieu>_extension.png`, `mill_body_<milieu>_extension.png` | 64 × 112 | PNG pour les cinq biomes ; module sans roue propre, cheminée ou sacs, secours dans `BuildingSprites.Scale.cs` |
| `mint_<milieu>.png` | 64 × 80 | PNG pour les cinq biomes ; presse, table et enseigne monétaire, secours propre |
| `shrine_<milieu>_large.png` | 96 × 112 | PNG pour les cinq biomes ; colonnes, autel et marches, secours propre |
| `minedepot_large.png` | 192 × 144 | Chevalement, entrée de galerie, hangars, aire de tri |
| `dam_large.png` | 160 × 112 | Mur de pierre, culées, vannes et passerelle |
| `dam_side_large.png` | 96 × 176 | Ouvrage orienté selon le courant |

Ces variantes sont procédurales tant qu'aucun PNG conforme n'est fourni ; aucune image n'est nécessaire à la simulation.

Monuments d'offrande : les cinq modèles sont dessinés depuis `Colony.Monuments` par `ColonistsView.Scale.cs` (Code). Les sorties d'atelier restent sur une étagère devant le bâtiment et ne sont jamais peintes comme un stock de dépôt. Les bénédictions sont des liserés et un marqueur de champion ; elles suivent les parcelles et les échéances du backend. Les captures des panneaux et des silhouettes sont répertoriées dans [validation/ECHELLE.md](validation/ECHELLE.md).
Les mines, enclos et marchés sont détaillés dans `BuildingSprites.Large.cs` et `BuildingSprites.RuralDetails.cs` : roche et galerie profonde,
treuil, charpentes, wagonnet, clôtures rivetées, paille, eau, toiles plissées et mobilier de marché. L'entrée de galerie est protégée par un auvent à une pente adossé à la roche. Le chantier de mine affiche des éléments entiers selon son avancement : fondations, ossatures, puis couverture et mécanismes, avec ses échafaudages ; sa silhouette n'est plus découpée horizontalement.
La galerie de contrôle est `validation/mines_enclos_marche_details.png` ; les extensions sans PNG sont visibles dans `validation/extensions_detaillees_secours.png`.
La correction de la mine est visible dans `validation/mine_toit_chantier_corriges.png` ; ses stades de construction dans `validation/mine_etapes_construction.png` (`--mine-stages`).
Les PNG ci-dessous restent les sources des noyaux et des petits ateliers.

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `hut.png` | Hutte (dort 4 colons) | 64 × 80 | PNG (T-025) |
| `kiln.png` | Charbonnière (tas de bois couvert de terre) | 64 × 80 | PNG (T-025) |
| `bloomery.png` | Bas fourneau (four de pierre à minerai) | 64 × 80 | PNG (T-025) |
| `forge.png` | Forge (foyer, enclume) | 64 × 80 | PNG (T-025) |
| `mill_body_<milieu>.png` | Moulin à eau sans roue ni coursier fixe, cinq milieux | 64 × 80 | PNG et source procédurale ; ancien `mill.png` conservé mais inutilisé |
| `mill_wheel_east_0.png` … `_3.png`, `mill_wheel_west_0.png` … `_3.png` | Roue à aubes de profil, côté de l'eau motrice | 24 × 40 | PNG et secours ; quatre poses, phase intégrée selon le débit |
| `mill_wheel_north_0.png` … `_3.png`, `mill_wheel_south_0.png` … `_3.png` | Roue de face, derrière au nord ou devant au sud | 40 × 32 | PNG et secours ; vanne, coursier et écume assemblés en code |
| `oven.png` | Four à pain | 64 × 80 | PNG (T-025) |
| `dam.png` | Barrage vu de face (franchit un cours d'eau est-ouest) | 32 × 48 | PNG (T-025) |
| `dam_side.png` | Barrage vu de côté (franchit un cours d'eau nord-sud) | 32 × 48 | PNG (T-025) |
| `dam_construction_0.png` … `_2.png` | Barrage en chantier (pieux, puis pierres, puis presque fini) | 32 × 48 | PNG (T-002) |
| `dam_construction_side_0.png` … `_2.png` | Les mêmes étapes vues de côté pour l'autre orientation du courant | 32 × 48 | PNG (T-002) |
| `pen.png` | Enclos **vide** (clôture, auge, abri de paille) ; les bêtes viennent de `animals/` (T-015) | 64 × 80 | PNG (T-017), enclos vide |
| `loom.png` | Métier à tisser sous auvent (laine, vêtements) | 64 × 80 | PNG (T-017) |
| `market.png` | Marché : étals à auvent rayé | 64 × 80 | PNG (T-017) |
| `infirmary.png` | Infirmerie : chaumière claire à croix rouge | 64 × 80 | PNG (T-017) |
| `storehouse.png` | Entrepôt : long hangar à portail double | 64 × 80 | PNG (T-017) |
| `well.png` | Puits : margelle, petit toit, poulie et seau | 64 × 80 | PNG (T-017) |
| `tavern.png` | Taverne : grande maison aux fenêtres chaudes, enseigne à chope | 64 × 80 | PNG (T-017) |
| `school.png` | École : chaumière à petit clocher, ardoise devant la porte | 64 × 80 | PNG (T-017) |
| `cask.png` | Fût : tonneaux de chêne cerclés où fermente la bière (vide, en fermentation, prête) | 64 × 80 | PNG (T-023) |

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

Berges des fleuves larges : **Code**, suggestion facultative de Claude livrée le 2026-10-04.
`TerrainPainter.Riverbanks.cs` ajoute plages de sable irrégulières, liseré humide, galets et eau peu profonde ;
`RiverbankDetails.cs` pose roseaux et touffes sur la terre, selon le biome. Eau plus froide en taïga/toundra,
plus verte en marais/jungle et transition du fleuve vers le lac adoucie. Aucun nouveau PNG de production ni changement de taille.

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
| `world/river_segment.png` | Tronçon de fleuve qui relie deux colonies (répétable) | 16 × 8 | **Plus utilisé** depuis la carte hexagonale (les fleuves sont dessinés case par case, voir 4.7) |
| `world/map_background.png` | Fond sobre de carte, sans géographie inventée | 1024 × 640 | **Plus utilisé** depuis la carte hexagonale (le monde a maintenant sa vraie géographie) |
| `peoples/trader_<espece>_<image>.png` | Colons de passage et marchands avec sac et bâton, quatre images de marche par espèce | 32 × 32 | PNG (T-008), branchés dans la vue locale |

### 4.7 Carte du monde hexagonale — `world/`

La carte du monde est une grille de **64 × 40 hexagones « pointe en haut »** (lignes impaires décalées d'un demi-hexagone
vers la droite), dessinée dans `Game/Scripts/WorldMapView.cs`. Chaque case a un **biome**, un **relief** et parfois une
**rivière** ; c'est elle qui donne son terrain à la carte locale d'une colonie qui s'y installe. Les onze sols et les trois
reliefs sont désormais illustrés en PNG (T-012). La palette de la légende et le secours procédural sont assortis aux images.

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `world/hex_<biome>.png` | Le sol d'une case, un fichier par biome (clés ci-dessous) | **32 × 37**, hexagone pointe en haut qui remplit l'image, transparent autour | PNG (T-012), onze fichiers branchés |
| `world/relief_hills.png`, `relief_mountains.png`, `relief_peaks.png` | Relief posé par-dessus le sol : collines, montagnes, sommets infranchissables (enneigés) | **32 × 37**, même cadre que l'hexagone, transparent | PNG (T-012), trois superpositions branchées |

Clés de biome (`WorldMapView.BiomeKey`) : `ocean`, `ice` (banquise), `tundra`, `taiga`, `temperate_forest`, `grassland` (prairie),
`steppe`, `desert`, `savanna`, `jungle`, `swamp` (marais). L'image est étirée à la taille de la case (le joueur zoome de 1 à 6 fois) :
prévoir un motif qui reste lisible petit (≈ 13 px de large au zoom minimal) — pas de détails d'un pixel.
Les fleuves (traits bleus d'un centre de case à l'autre, plus épais pour les grands fleuves), la case survolée, les marqueurs de
colonie (`world/colony_*.png`) et les caravanes (`world/caravan_*.png`) restent tels quels.

### 4.6 Autres éléments du monde (déjà dessinés en code)

Arbres, buissons, souches (`flora/`), parcelles de champ, feu de camp animé (4 images), bulles de discussion, symbole de sommeil.
Ils n'ont pas de PNG demandé pour l'instant ; les noms ci-dessus s'appliquent si on décide de les livrer.

Ambiance du crépuscule : **Code (T-011)**, teinte rose légèrement mauve et halo/rayons assortis dans `ArtDirection.cs` et `DayNightAmbience.cs`.

Aspect régional : **Code (T-013)**. `map.Biome` fixe les couleurs de l'herbe, de la terre et du sable, ainsi que les essences
d'arbres. Conifères en taïga, arbres bas en toundra, acacias en savane/désert, feuillage tropical en jungle, saules et roseaux
en marais. Les eaux stagnantes des marais sont verdâtres ; les fleuves gardent leur courant. La géométrie et les ressources
restent celles de la simulation. Les milieux locaux (berges, montagnes) continuent d'habiller bâtiments et habitants.

### 4.8 Bêtes de l'enclos et marques d'état — `animals/`, `effects/`

Ancrage : milieu du bord bas. Les animations sont des boucles de **4 images** (`<espece>_0.png` à `_3.png`), vers la droite ; le jeu applique un miroir pour l'autre sens.

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `animals/chicken_0.png` … `_3.png` | Poule (picore, marche) | 12 × 12 | PNG (T-015) |
| `animals/sheep_0.png` … `_3.png` | Mouton (broute, marche) | 16 × 14 | PNG (T-015) |
| `animals/cow_0.png` … `_3.png` | Vache (broute, marche) | 24 × 18 | PNG (T-015) |
| `effects/status_sick.png` | Marque de fièvre au-dessus d'un colon | 12 × 12 | PNG (T-016) |
| `effects/status_injured.png` | Marque de blessure | 12 × 12 | PNG (T-016) |
| `effects/status_boosted.png` | Marque de colon revigoré par le ragoût | 12 × 12 | PNG (T-016) |
| `icons/milestone.png` | Jalon atteint (étoile ou fanion) | 16 × 16 | PNG (T-022) |

### 4.9 Savoirs, guerre et diplomatie — `icons/`, `world/`

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `world/warband_0.png` … `_3.png` | Bande de guerriers en marche sur la carte du monde (trois silhouettes armées, lances et boucliers, vers la droite ; miroir pour la gauche) | 24 × 16 | PNG (T-027), quatre poses branchées, aller/retour |
| `world/pact_war.png` | Épées croisées posées au milieu du trait d'une guerre | 16 × 16 | PNG (T-027), épées croisées branchées |
| `icons/knowledge_<savoir>.png` | Une icône par savoir (19) : `agriculture`, `husbandry`, `metallurgy`, `milling`, `masonry`, `irrigation`, `weaving`, `medicine`, `brewing`, `commerce`, `hydraulics`, `writing`, `diplomacy`, `croprotation`, `fortification`, `warfare`, `herbalism`, `coinage`, `philosophy` | 16 × 16 | PNG (T-028), dix-neuf icônes branchées |

### 4.10 Territoires, nouvelles filières et village organique — Code

Les vingt nouvelles icônes 16 × 16 utilisent `ResourceIcons.cs`, avec transparence et remplacement PNG habituel dans `icons/` : `mineralcoal`, `clay`, `pottery`, `copperore`, `copper`, `copperware`, `flax`, `linen`, `hides`, `leather`, `shoes`, `grapes`, `wine`, `goldore`, `gold`, `ruby`, `sapphire`, `emerald`, `diamond`, `jewelry`.

`potterykiln.png`, `tannery.png` et `goldsmith.png` attendent chacun 64 × 80 ; leurs secours sont dans `BuildingSprites.Industries.cs`. La mine suit le contrat `minedepot_large.png` ci-dessus. Le catalogue visuel contrôlé est `validation/territoires-filieres.png`.

Établissements, gisements reconnus et observation locale : `TerritoryOverview.cs`, `WorldMapView.cs` et `ColonistsView.cs` (Code). Cultures de lin et de vigne : `ColonistsView.cs` (Code). Sentiers/chemins, espaces publics et accès : `TerrainPainter.Roads.cs`, `ColonistsView.Places.cs` (Code, T-031/T-032), aperçu `validation/village-chemins-places.png`. Ces vues lisent les données de simulation et ne posent aucun ouvrage.

### 4.11 Faune sauvage, transport et cueillette

Sprites dessinés pixel par pixel en C# dans `NatureArt.cs`, puis exportés en PNG transparent ; aucune génération d'image par IA. Une pose par espèce, miroir selon le déplacement. Les formes procédurales des hardes restent disponibles en secours.

| Fichier | Représente | Taille | État |
|---|---|---|---|
| `animals/wild_<espece>_0.png` | `rabbit`, `deer`, `boar`, `wolf`, `bear`, `junglefowl`, `mouflon`, `aurochs`, `horse` | 32 × 32 | 9 PNG branchés |
| `animals/dog_0.png`, `animals/cart_0.png` | Chien du chasseur, charrette du porteur | 32 × 32 | PNG branchés |
| `terrain/bridge_deck_1.png` … `bridge_deck_6.png` | Tablier continu avec garde-corps, orienté par `BridgeSite.Horizontal` | (32 × longueur) × 32 | PNG et secours identique ; chantier procédural selon `BuiltCells` |
| `terrain/bridge_landing.png` | Culée de pierre et raccord évasé au sentier de berge | 32 × 32 | PNG et secours ; placé selon les deux `Landings` |
| `icons/honey.png`, `wax.png`, `mushrooms.png`, `herbs.png`, `horses.png`, `oxen.png`, `dogs.png`, `carts.png` | Huit ressources, exportées de `ResourceIcons.cs` | 16 × 16 | PNG branchés |

Ruches et chantier de pont : code dans `ColonistsView.Nature.cs`. Champignons et herbes utilisent les icônes des ressources réellement disponibles. Légende des royaumes et informations de chef/roi/loyauté : `WorldMapView.cs`.
Contrôle reproductible : `validation/nature_transport.tscn` ; captures `nature_transport.png`, `nature_secours.png`, `nature_infobulle.png`, `porteur_infobulle.png`, `caravane_infobulle.png`, `royaumes_legende.png`, `colonie_infobulle.png` dans `validation/`.
