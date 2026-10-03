# Validation graphique — GodColony

## T-002 / T-004 / T-005 / T-007 / T-008 / T-009 / T-010 — 2026-10-04

33 nouveaux PNG contrôlés par `python Game/Assets/tools/verifier_livraison.py` : dimensions exactes, noms du catalogue, RGBA 8 bits, étapes et roue distinctes. Build : 0 avertissement / 0 erreur. Les captures suivantes ont été inspectées :

- `toutes_taches.png` : trois étapes de barrage dans les deux orientations et barrage fini ; monnaie ; quatre marqueurs de peuple ; roue et corps du moulin séparés ; seize voyageurs. Scène `art_restant.tscn`, relancée sans export pour vérifier le chargement des fichiers livrés.
- `carte_monde.png` : le vrai `WorldMapView`, quatre colonies et trois caravanes. La scène isolée `carte_monde.tscn` prépare des voyages avec `Trade.Depart`, puis fige l'horloge à mi-trajet ; ce n'est pas une partie autonome ou sauvegardée. Elle vérifie aussi le chargement de quatre PNG de personnage, du repos et du portrait, puis le secours natif quand une pose manque. Les fichiers temporaires de test sont supprimés dans un bloc finally.
- `ouvrages.png` : les vrais `MapView` et `ColonistsView`, moulin placé avec `Urbanism.BuildInstantly` sur un emplacement admis par la simulation, chantier de barrage sur une rivière voisine ; caméra ×4. Scène isolée `ouvrages.tscn`. La roue est désormais séparée et son animation lit `Hydrology.MillFlow`, comme la production ; la phase intègre le débit, sans saut lors d'un changement de courant, et le temps se fige en pause dans le jeu.
- `taches_en_jeu.png` : lancement normal avec `--demo-workshops --demo-dam --focus-dam --zoom=1.5`, notamment l'icône de pièces dans l'interface. Le moulin n'y est pas cadré ; sa validation utilise la scène ouvrages.

Après compilation, lancer Godot avec `--path Game res://Assets/validation/<scene>.tscn -- --capture=chemin.png`. Seule `art_restant.tscn -- --export-art` écrit les 33 PNG depuis le système pixel art natif ; les autres lancements chargent les images. Aucun changement de scène principale ni de simulation. T-010 est maintenant intégrée, aucun branchement attendu de Claude.

## T-006 — caravane — 2026-10-04

`t006_caravane.png` : quatre poses ×8, boucle droite/gauche ×4 et aperçu à taille native. Scène `caravane.tscn` utilisant `CaravanSprites.Draw` et les PNG livrés, avec filtre nearest.

Après compilation, lancer Godot : `--path Game res://Assets/validation/caravane.tscn -- --capture=chemin.png`. Ajouter `--export-caravan` pour réexporter explicitement les quatre PNG à partir du système pixel art natif ; sans cette option, la scène ne les écrit pas. Les fichiers sont RGBA 8 bits, 24 × 16, fond transparent, quatre poses distinctes. Build sans erreur/avertissement ; capture inspectée. Branchement T-010 désormais terminé (voir ci-dessus).

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
