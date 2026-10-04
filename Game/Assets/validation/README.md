# Validation graphique — GodColony

## Économie et production — 2026-10-04

Les captures `ui_economy.png`, `ui_commerce.png`, `ui_recipes.png` et `ui_active.png` montrent les **vrais contrôles** de `WorldPanel` et `Hud`, en 1600 × 900. Les variantes `ui_economy_compact.png`, `ui_production_compact.png`, `ui_recipes_compact.png` et `ui_costs_compact.png` vérifient l'affichage en 1100 × 700. Les petites fenêtres masquent les renseignements régionaux secondaires dans la vue des stocks ; ils restent dans l'infobulle. Aucune sauvegarde utilisateur n'est chargée.

La scène `economie_production.tscn` crée un monde de contrôle de graine 42 et ajoute explicitement des **données de démonstration** (stocks, mesures de travail, ateliers et comptes rendus d'échange). La caravane est créée par `Trade.Depart`. Le mode `active` fait avancer ce monde avec `WorldState.Step` jusqu'à une véritable action d'artisanat commencée : sa jauge est comparée à la progression du modèle. Le fond vient du même `TerrainPainter` que le jeu ; les stocks de ces captures ne sont donc pas ceux d'une partie sauvegardée.

Après compilation, lancer Godot avec `--path Game res://Assets/validation/economie_production.tscn -- --preview=economy --capture=chemin.png`. Modes disponibles : `economy`, `commerce`, `production`, `recipes`, `costs`, `active`. Ajouter `--compact` pour la petite fenêtre. La scène se ferme après 50 images, après contrôle des stocks, des besoins, de la simulation inchangée par l'affichage, de l'identité du contenu et du défilement à travers plusieurs rafraîchissements, des boutons d'onglet et de repli, des manques et de la fabrication réelle. Résultat : **ECONOMY_PRODUCTION_UI_OK** dans les huit scénarios capturés.

Projet principal : `dotnet build Game/GodColony.csproj --no-restore` **0 avertissement / 0 erreur** ; parcours des menus, de la fondation, de la reprise, des raccourcis et du panneau économie : **INTERFACE_SMOKE_OK**. Le contrôle complet a été relancé avec l'accès aux fichiers temporaires Godot, car le bac à sable empêchait initialement la relecture de son fichier de paramètres. Les avertissements de cache/certificats du lancement restreint n'ont pas empêché les captures.

Intégration par remplacement ciblé des seuls panneaux, après comparaison avec la copie de préparation : les modifications récentes de Claude sur le lait, les vaches et l'abondance des bêtes ont été conservées. Les tableaux utilisent ses API de village présentes dans le dossier de travail ; ses fichiers de simulation et tests n'ont pas été modifiés ni inclus dans le commit graphique. Aucun changement de scène principale.

## T-012 / T-013 — biomes du monde et paysages locaux — 2026-10-04

- `t012_biomes.png` : onze hexagones et trois reliefs, agrandis ×4, puis à taille native ; chaque biome est aussi montré à 13 pixels de large. Les PNG de production font exactement **32 × 37, RGBA 8 bits**, avec sol opaque dans l'hexagone et coins transparents.
- `t012_carte_monde.png` : vrai `WorldMapView`, quatre colonies, zoom initial ×2,2 ; palette et légende assorties, reliefs et fleuves superposés. Un aplat sous les PNG élimine les interstices de découpe aux zooms fractionnaires ; il reste dans le calque de terrain fixe.
- `t012_vue_globale.png` : même contrôle, ramené au zoom minimal par son entrée de molette ; contrôle de la lecture à distance et des raccords.
- `t013_paysages.png` : neuf régions habitables générées par `MapStyle.For` à partir de vraies cases du monde (graine 42), avec leurs proportions de terrain et de forêt. Les cadrages choisissent des zones de la carte sans y ajouter de végétation. Chaque panneau utilise `TerrainPainter.Paint`, `EnvironmentDetails.Draw` et `FloraPainter.Draw`, comme le jeu.
- `t013_en_jeu.png` : scène principale habituelle, `--zoom=1.5 --capture=chemin.png`, avec les vrais habitants, cultures, eau et interface.

Scène isolée : `biomes.tscn`. Après compilation, lancer Godot avec `--path Game res://Assets/validation/biomes.tscn -- --capture=chemin.png` pour la planche, ajouter `--preview=world` pour la carte (et `--overview` pour la vue entière), ou `--preview=local` pour les paysages. **Seule l'option explicite `--export-biomes` écrit les 14 PNG de production**, depuis `WorldBiomeArt`. Aucun changement de scène principale.

À chaque lancement de cette scène, contrôle du chargement et des dimensions des images, de l'opacité du sol et de la transparence des coins. Onze régions de géométrie identique vérifient le déterminisme du terrain et des essences ; empreintes avant/après du relief, sol, flore, croissance, humidité, eau et baies inchangées. La taïga doit compter plus de 90 % de conifères ; les régions sèches plus de 85 % d'acacias. La galerie vérifie aussi l'empreinte après le dessin de la flore. `--smoke-menu` : **INTERFACE_SMOKE_OK**, fondation, panneaux et commandes existantes conservés.

**Contexte de compilation** : Claude a inclus les premières adaptations locales dans `d1958c8`, puis commencé d'autres modifications de simulation (type `Ailment` temporairement manquant). Les captures ont donc été vérifiées sur une copie de `d1958c8` avec l'intégralité des fichiers graphiques de cette livraison : build **0 avertissement / 0 erreur**, captures et smoke réussis. **Dernière vérification du projet principal : build 0 avertissement / 0 erreur et INTERFACE_SMOKE_OK**, après disponibilité des nouveaux types de simulation. Le dossier de copie est supprimé après contrôle ; aucune modification de simulation ni de ses tests par Codex. Les restrictions locales de cache shader/certificats de Godot n'empêchent pas les captures.

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
