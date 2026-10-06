# Cahier des charges graphique — GodColony

Ce document est le **tableau d'échange** entre Claude (moteur et simulation) et ChatGPT/Codex (graphismes).
Les règles communes sont dans [AGENTS.md](../../AGENTS.md). Les tâches terminées sont archivées dans [TACHES_TERMINEES.md](TACHES_TERMINEES.md), à consulter uniquement si nécessaire.

> Lecture rapide : section 1 (règles), section 3 (ce qu'on attend d'un fichier image),
> section 5 (tâches ouvertes). Consulter le catalogue séparé uniquement pour les éléments concernés.

---

## 1. Règles de collaboration

1. **Dossier partagé** : vérifier les modifications en cours et préserver les travaux des autres assistants. Suivre `AGENTS.md` ; pas de commit sans demande explicite.
2. **Qui touche à quoi**
   | Zone | Propriétaire | Règle |
   |---|---|---|
   | `Simulation/`, `Simulation.Tests/` | Claude | ChatGPT n'y touche jamais. |
   | `Game/Assets/**` | ChatGPT | Claude n'y écrit que les documents de coordination. |
   | `Game/Scripts/View/**` (dessin du monde : terrain, bâtiments, personnages, eau…) | ChatGPT | Claude n'y modifie que ce qui est nécessaire pour brancher la simulation (il le note dans le section 6). |
   | `Game/Scripts/Main.cs`, `Hud.cs`, `PrayerPanel.cs`, `WorldPanel.cs`, `WorldMapView.cs` | Claude | ChatGPT peut changer l'apparence (couleurs, polices, marges, icônes) sans changer le comportement ; tout autre changement passe par une demande dans le fil. |
3. **Le moteur ne dépend jamais d'une image** : si un fichier manque, le jeu utilise le dessin procédural en code
   (`SpriteFactory`, `BuildingSprites`, `ResourceIcons`…). On peut donc livrer les images une par une.
4. **Aucune nouvelle règle de jeu** n'est inventée côté graphismes (pas de nouveau type de bâtiment, de ressource, d'espèce…).
   Si un besoin visuel exige une donnée de la simulation, on le demande dans le section 6.
5. **Les noms de fichiers et les tailles du catalogue font foi.** Un écart doit être annoncé ici *avant* la livraison.

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
  animals/     bêtes de l'enclos (poule, mouton, vache)       animals/<espece>_<image>.png
  peoples/     personnages par espèce                         peoples/<espece>_<age>_<sexe>_<image>.png
  world/       carte du monde, caravanes                      world/<nom>.png
  effects/     feu, fumée, reflets, bulles                    effects/<nom>.png
