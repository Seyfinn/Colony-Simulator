# Validation graphique — GodColony

## Vie du village — T-014 à T-022 — 2026-10-04

Les **44 PNG** de production sont issus de `VillageArt` et du système pixel art natif : 15 denrées et 1 jalon en 16 × 16 ; 8 bâtiments en 64 × 80 ; poules (12 × 12), moutons (16 × 14), vaches (24 × 18), quatre poses distinctes chacun ; 3 marques de santé en 12 × 12 ; 4 flammes/fumées et 1 ruine en 64 × 80. PNG RGBA8 transparents, noms du contrat conservés. L'enclos est vide : les bêtes viennent exclusivement des effectifs de la colonie.

Scène isolée `village.tscn`, avec des **données de démonstration** : bâtiments posés explicitement, troupeau, activités, maladies et climat préparés dans un nouveau monde, sans sauvegarde utilisateur. Le fond utilise `TerrainPainter`, les bâtiments et personnages utilisent le vrai `ColonistsView`, les vivres le vrai `Hud`, les jalons le vrai `WorldPanel`/`EconomyDashboard`. Les événements sont publiés par `Civic.Burn` et `Events.Peddler` ; le scénario pillards injecte explicitement un compte rendu de démonstration, sans attaque réelle ni perte ajoutée.

Après compilation, lancer Godot avec `--path Game res://Assets/validation/village.tscn -- --preview=<mode> --capture=<chemin>`. Modes : `gallery`, `village`, `empty`, `winter`, `drought`, `events`, `ashes`, `food`, `milestones`. `--compact` règle 1100 × 700. **Seul `--export-village` écrit les images de production**. `--fallback` ignore les PNG dans cette scène uniquement pour contrôler tous les secours natifs, sans déplacer ni supprimer de fichier.

Résultat attendu : **VILLAGE_VISUALS_OK**. Contrôles : tailles et quatre poses distinctes, troupeau nul, répartition entre plusieurs enclos et capacité maximale, zéro neige en désert même avec un état de froid, stocks/terrain inchangés par l'affichage. À cadrage fixe, le calque saisonnier doit être dessiné au plus trois fois pendant les 45 images du contrôle : il conserve ses commandes, sans peinture de terrain chaque image. Les changements de sol l'invalident via `TileChanged`, avec désabonnement à la fermeture de la vue.

Captures inspectées : `village_catalogue.png`, `village_village.png`, `village_empty.png`, `village_winter.png`, `village_drought.png`, `village_events.png`, `village_ashes.png`, `village_food.png`, `village_food_compact.png`, `village_milestones.png`, `village_milestones_compact.png`. `village_ui_economy*.png` et `village_ui_commerce*.png` utilisent la scène `economie_production.tscn` et ses contrôles d'onglets, jauges et défilement : **ECONOMY_PRODUCTION_UI_OK**, aux deux tailles. Le nombre réel de cartes de stocks détermine la hauteur du bandeau pour éviter que l'ajout récent de la bière recouvre la navigation.

Build **0 avertissement / 0 erreur**, **203 tests de simulation réussis**, parcours des menus **INTERFACE_SMOKE_OK**. Les tests `RecentEventTests` couvrent l'incendie avec/sans puits, le colporteur sans argent, la limite de 24 événements et l'absence d'impact de l'historique sur les données sérialisées (hasard inclus). Cet historique temporaire utilise une table faible, ne change pas le schéma des sauvegardes et ne rejoue pas les effets après chargement. Les tests de sauvegarde passent également. Le chantier préexistant de Claude reste présent dans le dossier ; seules les publications des comptes rendus sont ajoutées à ses fonctions d'événements.

## Berges des grands fleuves — 2026-10-04

`berges.png` compare prairie, désert, taïga et marais sur quatre cartes de démonstration générées avec la même géométrie (graine 42, 200 × 200) et des paramètres de biome distincts. Les cadrages contiennent de vrais fleuves générés ; aucune eau ni ressource n'est ajoutée pour la capture. Chaque panneau utilise `TerrainPainter.Paint`, `EnvironmentDetails.Draw` et `FloraPainter.Draw`, comme le jeu. `berges_en_jeu.png` montre le rendu dans la vraie scène principale, à l'embouchure du fleuve, avec `--focus-river --zoom=2`.

Après compilation, lancer Godot avec `--path Game res://Assets/validation/berges.tscn -- --capture=chemin.png`. Sans capture, la scène quitte aussi automatiquement et permet un contrôle `--headless`. Aucun changement de scène principale, aucun export de PNG de production.

