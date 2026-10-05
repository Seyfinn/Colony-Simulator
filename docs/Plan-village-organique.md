# Plan backend — création et croissance d’un village organique

Document de conception et d’architecture à transmettre à l’agent chargé de l’implémentation. Il contient des contrats et du pseudo-code, aucun code C# final. Les paramètres proposés constituent une première configuration à équilibrer par simulation.

**Révision performance :** les rendez-vous horaires et quotidiens déclenchent des contrôles légers et des demandes. Ils ne déclenchent pas systématiquement les recherches d’emplacement, A* ou analyses de quartier. La planification coûteuse est événementielle, incrémentale et soumise à un budget global au monde, conformément aux sections 9 et 10. Cette règle précise et remplace toute lecture « reconstruire le village chaque heure » des algorithmes conceptuels ci-dessous.

## 1. Décision de conception

La colonie doit devenir un **réseau de petits quartiers**, chacun assez compact pour ressembler à un lieu habité, mais séparés par des jardins, des champs, des bosquets et des chemins. Son organisation doit raconter sa croissance : le camp initial devient une place, les premières maisons forment un hameau, les activités attirent leurs propres bâtiments, puis des quartiers secondaires apparaissent le long des accès existants.

La simulation choisit les lieux, les accès et les projets. L’affichage lit ces décisions. Les quartiers ne sont ni des rectangles peints sur la carte ni des contraintes imposées par le joueur : ce sont des pôles de développement à vocation dominante.

### Règles du jeu

1. **Le terrain et les besoins déterminent la forme du village.** Les maisons cherchent le calme et les services ; les ateliers recherchent leurs ressources et leurs partenaires ; les champs privilégient la terre cultivable et l’accès au dépôt. Le moulin conserve son obligation de débit hydraulique.
2. **Les quartiers apparaissent quand une activité en a besoin.** À la fondation, seul le cœur existe. Aucun quartier industriel vide n’est construit pour compléter une composition visuelle.
3. **Le village grandit en plusieurs noyaux.** Une fois le premier groupe de maisons suffisamment rempli, la demande suivante favorise un nouveau groupe relié au village. Les bâtiments d’un même groupe restent proches ; les groupes sont plus espacés.
4. **Chaque projet possède un accès réservé et praticable.** On vérifie le chemin avant d’accepter le bâtiment ou le champ. Le terrain peut être parcouru avant d’être aménagé en route ; une route achevée n’est donc pas une condition qui retarde la première hutte.
5. **Les chemins commencent modestement.** Les passages répétés créent des sentiers. La colonie aménage ensuite les axes utiles en chemins de terre. Les trajets réutilisent les chemins existants et contournent les obstacles ; ils ne forment pas une grille ni une étoile obligatoirement centrée sur le feu.
6. **L’étalement reste économiquement viable.** Un emplacement trop coûteux en marche est refusé ou attend un entrepôt de proximité. Les routes accélèrent les trajets, mais ne permettent ni de franchir un lac ni d’ignorer le relief.
7. **Les quartiers conservent leur histoire.** Un nouveau besoin agrandit ou complète un quartier ; il ne téléporte pas ses bâtiments. Une destruction libère une parcelle, pas tout le plan du village.
8. **La survie garde la priorité.** En crise, les améliorations routières et l’expansion de confort attendent. Les règles physiques restent obligatoires ; les préférences esthétiques peuvent être assouplies pour loger des habitants.

Les nuisances industrielles servent d’abord à séparer les implantations. Cette version n’ajoute pas une simulation de pollution, de vent ou de maladies industrielles.

### Répartition fonctionnelle

| Vocation | Contenu principal | Implantation |
|---|---|---|
| Cœur civique | Feu, place, marché, puits, taverne, école, infirmerie | Accessible depuis les logements ; peut conserver les premières huttes |
| Résidentiel | Groupes de huttes, espaces de rencontre, jardins non productifs | Hors du voisinage immédiat des ateliers lourds, près d’un accès et des services |
| Industriel | Charbonnière, bas fourneau, forge ; ateliers légers selon les relations de production | Vers forêt/carrière et entrepôt, à l’écart des logements |
| Agricole | Groupes de champs, enclos, entrepôt ; éventuellement huttes rurales | Sol cultivable, proximité de l’irrigation si disponible, marge entre champs et habitat dense |

Le four et le métier à tisser peuvent occuper une transition entre habitat et activités. Le moulin crée au besoin un petit pôle productif sur la rivière. Un fût reste associé à une taverne, même si son implémentation actuelle le classe parmi les bâtiments civiques. Un barrage demeure un ouvrage hydraulique soumis aux règles et prières existantes, sans créer un quartier artificiel.

## 2. Diagnostic du projet existant

Les points suivants proviennent de la lecture du code actuel, y compris les modifications déjà présentes dans le répertoire de travail.

| Point d’intégration | Comportement observé | Modification prévue |
|---|---|---|
| `Colonies/Urbanism.cs` | Recherche dans une fenêtre de ±14 cases autour du feu ; préférence pour la distance minimale ; ateliers via le même mécanisme | Remplacer le choix spatial par le planificateur de quartiers ; garder une façade compatible |
| `Colonies/Farming.cs` | Champs 4 × 4, recherche à moins de 16 cases du feu | Conserver les parcelles et la croissance des cultures ; rechercher dans les pôles agricoles |
| `Colonies/ColonyFounder.cs` | Fondation, clairière du camp, `GatherSpots`, carrière, pathfinder | Initialiser le plan du village et évaluer les possibilités d’expansion autour du camp |
| `Colonies/ColonyBrain.cs` | `Think` horaire ; logement, réserve de lits, ateliers, canal, civique ; `PlanFields` au printemps | Conserver les décisions économiques ; centraliser l’admission des projets spatiaux |
| `Colonies/ColonistAI.cs` | Repas, dépôts et récupération de matériaux près du feu ; construction choisie surtout par proximité | Choisir des points de service accessibles et les chantiers selon leur priorité avant leur distance |
| `Map/LocalMap.cs` | Terrain, flore, relief, hydrologie et coût de déplacement | Ajouter une couche routière indépendante ; ne pas remplacer le type de sol |
| `Pathfinding/Pathfinder.cs` | A* à huit directions, relief, diagonales sans passage à travers les coins | Ajouter une politique de traversée et des coûts partagés avec le mouvement |
| `WorldState.cs` | `Step` à chaque tick, pensée horaire, traitements quotidiens | Conserver les cadences ; aucune recherche globale d’implantation à chaque tick |
| `Persistence/WorldSave.cs`, `StateGraph.cs` | Format v1, empreinte stricte des champs ; liste de types autorisés ; pathfinder reconstruit au chargement | Versionner le nouveau format, intégrer les nouvelles données et reconstruire les caches |

Deux précautions supplémentaires : `Civic.FindSite` doit recevoir le type précis du bâtiment au lieu de chercher toujours un emplacement d’atelier ; `Urbanism.BuildInstantly` doit router une hutte vers une implantation résidentielle. Les sondages de `FoodChain.NextWorkshopToBuild` doivent rester sans effet de bord.

Les cartes locales sont déjà grandes : taille par défaut 200 × 200, tailles 128/200/256 proposées dans les menus. Le premier changement porte sur leur utilisation, pas sur leur agrandissement. La carte du monde représente actuellement les colonies par un marqueur : agrandir le village local ne modifie pas automatiquement ce marqueur.

## 3. Limites et sources de vérité

### À conserver

- C# pur dans `Simulation/` ; aucune référence Godot, aucun pixel, aucune caméra, aucun sprite.
- Les `Building`, `Field`, `FieldPlot`, `Stockpile`, besoins, compétences, recettes et découvertes existants.
- Huttes et bâtiments ordinaires de 2 × 2 cases, champs de 4 × 4, barrage de 1 × 1 pour cette version.
- La capacité des huttes, le coût des bâtiments, les étapes des cultures, les prières et les trajets des caravanes sur la carte du monde.
- Les secteurs économiques existants. Un quartier industriel n’est pas un nouveau `WorkSector`.

Des ensembles de champs 4 × 4, décalés et séparés par des chemins, peuvent former une campagne irrégulière. Changer simultanément toutes les dimensions des bâtiments ou créer des champs polygonaux augmenterait inutilement le risque sur les lits, la récolte, l’irrigation et les sauvegardes.

### Sources de vérité

| Donnée | Source authoritative | Données dérivées |
|---|---|---|
| Terrain naturel et eau | `LocalMap` | Aptitudes et composantes accessibles |
| Objets, travaux, résidents | Objets existants `Building`, `Field`, `Canal`, `Colonist` | Occupation de la grille, statistiques de quartier |
| Identité des quartiers et parcelles | `SettlementLayout` | Enveloppes, saturation, sites candidats |
| Surface routière réalisée et usure | `RoadLayer` appartenant à `LocalMap` | Graphe des chemins réalisés |
| Tracé accepté d’un accès / travaux routiers | `RoadSegment` dans `SettlementLayout` | État de connexion, progression agrégée depuis ses cellules |
| Ressources | Unique `Colony.Stock` | Points où les habitants peuvent accéder à ce stock |

Un projet ne duplique jamais `Building.Progress`. Une route planifiée ne devient pas une route achevée par le seul fait d’exister dans une liste. Les caches ne prennent aucune décision non reproductible et ne sont pas nécessaires à la sauvegarde.

## 4. Architecture proposée

Les noms ci-dessous sont les contrats recommandés. Les services de calcul peuvent suivre le style statique actuel ; seules les données de jeu sont des objets persistants.

### Nouveaux fichiers dans `Simulation/Colonies/`

