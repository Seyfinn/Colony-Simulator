# Spécification backend — grandes colonies et pouvoirs divins

Date : 6 octobre 2026. Document de transmission au développeur backend.

Statut : conception retenue pour implémentation par lots ; les constantes proposées sont des réglages initiaux à mesurer, pas des résultats validés. Aucun code C# final n'est fourni ici.

Référence de périmètre : [liste des ajouts](Plan-grandes-colonies-pouvoirs.md), A-01 à A-14. Les commandes de prestige restent dans [leur plan séparé](Plan-commandes-prestige.md). Aucun nouveau pouvoir de soin, péage, migration d'ancienne sauvegarde ou système de propriété individuelle n'est inclus.

## 1. Règles de jeu

1. Un village prospère cherche à s'agrandir en quartiers avant de disperser ses habitants. Sa taille seule ne justifie plus une demande de schisme. Les habitants conservent leurs choix d'implantation.
2. Une grande implantation devient efficace en utilisant régulièrement ses équipements : préparation partagée des lots, spécialistes expérimentés, services collectifs et livraisons groupées. Aucun bonus n'est calculé directement à partir du nombre de citoyens d'un empire.
3. La distance, la capacité des services, les ressources et le coût du travail limitent ces gains. Un camp près d'un gisement peut rester plus intéressant qu'une extraction lointaine depuis le village.
4. Les camps appartiennent à leur colonie politique et ont des stocks locaux. Leur fondation et leur ravitaillement continuent d'utiliser les habitants, biens et voyages existants. Leur développement ou leur indépendance a une justification concrète.
5. Les lots répondent à une demande ; ils ne créent ni matières ni débouchés. Un petit lot reste possible lorsqu'il faut nourrir le village immédiatement.
6. Les affectations stables favorisent la pratique d'un métier. Elles cèdent devant les besoins individuels, les quotas de survie et les urgences saisonnières.
7. Un service n'agit que sur des usagers qui peuvent réellement l'atteindre et y être accueillis. Plusieurs quartiers peuvent partager le même service ; un service saturé motive un nouvel équipement.
8. Un raccourci doit économiser suffisamment de travail pour payer son aménagement. Son existence comme projet ne rend aucune cellule praticable ou aménagée gratuitement.
9. Accepter un souhait applique un pouvoir à sa cible valide. Refuser ou ignorer ne produit aucun effet. Les bénédictions sont bornées, ne se cumulent pas et ne remplacent pas l'agriculture ou le combat.
10. L'interface lit et explique la simulation : elle ne décide ni de la rentabilité, ni des routes, ni de l'effet d'une faveur.

### Vocabulaire et portée

- `Colony` : identité politique, citoyens, prières, diplomatie et offrandes. Ses membres peuvent vivre ou voyager dans plusieurs lieux.
- `Settlement` : établissement local, carte, habitants présents, bâtiments et stocks. Les gains de production et capacités de service sont calculés ici.
- `District` : quartier de cet établissement, sans nouveau stock ni gouvernement.
- `Realm` : regroupement politique de colonies ; il ne multiplie pas les capacités locales.
- Effectif productif : utiliser les travailleurs présents et les facteurs d'âge existants. Ne pas compter les enfants, absents et voyageurs comme des adultes disponibles.

## 2. Contrats actuels vérifiés et pièges d'intégration

| Point actuel | Conséquence de conception |
|---|---|
| `WorldState.Step` avance l'horloge, le planificateur, les traitements quotidiens/horaires, les caravanes puis les colons. | Conserver ces phases et insérer des appels ciblés ; aucun passage global supplémentaire par colon et par bâtiment à chaque tick. |
| `Colony.UseSettlement` donne accès aux adaptateurs locaux. | Toute lecture/écriture locale doit porter sur l'établissement explicite ; les règles politiques ne sont exécutées qu'une fois par colonie. |
| `Schism.Daily` permet une demande à 30 présents, ou à 20 avec mauvaise humeur/sans-abri. | Remplacer l'éligibilité fondée sur la taille par une raison durable, sans supprimer les prières ni la sécession territoriale existante. |
| `Colony.AssignSectors` utilise déjà un avantage de maintien de +3 et les compétences. | Conserver cet arbitrage et améliorer le choix de métier/atelier à l'intérieur d'un secteur. |
| `Recipe` est défini dans `ToolChain.cs`, pas dans un fichier `Recipe.cs`. | Conserver le type existant ; une recette de lot est une instance concrète de ce type. |
| Les intrants sont pris à l'arrivée à l'atelier par `TakeCraftInputs`. | Un lot proposé pendant le trajet doit être revalidé à l'arrivée. Une activité engagée conserve ensuite sa recette. |
| `Activity` possède `CommittedRecipe`, `InputsInventory`, `InputsTaken` et `InputLaborHours` ; `PausedCraft` conserve l'ouvrage. | Étendre cette activité ; ne pas créer un deuxième état de fabrication concurrent. |
| `Crafting.Pending` compte actuellement des activités et les chaînes multiplient parfois ce nombre par un rendement fixe. | Ajouter un comptage exact des unités et mettre à jour tous les consommateurs avant d'activer les lots. |
| `Building.ExtensionOfId` et `SettlementPlanner.PlanExtension` existent pour enclos/marchés. | Étendre ces modules et leur admission ; les extensions achevées donnent la capacité, jamais les chantiers. |
| Les produits finis sont actuellement placés dans `Carrying`. | Les grands lots nécessitent une sortie d'atelier physique pour éviter une charge illimitée ou la destruction de l'excédent. |
| `SettlementServices` décrit actuellement repas, rencontres et dépôts ; école/soins utilisent aussi `Civic` et `ColonistAI`. | Ajouter la sélection et les capacités aux services concernés ; ne pas supposer qu'ils passent déjà tous par `ServicePoint`. |
| `LogisticsPlanner.Cargo` regroupe déjà plusieurs besoins ; `Carts` permet déjà de cumuler certaines récoltes. | Améliorer le remplissage et l'attente des flux, pas ajouter un transporteur parallèle. |
| Les recherches urbaines disposent d'un budget mondial et d'un A* persistant. | Les raccourcis et extensions utilisent le même budget et les mêmes principes de reprise. |
| Une réponse favorable à un souhait enregistre aujourd'hui `AcceptedTicks`, sans effet. | Ajouter l'application explicite et corriger la narration provisoire dans `DivineWishes` et `PrayerBook`. |
| `Warfare.Strength` reçoit des nombres, tandis que la bataille possède la liste réelle des guerriers. | Une bénédiction individuelle exige de faire passer les mêmes combattants dans les estimations et le calcul réel. |

