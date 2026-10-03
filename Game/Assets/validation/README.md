# Validation graphique T-001 — 2026-10-03

## T-006 — caravane — 2026-10-04

`t006_caravane.png` : quatre poses ×8, boucle droite/gauche ×4 et aperçu à taille native. Scène `caravane.tscn` utilisant `CaravanSprites.Draw` et les PNG livrés, avec filtre nearest.

Après compilation, lancer Godot : `--path Game res://Assets/validation/caravane.tscn -- --capture=chemin.png`. Ajouter `--export-caravan` pour réexporter explicitement les quatre PNG à partir du système pixel art natif ; sans cette option, la scène ne les écrit pas. Les fichiers sont RGBA 8 bits, 24 × 16, fond transparent, quatre poses distinctes. Build sans erreur/avertissement ; capture inspectée. La carte du monde attend le branchement T-010 par Claude.

## T-003 — canaux secs et en eau — 2026-10-04

- `t003_catalogue.png` : 32 tuiles au grossissement ×4, deux rangées sèches puis deux en eau, masques 0 à 15 dans chaque groupe.
- `t003_colonie.png` : contrôle du lancement normal, `--advance-hours=96 --focus-fields --zoom=1.5`. Aucun canal visible dans cette partie ; cette image vérifie uniquement le démarrage normal.
- `t003_rendu_terrain.png` : capture de `canaux.tscn`, scène isolée qui pose les 16 formes sèches et les 16 formes remplies sur une carte de démonstration. Utilise le même `TerrainPainter.Paint` que `ChunkView`, sélectionne les vrais PNG selon les masques calculés et ne modifie pas une colonie en cours.

Pour reproduire : compiler, puis lancer Godot avec `--path Game res://Assets/validation/canaux.tscn -- --capture=chemin.png`. Le projet conserve sa scène principale habituelle.

`python Game/Assets/tools/generer_canaux.py` exporte les 32 fichiers et vérifie leurs dimensions/formats, ouvertures et raccords. Build : 0 avertissement / 0 erreur. Planche et captures inspectées.

## T-011 — crépuscule rose

`crepuscule_rose.png` : capture en jeu à 19 h 40, avec `--advance-hours=11.5 --zoom=1.5`. Teinte rose légèrement mauve du soir, halo moins intense et rayons assortis. Build sans erreur ni avertissement ; capture inspectée.

## Extension T-001-D — diagonales

- `t001_diagonales_retenue.png` : nouvelle capture avec les mêmes options de barrage et de caméra que T-001 ; les coudes artificiels sont remplacés par les segments diagonaux réels.
- `t001_diagonales_catalogue.png` : exemples ×4, sans lissage. Deux rangées : masques 32, 160, 16, 80, 64, 128 puis 34, 40, 17, 132, 164, 176. Puis quatre sources, quatre chutes et quatre débords, chacun dans l'ordre NE, SE, SO, NO. Les accents sont montrés seuls et superposés en jeu.

L'export vérifie désormais 292 PNG RGBA 32 × 32 (40 initiaux + 252 ajouts), les masques à huit directions et la continuité d'une bande d'eau dans deux assemblages diagonaux 2 × 2. Les centres des cases latérales restent secs : aucun coude artificiel. Build : 0 avertissement / 0 erreur ; capture et planche inspectées.

## Livraison initiale T-001

- `t001_retenue.png` : capture du jeu avec `--demo-dam --focus-dam --zoom=1.5`, après compilation ; rivière, raccords diagonaux, barrage et retenue.
- `t001_catalogue.png` : planche au grossissement entier ×4, sans lissage. Lecture de gauche à droite : deux rangées de rivières (masques 0 à 15), une rangée d'accents de sources puis de chutes (1, 2, 4, 8), deux rangées de rives (0 à 15). Les accents sont présentés seuls ; dans le jeu ils se superposent aux rivières.

Les 40 fichiers de production dans `../terrain/` sont tous des PNG RGBA 8 bits en 32 × 32. `python Game/Assets/tools/generer_rivieres.py` réexporte et vérifie les dimensions et les ouvertures cardinales, sans dépendance externe.

`dotnet build Game/GodColony.csproj` : 0 avertissement, 0 erreur. Capture et planche regardées. La simulation n'a pas été modifiée.