| Fichier / classe | Responsabilité et données principales |
|---|---|
| `SettlementLayout.cs` / `SettlementLayout` | État du développement : `Seed`, `RulesVersion`, `Districts`, `Parcels`, `Projects`, `RoadSegments`, compteurs d’identifiants, dates de planification, révision spatiale, demandes différées et leur motif |
| `District.cs` / `District` | `Id`, `Kind`, `AnchorX/Y`, `CreatedTicks`, `LastExpandedTicks`, `ParentDistrictId`, `Status`. Vocation et ancre stables ; emprise et charge calculées depuis les objets associés |
| `PlotReservation.cs` / `PlotReservation` | `Id`, `DistrictId`, `ProjectId` nullable, identifiant de l’objet occupant, `Kind`, cellules exactes, accès, `State`. Sépare emprise bâtie, cour facultative, espace public et corridor ; corridors partageables |
| `DevelopmentProject.cs` / `DevelopmentProject` | `Id`, demande d’origine, priorité, parcelle, bâtiment/champ concerné, segments d’accès et prérequis de défrichage. État de coordination, sans seconde progression du bâtiment |
| `SettlementRules.cs` / `SettlementRules` | Paramètres, budgets, seuils, profils et version des règles. Configuration immuable et reproductible |
| `BuildingPlacementProfile.cs` / `BuildingPlacementProfile` | Vocation préférée/compatible, emprise, besoin de débit, espace d’accès, distance aux nuisances, affinités avec partenaires, services nécessaires |
| `SettlementPlanner.cs` / `SettlementPlanner` | Orchestration : créer une proposition, décider densification/expansion, accepter atomiquement, annuler, réévaluer les demandes bloquées |
| `SettlementPlanningScheduler.cs` / `SettlementPlanningScheduler` | Avancer les recherches par petits lots à cadence fixe de simulation ; budget partagé entre toutes les colonies, priorité et équité déterministes |
| `PlanningJob.cs` / `PlanningJob`, `SettlementPlanningState` | État persistant des demandes en file, étapes, curseurs et propositions prêtes. `SettlementPlanningState` appartient au monde ; les travaux gardent leur colonie propriétaire |
| `DistrictPlanner.cs` / `DistrictPlanner` | Trouver une ancre viable et des prolongements d’un quartier ; mesurer saturation et potentiel sans réserver tout le territoire |
| `SitePlanner.cs` / `SitePlanner` | Tester les contraintes physiques, noter les parcelles et leurs accès, produire une proposition sans modifier le monde |
| `SettlementServices.cs` / `SettlementServices` | Calculer les lieux de repas, rencontre, dépôt et récupération de matériaux ; choisir par temps de trajet ; aucun inventaire parallèle |
| `RoadPlanner.cs` / `RoadPlanner` | Trouver un accès continu, partager les tronçons existants, proposer un aménagement ou une liaison complémentaire utile |
| `RoadDevelopment.cs` / `RoadDevelopment` | Comptage des passages, vieillissement quotidien, travaux par cellule et propositions d’amélioration ; aucune recherche globale au déplacement d’un colon |

`DevelopmentRequest`, `PlacementProposal`, `PlacementFailure` et `SettlementSensors` peuvent être définis dans les fichiers correspondants. Ce sont des valeurs de calcul ; ne les persister que lorsqu’une demande différée influence la suite de la simulation.

### Nouveaux fichiers dans `Simulation/Map/` et `Simulation/Pathfinding/`

| Fichier / classe | Responsabilité |
|---|---|
| `Map/RoadLayer.cs` / `RoadLayer` | Tableaux plats indexés `y × Width + x` : surface réalisée, trafic accumulé, observation quotidienne, travail d’aménagement par cellule. Même dimensions que la carte |
| `Map/LocalSpatialIndex.cs` / `LocalSpatialIndex` | Cache reconstructible : occupation par bâtiment/champ/tombe/canal/parcelle, corridors, entrées, points de service ; accès direct aux cellules |
| `Map/TerrainSuitability.cs` / `TerrainSuitability` | Cache de constructibilité, aptitude agricole, distances aux ressources visibles et composantes accessibles. Invalidation locale sur mutations pertinentes |
| `Pathfinding/TraversalCost.cs` / `TraversalCost` | Coût d’un pas et durée effective partagés par A*, déplacements et budgets du planificateur |
| `Pathfinding/LocalNavigation.cs` / `LocalNavigation` | Adapter le pathfinder aux occupations et aux entrées ; concaténer sortie du bâtiment, trajet extérieur et entrée intérieure si nécessaire |
| `Pathfinding/IncrementalPathSearch.cs` / `IncrementalPathSearch`, `PathSearchState` | A* reprenable pour les recherches du planificateur ; contexte indépendant du pathfinder individuel et frontière de recherche persistable |

`RoadSegment` peut être défini avec `RoadPlanner` : `Id`, cellules ordonnées contiguës, parcelles propriétaires, projet de travaux actif éventuel, fonction de liaison, surface cible et priorité. La propriété d’un accès repose sur une parcelle durable, pas sur un projet qui sera purgé après achèvement. Le graphe n’est pas une seconde grille persistante.

### Énumérations

| Nom | Valeurs et usage |
|---|---|
| `DistrictKind` | `Civic`, `Residential`, `Industrial`, `Agricultural` |
| `DistrictStatus` | `Emerging`, `Active`, `Dormant` ; absence d’activité ne détruit pas l’identité |
| `ParcelKind` | `Building`, `Field`, `PublicSpace`, `AccessCorridor` |
| `ReservationState` | `Reserved`, `Occupied`, `Released` |
| `DevelopmentKind` | `Housing`, `Field`, `Workshop`, `Civic`, `Logistics`, `RoadImprovement`, `AccessRepair` |
| `DevelopmentPriority` | Ordre explicite : `Survival`, `Housing`, `Production`, `Comfort` ; à affiner avec les capteurs existants |
| `ProjectState` | `Accepted`, `Working`, `Paused`, `Completed`, `Cancelled`, `Blocked` |
| `RoadSurface` | `None`, `Trail`, `DirtRoad` ; pavage réservé à une extension ultérieure |
| `PlacementFailureKind` | `NoSuitableTerrain`, `NoAccess`, `TravelBudgetExceeded`, `NoCompatibleDistrict`, `NoSpace`, `SearchBudgetExceeded`, `PrerequisiteMissing` |

### Ajouts aux classes existantes

- `Colony` : `Layout`, accès à un index spatial et à la navigation reconstructibles ; `SpatialSensors` pour éviter d’alourdir le record économique `ColonySensors`. Ne pas confondre les réservations de parcelles avec `Colony.Reserved`, qui réserve actuellement les cibles des activités individuelles.
- `WorldState` : `SettlementPlanningState` et appel au planificateur global aux ticks prévus. Aucune référence à la vitesse d’affichage ; changer de ×1 à ×200 ne change pas les décisions obtenues au même tick.
- `Building` : identifiant local stable, `DistrictId`, `ParcelId`, cellule d’entrée intérieure et cellule d’accès extérieure. `X/Y`, les lits et `Residents` gardent leur sens actuel. Pour le barrage, une cellule de travail sur la berge remplace l’entrée intérieure.
- `Field` : identifiant local stable, `DistrictId`, `ParcelId`, `AccessX/Y` à l’extérieur du champ. Les `Plots` restent les cultures productives, jamais les cellules du chemin.
- `LocalMap` : `Roads`, révisions distinctes de traversabilité et coût, événement de changement routier. Le tableau des routes appartient à `RoadLayer`, pas à `SoilType` ou `Surface`.
- `Activity` : ajouter à la fin de l’énumération `BuildRoad` et `ClearAccess` ; référence au segment/cellule pour les travaux. Réutiliser `Chop` pour un arbre lorsqu’il doit être abattu normalement. Utiliser la compétence `Construction` pour l’aménagement.
- `Colonist` : version de traversabilité du chemin courant si nécessaire ; aucun « métier résidentiel » ni obligation de travailler dans son quartier d’habitation.

Tous les bâtiments/champs existants et tous les nouveaux projets disposent d’identifiants locaux déterministes. Aucun identifiant ne dépend de l’ordre d’itération d’un dictionnaire.

### Contrats des méthodes principales

| Méthode conceptuelle | Entrées → sortie / effets |
|---|---|
| `SettlementPlanner.InitializeLayout` | Colonie et carte nouvellement fondées → cœur initial et index ; appelée une seule fois par fondation |
| `SitePlanner.TryPropose` | Demande, état et budget → `PlacementProposal` ou `PlacementFailure` ; aucune mutation |
| `DistrictPlanner.FindGrowthCandidates` | Profil, quartiers et aptitude → liste ordonnée d’ancres/sites, sans réservation |
| `RoadPlanner.FindConnection` | Accès et occupation future → tracé, coût et prérequis ; aucune mutation |
| `SettlementPlanner.TryCommit` | Proposition et révision évaluée → projet accepté, ou échec sans effet partiel |
| `SettlementPlanner.OnObjectCompleted` | Bâtiment/champ et projet → parcelle occupée, quartier actif et services reconstruits |
| `SettlementPlanner.OnObjectRemoved` | Objet détruit/annulé → libération cohérente, activités réévaluées et index invalidé |
| `SettlementServices.FindBest` | Colon, usage, destination suivante éventuelle → point accessible et chemin, ou absence |
| `LocalNavigation.FindPath` | Départ, objectif d’activité, mode et occupation → chemin valide incluant les accès intérieurs utiles |
| `RoadDevelopment.OnStepCompleted` | Pas réellement terminé → trafic local et éventuel franchissement de seuil |
| `RoadDevelopment.OnDayStart` | Jour et état économique → usure, bilan et demandes d’amélioration bornées |