Relire ces fonctions avant modification : le dossier est partagé et leur contenu peut évoluer. Les contrats actuels sont le point de départ, pas une raison de conserver une erreur découverte pendant l'intégration.

## 3. Architecture proposée

Les identifiants de types suivent les noms anglais déjà présents. Texte utilisateur, commentaires et explications restent en français. Les méthodes ci-dessous sont des contrats proposés, pas des signatures C# obligatoires.

### 3.1 Nouveaux fichiers de règles dans `Simulation/Colonies/`

| Fichier / type | Responsabilité et méthodes clés |
|---|---|
| `ScaleRules.cs` / `ScaleRules` | Constantes initiales de lots, stabilité, extensions, services et regroupement des livraisons. Pas de framework de configuration. |
| `GrowthPolicy.cs` / `GrowthPolicy` | `ObserveDaily`, `EvaluateDeparture`, `CompareGrowthOptions`. Raisons durables et coût marginal d'un départ ; réutilise `ExpansionPlanner` et les résultats de placement. |
| `BatchProduction.cs` / `BatchProduction` | `Choose`, `BuildRecipe`, `TryCommit`, `Finish`, `AvailableOutput`, `TryCollect`. Choix borné d'un lot et stock de sortie physique. Les transitions passent toujours par `ColonistAI`. |
| `WorkshopCapacity.cs` / `WorkshopCapacity` | `Slots`, `MaxBatch`, `FindFreeSlot`, `NeedsExtension`, `CanAdmitExtension`. Capacités des bâtiments principaux et de leurs extensions achevées. |
| `SpecialistAssignments.cs` / `SpecialistAssignments` | `RefreshDaily`, `PreferredJob`, `Invalidate`. Préférence de métier/atelier, sans réservation exclusive d'un métier. |
| `CivicServices.cs` / `CivicServices` | `Candidates`, `TrySelect`, `Occupancy`, `Coverage`, `NeedsAdditionalSite`. Sélection accessible et places pour école, soins et taverne. |
| `RoadShortcuts.cs` / `RoadShortcuts` | `RecordCompletedTrip`, `ReviewDaily`, `AdvanceSearch`, `Revalidate`. Alimente les travaux et recherches existants, sans autre ordonnanceur. |
| `DivinePowers.cs` / `DivinePowers` | `ApplyAccepted`, `InvalidateTargets`, `ExpireDue`, `HarvestBonus`, `ChampionStrengthBonus`. Application idempotente et consommation des effets. |
| `ScaleMeasures.cs` / `ScaleMeasures` | Compteurs bornés et vues immuables pour équilibrage et interface. Aucun choix de gameplay dans les accesseurs. |

Le regroupement des transports reste dans `LogisticsPlanner`/`TerritorialTravel` et la circulation locale dans `Carts`/`ColonistAI` : aucun nouveau moteur de transport.

### 3.2 État persistant minimal

| Propriétaire | Champs / types à ajouter | Règle de possession |
|---|---|---|
| `Settlement` | `GrowthState` de type `SettlementGrowthState` ; `ScaleLedger` de type `SettlementScaleLedger` ; liste bornée `TravelFlows` de `TravelFlow` ; `SupplyWaits` de `SupplyWaitState`. | Données locales, jamais recopiées dans tous les adaptateurs de `Colony`. |
| `SettlementGrowthState` | Dernier jour observé ; jours consécutifs de mécontentement, manque de logements, impossibilité locale confirmée ; dernière raison exposée. | Un budget de recherche épuisé n'incrémente pas l'impossibilité physique. |
| `Colonist` | `PreferredWorkshopId`, `PreferredProduct`, `SpecialtyChosenTicks`, `SpecialtyBlockedSinceTicks` ; compteur de trajet courant si nécessaire. | Préférence locale invalidée en cas de déménagement ; compétences et secteur existants restent la vérité. |
| `Activity` | `BatchCount` (1 par défaut), `WorkshopSlotId` (-1 hors atelier), `PlannedRecipe`, compteur de travail effectivement exécuté. | `CommittedRecipe` devient la vérité après prise des intrants. Une pause conserve le même lot et sa place. |
| `Building` principal | `OutputInventory` de type `Stockpile` ; coûts de travail restant attachés aux sorties par ressource ; suivi d'utilisation dans le registre local. | Sorties finies sur place, distinctes du stock commun. Pas de stock doublé sur les extensions. |
| `Colony` | `DivineEffects` de `DivineEffect` et compteur d'identité local. | Une seule identité politique possède chaque effet à un instant donné. |
| `DivineEffect` | Id, identité du souhait source, type, cible, début, fin, état ; liste de `BlessedPlot` pour les récoltes. | Identité source = `(ColonyId, WishId)` figée, même après transfert politique. |
| `BlessedPlot` | Cellule, `PlantedTicks`, `LastHarvestTicks` lors de l'accord, état consommé/invalide. | Uniquement le cycle présent lors de l'accord ; aucune nouvelle semaille n'en bénéficie. |
| `TravelFlow` | Origine/destination stables, compte récent, temps de trajet réel, dernier jour, référence du raccordement en étude. | Fenêtre de 14 jours, maximum 256 couples ; éviction déterministe des moins récemment utilisés, puis départage par clés. |
| `SupplyWaitState` | Source, destination, première date du besoin différé, dernière cause. | Pas de réservation fictive de cargaison pendant l'attente ; revalider au départ. |
| `PlanningJob` | Références des deux extrémités, mesure du flux, coût initial, coût proposé, état de recherche du raccourci ; référence du principal pour extension. | Réutiliser l'A* persistant et les curseurs existants. |
| `SettlementScaleLedger` | Anneau journalier de 30 jours : production, intrants/combustible, travail, transport, occupation/capacité, attente des services. | Compteurs d'observation ; toute décision utilisant la fenêtre doit retrouver les mêmes valeurs après reprise. |

`BatchChoice`, `GrowthOption`, `ServiceCoverage`, `ScaleSnapshot`, `DivineEffectView` et les candidats de comparaison sont des résultats temporaires. Ne pas les persister ni les traiter comme nouvelles autorités du monde.

