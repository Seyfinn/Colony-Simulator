# Changements demandés — production, ponts, moulins et navigation

Document de transmission à l'agent chargé de réaliser cette nouvelle série de changements dans GodColony. Il complète `docs/Demandes-empire-agriculture-interface.md` ; les choix déjà confirmés dans ce premier document restent applicables.

## Objectif

Comprendre pourquoi les habitants passent beaucoup de temps à se promener, utiliser davantage la main-d'œuvre disponible pour les stocks et le commerce, construire quelques ponts utiles et cohérents, corriger les moulins à eau et rendre la liste des colonies complète et cohérente avec la carte.

Les observations de l'utilisateur sont des problèmes à reproduire. Les pistes issues du code ci-dessous ne constituent pas encore des diagnostics confirmés. Les critères proposés traduisent les résultats attendus sans imposer de valeurs d'équilibrage arbitraires.

## 1. Promenades et main-d'œuvre disponible : produire au-delà des besoins immédiats

### Observation et demande

L'utilisateur voit les habitants passer énormément de temps à se promener. Il souhaite que les colonies disposant de main-d'œuvre libre cherchent à constituer des stocks et à produire davantage, pour favoriser la production et le commerce.

### Diagnostic à réaliser

Distinguer la promenade volontaire pendant les loisirs de la promenade utilisée faute de travail réalisable. Mesurer le temps consacré au travail, aux trajets utiles, aux besoins personnels, aux loisirs et à la promenade. Pour les adultes disponibles aux heures de travail, rechercher pourquoi aucun travail ne commence : affectation au temps libre, objectif de stock atteint, manque d'intrants, poste occupé, tâche inaccessible ou autre blocage.

Dans le code examiné, `ColonistAI` appelle `Wander` pour les habitants du secteur `Free`, mais aussi après un échec ou une impossibilité de commencer le travail. `ColonyBrain` répartit les bras selon les pressions et les objectifs de réserves. Ces mécanismes expliquent des cas possibles ; leur poids réel dans une partie reste à mesurer.

### Changements à réaliser

- Lorsque les besoins prioritaires sont couverts, affecter une partie des adultes disponibles à des productions utiles et à la constitution de réserves supplémentaires.
- Choisir les productions selon les ressources locales, les ateliers, les intrants et les possibilités réelles de commerce.
- Tenir compte des stocks existants et de ceux déjà engagés ou en transit ; éviter les productions sans débouché et l'accumulation illimitée de denrées périssables.
- Permettre une réaffectation vers un travail utile quand le secteur initial n'offre aucune tâche réalisable.
- Préserver le sommeil, les repas, la santé, les relations et les loisirs nécessaires. La demande vise les bras disponibles, pas la suppression de tout temps libre ni le travail forcé des enfants.
- Expliquer les décisions dans les informations existantes du jeu, pour que le joueur comprenne pourquoi la colonie produit ou laisse du temps libre.

Les quantités cibles et la part de main-d'œuvre consacrée aux surplus restent des choix d'équilibrage. Privilégier une adaptation aux besoins et aux débouchés plutôt qu'un relèvement uniforme de tous les stocks.

### Critères de réussite

Une colonie ayant des adultes disponibles et des productions utiles accessibles constitue davantage de réserves et de surplus commercialisables. La promenade par défaut pendant les heures de travail diminue dans ces situations. L'agent fournit une comparaison avant/après du temps d'activité, des stocks, des échanges et de la survie, sans garantir artificiellement des ventes en l'absence d'acheteurs.

## 2. Quelques ponts bien placés et visuellement cohérents

### Observation et demande

Les ponts actuels semblent peu beaux et peu logiques. L'utilisateur souhaite une logique de construction crédible : quelques franchissements bien placés, plutôt qu'une dispersion de cases de pont.

### Changements à réaliser

- Planifier un ouvrage qui relie réellement deux berges praticables, avec un tablier continu et une orientation cohérente avec le cours d'eau.
- Choisir les sites selon les trajets utiles, les accès aux deux extrémités, la largeur à franchir et le coût de construction.
- Exploiter les ponts existants avant d'en ajouter un proche ; justifier les nouveaux ouvrages par un besoin de liaison ou un gain de trajet suffisant.
- Prévoir un chantier cohérent pour l'ensemble du franchissement et une comptabilité correcte des matériaux et du travail.
- Relier le pont aux chemins des berges et permettre aux habitants de l'utiliser effectivement.
- Revoir le rendu pour montrer un ouvrage continu, orienté, avec des extrémités ancrées aux berges ; conserver un rendu procédural de secours cohérent.