Une proposition complète contient : demande d’origine, quartier existant ou nouvelle ancre proposée, emprise, entrée et accès, tracé de raccordement, services opérationnels utilisés, temps de trajet, prérequis, détail du score et révision évaluée. Un `ServicePoint` est une valeur dérivée : identifiant stable issu de son camp/bâtiment/parcelle, usages compatibles et cellules d’interaction. Un espace de rencontre ne vaut pas automatiquement un point de dépôt.

La façade `Urbanism` délègue à ces contrats. La recherche d’atelier doit recevoir le `BuildingType` exact ; actualiser les appelants plutôt que deviner le type. `Find*` reste une sonde pure, et `Plan*` revalide l’emplacement demandé avec le profil et l’accès. Les outils `BuildInstantly` peuvent conserver leur exemption économique de développement, mais respectent les contraintes physiques, associations de quartier et notifications d’achèvement. Aucun appelant ordinaire ne contourne l’admission par une insertion directe dans `Buildings` ou `Fields`.

Une sonde pure n’est pas nécessairement rapide. Les producteurs de demandes et capteurs horaires n’appellent donc pas les recherches complètes `Find*`. Ils utilisent les préconditions économiques et un résultat spatial en cache (`Possible`, `WaitingForChange`, `Pending` ou proposition prête). Adapter notamment le sondage du moulin dans `FoodChain.NextWorkshopToBuild` pour qu’il ne lance pas une recherche hydraulique et spatiale à chaque pensée. Les helpers synchrones complets peuvent rester pour les outils de développement et les tests, sans être le chemin d’exécution normal de `Think`.

### Profils pour tous les bâtiments actuels

| `BuildingType` | Vocation préférée | Relations / contrainte spécifique |
|---|---|---|
| `Hut` | Residential ; Civic compatible au début ; Agricultural pour un noyau rural | Accès aux repas et repos, calme, place pour les sorties et quatre lits |
| `Kiln` | Industrial | Industrie lourde ; forêt et dépôt ; voisinage des fours et forge |
| `Bloomery` | Industrial | Industrie lourde ; minerai visible, charbonnière, forge, dépôt |
| `Forge` | Industrial | Industrie lourde ; bas fourneau et dépôt |
| `Mill` | Industrial ou bord d’un pôle Agricultural | Débit hydraulique impératif, dépôt de grain, four accessible |
| `Oven` | Industrial léger ou transition Civic | Moulin/dépôt, proximité du lieu de distribution ; éviter le groupe lourd si trajet moins utile |
| `Pen` | Agricultural | Champs, dépôt et espace hors habitat dense |
| `Loom` | Industrial léger ; Residential compatible | Laine/enclos et dépôt ; compatible avec une rue d’habitation |
| `Market` | Civic | Place accessible et dépôt ; commerce existant inchangé |
| `Infirmary` | Civic ; Residential compatible | Services proches des logements, hors voisinage lourd |
| `Storehouse` | Selon le besoin : Agricultural, Industrial ou Civic | Accès depuis un dépôt existant pendant sa construction ; puis nouveau service de stock |
| `Well` | Civic ; Residential compatible | Accès public ; le jeu actuel ne demande pas un aquifère local, ne pas inventer cette contrainte |
| `Tavern` | Civic ; Residential compatible | Rencontre et repas ; proximité des maisons |
| `School` | Civic ; Residential compatible | Accès depuis les logements, hors voisinage lourd |
| `Cask` | Quartier de sa taverne | Dépend d’une taverne réelle ; choisir un emplacement proche de celle-ci |
| `Dam` | Aucune vocation obligatoire | Ouvrage spécial 1 × 1 ; hydraulique et prière, accès de travail sur la berge |

La vocation n’est pas une obligation de construire tous ces bâtiments. Les règles de découverte et de demande économique restent responsables de leur apparition.

## 5. Emprise, quartiers et paramètres initiaux

### Ce que signifie « plus grand »

L’emprise habitée comprend les logements, services, ateliers et champs actifs, reliés par les accès. Une très longue piste vers une carrière ne suffit pas à déclarer le village étendu. La mesure principale est le diamètre de cette emprise et la distance entre ses noyaux, complétée par les temps de trajet.

Sur une carte ouverte de 200 × 200, viser à titre de direction artistique :

- Hameau de 5 à 10 habitants : une place, un groupe de maisons, une campagne initiale ; emprise d’environ 25 à 40 cases.
- Village de 12 à 30 habitants : deux groupes résidentiels selon la demande, un ou plusieurs pôles agricoles, un pôle industriel si ses savoirs et ressources le permettent ; emprise d’environ 45 à 70 cases.
- Village prospère de 30 à 60 habitants : plusieurs noyaux et entrepôts de proximité ; emprise d’environ 60 à 100 cases.

Ces fourchettes sont des objectifs de calibration pour des terrains favorables, pas des minima qui obligent à construire du vide. Une gorge, un désert ou un faible effectif peuvent légitimement donner une forme différente. Les seuils n’interdisent pas un moulin isolé ou un groupe rural plus tôt si le terrain le justifie.

### Paramètres de départ

| Paramètre | Valeur proposée | Règle |
|---|---|---|
| Place initiale | Clairière 3 × 3 existante, accès préservés | Ne pas dégager une place monumentale dès la fondation |
| Groupe résidentiel | Cible de 3 à 6 huttes | Pas de quota automatique ; les lits restent déclenchés par la demande réelle |
| Espaces entre huttes | 1 à 3 cases selon la parcelle | Au moins un passage accessible ; varier les cours sans les aligner |
| Séparation habitat / industrie lourde | 6 cases libres entre emprises comme préférence forte | Plancher de 3 cases en implantation dégradée ; aucune nouvelle mécanique de pollution |
| Ancre résidentielle secondaire | Candidats principalement à 18–30 cases du noyau parent | Réduire sur petite carte ; écarter tout candidat non viable en marche |
| Fenêtre locale de recherche | 8–14 cases autour d’une ancre | Ce n’est plus un plafond autour du feu |
| Expansion exploratoire | Passes à 20, 35, 50 puis 70 cases autour des pôles | Toujours limitée par la carte et le trajet ; rechercher au-delà uniquement si un service local le permet |
| Délai de confort entre nouveaux quartiers | 2 jours de jeu | Urgence de logement et activité imposée par le terrain exemptées |
| Survie pour agrandir confort/logistique | Survie assurée et au moins 4 jours de nourriture | Réutiliser les protections de chauffage et de logement existantes |
| Trajet logement → repas/rencontre | Cible ≤ 1,5 seconde de simulation à ×1 | Temps réel calculé sur le chemin, pas distance géométrique |
| Trajet logement → dépôt / service critique | Cible ≤ 3 secondes | Dépassable pour un bâtiment exceptionnel, avec motif et sans déplacer les maisons |
| Marche aller-retour parcelle → dépôt | Cible ≤ 4 secondes, plafond normal 6 secondes | Au-delà, autre emplacement ou entrepôt préalable ; la durée de récolte s’ajoute |
| Coût de mouvement sur sentier | 0,85 du terrain nu sec | Vitesse et choix A* utilisent la même règle |
| Coût sur chemin de terre | 0,65 du terrain nu sec | Pas de bonus sur rivière ou canal ; pas de passage sur eau profonde |
| Apparition d’un sentier | Usure ≥ 8 passages équivalents | Seulement sur cellules éligibles, hors bâtiments, cultures et eau |
| Perte d’usure non aménagée | 15 % par jour | Un chemin de terre entretenu ne disparaît pas en une journée |
| Travail d’aménagement de terre nue | 0,35 seconde par cellule, avant facteur de compétence | Défrichage et marche supplémentaires ; aucun matériau de pavage en première version |
| Main-d’œuvre routière facultative | Au plus 10 % des travailleurs, arrondi inférieur | Si le plafond vaut zéro, travaux seulement pendant une disponibilité réelle ; aucune mobilisation en crise |
| Sondages de site | Jusqu’à 24 candidats préfiltrés et 6 finalistes par demande nouvelle | Plafonds par travail événementiel, jamais 6 A* complets à chaque pensée ; recherche répartie sur plusieurs lots |
| Nouveaux projets par pensée | Limites actuelles : 1 bâtiment, ou 2 huttes si nombreux sans-abri ; jusqu’à 2 champs | Limiter aussi les demandes logistiques ; un seul aménagement routier facultatif actif |

Les secondes ci-dessus utilisent le temps de simulation à vitesse ×1. Une journée dure 45 secondes et la marche actuelle est de 10 cases par seconde : éloigner arbitrairement un champ de 60 cases peut prendre une part excessive de la journée. La vitesse d’affichage du jeu ne change aucun budget.

Sur cartes 128/256, ajuster les distances esthétiques par `min(Width, Height) / 200`, borné entre 0,65 et 1,3. Ne pas ajuster les tailles des bâtiments ni les budgets de déplacement. Toutes les constantes vivent dans `SettlementRules` et sont évaluées en tests avant de devenir des promesses d’équilibrage.

### Apparition d’un nouveau quartier

Un quartier spécialisé n’existe officiellement qu’après acceptation de son premier projet. Les emplacements potentiels découverts à la fondation sont des candidats, pas des parcelles réservées.

Pour les logements, ouvrir un noyau secondaire si une demande réelle reste insatisfaite et si le premier groupe compte déjà au moins trois huttes acceptées, puis si au moins une condition vaut : manque de parcelles raisonnables, fermeture des passages, coût de densification supérieur à l’extension, ou bon site secondaire permettant d’éviter une masse unique. En urgence, le choix viable le plus rapide prime sur cet objectif.