### 3.3 Énumérations

- Nouveau `GrowthReason` : `None`, `PersistentHousingPressure`, `LocalCapacityBlocked`, `PersistentDiscontent`, `BetterSettlementOpportunity`. Plusieurs faits peuvent soutenir une raison ; la narration donne la raison principale.
- Nouveau `DivineEffectKind` : `HarvestYield`, `ChampionStrength`.
- Nouveau `DivineEffectStatus` : `Active`, `Consumed`, `Expired`, `TargetInvalid`.
- Ajouter à `DivineWishStatus` un résultat terminal `AlreadyBlessed` pour une cible déjà favorisée au moment de l'accord, avec explication visible. Une prière approuvée reste un accord ; elle n'est pas présentée comme un effet supplémentaire.
- Ajouter `CollectWorkshopOutput` à la fin de `ActivityKind`.
- Ajouter `RoadShortcut` et `WorkshopExtension` à la fin de `DevelopmentKind`. Réutiliser `SegmentFunction.Link`, `ProjectState` et les priorités existantes.
- Réutiliser `BuildingType`, `ResourceType`, `SkillType`, `TerritorialPurpose` et les états de sauvegarde existants ; aucun nouveau bâtiment ou métier n'est nécessaire pour ce périmètre.

Ne jamais insérer de valeur au milieu d'une énumération existante. Départs, tri et tirages doivent rester explicites ; ne pas étendre une boucle aléatoire à de nouvelles valeurs par accident.

## 4. Réglages initiaux et formules

Tous ces réglages sont des hypothèses de départ. Les seuils politiques sont des conditions observées, pas des paliers « petit/grand village ».

### 4.1 Croissance

- Pression de logement : au moins 4 sans-abri pendant 5 jours, avec besoin de logements encore non résolu.
- Mécontentement : humeur moyenne inférieure à 0,45 pendant 10 jours. Retour au calme au-dessus de 0,55 pendant 3 jours pour éviter les oscillations.
- Saturation locale : 5 observations quotidiennes confirmant `NoSpace` ou `TravelBudgetExceeded` après exploration complète des fronts utiles, sur révision pertinente inchangée. `Pending`, recherche périmée ou budget épuisé ne signifient pas saturation.
- Opportunité : un établissement extérieur réalisable amortit installation, transport et services, avec un coût marginal par habitant déplacé au moins 15 % inférieur à l'alternative locale sur un horizon de 40 jours. Utiliser uniquement des informations connues ; absence d'information = option inconnue.
- Conserver les garde-fous actuels de `Schism` : groupe viable, place dans la limite mondiale, absence de guerre, délai de refus et accord du joueur. Revalider le groupe et ses ressources au moment de l'accord.
- Une ambition personnelle contribue au choix du meneur parmi des options recevables. Elle ne réintroduit pas un schisme déclenché par le nombre d'habitants.

### 4.2 Lots

Premier déploiement : four à pain et moulin. Étendre ensuite au bas fourneau et à la forge si les mesures sont bonnes. Exclure frappe, fermentation, extraction de gisement et offrandes du mécanisme générique : leurs quotas, durées ou réserves ont des contrats distincts.

Pour `k` répétitions d'une recette de référence :

```text
matières transformées(k) = k × matières transformées(1)
temps(k) = préparation + k × geste
préparation = 0,30 × temps(1)
geste = 0,70 × temps(1)
combustible partagé(k) = plafond(fixe + k × variable)
fixe = 0,50 × combustible(1) ; variable = 0,50 × combustible(1)
sortie(k) = k × sortie(1)
```

Le combustible partagé est explicitement déclaré par recette : le bois qui devient charbon est une matière transformée et n'est jamais économisé comme du chauffage. Une recette sans combustible déclaré conserve tous ses intrants linéaires. Les mêmes intrants multiples sont fusionnés avant débit. Pour `k = 1`, conserver exactement la recette actuelle.

Exemple actuel du pain à la farine : 3 farines + 1 bois donnent 4 pains en 14 secondes. Un lot de 4 utilise 12 farines + 3 bois et donne 16 pains en 43,4 secondes avant compétence. C'est 22,5 % de travail de cuisson en moins et 25 % de bois en moins par pain ; cela ne mesure pas le coût de toute la chaîne.

- Capacité initiale d'un atelier éligible : 2 répétitions, 1 poste.
- Première extension achevée : 4 répétitions, 1 poste.
- Deuxième extension : 4 répétitions, 2 postes, si chaque poste a une case de travail accessible distincte.
- Extensions suivantes : aucun gain automatique au-delà de ces plafonds ; ne pas construire un module sans capacité ou usage supplémentaire défini.
- Un seul lot actif par poste, y compris pendant le trajet engagé et la pause. Les postes ne sont pas les métiers des habitants.
- Maximum de sortie stockée : deux lots maximaux par poste. Compter ce stock et les lots engagés avant de démarrer, pour ne pas dépasser la capacité.
- Choisir `k` de 1 au plafond selon demande nette, intrants disponibles, stockage de sortie, délais et réserves vitales. Autoriser uniquement l'excédent indivisible d'une recette de base, pas d'un grand lot.
- Le débit du moulin et la vitesse personnelle sont appliqués après le calcul du lot, une seule fois. Pas de second bonus de productivité pour la taille.

### 4.3 Investissement, spécialisation et services

- Extension : utilisation productive supérieure à 70 % des heures de travail possibles sur 10 jours complets, demande encore présente, source d'intrants crédible, survie assurée et amortissement au plus 40 jours. Le trajet et les repas ne comptent pas comme occupation productive.
- Réutiliser les coûts de construction d'extensions là où ils sont adaptés ; chiffrer les nouveaux modules à partir du coût du bâtiment et de leur emprise, puis valider leur amortissement. Ne pas ajouter une taxe d'entretien abstraite dans cette version.
- Préférence d'artisan : horizon de demande de 3 jours ; conserver le choix au moins 3 jours sauf impossibilité, départ ou crise. Changer si le gain de travail estimé dépasse 15 %, ou si le travail préféré est bloqué depuis 1 jour.
- École : 8 places simultanées ; infirmerie : 4 places de patients et 1 soignant actif par bâtiment ; taverne : 8 places. Valeurs de départ, à exposer dans `ScaleRules`.
- Les trajets vers un service ne doivent pas dépasser 10 % du temps quotidien disponible pour le travail ou l'activité concernée, aller-retour inclus. Réutiliser les budgets existants lorsqu'ils sont plus précis ; ne pas comparer des secondes réelles à des heures de simulation sans conversion.
- Saturation de service sur 5 jours ou défaut durable de couverture : proposer un site supplémentaire, après comparaison avec un raccordement utile. Les services vitaux sont prioritaires sur le confort.