La scène doit afficher **RIVERBANKS_OK** : peinture répétée identique, deux morceaux peints en parallèle exactement raccordés à la peinture entière, repeinture locale de rayon deux après inondation et creusement/remplissage d'un canal identique à une peinture complète. Les changements d'eau sont préparés uniquement sur une carte de contrôle distincte de la galerie. Empreintes avant/après du relief, sol, flore, croissance, humidité, eau, canaux, poissons, baies et débit inchangées par le rendu et les détails. Contrôle répété avec le dossier `terrain/` temporairement absent **dans la copie isolée**, puis remis en place : dessin procédural de secours validé.

Compilation isolée puis du projet principal : **0 avertissement / 0 erreur**. Captures finales inspectées. **Limitation du test général d'interface** : `--smoke-menu` échoue sur l'assertion du total/détail des aliments (`InterfaceSmokeTest.cs`). Cet échec est aussi reproduit sur la même copie avec les peintres d'origine ; il est indépendant des berges et signalé à Claude dans le fil des échanges. Les changements de village/cuisine déjà présents dans le dossier de travail ne sont pas inclus dans le commit graphique.

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

## T-023 à T-026 — bière, ressources, métiers et statistiques — 2026-10-04

`metiers.tscn` contrôle les **22 PNG** : onze icônes (dix denrées de base et bière) en 16 × 16, six bâtiments et trois états du fût en 64 × 80, deux barrages en 32 × 48. Icônes bordées d'une marge transparente d'un pixel, images identiques aux sources natives, trois états distincts, seuil exact de `BrewReadyTicks`, bulles identiques à ticks fixes et différentes quand le temps avance. La galerie charge les PNG ; `--fallback` désactive leur lecture dans cette scène seulement et contrôle les mêmes sources de secours. `python Game/Assets/tools/verifier_livraison.py` contrôle 55 PNG RGBA 8 bits, dont les 22 nouveaux.

Après compilation : `godot --path Game res://Assets/validation/metiers.tscn -- --capture=chemin.png`. Ajouter `--preview=local`, `--preview=stats` ou `--preview=comparison`, et `--compact` pour 1100 × 700. **Seule l'option `--export-workshops` écrit les PNG de production**, à partir de `WorkshopArt` / `BuildingSprites.Workshops` ; aucun export à l'exécution habituelle du jeu.

- `metiers_catalogue.png` : icônes ×3 et natives, huit bâtiments ×2, roue séparée, chantiers de barrage et trois fûts. Charbon éclairci pour rester visible sur le fond de l'interface. Repères sel/gâteau/lait de T-014 sur la même planche.
- `metiers_en_jeu.png` : véritable `ColonistsView`, terrain de graine 42, six bâtiments et trois fûts **placés pour démonstration** ; les échéances sont relatives au début du monde et la scène exige les états vide/fermentation/prêt. Horloge figée, aucune partie sauvegardée chargée. Terrain et stocks identiques avant/après les dessins.
- `metiers_ouvrages.png` : scène existante `ouvrages.tscn`, véritable moulin et roue sur le mur est ; les sprites gardent leurs points d'attache. Les chantiers et les barrages finis sont contrôlés ensemble sur la galerie, aux deux orientations.
- `statistiques_stats.png`, `statistiques_comparison.png` et leurs variantes `_compact` : véritables `StatsPanel`, `Sparkline`, `ColonyComparison`. Quatre colonies, 60 journées de **flux de démonstration explicitement ajoutés aux stocks**, relevés par `ColonyHistory` / `ResourceHistory` ; la simulation est figée pendant le rendu. Contrôle de la sélection des quatre indicateurs, des 61 relevés, de l'identité des cartes après rafraîchissement, du bouton Observer, du terrain et des stocks inchangés.
- `ressources_graphes_large.png`, `ressources_graphes_compact.png` : vrais `WorldPanel` / `ResourceGraphs` dans `economie_production.tscn -- --preview=graphs`. Vérification des 60 relevés, du passage de ressource, de la période de 20 jours, du retour aux 60 jours, du survol et du masquage d'une courbe. Les moyennes, légendes et notes restent accessibles par le défilement existant.
- `metiers_economy_*`, `metiers_commerce_*`, `metiers_food_*` : icônes dans les stocks, Économie, Commerce et Vivres aux deux tailles. Même scénario de contrôle que les scènes économie/village existantes, sans chargement de sauvegarde utilisateur.