Le quartier agricole cherche à prolonger ses parcelles, puis ouvre un nouveau pôle quand le terrain ou l’accès au dépôt rendent cette solution meilleure. Le pôle industriel suit les ressources visibles, ses partenaires et ses nuisances. Les profils guident le choix ; aucun secteur angulaire fixe « au nord les usines, au sud les maisons » n’est autorisé.

## 6. Algorithmes d’implantation

### Fondation : `InitializeLayout`

```text
FOUND(map, fondateurs, emplacement éventuellement choisi)
    si emplacement automatique :
        garder les critères de survie actuels de ColonyFounder
        ajouter un score de potentiel accessible :
            place pour quelques huttes, terre cultivable, accès vers forêt/carrière
        choisir parmi les sites valides ; jamais transformer un site invalide en site valide
    sinon :
        respecter l’emplacement du joueur validé par CanFoundAt
        fournir éventuellement un avertissement de potentiel, sans le déplacer

    créer le camp, ses membres, ressources et savoirs par la logique actuelle
    initialiser Layout avec une graine stable issue de Map.Seed et du camp
    créer le quartier Civic autour de la clairière réellement occupée
    enregistrer la place et ses accès, puis reconstruire l’index
    évaluer quelques directions de croissance accessibles, sans les réserver
    ne créer ni industrie fictive, ni champs hors saison, ni routes complètes
    toutes les fondations, dont CreateCamp pour un schisme, suivent cette initialisation
```

`FindCampSites` garde un filtre physique dur. Si aucun emplacement valide n’existe, retourner un échec explicite au lieu d’utiliser un centre de carte potentiellement sous l’eau. Le backend adapte les appelants au résultat d’échec ; ce changement est nécessaire à la garantie de viabilité.

### Demandes économiques et propositions spatiales

Une `DevelopmentRequest` décrit **ce qui manque**, jamais une coordonnée imposée : type de bâtiment ou de projet, priorité économique, capacité souhaitée, échéance agricole, raison, et profil de placement. Les règles actuelles de `Knowledge`, `Crafting`, `Farming`, `Civic`, logement et prières restent les producteurs de ces demandes.

```text
TRY_PROPOSE(request, colony)
    dédupliquer la demande avec les objets et projets déjà acceptés
    déterminer les vocations compatibles depuis le profil
    comparer les quartiers existants dans un ordre stable
    produire des candidats autour des fronts de croissance et des chemins existants
    si densification insuffisante ou extension préférable :
        produire quelques nouvelles ancres, sans modifier Layout

    pour chaque candidat dans le budget :
        vérifier bornes, emprise, relief, eau, flore et occupations
        vérifier place initiale, tombes, champs, canaux et corridors réservés
        choisir une entrée extérieure utilisable, puis une entrée intérieure si pertinente
        mesurer les relations au quartier et aux activités voisines
        estimer le trajet vers les services réellement disponibles
        éliminer ce qui dépasse les budgets ou les contraintes spécifiques
        calculer un score spatial normalisé

    pour les 6 meilleurs candidats :
        calculer un véritable chemin d’accès avec l’emprise future déjà occupée
        vérifier aussi que les entrées existantes restent accessibles
        calculer les durées exactes et les éventuels prérequis de défrichage
        recalculer le score avec ces informations

    si le lot de calcul est épuisé : conserver l’étape et retourner Pending
    retourner la meilleure proposition complète, avec motif et révision évaluée
    sinon retourner PlacementFailure(kind, motif, événement autorisant un nouvel essai)
```

Ce pseudo-code décrit le travail total d’une demande, et non un appel synchrone à exécuter entièrement dans `Think`. Le planificateur en conserve les étapes. En logement urgent, accepter le premier candidat suffisamment bon dont toutes les contraintes sont vérifiées au lieu de comparer obligatoirement six chemins. Un manque de budget n’est jamais un résultat physique `NoAccess` ou `NoSpace`.

### Contraintes physiques impératives

- Tester toute l’emprise, pas uniquement sa cellule d’origine ; tester les bornes avant tout accès à un tableau.
- Bâtiments : terrain sec et plat selon les règles actuelles, hors montagnes, champs, tombes, canaux, place et autres emprises. Conserver le refus des arbres/buissons pour une construction ordinaire dans cette version ; un changement de défrichage bâti demanderait ses propres travaux.
- Champs : terrain cultivable, hors sable/eau/montagne, plat sur les 4 × 4 et hors emprises/corridors. Conserver la sémantique actuelle de défrichage de `Farming.PlanField`, en comptant sa pénalité dans le choix ; ne pas attribuer une récolte aux cellules du chemin.
- Moulin : appeler réellement `Hydrology.MillFlow` sur le candidat ; proximité d’une rivière ne suffit pas. Étendre le périmètre de recherche via les pôles accessibles plutôt qu’un rayon fixe de 20 autour du feu.
- Barrage : conserver la sélection hydraulique, la validation du réservoir et l’accord via prière. Vérifier l’impact sur les entrées et accès du village ; la proposition routière ne donne jamais elle-même l’autorisation du barrage.
- Accès : même relief et mêmes règles de diagonale que les mouvements ordinaires. La capacité de secours à escalader des hauteurs ne sert jamais à valider un nouveau quartier.
- Réserver un passage vers chaque entrée et garder les passages entre les lots. Vérifier la connectivité des services essentiels, pas celle de chaque cellule vide du terrain.
- Une demande routière ne peut ni faire disparaître un bâtiment ni ouvrir une traversée d’eau profonde. Les gués et canaux déjà traversables conservent leur coût naturel ; aucune nouvelle mécanique de pont n’est requise.

### Score des sites

Utiliser des composantes normalisées entre 0 et 1. Rejeter d’abord les contraintes impératives ; aucun bonus ne compense un emplacement impossible.

```text
score = aptitude_terrain
      + cohésion_avec_le_quartier
      + utilité_pour_la_chaîne_de_production
      + accès_aux_services
      + réutilisation_du_réseau
      + intérêt_d’un_nouveau_noyau_si_demande
      - coût_de_marche
      - nuisance_et_saturation
      - coût_de_défrichage
      - consommation_de_bonne_terre_agricole
      + petite_variation_spatiale_déterministe
```

Pondérations initiales relatives : terrain 3, cohésion 2, chaîne 2, services 3, réseau 2, nouveau noyau 2, marche 4, nuisances/saturation 3, défrichage 2, bonne terre consommée 2, variation 0,15. Ajuster selon les profils : fertilité dominante pour un champ, nuisances et services pour une hutte, partenaires/ressources pour un atelier.

La cohésion favorise une **plage de distance**, plutôt que la distance zéro. Les cours et les fronts de rues n’ont pas tous la même taille. La variation stable sert à départager des choix comparables, jamais à rendre une implantation moins accessible. Les nouvelles ancres s’appuient sur des lieux de terrain et des accès, pas sur un bruit aléatoire appliqué à chaque bâtiment indépendamment.

### Admission atomique : `TryCommit`

```text
TRY_COMMIT(proposal)
    si la révision pertinente a changé : revalider ou recalculer
    vérifier de nouveau capacité, connaissance, priorité, emprise et accessibilité
    préparer toutes les mutations avant de les appliquer
    si un prérequis dur manque : différer ; ne réserver aucune moitié de projet

    attribuer les identifiants dans un ordre déterministe
    créer le quartier seulement si son premier projet va être accepté
    réserver la parcelle et le corridor, en partageant les accès déjà possédés
    créer le Building ou Field via la sémantique économique actuelle
    enregistrer son association au quartier et son entrée
    enregistrer le projet et les liaisons prévues
    mettre à jour index et révisions, puis émettre les notifications
    aucune déduction de ressources pour la simple réservation du terrain
```

Les matériaux ne sont pas retirés deux fois : les livraisons actuelles et compteurs `InTransit` restent responsables des matériaux. Les parcelles des objets achevés deviennent `Occupied` ; leur lien de projet devient nullable avant purge, tandis que leur occupant et propriété d’accès restent. Un champ créé par `Farming.PlanField` conserve son ouverture immédiate actuelle : son projet d’implantation est achevé à cette ouverture, sans progression agricole artificielle ; ses améliorations d’accès peuvent continuer séparément. Un projet suspendu conserve ses réservations ; aucune expiration automatique ne détruit un chantier en attente de bois. Une demande jamais acceptée ne bloque pas de terrain.

En première version, les pénuries et changements de priorité provoquent une suspension, pas l’annulation automatique d’un bâtiment. Pour une annulation administrative explicite d’un chantier incomplet, annuler/libérer les activités concernées, régulariser les matériaux en transit, puis restituer les matériaux livrés au stock une seule fois avec `ResourceFlow.Transfer`, jamais comme production ; le travail réalisé est perdu. Cette règle garde les matériaux incomplets comme une allocation récupérable, sans créer de nouvelle récupération de démolition. Libérer ensuite les réservations propres au projet. Les corridors partagés et tronçons déjà aménagés restent. Une destruction par incendie conserve son traitement de pertes existant, sans restituer fictivement ses matériaux.

## 7. Réseau de chemins et circulation

### Trouver un tracé : `FindConnection`

```text
FIND_CONNECTION(access, futur_occupant)
    identifier quelques points proches du réseau et des espaces publics accessibles
    si aucun réseau n’existe : utiliser un accès de la place initiale
    pour les meilleurs points de raccordement :
        A* huit directions avec règles physiques ordinaires
        interdire emprises bâties, cultures, tombes et eau profonde
        considérer l’emprise future comme déjà occupée
        favoriser sentiers et routes existants
        pénaliser arbres, buissons nourriciers et relief
        ajouter un faible coût spatial cohérent, fixe et positif
    choisir le tracé utile le moins coûteux, en incluant son défrichage
    fusionner les portions communes ; réserver seulement les cellules de l’accès
```