### 4.4 Transport et chemins

- Regroupement des missions internes non urgentes : attendre au plus 1 jour depuis la première demande, ou partir dès 70 % de capacité utile remplie. Le poids des provisions, équipements et cargaisons respecte le calcul existant.
- Une arrivée trop tardive pour couvrir la rupture prévue interdit l'attente, quelle que soit la priorité nominale. Nourriture d'urgence et évacuation ne sont jamais retardées pour remplir une charrette.
- Raccourci : gain de trajet au moins 25 % et coût amorti en 8 jours au plus par le flux réel récent. Comparer la route actuelle à la route proposée avec `TraversalCost`, et intégrer terrassement/défrichement réels autorisés.
- Examiner au plus 8 couples de flux par établissement à la revue quotidienne, et au plus 1 projet facultatif actif comme aujourd'hui. Toutes les cellules/A*/validations passent par le budget mondial existant.
- Pas de recherche quotidienne de tous les couples de bâtiments ni de boucle sur toute la carte. L'amélioration d'un chemin existant n'annule pas les trajets en cours tant qu'ils restent valides.

### 4.5 Pouvoirs

- Récolte : +25 % sur le rendement effectivement récolté des parcelles déjà en croissance ou mûres du champ cible au moment de l'accord. Une seule récolte par parcelle ; expiration au plus tard après 2 saisons. Ni accélération de pousse, ni suppression de sécheresse, ni protection contre le gel dans ce premier effet.
- Champion : ajout de 25 % de la force de base d'un guerrier, uniquement quand ce colon participe réellement au combat ; durée 1 année de simulation. Ce bonus individuel s'ajoute avant les facteurs collectifs existants.
- Utiliser un coefficient entier de 1250/1000 pour les récoltes ; arrondi déterministe au plus proche, égalités vers le haut. Le gain réel peut être nul sur un très petit rendement et doit être affiché sans promettre un minimum artificiel.
- Une cible ne porte qu'un effet du même type actif ; pas de cumul ni de prolongation gratuite. Filtrer ces cibles à la création des souhaits ; revalider lors de la réponse.
- L'offrande n'est ni rendue, ni consommée une deuxième fois. Le miracle ne crée pas de monnaie et ne donne aucun bonus à toute la colonie par défaut.

## 5. Pseudo-code détaillé

### 5.1 Croissance et décisions de séparation

```text
GrowthPolicy.ObserveDaily(monde, établissement)
    lire présence, logements, humeur et résultats locaux de planification
    mettre à jour une seule fois les compteurs du jour
    si révision spatiale pertinente changée : invalider la saturation ancienne
    conserver Pending comme attente, jamais comme impossibilité
    si logement manque : maintenir la demande Housing existante
    comparer développement local, service/raccordement, camp et importation
    conserver les coûts et motifs utiles dans une vue de décision

Schism.Daily(monde, colonie)
    exécuter d'abord la sécession territoriale existante
    vérifier guerre, délai, limite et viabilité du groupe
    évaluer les raisons durables du village concerné
    si aucune raison recevable : terminer sans tirage supplémentaire
    si agrandissement local viable résout la pression : privilégier ce projet
    sinon vérifier région connue, groupe, trajet et coût de l'installation
    appliquer la chance politique existante seulement à ce candidat recevable
    poser une prière indiquant la raison et les coûts

à l'accord
    recalculer présence, stocks libres, logements conservés et trajet
    si départ devenu impossible : expliquer le blocage sans retirer de biens
    sinon utiliser le départ/fondation existant avec prélèvements réels
```

La préférence locale n'est pas un veto éternel : si son projet reste bloqué sur une impossibilité confirmée, la solution extérieure redevient recevable. Un camp ne devient pas automatiquement une nouvelle colonie politique.

### 5.2 Prévision exacte de la demande

```text
Crafting.PendingUnits(établissement, produit)
    additionner sorties des recettes engagées de Activity et PausedCraft
    pour un trajet non commencé, compter la PlannedRecipe proposée
    ne jamais compter deux fois la même activité

demande nette(produit)
    objectif existant local + débouchés externes confirmés non déjà inclus
    moins stock libre utilisable
    moins sorties libres d'ateliers accessibles
    moins fabrications engagées et livraisons certaines utiles à l'horizon
    gérer les biens promis séparément selon les réservations existantes
```

Ajouter `PendingUnits` sans modifier silencieusement la signification de `Pending`. Migrer tous les usages de `FoodChain`, `ToolChain`, `ExtendedIndustry` et des décisions de chantier vers les unités exactes ; supprimer ensuite les multiplications par le rendement de base. Ne pas compter des offres commerciales comme des livraisons certaines.

Inclure aussi les produits portés en cours de livraison locale, sans les compter simultanément dans un buffer ou dans le stock. Un produit engagé pour une autre destination n'est pas une réserve disponible pour les ménages. Centraliser ce calcul dans les besoins existants pour éviter qu'une chaîne, un export et une extension réclament chacun les mêmes unités.

`Economy.Cost` utilise déjà le coût observé par `LaborLedger.HoursPerUnit`, avec un coût de référence en l'absence de mesure. Conserver cette remontée des économies réelles dans les prix et les arbitrages ; ne pas lui appliquer un nouveau coefficient de population. Les coûts de référence restent ceux d'une recette de base et ne présument pas qu'un atelier sera toujours utilisé au maximum.

### 5.3 Choisir, engager et achever un lot