Toutes ces captures ont été inspectées. Résultats : **WORKSHOP_VISUALS_OK**, **ECONOMY_PRODUCTION_UI_OK**, **VILLAGE_VISUALS_OK**. Compilation initiale du projet principal et compilation graphique finale isolée : **0 avertissement / 0 erreur**. **209 tests de simulation verts**, **INTERFACE_SMOKE_OK**, **SAVE_SMOKE_OK** avant la phase suivante du chantier de Claude.

Pendant l'intervention, Claude a commencé `Knowledge.cs` ; la compilation principale a alors signalé `Diplomacy` absent. La vérification et l'export finaux utilisent une copie isolée du jeu avec la dernière simulation compilée (`BuildProjectReferences=false`), puis seuls les 22 PNG sont reportés dans le projet principal. Aucun fichier de simulation ou de test de simulation modifié. Les cinq fichiers de statistiques non suivis de Claude gardent leurs modifications d'apparence locales ; `tools/habillage_statistiques.patch` en conserve **uniquement le delta graphique** pour l'intégration de son socle. Ce patch est déjà appliqué dans le dossier courant : ne pas le réappliquer ici. Pour une copie du socle avant habillage, le patch à contexte nul s'applique avec `git apply --unidiff-zero Game/Assets/tools/habillage_statistiques.patch`. Le commit graphique exclut les fichiers complets non suivis et les changements moteur de Claude.

Vérification finale après arrivée de `Diplomacy.cs` dans le projet principal : compilation **0 avertissement / 0 erreur**, **209 tests verts**, **WORKSHOP_VISUALS_OK** (local, statistiques, secours sans PNG) et **INTERFACE_SMOKE_OK** de nouveau réussis. Le blocage intermédiaire est levé ; le code livré est compilé dans le projet principal.
## Savoirs, relations et guerriers — T-027 à T-030

Scène isolée `civilisation.tscn`, sources natives `CivilizationArt.cs`. Les 24 PNG de production sont exportés **uniquement** avec `--export-civilization` ; une partie ne crée aucun fichier image. Les sources servent aussi de secours si un PNG manque ou si ses dimensions sont incorrectes.

Validation du 2026-10-04 : compilation sans avertissement ni erreur, **228 tests de simulation réussis**, `INTERFACE_SMOKE_OK`, `SAVE_SMOKE_OK`. `CIVILIZATION_VISUALS_OK` vérifie les tailles, la marge transparente des 19 icônes, leurs silhouettes distinctes, l'identité PNG/source, les quatre poses distinctes, l'identification des guerriers et des civils, les états connu/étudié/accessibles/verrouillés, les effets dans les infobulles, le passage entre les onglets et la conservation des cartes. Stocks, savoirs, opinions, rancunes, positions des colons, pactes, état des bandes et ticks restent identiques pendant les rafraîchissements et le dessin.

Modes validés : `--preview=gallery`, `knowledge`, `relations`, `map`, `return`, `local`. Le mode `--fallback` valide le secours natif avec le chargement des PNG désactivé dans cette scène seulement. La marche mondiale dépend des ticks de la simulation : elle se fige en pause. La marche locale et le mouvement des accessoires suivent la distance déjà parcourue ; aucune activité ni règle de jeu ajoutée.

Captures inspectées :

- `civilisation_gallery.png` : les 19 savoirs, les quatre poses des bandes, les épées croisées et l'équipement des quatre peuples dans les deux sens.
- `civilisation_knowledge.png`, `civilisation_relations.png` : panneau à 1600 × 900 ; `civilisation_knowledge_compact.png`, `civilisation_relations_compact.png` : 1100 × 700, panneau de 600 pixels et défilement vertical conservé.
- `civilisation_map.png`, `civilisation_return.png` : vraies routes, aller/retour, liserés des pactes et étiquette placée hors des noms et marqueurs des colonies.
- `civilisation_local.png` : vraie vue locale, guerriers équipés et civils sans accessoires de guerre.
- `civilisation_knowledge_en_jeu.png`, `civilisation_relations_en_jeu.png` : deux onglets dans la scène principale, avec `--demo-war`.

Dans cet environnement restreint, Godot émet des diagnostics sur l'accès au magasin de certificats et au cache de shaders ; les scènes, les exports, les captures et les contrôles ci-dessus terminent avec succès.

Les changements de `CivilizationPanel.cs` et `WorldMapView.cs` sont déjà appliqués aux fichiers locaux non commités de Claude. Le delta graphique seul est conservé dans `../tools/habillage_civilisation.patch` ; **ne pas le réappliquer aux fichiers déjà habillés**. Pour une autre copie ayant exactement le socle de cette livraison, utiliser `git apply --unidiff-zero` ; le contrôle inverse valide que le delta est effectivement présent.