Le bruit de coût est cohérent à l’échelle de plusieurs cellules, pas un tirage par pas. Tous les coûts restent strictement positifs. Une ligne droite dans une plaine est acceptable : l’aspect organique vient surtout des destinations, du terrain et des ramifications. Ne pas ajouter des virages artificiels pour atteindre un quota de sinuosité.

Préférer le raccordement à un chemin de quartier plutôt qu’au feu lorsqu’il est utile. Ajouter une boucle ultérieure seulement si elle dessert un flux fréquent, raccourcit le trajet d’au moins 25 % et rembourse son travail d’aménagement en environ huit jours. Le graphe n’est ni entièrement recalculé ni complété entre toutes les paires de bâtiments.

### Usure et amélioration : `RoadDevelopment`

```text
ON_STEP_COMPLETED(colonist, from, to)
    enregistrer un passage sur la cellule atteinte, une seule fois par pas terminé
    conserver les passages de voyageurs ; ne pas compter une interpolation graphique
    si cellule nue éligible et usure suffisante : devenir Trail
    si cellule cultivée, bâtie, sous l’eau ou végétation protégée : pas de sentier créé
    notifier seulement un véritable changement de surface

ON_DAY_START_ROADS()
    avancer le jour de référence ; ne pas parcourir toute la grille
    enregistrer le bilan des cellules ayant reçu des passages
    planifier les expirations de sentiers dans un agenda déterministe
    appliquer l’usure à partir de sa date de référence, sans balayage quotidien
    conserver les DirtRoad ; entretien éventuel reportable, sans catastrophe immédiate
    si prospérité et trafic utile ont changé : mettre une demande d’amélioration en file

BUILD_ROAD_CELL(actor, cell)
    vérifier réservation, terrain et priorité du projet
    s’il reste un arbre : réaliser un travail Chop avec production normale de bois
    si buisson à enlever : travail ClearAccess explicite, jamais effacement par simple usure
    accumuler le travail Construction ; traiter les souches à l’aménagement
    une fois travail suffisant : poser DirtRoad et actualiser les coûts
```

Un accès peut contenir au début de la terre nue, des sentiers et un gué. Il reste **praticable**, même si son aménagement n’est pas uniforme. Les itinéraires planifiés peuvent être exposés à l’affichage comme projets, distincts des surfaces réalisées.

L’aménagement facultatif avance par petits lots, par exemple huit cellules, choisis sur un axe accepté. La construction d’une hutte ne dépend jamais de l’aménagement complet de ce chemin. Pour les accès traversant un gué ou un canal praticable, la couche routière reste `None` sur l’eau : le segment sait qu’il traverse cette cellule sans appliquer un bonus routier.

### Un coût commun au pathfinder et au mouvement

Le coût actuel d’A* et les estimations utilisant seulement `path.Count` ne suffisent plus. Définir une fonction de durée par arête : longueur géométrique du pas, coût du terrain sec ou du franchissement, surface routière réalisée et pénalité de montée. Le mouvement consomme cette même durée, convertie avec `TicksPerSecond`.

```text
EDGE_SECONDS(from, to, mode)
    rejeter tout pas interdit par terrain et politique d’occupation
    base = coût du terrain selon les règles existantes
    si terre sèche et route réalisée : appliquer son multiplicateur
    si rivière ou canal en eau : conserver le coût de franchissement sans bonus
    durée = longueur_du_pas × coût / vitesse_de_marche + coût_temporel_de_montée
    retourner durée strictement positive
```

L’heuristique octile d’A* doit être multipliée par la durée minimale possible d’un pas orthogonal, y compris le bonus routier maximal. Garder l’heuristique actuelle non réduite avec des routes moins coûteuses pourrait surestimer les distances et perdre la garantie d’un meilleur chemin. Le même profil de coût est utilisé dans les validations de retour/isolement.

Dans `ColonistAI.Move`, consommer le temps du tick sur le pas courant ; la progression géométrique correspond à la fraction de la durée de cette arête. À l’arrivée, enregistrer le passage. Vérifier le prochain pas avec l’occupation et le terrain actuels. Une route devenue meilleure n’annule pas tous les chemins ; un changement qui rend le prochain pas interdit déclenche une nouvelle recherche.

### Entrées et occupation des bâtiments

Le terrain actuel ne connaît pas les bâtiments comme obstacles et certaines activités visent leurs cellules internes. Ajouter des obstacles extérieurs sans adapter le sommeil et le travail bloquerait les colons.

- Les chemins extérieurs évitent les emprises de bâtiments et les entrées sont hors emprise, reliées à une cellule intérieure adjacente.
- Construction, approvisionnement, artisanat, soins et services utilisent une cellule de travail accessible associée au bâtiment, généralement à l’entrée. Les lits gardent leurs cellules internes.
- `LocalNavigation` construit un petit trajet intérieur entre le lit et la porte, puis le trajet extérieur ; à l’arrivée, il ajoute le trajet intérieur nécessaire. Les cellules du bâtiment de départ/destination sont autorisées uniquement dans ces portions intérieures, pas comme raccourci à travers un autre bâtiment.
- Vérifier que la sortie fonctionne dans les deux sens. Lorsqu’un bâtiment est détruit, un habitant à l’intérieur se retrouve sur un terrain désormais ordinaire ; l’activité est réévaluée.
- Une hutte initiale doit pouvoir loger tous ses résidents et permettre à chacun de ressortir. Le secours existant contre un colon piégé par le relief reste disponible pour les accidents, jamais comme moyen de planifier un accès normal.

Cette couche d’occupation est un ensemble unique de règles utilisé par proposition, pathfinder et vérification du mouvement. Ne pas ajouter un simple booléen « bâtiment = obstacle » dans `LocalMap.IsWalkable` tout en laissant les autres appelants inchangés.

## 8. Faire vivre les quartiers sans refaire toute l’économie

### Accès au stock

Conserver **un stock économique commun**. Le camp et les entrepôts achevés deviennent des points physiques où les colons peuvent déposer une récolte et récupérer des matériaux. La capacité supplémentaire par entrepôt existe déjà dans `Civic.StorageCapacity` ; permettre plusieurs entrepôts en réponse à un besoin spatial réel.

Un entrepôt en chantier ne compte jamais comme service disponible. Un projet de champ trop éloigné attend l’achèvement d’un entrepôt s’il dépend de celui-ci pour son budget. Pour bâtir cet entrepôt, ses propres matériaux doivent venir d’un point déjà opérationnel : aucune dépendance circulaire « dépôt requis pour construire le dépôt ».

Les lieux de repas peuvent être le camp, une taverne achevée ou un petit espace public associé à un groupe résidentiel actif. Le repas est toujours retiré une seule fois de `Colony.Stock`, au commencement de l’action. Cette version abstrait l’approvisionnement collectif des repas et l’accès au stock partagé ; elle ne simule pas des inventaires locaux ni des charrettes transférant physiquement la nourriture entre dépôts.

Les espaces publics secondaires sont modestes, sur terrain déjà utilisable, associés à une occupation réelle. Ils ne fournissent ni capacité de stockage ni chauffage supplémentaire gratuit. La consommation de chauffage et les bonus du puits, de l’école ou de l’infirmerie conservent les règles actuelles.

### Choix individuels

```text
CHOOSE_SERVICE(actor, purpose)
    demander les points compatibles réellement disponibles
    chercher ceux atteignables depuis sa position actuelle
    comparer leur temps de trajet, puis son quartier et ses habitudes
    garder le camp comme solution de repli s’il est accessible
    retourner un point et son chemin ; sinon échec explicite

DELIVER_HARVEST(actor)
    marcher vers un point StockAccess achevé
    à l’arrivée : ajouter une seule fois la charge au stock commun
    enregistrer le travail de production et de transport comme aujourd’hui
    vider la charge et clôturer le cycle ; ne pas exiger IsAtCamp

FETCH_MATERIALS(actor, site)
    choisir un StockAccess existant, atteignable et utile au trajet vers le chantier
    y retirer les matériaux avec les compteurs InTransit existants
    les apporter à l’accès du chantier ; aucun prélèvement à distance supplémentaire

RELAX_OR_EAT(actor)
    préférer un lieu utile proche de son logement ou de son activité actuelle
    choisir la taverne si ses avantages justifient le trajet
    éviter de renvoyer automatiquement tous les habitants au feu
```

Remplacer les usages de `StartNearCamp`, `IsAtCamp` et de la sélection systématique dans `GatherSpots` lorsque leur sens réel est « accès à un service ». Conserver `GatherSpots` pour les comportements qui concernent effectivement le camp et comme repli.

Les récoltes, matériaux et coûts de production doivent aussi fonctionner lors d’une annulation, d’un décès ou d’un départ. Modifier la condition de dépôt dans `Act` ainsi que le routage de l’activité `Deliver` ; changer uniquement la destination graphique ne suffit pas.

### Travail et agriculture

Les meilleurs travailleurs restent affectés par compétences. Parmi les tâches compatibles, choisir celles dont le rendement tient compte du trajet réel depuis la position du colon, du travail, puis du dépôt utile. Ne pas remplacer le système d’affectation par un enfermement des habitants dans leur quartier.

La navigation piétonne autorise l’entrée dans une parcelle cultivée pour y travailler ; la planification routière interdit d’y tracer une route. Ces deux profils partagent les contraintes physiques, mais n’ont pas la même politique d’usage. Les passages agricoles ne créent pas de sentiers sur les cultures. Les activités qui prélèvent déjà leurs ingrédients au stock commun à l’atelier conservent cette abstraction économique ; ne pas ajouter une seconde déduction par un trajet de matériaux nouveau.