```text
BatchProduction.Choose(colon, atelier, recette de base)
    trouver un poste libre et une case praticable de ce poste
    calculer demande nette et capacité de sortie libre
    pour k du maximum autorisé jusqu'à 1
        construire une recette concrète fusionnant les intrants identiques
        vérifier stock disponible après engagements et réserves vitales
        vérifier débouché, horizon et vieillissement des intrants périssables
        vérifier capacité de sortie, même si tous les lots engagés finissent
        si recevable : retourner k, recette, poste et durée estimée
    retourner aucun travail

ColonistAI.TryCraft
    garder l'arbitrage existant entre filières et urgences
    préférer le métier habituel parmi les travaux réellement recevables
    choisir un lot, calculer le chemin, puis revalider poste et demande
    à Commit : réserver le poste par l'activité, sans retirer les intrants
    si le chemin échoue : aucune réservation ni aucun retrait

TakeCraftInputs à l'arrivée
    si une recette est déjà engagée : ne reprendre aucun intrant
    sinon revalider recette proposée, poste, intrants et demande
    si elle n'est plus recevable : annuler et replanifier au prochain choix
    débiter atomiquement avec TryTakeInputs dans InputsInventory
    figer CommittedRecipe, durée et quantité ; marquer InputsTaken

pendant le travail
    accumuler le travail effectivement réalisé
    pauses repas/repos et santé gardent recette, intrants et poste
    les intrants continuent de vieillir avec le mécanisme actuel

Finish
    vérifier que le lot possède encore ses intrants et son atelier
    consommer l'inventaire engagé une seule fois
    déposer la sortie et son coût de travail dans OutputInventory
    publier les compteurs de fabrication ; libérer le poste
    proposer la livraison physique avant de reprendre un nouveau lot

annulation / mort / départ / atelier disparu
    libérer le poste et rendre uniquement les intrants encore présents
    annuler les engagements spéciaux si nécessaire
    les intrants périmés déjà perdus ne sont pas recréés
```

Le lot n'est pas recalculé parce qu'une extension finit ou qu'un artisan progresse pendant sa pause. Le coût du lot prend la compétence au moment de l'engagement, selon le contrat actuel de durée des activités. Pas de double prise des intrants dans la reprise.

Tous les sélecteurs de `FoodChain`, `ToolChain`, `ExtendedIndustry` et `Crafting` doivent consulter la même disponibilité de postes ; retirer leurs éventuelles hypothèses « un artisan par type d'atelier ». À l'arrivée, une recette devenue irrecevable est abandonnée sans débit : ne pas remplacer sa quantité tout en gardant l'ancienne durée. Les intrants ne sont plus simplement « réservés » une fois retirés : ils appartiennent exclusivement à `InputsInventory`.

### 5.4 Sorties d'atelier et transport local

```text
TryCollect(colon, atelier)
    choisir une ressource utile et un trajet valide vers la sortie
    à l'arrivée, recalculer quantité disponible et capacité réelle de port
    retirer atomiquement cette quantité, avec ses âges et coûts proportionnels
    transférer à Carrying et aux données de cargaison existantes
    ensuite utiliser Deliver vers un dépôt accessible

au dépôt
    transférer quantité et âge au stock local une seule fois
    conserver l'intégration actuelle du coût de travail dans LaborLedger
    ne pas enregistrer une deuxième production à la livraison
```

Les sorties sont des biens physiques comptés dans l'inventaire total, mais indisponibles au stock commun avant livraison. Les capteurs de survie doivent distinguer nourriture au dépôt et nourriture récupérable à l'atelier : une colonie affamée donne priorité à cette récupération accessible avant de déclarer qu'elle n'a aucun aliment.

Toute destruction d'atelier traite explicitement sa sortie comme perte physique, enregistrée une seule fois. Le retrait d'un bâtiment, l'abandon d'un établissement et le transfert politique doivent passer par cette transition. Ne jamais déposer implicitement tout le buffer au stock pour masquer une destruction.

### 5.5 Spécialistes et extensions

```text
SpecialistAssignments.RefreshDaily(établissement)
    dériver postes utiles de demande, matériel et ateliers accessibles
    classer travailleurs admissibles selon compétence et continuité existantes
    conserver préférences encore utiles dans les quotas WorkShares
    comparer alternatives sur horizon de 3 jours avec coût de trajet
    changer selon seuil de gain ou blocage durable
    utiliser des départages par identifiants, sans nouveau tirage

WorkshopCapacity.NeedsExtension(principal)
    vérifier 10 jours de mesures, demande et approvisionnement
    comparer extension à autre atelier dans un quartier mieux placé
    inclure construction, transport et heures productives économisées
    si rentable et survie assurée : déposer une demande WorkshopExtension
    sinon exposer motif : intrants, demande, main-d'œuvre, accès ou coût

admission du projet d'extension
    utiliser recherche reprenable, parcelle et accès validés atomiquement
    ne pas appeler une recherche synchrone non bornée depuis Think
    respecter ExtensionOfId et ne pas bloquer l'entrée du principal
    compter la capacité uniquement après fin des travaux
```

Les préférences guident la sélection ; un artisan malade ou sans travail n'enferme pas le village dans un quota impossible. Les activités en cours ne sont pas abandonnées chaque heure pour améliorer un classement.

### 5.6 Services partagés

```text
CivicServices.TrySelect(colon, usage)
    lister bâtiments achevés compatibles et cases de service
    compter engagements en route + usages sur place, pas uniquement arrivés
    classer quelques candidats selon trajet et disponibilité
    vérifier chemin réel et plafond de trajet
    au Commit, revalider capacité et réserver par Activity.Building
    en fin/abandon, la disparition de l'activité libère la place

Coverage quotidienne
    compter les demandes non servies et temps réellement perdus
    si déficit durable : demander autre bâtiment ou raccordement
    éviter les projets doublons déjà acceptés / en recherche
```

Adapter `TryStudy`, `TryRecover`, `TryRelax`, `TryHeal`, les apprentissages et `Health.Treat` pour que l'effet de service concerne les usagers réellement présents. Garder les soins de base et le repos hors bâtiment quand aucun service n'est accessible. Ne pas donner le bonus scolaire à tous les enfants par simple `Civic.Has(School)`.

Les puits, murs et stockage global ont des contrats spécifiques existants : ne pas les transformer en nouveaux réseaux de service dans ce lot. Le marché conserve ses règles d'extraction et de commerce ; ses étals ne donnent pas un bonus universel aux ménages.

### 5.7 Missions internes groupées