```

Les ressources et bâtiments sont définis dans `ResourceType` et `BuildingType` ; vérifier ces énumérations et les noms du catalogue plutôt que maintenir une liste dupliquée ici. Les milieux correspondent à `WoodlandBiome`.

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
puis le note dans le section 6.

## 4. Catalogue

Ponts et moulins : les nouveaux contrats remplacent l'assemblage de tuiles de pont et le coursier fixe à l'est.
`terrain/bridge_deck_<longueur>.png` fait (32 × longueur) × 32, pour 1 à 6 cases ; `bridge_landing.png` fait 32 × 32.
Le tablier est continu, orienté par `BridgeSite.Horizontal`, les culées sont placées sur `Landings` et le chantier suit uniquement `BuiltCells`.
Les sources pixel art de `NatureArt` restent utilisées en secours, y compris pour le pont complet.
`buildings/mill_body_<milieu>.png` fait 64 × 80 et sa variante `_large.png` 96 × 112, sans roue ni eau peinte.
`mill_wheel_<côté>_<image>.png` comporte quatre poses : est/ouest en 24 × 40, nord/sud en 40 × 32.
Le montage de `WaterEffects` raccorde la roue à l'eau du côté renvoyé par `Hydrology.MillWater`, avec un coursier sec à débit nul.
La roue nord passe derrière le bâtiment, les autres devant ; sa phase intégrée se conserve au changement de débit.
La scène `validation/waterworks.tscn` propose `--preview=bridges`, `--preview=mills` et `--preview=places`,
avec `--fallback`, `--scroll` (menu) et `--capture=<chemin>` ; `--export-waterworks` exporte uniquement ces nouveaux éléments.
Le menu conserve les lieux fermés grisés, place la mention d'évacuation en tête, tronque avec points de suspension
et donne le libellé complet au survol ; son volet de 340 px défile au-delà de huit entrées.

Validation du 2026-10-05 : compilation sans avertissement ; contrôles de phase (pause, débit nul, variation de débit),
orientations issues d'Hydrology, progression des chantiers et absence de mutation des ouvrages par le dessin.
Captures dans `validation/` : `bridges_continuous.png`, `bridges_continuous_fallback.png`,
`mills_oriented.png`, `mills_oriented_fallback.png`, `colonies_menu.png` et `colonies_menu_scroll.png`.
Le menu montre quinze établissements de trois empires, trois évacuations et trois lieux fermés ; le rafraîchissement garde le volet ouvert.

Le contrôle global `--smoke-menu` s'arrête sur « Sans sélection, C doit rejoindre le camp »
(`InterfaceSmokeTest`, recentrage) ; les six contrôles ciblés ci-dessus passent indépendamment.

Contrat nature et transport : `animals/wild_<espèce>_0.png` (les neuf noms de `WildSpecies` en minuscules),
`animals/dog_0.png`, `animals/cart_0.png` et `terrain/bridge.png` font 32 × 32 pixels RGBA.
Les huit nouvelles icônes (`honey`, `wax`, `mushrooms`, `herbs`, `horses`, `oxen`, `dogs`, `carts`) font 16 × 16.
Les sources sont dessinées pixel par pixel dans `NatureArt`, selon la méthode procédurale demandée par l'utilisateur.

Les emprises différenciées demandées par l'utilisateur emploient désormais des variantes `*_large.png` (ateliers, entrepôts, marché, enclos, mine et barrage)
et `*_small.png` (puits et fût). Leurs dimensions suivent l'emprise réelle : largeur × 32, hauteur × 32 + 16. Les sources de secours sont dans
`BuildingSprites.Large.cs` ; les anciens PNG servent aussi de noyaux aux ailes des bâtiments agrandis. Le catalogue détaille ces nouveaux contrats.

Les agrandissements progressifs d'enclos et de marché utilisent `pen_extension.png` et `market_extension.png` (variantes de biome facultatives),
en 64 × 112 pour leur emprise de 2 × 3 cases. Leur rendu procédural distingue la parcelle clôturée et les nouveaux étals ; aucun troupeau n'est peint dans les images.

Voir [CATALOGUE_GRAPHIQUE.md](CATALOGUE_GRAPHIQUE.md) ; rechercher le nom ou le type de l'élément concerné.

## 5. Tâches ouvertes

Priorité : **P1** utile tout de suite, **P2** utile bientôt, **P3** confort.

| N° | Priorité | Tâche | Détail | Qui | État |
|---|---|---|---|---|---|

T-033 à T-036 livrées le 2026-10-06 : extensions de four et moulin, silhouettes de frappe et sanctuaire, monuments, économies d'échelle, village, monnaie et pouvoirs, territoires et routes. Contrats et captures : [catalogue](CATALOGUE_GRAPHIQUE.md), [validation](validation/ECHELLE.md). Les tâches sont archivées dans [TACHES_TERMINEES.md](TACHES_TERMINEES.md).


T-031 et T-032 terminées en code le 2026-10-05 ; les validations sont dans [TACHES_TERMINEES.md](TACHES_TERMINEES.md). Les travaux T-014 à T-030 y sont également archivés.

## 6. Coordination et archives

Noter ici uniquement un besoin d'intégration encore actif, avec fichiers concernés et action attendue. Retirer l'entrée lorsqu'elle est résolue. L'historique des anciennes interventions est conservé dans [HISTORIQUE_ECHANGES.md](HISTORIQUE_ECHANGES.md) ; il ne sert pas de liste de travail.

## 7. Procédure de livraison

1. Vérifier les modifications en cours et les tâches ouvertes.
2. Produire les éléments aux noms et tailles du catalogue.
3. Compiler, lancer Godot et regarder le résultat à l'écran ; adapter les contrôles au changement.
4. Actualiser l'état dans le catalogue et déplacer la tâche terminée vers `TACHES_TERMINEES.md` avec sa validation. Garder uniquement les besoins d'intégration actifs en section 6.
5. Faire un commit seulement si l'utilisateur le demande ; ne pas y inclure les travaux des autres.