`TryGatherFood` doit comparer les cycles complets, pas `2 × path.Count / vitesse`. Pour la récolte, le retour peut se faire vers un entrepôt différent du camp. Pour les champs, actualiser `Farming.WorkersNeeded` avec une estimation du semis, des déplacements entre parcelles et du cycle récolte→dépôt ; prévoir les journées restantes avant fin des semis ou gel. Réutiliser les coûts mesurés quand l’échantillon est suffisant, avec une estimation conservative de repli.

`PlanFields` compte les **parcelles productives** et les demandes déjà acceptées. Ouvrir un champ seulement si son travail peut raisonnablement être absorbé à temps. Une route améliore le trajet, jamais le rendement agricole par un bonus indépendant.

## 9. Intégration au Tick et à la pyramide des priorités

### Cadences

| Cadence | Travail ajouté |
|---|---|
| Chaque tick | Mouvement déjà existant ; vérification locale du prochain pas ; un compteur par pas terminé ; aucune recherche de quartier |
| Tous les 5 ticks, au niveau du monde | Un petit lot de planification si une demande attend ; budgets globaux de candidats, expansions A* et validations |
| Chaque heure, dans `ColonyBrain.Think` | Contrôle des besoins par compteurs/seuils ; admission de propositions déjà prêtes et encore valides ; répartition économique existante |
| Chaque jour | Changement de date, bilans des seules cellules utilisées et demandes de contrôle espacées ; aucun balayage routier de toute la carte |
| Après un changement utile | Mise à jour locale des compteurs/index ; marquage ciblé et insertion dédupliquée d’une demande de recherche |

Ne pas déplacer les traitements quotidiens de lifecycle/climat/événements uniquement pour l’urbanisme. Si leurs changements surviennent après un capteur spatial, ils marquent la zone à réévaluer avant la prochaine proposition. Les validations au moment de l’admission et du mouvement empêchent d’utiliser un cache périmé.

### Événements qui justifient une recherche

- Population/logement : le nombre de lits promis devient insuffisant, une hutte termine ou brûle, ou l’éligibilité à une hutte anticipée change. Une naissance qui ne change pas la demande de lits n’entraîne pas une recherche complète.
- Agriculture : début de saison pertinente, manque significatif de parcelles productives, nouveau dépôt ou transformation de terres utiles.
- Production/services : découverte débloquant un bâtiment, seuil économique de construction franchi, achèvement ou destruction d’un partenaire/dépôt, disparition durable d’une ressource.
- Terrain et occupation : mutation dans la zone utilisée par une proposition ou son chemin, ouverture/libération d’une parcelle. La maturation d’une baie ailleurs sur la carte ne rend pas tous les projets périmés.
- Routes : franchissement d’un seuil d’usage ou changement de surface utile. Un simple passage n’ouvre pas une nouvelle recherche de tracé.

Les besoins sont des demandes persistantes identifiées par leur fonction, pas de nouveaux objets recréés à chaque heure. Utiliser une hystérésis pour les seuils économiques et un état `WaitingForChange` pour une impossibilité connue. Un contrôle quotidien léger peut relancer une demande oubliée ; il ne doit pas ignorer son motif d’attente ni vider tous les caches.

### Budget global et recherches progressives

Le budget est **partagé entre toutes les colonies**, afin que seize colonies ne multiplient pas seize fois le travail maximal d’un tick. Configuration initiale à mesurer :

| Limite | Valeur initiale de profilage |
|---|---|
| Rendez-vous du planificateur | Tous les 5 ticks, soit quatre rendez-vous par seconde à ×1 |
| Préfiltrages de candidats | 8 par rendez-vous pour le monde entier |
| Expansions A* du planificateur | 128 au total par rendez-vous |
| Cellules de validation / maintenance locale | 256 au total par rendez-vous, avec poursuite au suivant |
| Contextes A* actifs | 2 au maximum pour le monde entier ; les autres demandes attendent |

Ces valeurs ne garantissent pas un temps en millisecondes : elles sont un point de départ pour les mesures et doivent aussi limiter le nombre de transitions entre étapes. Les tableaux sont réutilisés ; leur allocation ne se répète pas pour chaque candidat. Les chemins individuels continuent à utiliser le moteur existant ; le budget ci-dessus porte sur la planification urbaine ajoutée, pas sur une suppression des déplacements des colons.

Les recherches urgentes passent avant le confort. À priorité égale, faire tourner les colonies dans un ordre stable avec conservation du curseur, pour qu’une colonie ne monopolise pas le budget. Ne jamais lancer un A* sans limite pour « finir » un lot. Ne pas bloquer les activités en cours pendant que la meilleure implantation est recherchée.

```text
ON_RELEVANT_CHANGE(colony, category, area)
    mettre à jour les compteurs et invalider seulement les données concernées
    créer ou réveiller une demande dédupliquée

PROCESS_WORLD_PLANNING_QUANTUM(world)
    s’il n’y a aucun travail prêt à avancer : retourner immédiatement
    initialiser les budgets globaux du rendez-vous
    choisir les travaux par priorité, puis rotation équitable des colonies
    avancer leurs étapes en respectant tous les budgets :
        candidats → contraintes → chemin → score → validations → Ready
    conserver curseurs et frontières des recherches inachevées
    aucune mutation du monde par une recherche non terminée
```

Le backend ne connaît pas `GameSpeed`. Les nombres d’opérations et leur calendrier sont identiques au même tick, quel que soit le débit réel de la machine. Les urgences utilisent en premier des parcelles et raccordements locaux déjà évalués ; l’ouverture d’un quartier éloigné reste une recherche plus longue.

### Pyramide

| Niveau | Décisions spatiales autorisées |
|---|---|
| Survie | Tâches alimentaires et chauffage existants ; accès indispensable à une tâche vitale lorsque le terrain permet réellement un contournement |
| Logement | Huttes nécessaires et leurs accès ; implantation de repli plus compacte si urgence |
| Production | Ateliers, champs saisonniers, réserves, irrigation, entrepôt nécessaire à une activité utile ; protections actuelles de savoirs et ressources |
| Confort / expansion | Huttes anticipées selon les règles existantes, nouveaux noyaux de confort, routes aménagées et boucles facultatives |

Cette table guide l’admission spatiale et la sélection des tâches, pas une réécriture complète de la distribution économique. Les semailles et moissons continuent à relever de la survie saisonnière dans les parts de travail ; un champ futur ne nourrit pas immédiatement une colonie affamée.

Un accès nécessaire hérite de la priorité de son projet. Son aménagement en `DirtRoad` reste facultatif. Si une inondation coupe réellement tous les trajets, un projet routier ne peut pas la résoudre : suspendre le service inaccessible, rechercher un repli, émettre un motif `NoAccess` et garder les besoins individuels actifs.

### `Think` horaire

```text
THINK(colony, map, clock)
    garder les traitements existants de carrière et fûts
    lire les compteurs spatiaux actualisés par événements
    mesurer les besoins économiques ; comparer les seuils et demandes précédents

    créer ou réveiller uniquement les demandes utiles qui ont changé :
        champs au printemps, selon alimentation et capacité de travail
        huttes pour sans-abri, puis réserve de lits selon règles existantes
        ateliers avec savoirs et débouchés actuels
        irrigation et bâtiments civiques selon règles existantes
        logistique / amélioration routière seulement si utiles et autorisées

    mettre les recherches nécessaires dans la file globale, sans les exécuter ici
    admettre dans les limites actuelles les propositions Ready encore valides
    si une proposition est périmée : la renvoyer en file, sans recherche synchrone
    une demande bloquée ne doit pas empêcher une proposition réalisable
    recomposer les capteurs économiques si des chantiers ont été ajoutés

    conserver AskForDam et le circuit de prière ; valider l’impact spatial
    calculer les parts actuelles de travail
    intégrer les travaux d’accès/routiers dans Construction et leurs limites
    appliquer le lissage et AssignSectors comme aujourd’hui
    narrer uniquement les décisions ou blocages durables nouveaux
```

Pour les demandes de même priorité, conserver le sens de l’ordre actuel : logement nécessaire, anticipation si autorisée, ateliers, canaux et civique. Les règles de printemps gardent leur fenêtre spécifique. Le backend doit expliciter les protections dans les producteurs de demandes au lieu de créer un nouveau moteur économique concurrent.

### Construction et main-d’œuvre

- Étendre le capteur qui représente le travail de construction pour inclure un travail d’accès/routier réellement autorisé. Sinon, un projet routier resterait sans bâtisseur quand aucun bâtiment n’est en chantier.
- Dans `TryConstruct`, choisir d’abord la priorité de chantier, ensuite la possibilité de travailler, puis le coût de trajet. Le simple « chantier le plus proche » ne doit pas laisser une hutte urgente derrière une route de confort.
- Les accès qui nécessitent un défrichage utilisent les secteurs/compétences existants. Une forêt praticable n’a pas besoin d’être entièrement abattue avant d’accéder au chantier.
- Le plafond routier s’applique seulement aux aménagements facultatifs. Les travaux indispensables d’un projet prioritaire restent dans le budget de construction de ce projet.
- En crise, arrêter l’ouverture et l’affectation de travaux facultatifs dès la pensée suivante. Garder leurs parcelles et leur progression ; ne pas laisser le lissage maintenir pendant des jours une affectation routière devenue interdite.
- Les promesses de lits/champs déjà en chantier comptent dans la demande, pour éviter l’ouverture répétée du même projet.