```text
LogisticsPlanner.PlanSupply / PlanReturn
    obtenir les besoins actuels, puis retrancher ce qui arrive déjà à temps
    grouper uniquement les besoins de même source et destination
    construire Cargo avec priorités, provisions et capacité existantes
    estimer la date d'arrivée, vieillissement inclus
    si rupture avant arrivée : départ immédiat réalisable, sinon blocage explicite
    si urgence : partir sans seuil de remplissage
    sinon partir si remplissage >= 70 % ou attente >= 1 jour
    sinon retenir seulement la première date d'attente, sans prélever de cargaison

au départ
    revalider trajet, propriétaires, habitants disponibles et stocks libres
    charger par TerritorialTravel.Depart, avec transactions existantes
    supprimer l'attente seulement si le départ réussit ou le besoin disparaît
```

Pas de tournées à destinations multiples dans cette version. Étendre le dimensionnement des charrettes à la charge et aux trajets mesurés plutôt qu'au seul plafond actuel d'une ou deux charrettes. Le nombre fabriqué reste borné par le besoin simultané réellement observé, les artisans et les matières.

### 5.8 Raccourcis autonomes

```text
RecordCompletedTrip
    pour un trajet terminé, enregistrer extrémités, durée et usage
    pour un trajet abandonné, ne pas publier un passage fictif terminé
    mettre à jour un couple borné ; expirer les données vieilles

ReviewDaily
    si crise ou ouvrage facultatif actif : conserver l'attente
    choisir au plus 8 couples au plus grand coût de détour observé
    créer une demande RoadShortcut sans doublon

SitePlanner.Advance pour RoadShortcut
    vérifier les extrémités et révisions actuelles
    rechercher progressivement une variante praticable
    estimer le coût avec les surfaces futures réalisables
    réserver seulement après validation complète du corridor
    si gain >= 25 % et amortissement <= 8 jours : accepter un Link
    créer un DevelopmentProject exécuté par RoadWorks
    sinon classer le motif et les événements qui permettront un nouvel essai
```

Un A* sur les coûts actuels ne suffit pas à découvrir un axe avantageux après travaux : la recherche doit considérer le coût futur aménagé, puis chiffrer séparément le travail. Aucune traversée de bâtiment, culture, eau infranchissable ou pente interdite n'est admise pour obtenir un bon score. Les ponts existants sont utilisables ; la construction de nouveaux ponts reste dans leur mécanisme propre, pas comme sous-produit gratuit d'un raccourci.

Si inondation ou construction invalide un projet, suspendre/rechercher à nouveau selon les règles existantes. Une perte temporaire d'accès nécessaire relève de `AccessRepair` et garde sa priorité ; elle ne doit pas attendre un raccourci de confort.

### 5.9 Application et vie des pouvoirs

```text
WorldState.AnswerPrayer(prière, accord)
    vérifier identité de la prière et statut Pending
    appliquer la réponse via PrayerBook, comme aujourd'hui
    si souhait accepté : DivinePowers.ApplyAccepted(monde, colonie, souhait)

ApplyAccepted
    exiger AcceptedTicks présent et statut encore compatible
    si un effet de ce souhait existe : retourner le résultat, sans rien ajouter
    vérifier cible, propriété et type du souhait
    si cible invalide : marquer TargetInvalid et publier la raison
    si effet du même type déjà actif sur la cible : marquer AlreadyBlessed
    sinon préparer l'effet et sa date de fin
    pour récolte : figer uniquement les cycles des parcelles présentes
    ajouter l'effet et marquer le souhait Fulfilled dans la même transition
    publier un fait d'application et une narration correspondant à l'effet réel

HarvestBonus(établissement, parcelle, rendement réel, horloge)
    lire les effets récolte actifs du champ, sur identité locale complète
    trouver le cycle exact non consommé de cette parcelle
    si expiré, détruit ou cycle changé : aucun bonus
    sinon calculer le rendement entier après pertes normales et coefficient
    consommer l'entrée une seule fois, puis terminer l'effet si tout est traité

ChampionStrengthBonus(combattants réels, horloge)
    additionner uniquement les bonus des participants valides et présents
    ne pas bénir les armes, les alliés ni une bande dont le champion est absent
    le combattant peut être en mission : utiliser son appartenance réelle

ExpireDue / InvalidateTargets
    expirer aux dates prévues ; lectures utilisent toujours now < ExpiresTicks
    invalider décès, suppression du champ ou fin/perte du cycle agricole
    un souhait Fulfilled reste historique même si son effet expire ensuite
```

Le bonus de récolte est consommé à la moisson effective, avant de réinitialiser `PlantedTicks`/`LastHarvestTicks` et l'état de la parcelle. Réutiliser le rendement normal après piétinement, puis appliquer le coefficient ; aucune ressource n'est attribuée directement au stock.

Modifier `TryFulfill` pour exiger aussi un accord et une preuve interne d'application liée au souhait. Un appel externe avec le seul booléen `effectApplied = true` ne doit pas pouvoir fabriquer un exaucement sans effet. Toutes les réponses utilisateur et tests passent par ce contrat.

La bataille, les estimations d'attaque/défense et les décisions qui utilisent ces estimations partagent la même fonction de force sur une sélection de combattants. Conserver le nombre et l'ordre des tirages aléatoires de résolution des combats quand le seul ajout est le bonus.

## 6. Intégration dans `WorldState.Step` et pyramide

```text
Step
    avancer Clock comme actuellement
    si effet arrivé à échéance : expirer les entrées dues, avant leurs usages
    SettlementPlanningScheduler.Tick : budget unique incluant nouveaux jobs

    au changement de jour, par établissement actif sous UseSettlement
        conserver terrain, nature, agriculture, chemins et lifecycle existants
        vieillir les sorties d'atelier avec les mêmes règles de Stockpile
        après les changements quotidiens : fermer les compteurs de la veille
        GrowthPolicy.ObserveDaily ; SpecialistAssignments.RefreshDaily
        proposer extensions/services/raccourcis selon priorités existantes
        ne pas exécuter les A* complets pendant cette revue

    à l'heure de pensée, par établissement
        conserver ColonyBrain.Think et sa pyramide
        inclure unités exactes, sorties récupérables et déficits de service
        poursuivre les activités et services existants

    à l'heure politique, par colonie une seule fois
        garder chefs, royaumes, diplomatie et sécessions
        Schism.Daily utilise GrowthPolicy
        valider les effets dont les cibles ont changé

    caravanes et colons : phases existantes
        engagements/livraisons/achèvements mettent à jour les compteurs locaux
        aucune décision ne dépend de la vitesse d'affichage
```

