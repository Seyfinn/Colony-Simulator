# Tâches terminées — graphismes et interface

Archive des tâches finies. Les tâches en cours sont dans [`CAHIER_DES_CHARGES.md`](CAHIER_DES_CHARGES.md) (section 5).
Une ligne par tâche, la plus récente **en haut**. Quand une tâche du cahier est terminée, on la déplace ici
(et on met son état à **PNG** ou **Code** dans le catalogue).

| Date | N° | Tâche | Par | Fichiers / commit | Notes |
|---|---|---|---|---|---|
| 2026-10-03 | T-001 | Vraie rivière — PNG à connexions, sources, chutes, rives de retenue | ChatGPT/Codex | `terrain/river_0.png` … `_15.png`, `river_end_1/2/4/8.png`, `river_fall_1/2/4/8.png`, `lake_edge_0.png` … `_15.png` ; `View/RiverTiles.cs`, `TerrainPainter.cs`, `MapView.cs`, `AssetLibrary.cs` ; export `tools/generer_rivieres.py` ; commit **Graphismes : livrer les rivières, sources, chutes et rives de retenue** (commit portant cette entrée) | 40 PNG RGBA 32 × 32 branchés, fallback procédural ; diagonales raccordées visuellement, embouchures ouvertes. Build sans erreur/avertissement, masques contrôlés et capture en jeu inspectée dans `validation/`. |
| 2026-10-03 | — | Carte du monde rudimentaire (colonies, fleuve, caravanes en carrés) | Claude | `Game/Scripts/WorldMapView.cs`, `WorldPanel.cs` | À remplacer par les images de T-006 / T-007. |
| 2026-10-03 | — | Chargement des PNG de `Game/Assets/` | Claude | `Game/Scripts/View/AssetLibrary.cs`, `ResourceIcons.cs`, `BuildingSprites.cs` | Le moteur retombe sur le dessin en code si le fichier manque. |
| 2026-10-03 | — | Panneau « Économie » et boutons de colonie | Claude | `Game/Scripts/WorldPanel.cs` | Apparence provisoire, libre à ChatGPT de la refaire. |
| 2026-10-03 | — | Pastille de prière et panneau de décision | Claude | `Game/Scripts/PrayerPanel.cs` | Apparence provisoire. |
| 2026-10-03 | — | Icônes provisoires farine, pain, pièces ; sprites moulin et four | Claude | `ResourceIcons.cs`, `SpriteFactory.cs` | Remplacés ensuite par `BuildingSprites` (ChatGPT) pour les bâtiments. |
| avant 2026-10-03 | — | Personnages des quatre espèces avec costumes par milieu | ChatGPT | `PeoplesSprites.cs`, `PeopleCostumes.cs`, `BiomeVisuals.cs` | Dessinés en code. |
| avant 2026-10-03 | — | Bâtiments par métier (forge, moulin, four, barrage de face et de côté, charbonnière, bas fourneau) | ChatGPT | `BuildingSprites.cs` | Dessinés en code. |
| avant 2026-10-03 | — | Rendu de l'eau : rivières et canaux à connexions, reflets animés | ChatGPT | `WaterGeometry.cs`, `WaterEffects.cs`, `TerrainPainter.cs` | Dessinés en code ; T-001 et T-003 demandent de vraies images. |
| avant 2026-10-03 | — | Arbres par bosquets et par milieu | ChatGPT | `TreeSprites.cs`, `TreeDistribution.cs`, `FloraPainter.cs` | Dessinés en code. |
| avant 2026-10-03 | — | Ambiance jour/nuit et lumières du feu | ChatGPT | `DayNightAmbience.cs`, `ArtDirection.cs` | |
| avant 2026-10-03 | — | Tri en profondeur (colons derrière arbres et huttes) | Claude | `ColonistsView.cs`, `FloraPainter.cs` | |