### Échecs et reprises

`PlacementFailure` décrit une impossibilité physique. L’épuisement du budget retourne `Pending` et conserve le travail de recherche ; il ne déclare pas définitivement « aucun terrain ». Une impossibilité est réessayée après l’événement pertinent : changement d’occupation, nouveau service, nouvelle connaissance, transformation du terrain, début de saison ou reprise quotidienne espacée réellement justifiée.

En logement urgent, autoriser un quartier civique mixte, une cour réduite et une extension moins distante. Ne jamais assouplir eau, collisions, entrée utilisable ou conservation des accès essentiels. Une recherche bornée infructueuse n’ajoute pas silencieusement un bâtiment à la position du feu.

## 10. Déterminisme, performance et persistance

### Déterminisme

- Dériver la graine spatiale de données immuables, avec une fonction de mélange entière stable documentée et versionnée. Ne pas utiliser `string.GetHashCode`, l’heure système ni des identifiants aléatoires.
- Les goûts spatiaux sont des fonctions de la graine, coordonnées, vocation et identité de la demande. Ils ne consomment pas `WorldState.Random`, `Chance` ou `Politics`, ce qui évite de déplacer les tirages de naissance, santé ou événements.
- Trier les candidats et départager à score égal par identifiant de quartier, puis `Y`, `X`, direction d’entrée. Définir également un départage stable pour la file de priorité A*.
- Une demande inchangée ne change pas de candidat chaque heure. La révision évaluée sert à invalider une proposition, pas à tirer une nouvelle préférence aléatoire à chaque passage d’un colon.
- Conserver l’ordre des mises à jour et les compteurs d’identifiants. Après sauvegarde/rechargement, la prochaine décision doit être la même qu’en continu.

### Performance

- **Réutiliser les résultats.** Garder un petit catalogue ordonné de parcelles potentielles par quartier et profil, avec raccordements communs. Une maison supplémentaire revalide souvent une parcelle connue plutôt que de redécouvrir la carte. Les parcelles du catalogue ne réservent pas de terrain.
- **Actualiser localement.** Suivre occupation, terrain, services et coût routier par régions de 16 × 16 cellules, avec révisions séparées. Une nouvelle hutte invalide ses abords et accès ; un sentier devenu route ne force pas à recalculer la fertilité ou le nombre de lits.
- **Maintenir des compteurs.** Nombre de lits disponibles/promis, champs utiles et objets du quartier se mettent à jour à l’acceptation, achèvement, destruction ou arrivée pertinente. Ils se reconstruisent au chargement. Le capteur spatial horaire ne parcourt pas tous les bâtiments et toutes les parcelles pour les recompter.
- **Réutiliser les services.** Préfiltrer par accessibilité et distances connues ; calculer le trajet exact pour le ou les finalistes nécessaires. Ne pas lancer un A* vers chaque entrepôt pour chaque colon. Un cache de trajets distingue les demandes de placement des déplacements individuels, avec limite de taille et invalidation ciblée.
- **Réutiliser les chemins.** Raccorder un nouveau bâtiment à une branche connue ; vérifier sa courte desserte et les portions communes concernées. Conserver les passages essentiels comme corridors protégés pour éviter une recherche de connectivité complète à chaque nouvelle parcelle.
- **Traiter les routes actives.** Suivre les cellules fréquentées et les sentiers ayant une expiration prévue ; éviter une nouvelle boucle quotidienne sur les 40 000 cellules de chaque carte. L’usure est une fonction de la valeur et du jour du dernier passage, calculée sans mutation lors de la lecture. Employer un calcul entier/fixe versionné pour que la fréquence de lecture n’affecte ni l’usure ni les décisions. Une cellule expirée cesse de donner son bonus dès sa date effective, même si sa notification d’affichage attend un lot de maintenance.
- **Borner les recherches.** Appliquer les budgets globaux de la section 9 à toutes les étapes, y compris réévaluations et validations. Une mutation réellement globale, telle qu’un lac de retenue, reconstruit les données utiles par lots ; le mouvement teste immédiatement le terrain actuel afin de rester correct pendant ce travail.
- **Éviter les pics synchronisés.** Les pensées horaires gardent leur rythme économique mais ne lancent pas les gros calculs. Les travaux en file sont répartis entre les rendez-vous suivants. Les notifications sont regroupées et les cellules déjà marquées ne sont pas ajoutées plusieurs fois.
- **Garder le déterminisme.** Compter les opérations, jamais les millisecondes disponibles. Le budget d’image du frontend peut ralentir la livraison des ticks ; il ne décide pas quels projets existent.
- **Limiter la mémoire.** Bornes sur catalogue, caches, travaux actifs et historique du trafic ; réutilisation de buffers ; purge des projets terminés après conservation des liens utiles et du récit.

Un A* progressif du planificateur possède son propre contexte. Il ne réutilise pas le workspace mutable de `Colony.Pathfinder`, que les décisions individuelles peuvent écraser entre deux lots. Sa révision de lecture doit rester cohérente : une mutation pertinente dans la fenêtre de recherche le rend périmé avant sa prochaine reprise ; un simple compteur de passages ne l’invalide pas. La recherche de secours individuelle reste distincte.

Les validations coûteuses précèdent l’état `Ready` et sont elles-mêmes réparties. `TryCommit` vérifie les emprises et certificats de révisions locaux ; si une vérification complète est devenue nécessaire, il retourne la proposition au planificateur. Il ne lance pas discrètement une recherche globale dans la pensée horaire.

### Mesurer avant de promettre un débit

Les cadences réelles expliquent le risque : avec 45 secondes par jour et 24 pensées par jour, une colonie est consultée environ 0,53 fois par seconde réelle à ×1, puis environ 107 fois à ×200 si le débit cible est tenu. Seize colonies représentent alors environ 1 707 consultations par seconde réelle. Six A* complets systématiques à chaque consultation seraient excessifs ; six finalistes lors d’une demande rare, traités par petits lots, constituent une charge différente.

Le frontend possède déjà un budget normal de simulation de 10 ms par image et un suivi du débit réel. Ce budget est vérifié après `WorldState.Step` : il limite la quantité de ticks livrés, mais un seul tick trop lourd peut encore provoquer un pic. Il ne prouve donc pas que le nouveau système sera rapide.

Avant validation, mesurer : temps total pour un nombre fixe de ticks, ticks livrés par seconde, vitesse réellement obtenue, médiane/95e/99e percentile de durée d’un tick, expansions de recherche, invalidations, taux de réutilisation, allocations et nombre de demandes en attente. Utiliser une machine et un corpus fixes, avec échauffement, et relever aussi les effectifs et le travail effectivement accompli.

Comparer 1, 4 et 16 colonies ; petites et grandes populations ; ×1, ×30 et ×200 ; village stable, vague d’arrivées, début de printemps et inondation. Séparer le coût CPU du backend de celui du dessin. Objectif initial : coût de planification urbaine faible devant celui de la simulation existante, par exemple inférieur à 5 % en moyenne, sans pic de tick dû à une recherche entière. Ce pourcentage est une cible de profilage, pas une garantie déjà démontrée. Si elle échoue, optimiser ou réduire les budgets avant la livraison, puis vérifier que le délai de réponse aux besoins reste acceptable.

### Sauvegardes

Le système actuel sauvegarde les champs privés et compare une empreinte stricte du schéma. Ajouter les nouvelles propriétés sans travail de format rendrait les sauvegardes v1 incompatibles.

Décision recommandée : format v2 avec migration v1 explicite.

1. Avant de modifier les classes, figer la description du schéma v1 actuel et des fichiers de test v1. Implémenter un lecteur v1 restreint à cette liste autorisée, indépendamment du nouveau `StateGraph.Schema`.
2. Le chargement identifie version et schéma connus. Une ancienne version reconnue passe par son lecteur/migrateur ; un schéma inconnu demeure refusé. Ne jamais accepter arbitrairement un fichier de schéma différent.
3. Ajouter aux types autorisés v2 les nouvelles classes persistantes, notamment `SettlementPlanningState`, `PlanningJob` et `PathSearchState`. Éviter de sauvegarder des interfaces, services de calcul et références déléguées.
4. Étendre l’exclusion/reconstruction des caches : index, navigation, aptitudes et graphe routier, comme pour le pathfinder actuel. Adapter aussi `WorldComparison` afin qu’il compare l’état autoritatif, puis vérifier séparément les comportements des caches reconstruits.
5. Persister graines/versions, identités, ancres, parcelles, projets actifs, routes et travail par cellule, trafic encore utile, délais et compteurs influençant les prochaines décisions. Persister aussi file, priorités, curseur d’équité, étapes et frontières A* inachevées : recommencer les recherches au chargement pourrait retarder leurs résultats en ticks et changer la simulation. `PathSearchState` conserve coûts, parents, marquages, frontière et départages dans des tableaux/listes autorisés par le sérialiseur ; ne pas sauvegarder directement une `PriorityQueue` non prise en charge. Les buffers du moteur sont des caches reconstruits à partir de cet état.
6. Valider dimensions des tableaux, identifiants uniques, coordonnées, liens quartier/objet, continuité des tracés, références de propriétaires, progrès bornés et compteurs en transit. Ajouter des limites de taille adaptées au format existant.