Ne pas déplacer les phases existantes de climat/lifecycle pour simplifier les nouvelles règles. Les observations quotidiennes nouvelles se placent après leurs mutations ; les validateurs vérifient toujours les données actuelles au moment d'une transition.

| Niveau | Travaux inclus |
|---|---|
| Survie individuelle | Manger, repos, récupération, se mettre à l'abri ; pause des lots au besoin. |
| Survie collective | Vivres accessibles, récupération du pain fini, chauffage, semailles/récoltes urgentes, ravitaillement d'urgence et accès nécessaires. |
| Logement et continuité | Hébergement, logements, accès des projets prioritaires, remplacements nécessaires. |
| Production utile | Outils, transformation alimentaire, approvisionnement, spécialiste disponible ; extension seulement si elle ne compromet pas les niveaux précédents. |
| Confort et investissements facultatifs | Services de confort, grands lots sans urgence, extensions de prospérité, raccourcis facultatifs, offrandes. |

La préparation d'un lot alimentaire peut rester une tâche de survie ; sa taille doit respecter le délai avant alimentation. L'application d'un souhait est une transition de réponse du joueur, sans monopoliser de main-d'œuvre ni court-circuiter les besoins.

## 7. Conservation, propriété, persistance et déterminisme

### Conservation des biens

```text
inventaire physique total = stocks d'établissements
    + intrants engagés Activity / PausedCraft
    + sorties d'ateliers
    + biens portés
    + caravanes / autres conteneurs physiques existants
```

Charrettes et équipements utilisés restent comptés selon leur convention actuelle : ne pas compter une seconde fois une charrette gardée au stock mais marquée en service. Une conversion retire ses intrants et crée ses produits selon la recette ; une perte et une consommation sont des sorties expliquées. Une promesse est un engagement, pas un deuxième bien.

Les coûts de sortie sont répartis proportionnellement lors des retraits, en laissant le reste exact attaché au buffer. Pour des produits périssables, transférer les âges réels avec les fonctions d'inventaire existantes ; pas de nouvel âge zéro au ramassage ou à la reprise. Un lot frais ne rajeunit pas un lot ancien.

`LaborLedger` garde son rôle de coût réel. Les indicateurs supplémentaires séparent travail productif, trajet et pauses personnelles ; ne pas créditer 16 productions au terme d'un lot de 16 puis 16 autres au dépôt. Vérifier l'intégration avec `WorkCycleStartTicks` et `WorkCycleExtraHours` pour éviter les doubles coûts d'intrants.

### Changements politiques et pertes

- Une sécession transporte avec l'établissement ses sorties, projets, observations et effets de champ. Un effet de champion suit la personne. Conserver l'identité source, sans copier l'effet chez deux propriétaires.
- Un schisme de personnes transporte les effets individuels concernés ; ceux des champs restent au lieu. Les préférences d'atelier sont invalidées au départ.
- Une conquête conserve les effets attachés aux cibles survivantes ; retirer ceux des morts et retirer les effets des champs détruits. Les invalidations n'ajoutent aucun tirage.
- Le principal détruit invalide ses postes et traite ses intrants/sorties selon les règles explicites de restitution/perte ; une extension détruite ne recrée pas les produits. Les lots engagés conservent leur recette mais doivent être annulés si leur poste physique disparaît.
- Un changement de propriétaire invalide un job avec ses anciens droits ; le replanifier avec la bonne colonie et révision, sans accepter un ancien résultat.

### Sauvegarde actuelle

Enregistrer les nouveaux types persistants dans `StateGraph`. Les objets temporaires et caches de disponibilité restent reconstruits et sans autorité persistante. Persister les compteurs, dates, lots proposés/engagés, sorties avec âges/coûts, effets, fenêtres de mesures décisionnelles et frontières A*.

Les listes d'échéances, index d'effets par cible et index de postes peuvent être des caches reconstruits ; leurs lectures ne font pas progresser le monde. L'expiration n'exige pas de rescanner toutes les offrandes à chaque tick : traiter les dates dues et vérifier la date lors de chaque usage. Les seuls traitements mutateurs de bénédiction agricole sont l'application, la moisson réelle, l'invalidation et l'expiration.

Ne pas ajouter de migration ni de lecture d'un ancien format pour cette évolution. Vérifier uniquement le format courant et la continuation identique après reprise. Si des postes sont indexés par un cache, les reconstruire depuis les activités actives et suspendues sans déplacer ni réattribuer les travaux.

Départager par identifiants stables, index de cellule et ordre de priorité explicite. Employer les générateurs existants uniquement pour les mécanismes qui en ont besoin ; lots, services, raccourcis et choix de spécialiste n'en ajoutent aucun. Les changements volontaires de schisme changeront certaines trajectoires : renforcer les invariants et analyser les nouvelles mesures, sans modifier les graines pour masquer une régression.

## 8. Contrat de lecture pour `Game/`

Le développeur backend fournit des vues immuables/lectures sans mutation, avec identités et raisons structurées. Ne pas créer un bus d'événements général ou une base de données pour ces panneaux.

| Vue | Données nécessaires |
|---|---|
| Économie | Quota mondial et part de la colonie, frappé/engagé, sorties en attente, taille/capacité/utilisation des lots, coûts observés et période de mesure. |
| Offrandes et prières | Modèle, matériaux, travail, état, cible, réponse, effet appliqué, expiration ou motif d'échec ; différencier accord et exaucement. |
| Territoires | Type de lieu, productions récentes, besoins, indices connus et confiance, route connue, livraison attendue, priorité, motif et âge de l'attente. |
| Village | Quartiers, capacités des services, usagers/attentes, ouvrages en chantier et surfaces réellement achevées. |
| Carte mondiale | Arêtes `WorldMap.Roads.Built`, niveaux réels et `TerritorialPurpose` de chaque mission. |
| Graphismes | Bâtiments Mint/Shrine et monuments réels : modèle, position, matériaux et état. Rendu procédural propre avant variantes d'images, conformément à T-033/T-034. |

Les nouvelles alertes utilisent les journaux et faits persistants existants quand ils conviennent ; ajouter un fait dédié seulement si le contrat manque. Une file d'affichage évite l'écrasement de deux alertes rapprochées. À l'ouverture/chargement, initialiser le curseur aux faits déjà présents ; aucune ancienne bénédiction ne revient comme nouvel événement.