Il n'est pas demandé de simuler l'ingénierie structurelle complète ni de fixer un nombre maximal arbitraire de ponts. « Quelques ponts » signifie éviter les ouvrages redondants et privilégier leur utilité.

### Piste dans le code

`Bridges.OnDayStart` choisit une case de rivière fréquentée, puis ajoute des cases d'eau voisines selon leur usure et les matériaux disponibles. Cette sélection mérite d'être remplacée ou complétée par la validation d'un franchissement complet entre berges ; son rôle dans les défauts observés reste à reproduire.

### Critères de réussite

Sur plusieurs configurations de rivière, les ponts terminés relient deux berges et servent aux déplacements. Ils ne forment pas de fragments isolés ni de groupes d'ouvrages proches sans utilité distincte. Leur dessin suit l'orientation et l'emprise réellement simulées.

## 3. Moulins à eau : implantation et apparence

### Observation et demande

L'utilisateur voit des moulins à eau qui paraissent éloignés de l'eau. Il demande de corriger leur logique de placement et leurs visuels pour qu'ils ressemblent à de vrais moulins à eau.

### Changements à réaliser

- Reproduire les cas observés et distinguer un emplacement invalide d'un dessin décalé ou d'une roue placée du mauvais côté.
- Exiger une relation physique cohérente entre le bâtiment, sa roue et une eau motrice. Une eau située vaguement à proximité ne suffit pas.
- Choisir l'orientation du moulin et de la roue selon le côté réellement alimenté en eau.
- Vérifier que l'eau considérée peut alimenter le moulin : rivière ou canal alimenté, avec une règle explicite pour les retenues. Ne pas assimiler automatiquement une eau immobile à un courant moteur.
- Faire correspondre le fonctionnement et l'animation à l'alimentation réelle : une roue sans eau motrice ne doit pas sembler fonctionner normalement.
- Revoir le bâtiment, la roue à aubes et leur raccordement à l'eau, y compris dans le rendu procédural de secours.
- Conserver un accès praticable pour les travailleurs et les livraisons.

### Piste dans le code

`SitePlanner` vérifie déjà la proximité de l'eau et utilise `Hydrology.MillFlow` pour les implantations nécessitant un débit. `MillFlow` examine les cases contiguës à l'emprise du bâtiment et considère actuellement l'eau de retenue ou de canal humide comme un débit de 1. Il faut vérifier les différents chemins de placement et leur cohérence avec l'emprise graphique, plutôt que supposer qu'aucune contrainte n'existe.

### Critères de réussite

Le moulin apparaît au bord de son alimentation réelle, avec la roue du bon côté et un raccordement lisible. Il fonctionne selon cette alimentation, et ses travailleurs peuvent y accéder. Contrôler plusieurs orientations ainsi qu'une situation sans débit ; les aperçus graphiques doivent eux aussi représenter des placements crédibles.

## 4. Menu des colonies : liste complète et cohérente avec la carte

### Observation et demande

Sur la carte, l'utilisateur voit plusieurs colonies qu'il identifie comme appartenant au même empire, mais le menu déroulant ne montre jamais plus de deux colonies. Il souhaite pouvoir retrouver et visiter toutes les colonies de cet empire depuis le menu.

### Diagnostic à réaliser

Reproduire une partie présentant cet écart et comparer les identifiants, les appartenances et les statuts des lieux affichés sur la carte et dans le menu. Vérifier aussi la mise à jour de la liste, son affichage, son défilement et le traitement des clics. Ne pas supposer qu'une limite à deux entrées est codée.

Le code examiné de `Hud.ShowPlaces` parcourt les `Settlement` du `Colony` observé sans limite explicite de deux entrées. La carte peut colorer plusieurs `Colony` selon leur `Realm`. Un écart de périmètre entre le menu et la carte est donc une hypothèse à vérifier ; une couleur commune ne suffit pas à prouver l'appartenance au même empire.