```text
MIGRATE_V1(oldWorld)
    préserver terrain, habitants, stocks, relations, projets, chemins et états aléatoires
    attribuer des identifiants aux objets existants dans leur ordre stable
    créer le cœur au camp, puis regrouper les objets par proximité et fonction
    conserver les bâtiments mixtes historiques dans leur quartier compatible
    créer des parcelles Occupied ou Reserved aux coordonnées existantes
    déterminer leurs accès sans déplacer ni effacer les objets
    initialiser la couche routière vierge et les futurs raccordements praticables
    ne construire aucune route gratuitement et ne consommer aucun tirage global
    si un accès ancien ne satisfait pas les nouvelles règles :
        enregistrer une anomalie historique et une stratégie de repli
        conserver temporairement une traversée legacy ciblée pour les activités/lits existants
        ne pas autoriser cette exception pour de nouveaux projets
    reconstruire les caches et reprendre la simulation
```

Les anciens quartiers resteront initialement compacts : leur transformation passe par les extensions futures. Une migration ne doit pas réorganiser les résidents ni déplacer leurs maisons pour améliorer la composition.

## 11. Contrat de lecture pour l’affichage

Le développeur backend expose des données lisibles et stables :

- Quartiers : identifiant, vocation, ancre, statut, emprise dérivée des objets réellement associés.
- Bâtiments/champs : coordonnées exactes, emprise, quartier, accès et état du chantier.
- Routes : cellules réalisées avec `Trail`/`DirtRoad`, tracés de projets clairement distincts, points de franchissement praticables.
- Espaces publics : cellules utilisables et fonction ; services disponibles, sans bonus inventé par la vue.
- Mesures : emprise active, nombre de noyaux, accessibilité et principaux temps de trajet.
- Notifications de changement de quartier/parcelle/route pour actualiser uniquement ce qui change.

L’affichage peut arrondir visuellement les angles des chemins tant que le dessin reste dans les cellules/corridors praticables. Les habitants suivent la géométrie de simulation. Il ne décale pas des bâtiments au hasard et ne dessine pas de route fonctionnelle entre deux points déconnectés.

Pour livrer l’effet village visible, un raccordement de l’affichage sera nécessaire : dessin des sentiers, chemins, espaces publics et vues de quartier, puis cadrage sur l’emprise active plutôt que sur le seul feu. Ce document définit ce contrat ; il n’introduit aucune responsabilité d’affichage dans le backend.

## 12. Mise en œuvre et critères de validation

### Ordre de livraison

1. **Sécuriser le format et le modèle.** Figer v1, créer données/version v2, identités, profils, couche routière et indexes reconstructibles ; conserver un fonctionnement existant tant que l’urbanisme n’est pas branché.
2. **Créer le village par quartiers.** Initialisation à toutes les fondations, propositions pures, admission atomique, profils de bâtiments/champs, accès réservés ; conserver les objets et règles économiques.
3. **Brancher circulation et chemins.** Entrées, navigation intérieure/extérieure, coût commun, usure, aménagement, raccordements et travaux.
4. **Décentraliser les usages.** Repas/rencontre, dépôts et matériaux ; entrepôts supplémentaires seulement si utiles ; ajuster estimations de travail agricole et alimentaire.
5. **Intégrer le cerveau.** Demandes économiques, priorités, suspensions, reprises et narration. Retirer les anciennes contraintes spatiales globales devenues concurrentes.
6. **Valider et calibrer.** Sauvegarde, invariants, longues simulations, scénarios de terrains et contrat de lecture. Le frontend dessine ensuite l’état réel.

Une livraison intermédiaire « bâtiments plus loin du feu » ne constitue pas la fin du travail : elle pourrait augmenter la famine et laisser tous les colons concentrés autour du camp.

### Tests significatifs à écrire lors de l’implémentation

| Ensemble | Scénarios et résultat attendu |
|---|---|
| `SettlementPlanningTests` | Même graine/même suite d’actions → mêmes quartiers, parcelles et chemins ; une sonde de site ne modifie rien ; absence de nouvelle demande → aucune nouvelle recherche |
| `DistrictGrowthTests` | Demande réelle et saturation → nouveau noyau viable ; petite colonie → aucun quartier industriel vide ; aucune reconstruction globale à l’heure suivante |
| `PlacementInvariantTests` | Aucune superposition de bâtiments/champs/tombes ; place protégée ; accès conservé après ajout ; moulin seulement avec débit ; projet périmé revalidé |
| `RoadTests` | Sentier après passages réels, aucune usure sur culture ou intérieur ; chemin partagé conservé à l’annulation ; pas de raccourci sur eau ; reprise après interruption de travaux |
| `NavigationTests` | Entrée, sommeil dans chacun des quatre lits et sortie ; diagonales sans coupe de coins ; destruction/inondation pendant trajet ; vitesse et coût estimé cohérents ; aucun transit à travers une maison tierce |
| `SettlementServicesTests` | Dépôt hors camp enregistré une seule fois ; entrepôt incomplet ignoré ; prélèvements et InTransit corrects après annulation/décès ; plus proche point réellement atteignable choisi |
| `BrainTests` | Crise → pas d’affectation routière facultative ; hutte urgente avant route ; projet spatial bloqué n’empêche pas un projet utile ; construction routière possible sans chantier bâtiment |
| `FarmingTests` / `GrowthTests` | Ouverture compatible avec temps de semis, rendement et trajet ; reprise sur saisons ; population et logement progressent sur terrain ouvert |
| `SaveTests` | Sauvegarde au milieu d’une livraison, d’un chemin intérieur, d’un aménagement et d’un A* progressif ; reprise strictement identique au continu ; migration v1 sans perte ; schéma inconnu refusé |
| `MultiColonyTests` / `SoakTests` | Jusqu’à 16 colonies ; longue exécution sans explosion des listes ni recherches permanentes ; chaque peuple et biome conservent des replis viables |
| `PlanningBudgetTests` | Seize demandes simultanées respectent le plafond global d’opérations ; reprise déterministe ; rotation équitable ; aucun budget multiplié par le nombre de colonies ; usure indépendante de la fréquence de lecture |

Les tests existants doivent être adaptés lorsqu’ils supposaient implicitement une implantation près du feu, sans supprimer leurs garanties de survie, comptabilité et reproduction. Étendre les suites actuelles plutôt que les remplacer par des tests qui reproduisent les formules du planificateur.

### Validation de l’objectif village

Sur un scénario de référence ouvert 200 × 200, avec ressources, savoirs et population suffisants :

- Les maisons constituent au moins deux groupes à partir d’une croissance justifiant un second noyau ; les ateliers lourds et champs forment des pôles identifiables.
- Les entrées et les champs ont un accès continu praticable ; les axes fréquentés montrent effectivement des sentiers et des tronçons aménagés.
- Les formes s’adaptent au terrain et diffèrent avec la graine. L’organique n’est pas testé par une obligation absurde de virages sur terrain parfaitement plat.
- L’emprise utile croît au-delà du voisinage actuel du feu et tend vers les fourchettes définies ; ses distances restent compatibles avec les budgets de travail.
- Les habitants mangent, se rencontrent et déposent à plusieurs lieux effectivement utilisés ; ils ne font pas tous un retour systématique au centre.
- Les taux de décès par famine, production, temps de marche, sommeil et travaux en attente sont mesurés sur un corpus fixe de graines avant/après. Toute régression de survie liée à l’étalement est corrigée ou justifiée par un changement d’équilibrage explicite, pas masquée en augmentant les stocks de départ.

Les terrains contraints ont des critères d’accessibilité et de fonctionnement, pas une obligation de quatre quartiers. Prévoir plaine, vallée étroite, forêt dense, rivière/gué, carte sèche, petites et grandes régions, quatre peuples, faible effectif, vague d’arrivées et incendie d’un entrepôt isolé.

### Définition de terminé pour le backend

Le nouveau système fonde et fait évoluer des colonies organiques sans le rayon fixe du feu ; il planifie les implantations et accès de manière déterministe ; la circulation utilise réellement les chemins ; les services permettent plusieurs lieux de vie ; les règles de survie, cultures, savoirs, commerce et comptabilité continuent à fonctionner ; les nouvelles sauvegardes reprennent à l’identique et les anciennes reconnues migrent sans déplacements arbitraires ; le frontend dispose de toutes les données pour représenter le résultat.

Le code de simulation et les tests n’ont pas été modifiés pour produire ce document. La conception est fondée sur la lecture du projet ; les performances et paramètres devront être vérifiés par l’agent d’implémentation.

## État d'implémentation (backend)

Livré et testé : format v2 avec lecteur v1 figé (`SchemaV1.txt`, sauvegardes témoins), plan du village (`SettlementLayout`, quartiers, parcelles, projets, tracés), planificateur par fronts de croissance et ancres de noyau, recherches reprenables à budget partagé (A* persistant), admission atomique, couche routière (sentiers par l'usage, chemins de terre aménagés par ≤ 10 % des bras, jamais en crise), navigation avec portes et obstacles, coût de pas commun (relief, route, irrégularité du sol), lieux de repas/rencontre/dépôt dérivés des bâtiments achevés, entrepôts de proximité demandés quand un champ est trop loin du dépôt, mesures de lecture (`VillageMeasures`).

Écarts assumés par rapport au texte ci-dessus :
- À la fondation, la première pensée résout son plan sur place (premiers champs, première hutte) ; ensuite tout passe par la file partagée.
- L'« irrégularité du sol » (0,94–1,06, fixée par la graine de la carte) entre dans le coût de pas du mouvement comme du planificateur, pour que les accès réservés et les sentiers épousent le même terrain ; ce n'est pas un bruit propre au planificateur.
- Les boucles routières facultatives (gain ≥ 25 %, remboursement en huit jours) ne sont pas implémentées : seuls les accès acceptés s'aménagent.
- Les valeurs de `SettlementRules` sont une première configuration ; la calibration fine (taille des groupes, seuils d'ouverture d'un noyau) reste à affiner sur de plus grandes populations.