L'interface reste jouable en pause et à vitesse accélérée ; elle ne parcourt pas toutes les cartes pour calculer les chiffres. Ne montrer ni statistiques d'une région inconnue ni réserve vraie d'un gisement comme connaissance des habitants.

## 9. Livraison par lots et contrôles d'acceptation

| Lot | Travail et dépendances | Vérification décisive |
|---|---|---|
| L0 | Mesures initiales, audit ciblé des consommateurs de Pending, stocks, services, transitions de bâtiment et sauvegarde. | Référence comparable et contrats actuels recensés sans refaire les systèmes. |
| L1 | A-01/A-02 : GrowthPolicy, schismes motivés, comparaison locale/camps. | Grande colonie heureuse sans demande causée uniquement par sa taille ; pression durable réelle pouvant encore ouvrir un départ soumis au joueur. |
| L2 | A-03 : unités exactes, lots four/moulin, postes et sorties physiques. | k=1 identique ; grand lot économisant préparation/combustible ; deux artisans ne consomment pas les mêmes matières. |
| L3 | A-04/A-07 : préférences, métriques d'utilisation et extensions bornées. | Continuité utile, urgence préemptive, capacité obtenue à l'achèvement et aucun poste fantôme. |
| L4 | A-08/A-06 : raccourcis et transport groupé, charrettes dimensionnées. | Gain réel de trajet, budget global respecté, nourriture d'urgence jamais différée pour remplissage. |
| L5 | A-05 : capacités et couverture de services. | École/soins/taverne réellement accessibles, saturation entraînant une demande cohérente sans duplication. |
| L6 | A-09/A-10 : effets divins et intégration agricole/militaire. | Accord appliqué une fois ; cycles suivants non bénis ; seul champion présent reçoit son bonus. |
| L7 | A-11/A-12/A-13/A-14 : vues, graphiques et illustrations. Ajouter avant ce lot les vues minimales utiles au contrôle des lots précédents. | Tous les chiffres proviennent du backend ; voies et monuments affichés correspondent au monde. |
| L8 | Calibration intégrée et campagne longue. | Avantage mesuré des grandes colonies, limites naturelles et intérêt des camps conservés. |

L6 peut avancer indépendamment après L0 si souhaité. L7 implique un agent interface/graphismes en plus du backend : le backend livre les contrats et les faits, pas du dessin Godot.

### Scénarios de test requis

1. **Lots** : formule k=1/k=2/k=4 ; matières transformées jamais économisées ; manque d'un intrant et débit atomique ; demande déjà couverte ; changement de recette entre trajet et arrivée ; sortie pleine ; financement réservé à la survie.
2. **Vie de l'artisan** : pause pour manger/repos, vieillissement, reprise sans second débit, mort, déménagement, atelier ou poste détruit. Aucune duplication ni restitution des pertes anciennes.
3. **Transport de sortie** : deux ramasseurs concurrents, retrait partiel, âge préservé, charrette occupée, chemin coupé, nourriture récupérable en urgence, dépôt effectif et destruction avant livraison.
4. **Spécialisation/extension** : même préférence après sauvegarde, absence d'intrants, urgence alimentaire, module inachevé, absence de case de poste, choix d'un deuxième atelier mieux placé.
5. **Croissance** : population élevée seule insuffisante ; compteur durable et hystérésis ; Pending ne vaut pas NoSpace ; accord tardif revalidé ; camp rentable sans séparation automatique.
6. **Services** : usagers en route comptés, capacités exactes, service inaccessible, plusieurs quartiers partageant un site, maladie/décès d'un soignant et absence de bonus à distance.
7. **Missions** : regroupement sans duplication d'un besoin en transit, provisions/cargaison dans la capacité, délai maximal, urgence avant arrivée, route hostile et changement de propriétaire pendant l'attente.
8. **Raccourcis** : corridor invalide, gain insuffisant, reprise A* en cours, crue/construction entre proposition et admission, crise suspendant le confort, égalité de résultat à cadence d'affichage différente.
9. **Pouvoirs** : refus/absence d'accord, double réponse, cible déjà bénie, décès ou récolte avant accord, toutes les parcelles déjà perdues, vigne récoltée une fois, fin exacte d'effet, champion absent, estimations cohérentes avec bataille, transfert politique unique.
10. **Sauvegarde** : aller-retour du format courant et continuation déterministe avant engagement, pendant lot/repas, avec sortie partiellement retirée, mission différée/en route, A* incomplet et effet partiellement consommé.

### Mesures et commandes de validation

- Simulation : `dotnet test Simulation.Tests -c Release`, puis Debug avant fusion ou pour comparer un échec suspect. Préserver les neuf graines de `GrowthTests`.
- Jeu : `dotnet build Game/GodColony.csproj`, puis vérification visuelle des vues, chemins et silhouettes, avec et sans images.
- Changements isolés : 5 ou 10 parties selon leur ampleur ; évolution intégrée : 15 graines, mesures séparées et résumé uniquement.
- Comparer une grosse implantation à plusieurs petites avec même population totale, compétences comparables, technologie, horizon et budgets initiaux. Déclarer les différences de terrain, gisements et trajets ; aucun accès gratuit aux ressources pour égaliser artificiellement les résultats.
- Mesurer production et nutrition livrées par adulte-jour, heures par unité, combustible par unité, temps de trajet, réserves, pertes, confort, épisodes de famine avec `StarvationWatch`, fondations/schismes et coûts des camps.
- Faire un scénario naturel avec lifecycle/migration et un scénario contrôlé de population stable. Mesurer les gains d'équipement hors pouvoirs divins, puis ajouter un scénario avec pouvoirs, pour ne pas attribuer leur avantage à la taille.
- Profiler petites et grandes populations, plusieurs colonies et débuts de saison : coût moyen, pics de tick, opérations de recherche et délais de réponse. La taille cible se fixe à partir de ces résultats, pas d'une promesse de centaines d'habitants.

**Terminé** signifie : conservation des biens et des coûts, reprise déterministe, décisions autonomes expliquées, gain mesuré d'un équipement utilisé, maintien des petites implantations utiles et vues fidèles. Le succès n'est pas seulement une population plus élevée obtenue en empêchant les départs.