### Changements à réaliser

- Établir une correspondance unique entre le vocabulaire empire/colonie, les appartenances simulées et les éléments présentés sur la carte.
- Alimenter le menu avec tous les lieux habités appartenant réellement à l'empire observé, notamment la colonie d'origine et les colonies secondaires.
- Si la carte regroupe des empires distincts dans un royaume, rendre cette distinction compréhensible. Si le diagnostic révèle que le périmètre d'empire implémenté ne correspond pas à celui voulu par l'utilisateur, expliquer la correction nécessaire.
- Actualiser la liste lors des fondations, des changements d'appartenance, des fermetures et du changement d'empire observé.
- Assurer le défilement d'une longue liste et la navigation vers le bon lieu ; actualiser la carte locale et les informations associées.
- Traiter explicitement les colonies fermées ou évacuées, sans clic conduisant vers un lieu invalide.

### Critères de réussite

Avec un empire ayant au moins trois colonies actives, toutes apparaissent et sont sélectionnables dans le menu. Le clic ouvre le bon lieu et ses données locales. La carte et le menu montrent un périmètre d'appartenance cohérent ; une colonie d'un autre empire n'est pas incluse par simple partage de couleur.

## Ordre de réalisation conseillé

1. Reproduire l'écart carte/menu et les situations de promenade, de ponts et de moulins signalées.
2. Corriger la navigation pour permettre de consulter tous les lieux concernés.
3. Adapter la mobilisation des bras disponibles et vérifier l'équilibrage économique.
4. Corriger la planification des ponts et l'implantation des moulins, puis leurs visuels.

## Repères pour l'agent

| Sujet | Points de départ |
| --- | --- |
| Travail et promenade | `Simulation/Colonies/ColonistAI.cs`, `ColonyBrain.cs`, `WorkSector.cs`, puis les décisions économiques et commerciales concernées |
| Ponts | `Simulation/Colonies/Bridges.cs`, `RoadWorks.cs`, `RoadDevelopment.cs`, `Game/Scripts/View/TerrainPainter.Roads.cs` |
| Moulins | `Simulation/Colonies/SitePlanner.cs`, `Hydrology.cs`, les règles de placement, `Game/Scripts/View/BuildingSprites.cs`, `ColonistsView.cs`, `WaterEffects.cs` |
| Menu et appartenances | `Game/Scripts/Hud.cs` (`ShowPlaces`), `Main.cs`, `WorldMapView.cs`, `WorldPanel.cs`, `Simulation/Colonies/Settlement.cs`, `Realm.cs` |

Ce repérage est une aide à la recherche, pas un inventaire exhaustif. Vérifier les comportements et les fichiers actuels avant toute modification.

## Consignes et validation

Respecter `AGENTS.md`, préserver les nombreux travaux en cours et ne faire aucun commit sans demande. Les décisions restent dans `Simulation/`, l'affichage dans `Game/`. Les habitants choisissent et construisent de manière autonome ; le joueur n'a pas à placer manuellement les ouvrages pour contourner les défauts.

Exécuter les contrôles adaptés, dont `dotnet test Simulation.Tests -c Release` pour la simulation et `dotnet build Game/GodColony.csproj` avant le contrôle visuel dans Godot. Vérifier visuellement les ponts, les différentes orientations de moulins et un menu contenant plus de deux colonies.

Pour l'évolution économique, utiliser 5, 10 ou 15 parties selon l'ampleur réelle du changement, conformément à `AGENTS.md`. Conserver les neuf graines de `GrowthTests`, réaliser les mesures supplémentaires à part et fournir leur résumé. Vérifier le déterminisme, la comptabilité des ressources et de la monnaie, les biens en transit, les pertes et les famines avec `StarvationWatch`. Si le format de sauvegarde évolue, vérifier son aller-retour actuel sans ajouter de migrations anciennes.

Le compte rendu doit expliquer les causes confirmées des problèmes, les corrections réalisées, les résultats avant/après et les éventuelles décisions encore nécessaires. Aucun nouveau choix de gouvernement ou refonte territoriale générale n'est implicitement demandé par cette liste.
