# Économie spécialisée, territoires et offrandes — plan de réalisation backend

Statut : conception à implémenter, établie à partir du code consulté le 5 octobre 2026. Ce document ne décrit pas des fonctionnalités déjà réalisées. Il constitue le contrat de conception pour l'agent chargé du backend ; aucun code C# final n'est fourni.

Décision explicite du joueur : **aucune compatibilité avec les anciennes sauvegardes n'est demandée**. Ce chantier peut introduire un nouveau format et exiger une nouvelle partie. Il doit néanmoins sauvegarder et reprendre correctement ses propres parties.

## 1. Intention du jeu et décisions retenues

Le joueur observe des habitants autonomes organiser une civilisation. Une colonie prospère parce qu'elle sait exploiter son territoire, développer ses métiers et obtenir des fournisseurs fiables. Elle ne doit pas pouvoir multiplier indéfiniment les biens avec quelques ateliers identiques dans chaque village.

Les règles retenues sont les suivantes :

1. **Autonomie complète.** Les habitants choisissent leurs champs, ateliers, routes, missions, camps et projets collectifs. Le joueur conserve ses décisions divines et diplomatiques existantes ; ce système n'introduit pas de placement manuel obligatoire.
2. **Spécialisation réelle.** Le terrain, les réserves, les compétences, les équipements et le coût du transport déterminent ce qu'un établissement produit efficacement. La subsistance initiale reste accessible ; soutenir une grande population ou une industrie exige une organisation spécialisée ou des importations.
3. **Interdépendance forte.** Les vivres, les combustibles, les minerais, les outils et les produits manufacturés peuvent devenir des dépendances structurelles. Des fournisseurs, des réserves de sécurité et des routes alternatives font partie du fonctionnement normal d'une colonie développée.
4. **Expansion d'une même colonie.** Un camp minier sur une autre région appartient à la colonie d'origine. Il reçoit des habitants et des marchandises réels. Il peut devenir un hameau, puis un village complet, sans créer automatiquement une nouvelle entité politique.
5. **Stocks locaux et transports réels.** Un village ne peut consommer ni construire avec les biens d'un autre village avant leur arrivée. Les trajets internes peuvent relier directement deux établissements, sans passage imposé par la capitale.
6. **Gisements principalement épuisables.** Les petites ressources ordinaires permettent de commencer ; les concentrations riches justifient les expéditions et les camps. Quelques sources permanentes de faible débit permettent une transition après épuisement, sans assurer toute l'industrie.
7. **Découverte progressive.** La colonie connaît des indices et des estimations, puis améliore sa connaissance par la prospection et l'exploitation. Elle n'accède pas aux réserves cachées de la carte.
8. **Monnaie limitée.** L'or peut être frappé en pièces, mais la capacité de frappe reste modeste. Produire et vendre des biens demeure la principale manière d'enrichir une colonie. La frappe augmente progressivement la monnaie disponible dans le monde.
9. **Raretés complémentaires.** Plusieurs pierres précieuses ont des répartitions différentes. Les bijoux et les offrandes combinant plusieurs pierres créent des motifs d'échange à longue distance.
10. **Offrandes et libre arbitre.** Une statue et ses matériaux sont un investissement réel. Une prière sans offrande reste possible. Le joueur peut accorder, ignorer ou refuser une demande, quelle que soit l'offrande.

Le renforcement durable d'un guerrier est une idée acceptée pour les futurs pouvoirs. Ce chantier prépare les demandes et les offrandes, mais ne réalise pas encore les effets divins, la refonte du combat, les royaumes, la navigation maritime ou une refonte générale des niveaux de bâtiments.

Les seuils, rendements, recettes nouvelles et quatre noms de pierres proposés ci-dessous sont des **valeurs de départ de conception**, à équilibrer. Les principes ci-dessus sont les décisions à préserver. Ne pas présenter les valeurs proposées comme des réglages déjà validés par le joueur.

## 2. Point de départ dans le code actuel

| Élément actuel | Conséquence pour ce chantier |
|---|---|
| `Colony` contient sa carte, ses habitants, son stock, son plan et ses services | Séparer l'appartenance politique du contexte physique avant de créer les camps |
| `WorldMap` associe une colonie à une seule région | Indexer les établissements et la propriété territoriale, puis adapter routes et diplomatie |
| `ColonyBrain.Think` traite la survie, les champs, les ateliers et l'urbanisme | Extraire la décision quotidienne locale, conserver la coordination politique dans `ColonyBrain` |
| `Economy` calcule déjà les coûts en travail, la rareté et des prix marginaux | Étendre ces calculs aux établissements et aux nouvelles filières ; ne pas créer une deuxième économie concurrente |
| `Trade` possède de vrais chargements, marchands et règlements | Étendre sa logistique aux établissements, aux réservations et aux urgences |
| Le lancement commercial dépend notamment de `SurvivalAssured` | Ce verrou doit devenir une protection contre les départs dangereux, sans interdire les importations de secours |
| Le minerai vient actuellement de couches procédurales exploitées par `LocalMap.Mine` | Introduire une autorité unique pour les réserves ; éviter de produire à la fois depuis une couche et un gisement |
| `Specialties` produit certaines spécialités au marché | Remplacer cette création au marché par de véritables sources et opérations de production |
| `ResourceType`, `SkillType` et `WorkSector` servent d'indices et participent aux sauvegardes | Conserver les identifiants numériques existants, compléter les tableaux et auditer tous les parcours d'énumérations |
| `WorldSave` écrit le format 2 et accepte le format 1 par migration | Introduire un nouveau format ; la reprise des anciens fichiers est hors périmètre |
| `StateGraph` utilise une liste de types autorisés et un schéma de champs | Enregistrer les nouveaux types et valider les fichiers du nouveau modèle |
| `Prayers` sait déjà présenter des décisions et restaurer leurs actions | Ajouter un souhait typé, sans traiter une acceptation vide comme un miracle réalisé |

Repères à préserver : le blé a actuellement `CropCycleFactor = 1.25` et `GrowthDays = 8.75` jours, avec rendement final augmenté dans les deux chemins de calcul. Le calendrier contient **20 jours par année**, cinq par saison. Les recettes existantes expriment le travail en secondes de simulation à vitesse ×1 ; les coûts économiques sont en heures de travail. Toute conversion passe par les constantes d'horloge, jamais par un facteur implicite de 24 ou un calendrier terrestre.

## 3. Modèle principal : colonie, établissement, région

### 3.1 Autorités et identifiants

Conserver `Colony` comme entité politique. Ajouter `Settlement` dans `Simulation/Colonies/` comme établissement physique. Une région est une case hexagonale de la carte du monde ; son terrain et ses ressources appartiennent au monde, même si l'établissement est abandonné.

| Objet | État dont il est l'autorité |
|---|---|
| `WorldState` | Horloge, colonies, registres territoriaux, voyages, routes mondiales, compteurs d'identifiants, bilan monétaire |
| `Colony` | Identité, peuple, citoyens, connaissances collectives, alliances, guerres, prières, stratégie, renseignements et décisions d'expansion |
| `Settlement` | Région, carte locale, stocks disponibles, bâtiments, champs, animaux, services, plan urbain, travail et besoins locaux |
| `RegionState` | Terrain modifié, gisements, points de passage et propriétaire territorial ; persiste après fermeture d'un camp |
| `Colonist` | Identité, citoyenneté, foyer, emplacement actuel, besoins, activité et compétences |
| `Caravan` étendue | Mission, participants, itinéraire, chargement, provisions, monnaie embarquée, état et engagements de transport |

Identifiants stables : `ColonyId`, `SettlementId`, `RegionTileIndex`, `DepositId`, `TripId`, `ProjectId`. Ils sont attribués par des compteurs persistants, sans consommation de hasard. Un bâtiment ou un champ conserve son identifiant local ; une référence mondiale utilise **le couple `(SettlementId, LocalId)`**. Les noms servent à l'affichage, jamais à résoudre une cible ou une propriété.

Pour limiter la refonte de la population, **`Colony.Members` reste la collection canonique des citoyens vivants**, y compris en voyage. Modifier son contrat : partir en caravane ne retire plus la citoyenneté. `WorldState` reconstruit un index non sérialisé des colons par identifiant. `Settlement.Residents` et `Settlement.PresentColonists` sont des vues/index dérivés, sans seconde collection propriétaire des personnes.

Ajouter au colon `HomeSettlementId` et un `ColonistLocation` : `Settlement` avec identifiant et coordonnées locales, ou `Travel` avec `TripId`. Les coordonnées ne changent de carte que lors d'une arrivée validée. Une migration politique transfère la citoyenneté une seule fois ; une visite commerciale conserve la citoyenneté d'origine. Les personnes encore dans un voyage sont comptées dans la population politique, mais pas dans les travailleurs disponibles d'un village.

Les anciens `Transients` représentent encore les entrées et sorties locales. Les intégrer à l'état de déplacement, avec un seul moteur d'actualisation par personne. Un colon ne doit jamais être traité à la fois par `Members`, `Transients` et les participants d'une caravane.

Les ajouts, décès, migrations et changements d'emplacement passent par des opérations centrales de population qui maintiennent les index. Les travailleurs assignables sont les personnes présentes, aptes, appartenant à la colonie et libres de mission ; un visiteur étranger n'est pas recruté automatiquement dans un atelier. Le foyer local existant devient une référence comprenant son `SettlementId` et son bâtiment, afin qu'un lit portant le même identifiant ailleurs ne soit pas confondu avec le sien.

### 3.2 Champs à déplacer de `Colony` vers `Settlement`

Déplacer le contexte physique : `Map`, `Pathfinder`, `Layout`, `CampX/Y`, `GatherSpots`, `Quarry`, `Stock`, `Labor`, `WorkShares`, bâtiments, champs, canaux, chemins, réservations, caches spatiaux, services, tombes, troupeaux, usure locale des outils, climat local et capteurs locaux. Relire les autres champs avant déplacement : un cumul politique doit rester politique, une position ou consommation doit devenir locale.

`Colony.Settlements` et `Colony.PrimarySettlementId` désignent les établissements. Les adaptateurs temporaires `Colony.Map` ou `Colony.Stock` peuvent désigner explicitement l'établissement principal pour compiler l'interface pendant la transition. Ils ne doivent jamais devenir un stock agrégé mutable. Les commandes backend finales prennent un `Settlement` quand elles consomment, produisent, construisent, soignent ou assignent du travail.

Les synthèses politiques utilisent des lectures : stock total, monnaie totale, production, importations et population. Un total ne donne aucun droit d'accès à distance. Une pièce au village B doit être transportée ou dépensée sur place pour payer une transaction qui s'y déroule ; aucun paiement instantané entre régions.

Les connaissances restent partagées dans une colonie, comme aujourd'hui. Les compétences restent individuelles. Les demandes politiques restent communes ; leur sujet physique désigne un établissement. Les calculs d'aval fluvial, de barrage et de voisinage doivent devenir territoriaux, y compris entre deux villages de la même colonie.

### 3.3 Carte mondiale et mémoire du terrain

Ajouter `RegionRegistry` / `RegionState` dans `Simulation/World/`. Une région initialement inexplorée possède un descriptif déterministe léger. Sa `LocalMap` est générée à la première visite avec la méthode actuelle de graine régionale, puis ses changements sont conservés. Ne pas générer toutes les cartes locales du monde au démarrage.

`WorldMap` expose conceptuellement `SettlementAt(region)`, `OwnerAt(region)`, `SettlementsOf(colony)` et des routes entre régions. Une seule implantation permanente par région pour cette première version. Les colonies peuvent traverser une région selon les règles diplomatiques ; elles ne peuvent pas y installer un camp si un autre propriétaire la contrôle sans droit explicite de cession ou d'installation.

La distance minimale de fondation d'une **nouvelle colonie politique** reste distincte de la création d'un camp de sa propre colonie : ne pas appliquer mécaniquement `CanSettle` et sa distance actuelle à tous les camps.

Un camp fermé laisse sa carte, ses excavations, ses réserves restantes et ses ruines. Une réinstallation n'offre ni un nouveau gisement ni une nouvelle dotation. Le plafond actuel de colonies politiques ne devient pas un plafond de villages. Ajouter un plafond configurable séparé d'établissements et de cartes actives pour protéger les performances ; valeur initiale proposée : huit établissements par colonie, sans chargement simultané de toutes les cartes inutilisées.

Conserver le cas du monde sans colonie : sa carte d'aperçu peut être exposée pour la création initiale, mais ne constitue pas un établissement avec population ou stock. `WorldState.Map` reste un adaptateur de consultation explicite pendant la transition, pas le contexte implicite des nouveaux systèmes.

## 4. Ressources, usages et filières

### 4.1 Catalogue unique

Ajouter `ResourceCatalog` dans `Colonies/`, alimenté par des données fixes : catégorie, unité, poids transportable, nutrition, conservation, usages, recettes, sources et coût de référence. Garder `ResourceType` comme identifiant. Ne pas multiplier les systèmes de ressources par métier.

Toutes les ressources existantes restent présentes. Ajouter à la fin de l'énumération, avec valeurs numériques explicites :

| Identifiant proposé | Nom du jeu | Source et débouché principal |
|---|---|---|
| `MineralCoal` | Charbon minéral | Gisements ; alternative déclarée au charbon de bois dans certaines recettes |
| `Clay` | Argile | Sols adaptés et carrières ; poterie |
| `Pottery` | Poterie | Conservation et équipement domestique renouvelé |
| `CopperOre`, `Copper` | Minerai de cuivre, cuivre | Gisements puis réduction ; objets de cuivre et ornements |
| `Copperware` | Objets de cuivre | Équipement et confort ; usage religieux possible |
| `Flax`, `Linen` | Lin, toile de lin | Culture puis tissage ; variante des vêtements existants |
| `Hides`, `Leather` | Peaux, cuir | Abattage réel puis tannage ; chaussures |
| `Shoes` | Chaussures | Équipement individuel à usure lente |
| `Grapes`, `Wine` | Raisin, vin | Vignes adaptées au climat puis fermentation ; consommation de confort |
| `GoldOre`, `Gold` | Minerai d'or, or affiné | Gisements principalement finis ; frappe, bijouterie, offrandes |
| `Ruby`, `Sapphire`, `Emerald`, `Diamond` | Rubis, saphir, émeraude, diamant | Gisements rares distincts ; bijoux et offrandes composées |
| `Jewelry` | Bijoux | Or ou cuivre et pierres ; prestige, cadeaux et exportations |

Ne pas ajouter une deuxième ressource « argent » : `Coins` existe et reste la monnaie commune. Ne pas ajouter acier, bronze, étain ou dizaines de variantes de bijoux dans ce chantier. La qualité d'une offrande peut conserver la liste de ses matériaux plutôt que créer une ressource pour chaque combinaison.

`Salt`, `Spices`, `Hardwood` passent à des sources environnementales identifiables : marais salants terrestres/littoral accessible, plantes d'épices, essences forestières. Leur répartition actuelle par biome peut rester un premier filtre. Le marché ne les fabrique plus sans source. `Food` et `Fish` restent limités par les ressources sauvages réellement accessibles et leur renouvellement.

### 4.2 Recettes et bâtiments

Étendre le type de recette existant avec un identifiant stable, des variantes explicites et, seulement si nécessaire, plusieurs sorties. Le backend conserve `FoodChain`, `ToolChain` et `Husbandry` comme points d'entrée, mais partage le catalogue et les réservations d'intrants. Pas de langage de recettes ou moteur générique de scripts.

Nouveaux bâtiments proposés à la fin de `BuildingType` : `MineDepot`, `PotteryKiln`, `Tannery`, `Goldsmith`, `Mint`, `Shrine`. Le four de poterie est distinct de la charbonnière actuelle `Kiln`. `Bloomery` reçoit les variantes cuivre/or appropriées pour cette abstraction du jeu. `Loom`, `Forge`, `Cask` et `Storehouse` sont réutilisés quand leur fonction suffit. `Shrine` est le lieu de préparation/installation ; la statue achevée est un monument, pas un atelier qui recrée des pierres.

Un `MineDepot` ne crée pas de minerai : il rend un chantier accessible, organise le stockage et améliore les déplacements. Le rendement reste borné par le gisement et les personnes au travail. Les bâtiments ont des profils de placement et des emprises compatibles avec les plans existants ; valider accès, sol, sécurité et terrain réel, notamment au pied des montagnes.

Recettes initiales proposées, à calibrer avec les durées et coûts existants :

| Atelier/opération | Intrants → sorties | Travail de référence proposé |
|---|---|---|
| Extraction | Travail sur gisement → minerai, argile ou pierre précieuse | Défini par richesse/accessibilité du gisement ; pas de recette sans réserve |
| `Bloomery`, cuivre | 3 minerais de cuivre + 2 charbons → 1 cuivre | 24 secondes |
| `Bloomery`, or | 3 minerais d'or + 2 charbons → 1 or | 32 secondes |
| `PotteryKiln` | 3 argiles + 1 bois → 2 poteries | 14 secondes |
| `Loom`, toile | 3 lins → 2 toiles | 14 secondes |
| `Loom`, vêtements | 2 toiles → 1 vêtement | 16 secondes ; alternative à la laine, même ressource de sortie |
| Abattage | Animal réellement retiré du troupeau → viande existante + peaux selon espèce | Conserver règles et travail d'abattage ; poules sans peau exploitable |
| `Tannery` | 2 peaux + 1 sel → 2 cuirs | 16 secondes |
| `Forge`, chaussures | 2 cuirs → 1 paire de chaussures | 16 secondes |
| `Forge`, objets domestiques | 2 cuivres → 1 objet de cuivre | 20 secondes |
| `Cask`, vin | 4 raisins → 2 vins | Travail de mise en cuve + fermentation proposée de 3 jours |
| `Goldsmith` | 1 or + 1 pierre d'un type accepté → 1 bijou | 24 secondes ; pierre exacte enregistrée avec le lot |
| `Mint` | 1 or → 2 pièces | 24 secondes **et quota monétaire** ; recette spéciale, détaillée section 10 |

« Charbon » dans une recette signifie une variante choisie : `Charcoal` ou `MineralCoal`, avec quantité explicite. Un charbon minéral ne remplace pas toutes les quantités de bois par magie. Les anciennes recettes fer/outils restent la référence ; ajouter uniquement leurs variantes de combustible validées.

Une recette commencée conserve **les intrants exacts réellement retirés**, la variante, le travail accompli et les sorties attendues. En cas d'annulation avant transformation irréversible, restituer ces intrants et leur âge d'origine. Après transformation, conserver le lot en cours ou constater une perte explicitement. Ne pas recalculer un remboursement avec la recette actuellement préférée.

Les chaussures et objets domestiques créent une demande de remplacement lente, liée à leur utilisation réelle. Le vin et les bijoux répondent à un budget de confort plafonné. La poterie peut prolonger certaines durées de conservation via une capacité équipée, sans augmenter la nutrition ni réinitialiser l'âge des aliments. Les nouvelles améliorations n'empilent pas des bonus multiplicatifs illimités.

Ajouter un petit `EquipmentState` au colon pour ses chaussures et un `HouseholdEquipment` à l'établissement pour la poterie/les objets domestiques. Valeurs de départ : une paire par adulte, usure proportionnelle à la distance réellement parcourue ; une poterie pour quatre résidents, durée utile de deux années de jeu une fois affectée ; un objet de cuivre pour quatre résidents, durée utile de quatre années. Les réserves non équipées ne s'usent pas comme si elles étaient utilisées. Les marchandises retirées pour équiper passent vers cet état physique, puis vers une perte lorsqu'elles cassent. Remplacement demandé seulement pour un objet usé/manquant, sans retirer à nouveau tout l'équipement à chaque jour.

Les petits budgets de confort et d'offrandes sont des parts de capacité de production/dépense, pas des ressources. Proposition : au plus 10 % du travail planifié et des pièces disponibles au-delà des réserves engagées ; cette borne devient zéro en crise. Le vin est consommé dans les lieux de sociabilité existants à fréquence bornée ; un bijou acquis n'est pas consommé chaque heure pour faire artificiellement monter la demande. Son propriétaire ou son emplacement collectif est conservé comme équipement/prestige, avec transfert réel en cadeau.

Prérequis proposés : `MineDepot` et petite prospection accessibles sans outils de fer obligatoires ; poterie et sanctuaire maçonné liés à `Discovery.Masonry`, tannage à `Husbandry`, lin à `Agriculture` puis `Weaving`, vin à `Brewing`, raffinage et bijouterie à `Metallurgy`, frappe à `Coinage`. Cette dernière découverte existe déjà et modifie actuellement le transport : conserver ses effets existants pendant ce chantier, ajouter l'accès à `Mint` et mettre à jour son descriptif. Ne pas créer un second savoir « monnaie » concurrent. La fondation d'un camp n'exige pas la connaissance qui ne pourrait être obtenue qu'après sa propre construction.

### 4.3 Agriculture et subsistance

Ajouter `CropKind` avec `Grain`, `Flax`, `Grapes` ; chaque `FieldPlot` possède sa culture et son cycle. La vigne utilise des parcelles durables, avec installation puis récoltes saisonnières ; le lin concurrence le blé en surface et en travail. Réutiliser rotation, irrigation, accès aux champs et compétences actuels. La conversion d'une parcelle ne produit jamais une récolte gratuite.

Conserver le cycle et le calcul actuels du blé pendant la première étape. La difficulté de production à grande échelle vient d'abord de la surface fertile exploitable, du travail saisonnier, de l'équipement et de l'accès. Toute baisse ultérieure du rendement de référence doit être isolée, mesurée et justifiée, sans annuler discrètement l'allongement de pousse déjà demandé.

La récolte par unité de surface repose sur un rendement de base puis des effets bornés : fertilité réelle, climat, irrigation, rotation et savoir-faire. Ne pas ajouter un bonus parce qu'une colonie porte l'étiquette « agricole ». Les gains de compétence et les infrastructures doivent déjà expliquer sa productivité.

Une petite population peut vivre de ressources sauvages et de quelques parcelles. La cueillette plafonne avec le renouvellement naturel des sites. La pêche plafonne avec les sites et leur productivité ; créer dix tâches sur le même site ne décuple pas sa capacité. Une grande ville minière ou industrielle doit choisir entre affecter beaucoup de bras à une agriculture médiocre et importer auprès d'un bassin agricole spécialisé.

Paramètres agricoles proposés pour commencer les essais : lin sur un cycle comparable au blé avec une récolte de trois fibres par parcelle standard, puis amélioration bornée par fertilité/savoir-faire ; vigne installée par travail et matériel, première récolte après une année de jeu, ensuite une récolte annuelle de quatre raisins par parcelle standard pendant sa saison adaptée. Fixer la date de plantation et la maturité, afin que traverser une frontière saisonnière ne donne pas une récolte instantanée. Le raisin peut être comestible avec nutrition déclarée dans le catalogue, tout en étant réservé à la fermentation seulement au-delà de la sécurité alimentaire. Ces nouvelles valeurs ne modifient pas celles du blé.

Les jours de réserve utilisent la **nutrition comestible disponible**, la consommation observée des personnes présentes et les personnes attendues avant le prochain ravitaillement. La farine seule n'est pas un repas ; les recettes réalisables peuvent alimenter une prévision séparée, avec intrants, combustible, accès et main-d'œuvre disponibles. Une promesse de livraison n'est pas un stock.

## 5. Gisements, rareté et découverte

### 5.1 Classes et séparation entre vérité et connaissance

Ajouter dans `Map/` : `Deposit`, `DepositCatalog`, `DepositExtraction`, et dans `Generation/` : `GeologyGenerator`. Ajouter dans `Colonies/` : `Prospection`, `ProspectionMission`, `DepositKnowledge`, `RegionKnowledge`. Conserver les types de réserve et ceux de connaissance distincts.

Un `Deposit` porte : identifiant, région, matériau, cellules d'accès, mode `Finite`/`Permanent`, réserve initiale et restante pour le fini, difficulté, débit maximal, travail accumulé borné, marqueur de budget quotidien et état physique d'accès. Les pierres ont leurs propres poches ; une gemme rare ne sort pas d'un tirage recommencé à chaque chargement d'une carte.

`DepositKnowledge` porte : colonie observatrice, site connu, état `Hint`/`Surveyed`/`Working`/`Depleted`, matériau présumé ou confirmé, estimation min/max, confiance, date et origine du renseignement. L'estimation peut se resserrer à mesure du travail. L'IA reçoit cette vue ; elle ne lit pas `RemainingReserve` ou des cellules souterraines pour choisir ses régions.

Une région visitée peut révéler des affleurements et traces de surface. La prospection confirme un site et estime sa richesse. Un renseignement commercial est daté et incertain ; acheter une rumeur ne téléporte pas un prospecteur et ne garantit pas une mine exploitable.

### 5.2 Répartition proposée

- Chaque région terrestre habitable conserve un petit potentiel de fer accessible, suffisant pour un démarrage modeste. La profondeur et les quantités ne permettent pas une production massive partout.
- Les montagnes concentrent les grands gisements ; certaines collines et formations géologiques offrent des alternatives. L'accessibilité de la mine et celle d'un emplacement habitable sont deux critères distincts.
- Des sources permanentes de fer, d'argile ou d'autres matériaux essentiels existent en petit nombre, à faible débit. Elles sont réparties dans les composantes terrestres accessibles suffisamment grandes. L'or et les gemmes ne bénéficient pas d'un robinet permanent destiné à sécuriser la monnaie.
- Rubis, saphirs et émeraudes sont rares et géographiquement complémentaires ; le diamant est exceptionnel. Une grande composante commerciale peut réunir plusieurs pierres, sans que chaque région possède tout.
- Sur une petite île sans transport maritime, une recette impossible à compléter reste un projet suspendu ou une autre offrande disponible. Ne pas générer artificiellement toutes les pierres autour d'une colonie pour supprimer ce problème.

Ordres de grandeur à utiliser pour les essais : un grand gisement utile représente au moins dix fois le petit potentiel régional ; un débit permanent doit soutenir une transition et une petite activité, pas équiper à lui seul une grande colonie. Calibrer en **jours de besoins industriels observés**, pas uniquement en unités de minerai.

### 5.3 Extraction : une seule source de rendement

Aujourd'hui `LocalMap.Mine` retire une couche et peut rendre du fer. Demain, les réserves enregistrées doivent être l'autorité du minerai. L'excavation peut encore retirer de la roche et modifier le terrain ; elle ne donne pas une deuxième fois le minerai d'un `Deposit`. Une cellule/couche exploitée ne peut être comptabilisée dans deux poches.

Les sources permanentes utilisent un accès de travail stable. Elles ne doivent pas abaisser indéfiniment l'altitude, régénérer une montagne, ou accumuler un crédit d'extraction pendant des années d'inactivité.

```text
EssayerExtraire(travailleur, établissement, gisement, travailRéel)
    vérifier présence locale, accès, mission, outils éventuels et droit territorial
    vérifier que le site a été découvert par la colonie
    si nouveau jour pour ce gisement : remettre le débit utilisé à zéro
    convertir le travail réellement effectué en crédit d'extraction borné
    quantité = unités entières permises par travail ET débit restant
    si fini : quantité = minimum(quantité, réserve restante)
    si quantité nulle : conserver uniquement un petit reste de travail autorisé
    sinon :
        débiter la réserve finie ou le budget de la source permanente
        inscrire le lot extrait dans le portage local du travailleur
        débiter le crédit correspondant ; enregistrer le travail et la production
        appliquer la modification de terrain prévue une seule fois
        actualiser l'estimation connue et déclencher l'alerte d'épuisement si nécessaire
```

Le plafond quotidien d'une source est **partagé par tous ses travailleurs**, persisté et indépendant du chargement de la carte. Aucun quota par colon ou par camp. L'exploitation concurrente reste séquentielle et déterministe. Utiliser des unités entières de réserve et un petit reste fixe de travail ; ne pas produire une dernière unité par arrondis successifs.

### 5.4 Prospection autonome

```text
ChoisirMissionProspection(colonie)
    relever matières manquantes et sites connus bientôt épuisés
    considérer régions voisines accessibles, indices visibles et renseignements datés
    estimer intérêt avec confiance, délai, provisions, danger et coût du travail retiré
    retenir le meilleur candidat connu au-dessus du seuil d'utilité
    affecter un ou deux adultes disponibles, sans vider un village en crise
    réserver provisions et équipement locaux ; créer un vrai voyage de prospection

AuSiteDeProspection(mission)
    faire travailler les prospecteurs pendant le temps prévu
    révéler uniquement le résultat que cette profondeur de recherche permet
    inscrire rapport daté dans les connaissances de leur colonie
    proposer étude complémentaire, exploitation locale ou projet de camp
    organiser retour ou continuation avec provisions suffisantes
```

La compétence `Mining` sert à la prospection ; pas besoin d'un nouveau métier magique. L'ordre des sites et résultats repose sur des identifiants stables. Une expédition abandonnée rend ce qu'elle rapporte réellement, sans inventer de réserve connue exacte.

## 6. Camps et villages secondaires

### 6.1 Projet de fondation

Ajouter `ExpansionPlanner`, `SettlementFoundingProject` et `SettlementStage` (`Camp`, `Hamlet`, `Village`) dans `Colonies/`. Réutiliser les réservations et le planificateur de sites existants pour les ouvrages locaux ; un projet territorial coordonne personnes, trajet et matériel, il ne remplace pas `DevelopmentProject`.

Conditions d'ouverture d'un camp : site connu assez intéressant, territoire autorisé, accès praticable, emplacement habitable, main-d'œuvre disponible, réserves et capacité de ravitaillement. Un grand gisement au sommet inaccessible ne suffit pas. La colonie compare l'exploitation locale, l'importation et l'investissement territorial.

La taille initiale proposée est de quatre à six adultes, adaptée à la population et aux compétences. Un établissement principal ne doit pas envoyer presque tous ses agriculteurs. Les fondateurs proviennent de citoyens existants ; ils emportent bois, nourriture, outils éventuels et monnaie si nécessaire. **Pas de nouveaux habitants, outils ou pièces gratuits.** Ne pas appeler la fondation politique actuelle et sa dotation initiale.

```text
ÉvaluerCamp(colonie, siteConnu)
    rendementEspéré = estimation du site × confiance × capacité de travail réaliste
    coût = installation + provisions + transport + entretien + opportunité du travail retiré
    comparer au coût d'importation connu et à la durée de vie estimée du gisement
    exiger un emplacement de vie accessible et une première route de ravitaillement
    si projet utile et viable : réserver personnes et lots ; lancer expédition de fondation

ÀArrivéeFondation(projet)
    revalider propriété, accès, emplacement et personnes encore présentes
    si impossible : choisir retour ou site alternatif déjà connu, sans téléportation
    sinon : créer Settlement et rattacher la région à sa colonie
        transférer chargement livré dans son stock local
        affecter foyers et emplacements des fondateurs
        installer camp de base, puis lancer ouvrages par le planificateur existant
        créer demande régulière de ravitaillement et objectif d'exploitation
```

Une installation sur une région vierge ne donne pas automatiquement un logement achevé : conserver la représentation actuelle du camp de départ, puis construire normalement. Elle ne détruit pas les réserves préexistantes en recréant la carte.

### 6.2 Développement organique

Le stade est une description du fonctionnement atteint, pas un bouton d'évolution ou un bonus de rendement. Valeurs proposées :

| Stade | Conditions indicatives |
|---|---|
| Camp | Équipe productive, ravitaillement organisé, abri minimal |
| Hameau | Environ douze résidents, logements stables, nourriture fiable et petit service local |
| Village | Environ vingt-quatre résidents, familles, plusieurs activités et services durables |

Vérifier les conditions pendant plusieurs jours avant changement de stade ; éviter les oscillations quotidiennes. Un village peut être principalement minier tout en possédant un potager, un puits, une infirmerie et des métiers secondaires. L'enfance, les familles, les apprentissages et les décès fonctionnent sur place. La colonie peut proposer des relocalisations de familles entières, avec transport et destination viable, sans déplacer une personne instantanément dans son sommeil.

Si le gisement s'épuise : anticiper par la durée restante estimée, prospecter, importer, réorienter une partie du travail, ou organiser un déménagement. Un village utile par ses routes, ses champs ou ses ateliers continue à vivre. Un camp non viable peut être évacué ; transporter ses habitants et ses biens récupérables, conserver les pertes et les ruines.

Un futur schisme peut séparer un ensemble d'établissements. Adapter dès ce chantier les références pour que `Schism` ne laisse pas des bâtiments, colons, prières ou cargaisons liés à une colonie supprimée. Le schisme reste soumis aux décisions existantes du joueur.

## 7. Transport, routes et propriété des biens

### 7.1 Étendre le voyage existant

Réutiliser `Caravan` comme voyage terrestre commun plutôt qu'ajouter un deuxième moteur de progression. Ajouter `TransportMissionKind` : `Trade`, `InternalSupply`, `Prospection`, `Founding`, `Relocation`. Le nom existant peut rester pour limiter la refonte ; des façades spécialisées construisent les missions.

Champs à ajouter/adapter : origine et destination par `SettlementId` ou région prospectée, colonie responsable, liste de participants par identifiant, étapes locales et mondiales, progression, provisions, `CargoLot`, portefeuille, engagements, motif de blocage et dernières dates d'entretien. Les missions initiales ont un aller puis un retour, avec échanges autorisés à destination ; ne pas réaliser encore un optimiseur général de tournées.

La séquence physique est : chargement → marche jusqu'au point de sortie → trajet régional → marche vers le lieu de livraison → transaction/livraison → éventuel retour. La progression mondiale peut rester abstraite comme aujourd'hui ; les cartes locales utilisent leur navigation réelle. Un déplacement interne voisin n'est jamais un transfert instantané.

### 7.2 Inventaires, lots et réservations

Ajouter `StockReservation` et `CargoLot`. Une réservation référence des unités appartenant à un inventaire, elle ne crée pas un deuxième inventaire. Une activité commencée, un chargement ou un projet livré possède en revanche les unités effectivement retirées. `Available(resource)` signifie total stocké moins réservations actives.

Pour les ressources périssables, préserver les dates de production et les métadonnées au transfert. Le code actuel distingue l'âge des lots de viande : retirer puis réajouter avec l'API ordinaire ne doit pas transformer une viande vieille en viande fraîche. La décomposition s'applique aussi en voyage, en activité et dans les réserves d'un projet, une fois par période.

Le poids du chargement remplace la seule somme des unités : minerai, outils, gemmes et nourriture ont des poids différents. Appliquer la capacité à chaque portion de voyage, portefeuille et provisions compris selon leur poids déclaré. Ne pas additionner arbitrairement achats et ventes s'ils occupent des étapes différentes, ni ignorer le poids maximal après les achats. Ne pas introduire des chevaux gratuits pour augmenter cette capacité ; les moyens de portage actuels sont la base.

```text
RéserverDépart(mission)
    calculer provisions aller + retour + marge de retard réaliste
    vérifier que la réserve locale de survie reste suffisante après départ
    vérifier participants disponibles et chargement maximal par étape
    réserver lots, pièces et participants dans un seul ordre déterministe
    si une condition échoue : libérer toutes les réservations de cette tentative

ChargerEtPartir(mission)
    revalider réservations, participants et route
    déplacer atomiquement les lots réservés et les pièces vers la cargaison
    marquer les participants en départ local puis en voyage
    retirer leurs capacités des travailleurs présents ; conserver leur citoyenneté

Livrer(mission)
    vérifier que cette étape n'a pas déjà été livrée
    si interne : déplacer lots vers le stock destinataire, sans vente ni création de pièces
    si commercial : régler transaction contre marchandise disponible, section 8
    enregistrer quantités et dates réelles ; terminer les engagements livrés
    choisir retour, attente bornée ou récupération selon état réel
```

Une réservation possède propriétaire, objet, quantité, priorité et expiration. La survie peut annuler une réservation de confort avant chargement ; les lots déjà embarqués restent physiquement ailleurs. L'annulation est notifiée au projet/contrat afin de ne pas conserver une livraison fictive attendue.

### 7.3 Ravitaillement interne et crises

`LogisticsPlanner` construit les besoins et rapproche surplus locaux et déficits, avec échéance et coût du trajet. Ordre : nourriture et chauffage urgents, nourriture régulière, outils et médicaments utiles, intrants industriels, chantier d'expansion, confort/offrandes. Une livraison destinée à sauver un camp peut être rentable pour la colonie sans constituer un profit marchand.

L'absence de route sûre suspend une promesse de livraison. Le village réduit son activité, recherche une source locale de transition ou propose l'évacuation. Les marchands et prospecteurs consomment leurs provisions ; ne pas remettre automatiquement faim et repos au maximum au retour. Si le moteur actuel simule le voyage par heures, ajouter un traitement des besoins de voyage sans lancer une deuxième IA locale sur les mêmes personnes.

Les urgences ne donnent pas une capacité surnaturelle : s'il n'y a ni biens à exporter, ni pièces, ni secours d'un allié, l'importation peut échouer. Le système expose la raison et prend des mesures de survie. Il n'imprime pas automatiquement de monnaie pour annuler la famine.

### 7.4 Routes et alliances

Conserver `RoadLayer` et `RoadDevelopment` pour les chemins locaux. Ajouter des améliorations terrestres sur les arêtes mondiales dans `WorldRoadNetwork` ; leur construction exige un travail et un acheminement de matériaux réels. Les habitants investissent d'abord sur les trajets fréquentés, selon le gain prévu et le coût.

Une route améliore la durée/coût/capacité effective suivant les règles choisies, sans rendre traversable un océan ou une falaise infranchissable. La première version conserve les traversées terrestres déjà permises. Les ouvrages de franchissement nouveaux nécessiteraient un chantier distinct.

Les fournisseurs et droits de passage utilisent les relations actuelles, avec accès explicite par propriétaire. Une alliance peut réduire les marges commerciales exigées et sécuriser le passage ; elle ne crée ni marchandise ni autorisation implicite de prendre un territoire. Les habitants présentent toujours les demandes politiques au joueur selon les règles existantes.

Les caches d'itinéraires incluent origine, destination, mode de déplacement, révision routière et révision des droits de passage. Après guerre, fermeture ou destruction, revalider au prochain segment. Un voyage bloqué conserve progression et chargement, puis attend, contourne ou revient selon ses provisions. Aucun retour instantané au stock d'origine.

## 8. Économie, fournisseurs et spécialisation

### 8.1 Besoins et dépendances

Ajouter `EconomicProfile`, `SupplyNeed`, `SupplierMemory`, `TradeCommitment` et `ProductionPlanner` dans `Colonies/`. Ce sont des données et règles du système existant, pas un marché boursier parallèle.

Les besoins locaux se répartissent en : survie, remplacement d'équipement, intrants de production, stocks de sécurité, confort et projets. Le besoin en minerai dérive d'une production d'outils réellement demandée, en tenant compte des stocks disponibles et des lots déjà réservés. Ne pas cumuler à chaque heure la totalité d'un objectif ancien.

```text
ConstruireBesoins(établissement, horizon)
    prévoir nutrition et chauffage des présents et arrivées connues
    calculer équipements nécessaires et remplacement observé
    relever objectifs productifs utiles et projets prioritaires
    remonter uniquement leurs recettes réalisables jusqu'aux intrants manquants
    soustraire stock disponible, production engagée crédible et arrivées garanties avant échéance
    garder séparés besoin physique, besoin à acheter et besoin urgent
```

Une arrivée garantie peut réduire une **commande supplémentaire**, mais n'augmente jamais la réserve comestible actuelle. Une arrivée estimée mais encore sans cargaison n'est pas garantie. Dès qu'un voyage est en retard, recalculer le risque de rupture.

`EconomicProfile` observe les volumes produits/consommés, le coût réel, les parts de travail, les exportations vendues et les dépendances. Les mentions « agricole », « minière », « artisanale » ou « marchande » sont des descriptions déduites. Elles n'accordent ni profession obligatoire ni bonus gratuit.

Un centre marchand peut acheter pour revendre s'il existe une demande connue et une marge après stockage/transport/pertes. Il ne réexporte pas des vivres nécessaires à ses propres habitants. La demande future n'est pas une vente déjà acquise.

### 8.2 Information commerciale

Les partenaires échangent des offres et besoins publiés, quantités disponibles à proposer, prix indicatifs, date et fiabilité. L'IA ne consulte pas tout le stock caché de tous les voisins pour anticiper parfaitement leurs besoins. À la rencontre effective, les vendeurs valident leurs biens et les acheteurs leurs pièces.

`SupplierMemory` mémorise les livraisons tenues, les refus, les retards, le coût complet et le délai. Les comparaisons utilisent ces observations ; un fournisseur nouveau possède une incertitude, pas une pénalité infinie. Les informations peuvent arriver avec les voyageurs, à la mise à jour diplomatique si un canal existant le permet, ou lors d'une prise de contact concrète. Pas de télécommunication inventée pour mettre à jour tous les prix en temps réel.

`TradeCommitment` commence comme engagement d'expédition identifié : ressource, quantité, échéance, origine, destinataire, prix limite et voyage associé. Il n'introduit pas crédit, dette ou pénalité monétaire automatique dans cette version. Un stock réservé ne doit pas servir à deux commandes. Les flux réguliers sont des engagements renouvelés, revalidés à chaque départ.

### 8.3 Décision de spécialisation

```text
RéviserProduction(colonie)
    pour chaque établissement et filière candidate connue :
        limiter capacité par sites, sol, ateliers, bras et intrants
        estimer coût marginal = travail + intrants + usure + stockage + transport + risque
        estimer valeur d'usage local ou recette commerciale raisonnablement attendue
        calculer surplus utile par heure de travail, avec coût d'opportunité
    couvrir d'abord survie et engagements critiques
    affecter travail et investissements aux filières au meilleur résultat attendu
    conserver une capacité minimale de transition pour les dépendances risquées
    changer progressivement les objectifs, avec délai et seuil d'amélioration
    publier nouveaux besoins/exportations et demandes de prospection/expansion
```

Une exploitation agricole efficace libère des bras grâce à de bonnes terres et des compétences. Une ville pauvre en terres peut acheter des vivres et vendre des outils fabriqués avec du minerai importé. Une colonie montagneuse peut exporter minerai et gemmes puis acheter grain et textiles. Ces comportements doivent sortir des coûts et possibilités ; ne pas les imposer à partir du biome seul.

Éviter les retournements incessants : conserver une moyenne observée sur quelques jours, attendre un gain significatif avant un investissement concurrent et intégrer les ouvrages déjà engagés. Lissage proposé : amélioration attendue d'au moins 15 % pendant trois jours pour changer la filière principale ; urgence de survie prioritaire sur ce délai.

L'objectif d'exportation est la demande agrégée **non déjà promise**, plafonnée par les acheteurs solvables, le transport et un horizon limité. Remplacer l'intérêt voisin actuel limité à quelques produits par ce calcul commun. Additionner les commandes distinctes plutôt que prendre arbitrairement le maximum, sans compter deux fois une commande transmise par plusieurs voyageurs.

### 8.4 Prix, rentabilité et paiement

Conserver les coûts observés de `LaborLedger`, les coûts de référence et les valeurs marginales de `Economy`. Adapter `Need`, `Surplus`, `UseValue` et `KeepValue` au stock et à l'horizon local. Le coût d'un bien inclut ses intrants et son travail ; le coût transporté inclut aussi portage, distance, provisions et pertes prévues.

Séparer explicitement les unités : heures de travail, pièces, poids et nutrition. Les comparaisons marginales peuvent rester en heures de travail. Le prix payé est un nombre entier de pièces, calculé **une fois pour le lot accepté**, avec un taux de référence explicite pièces/heure. Ne pas arrondir chaque unité jusqu'à des achats gratuits. Si un petit lot vaut moins d'une pièce et n'est pas du secours, agréger le lot ou ne pas le négocier.

`Stockpile` stocke actuellement des entiers. Conserver des quantités et pièces entières avec opérations vérifiées ; utiliser `long` pour les totaux mondiaux, quotas fractionnaires fixes et calculs intermédiaires. Ne pas ajouter `decimal` comme champ sérialisé sans extension volontaire de `StateGraph`, qui ne le prend pas actuellement en charge comme scalaire persistant. Les calculs de nutrition en `decimal` déjà présents peuvent rester des propriétés calculées.

La première version ne simule pas une inflation macroéconomique complète. La création monétaire reste faible et les prix réagissent à la rareté existante. Le taux pièces/heure est stable et versionné dans les règles d'équilibrage. Si une future évolution le rend variable, appliquer un seul facteur cohérent aux offres, limites et budgets ; ne pas confondre une hausse nominale des prix avec un gain de productivité.

```text
PlanifierÉchange(origine, offreConnue)
    déterminer les ventes qui préserveront réserves et engagements locaux
    déterminer achats utiles, poids aller/retour et provisions
    simuler ventes réalisables puis achats financés par portefeuille + ventes prudentes
    calculer coût de trajet et valeur attendue des deux sens
    si urgence : privilégier livraison nécessaire avant rupture, même à faible marge
    sinon : appliquer marge minimale actuelle adaptée au risque et aux alliances
    réserver et préparer uniquement le meilleur départ viable

RéglerÀDestination(voyage)
    revalider objets, limites de prix, stocks disponibles et pièces physiques
    réduire quantités si une promesse n'est plus réalisable
    calculer prix entier du lot et vérifier capacité après transaction
    valider ensemble les débits/crédits de marchandises et monnaie
    appliquer l'échange atomiquement, puis enregistrer gain réellement réalisé
    utiliser produit réel des ventes pour achats suivants autorisés
    si aucune vente n'a lieu : aucun revenu ; adapter achats ou repartir
```

La revente, la vente simple et les trajets aller-retour mixtes réutilisent ce règlement. Une vente espérée ne donne pas le droit de dépenser d'avance. Sans crédit dans cette version, une transaction qui nécessite des pièces inexistantes échoue. Le commerce entre établissements de la même colonie est un transfert interne, même si sa valeur de coût est utile à la stratégie.

## 9. Pyramide des priorités et autonomie

### 9.1 IA individuelle

Conserver les règles actuelles de faim critique et de repas déjà engagé. Un colon qui a réservé/pris un repas ne le consomme pas deux fois et n'abandonne pas sa seule possibilité de manger pour se coucher. Les décisions s'appliquent dans son emplacement réel.

Priorités individuelles : danger immédiat et état incapacitant → faim critique et eau/soins selon mécanismes actuels → repos nécessaire → responsabilité urgente viable → activité assignée → besoins sociaux/confort. Une responsabilité urgente ne fait pas travailler une personne morte, immobilisée ou en détresse.

Les voyageurs ont besoins, provisions et capacités propres. En transit mondial, leur moteur de voyage actualise déplacement et incidents ; le moteur individuel actualise leurs besoins selon un mode de voyage. Ne pas lancer la recherche locale d'un champ ou d'un lit dans la carte de leur foyer à distance.

### 9.2 Deux niveaux de cerveau

`SettlementBrain` reprend les capteurs et choix locaux aujourd'hui dans `ColonyBrain`. `ColonyBrain` coordonne les objectifs politiques et territoriaux. Les services de décision sont des classes statiques ou objets simples comme les systèmes actuels, sans hiérarchie d'agents génériques.

Pyramide locale :

1. Sauver habitants présents : nourriture accessible, chauffage, soins, sécurité.
2. Prévenir rupture avant prochaine livraison : récoltes, approvisionnement, réserves.
3. Maintenir moyens essentiels : outils, bois, logement, accès et services nécessaires.
4. Tenir engagements productifs et logistiques viables.
5. Investir dans spécialisation et agrandissement local.
6. Confort, bijoux et projets d'offrandes.

Pyramide politique : secours entre établissements → fournisseurs externes et routes critiques → maintien des filières exportatrices qui financent les besoins → remplacement des gisements → expansion viable → investissements de confort et religieux.

Pendant une pénurie, ne pas bloquer indistinctement le commerce et les exportations : vendre un stock non vital peut financer le prochain repas. Réduire les missions facultatives, autoriser les achats de secours, conserver les personnes indispensables sur place et vérifier les provisions du voyage. L'urgence peut abaisser le seuil de rentabilité ; elle ne supprime ni durée, ni poids, ni consommation, ni droits de passage.

Les offrandes et ateliers de luxe n'accaparent jamais les derniers vivres ou outils d'une crise. Un projet en cours peut suspendre ses prochains apports. Les matériaux déjà incorporés dans un monument ne deviennent pas automatiquement récupérables pour financer le secours.

## 10. Or et émission monétaire modeste

### 10.1 Fonctionnement retenu

Une unité d'or affiné peut être utilisée en bijouterie, investie dans une offrande ou transformée en deux pièces dans un atelier de frappe. La frappe exige compétence, travail, bâtiment fonctionnel et quota disponible. Les pièces obtenues appartiennent au stock local de l'atelier, puis circulent par les dépenses et transports réels.

Ajouter `Minting` et `MonetaryLedger` dans `Colonies/`. Le quota est une règle d'équilibrage de capacité monétaire, pas une banque mondiale ni un nouveau pouvoir du joueur.

Proposition initiale : au début de chaque année de **20 jours**, calculer un plafond mondial de frappe égal à 5 % de la monnaie effectivement encore présente. Le répartir par population citoyenne vivante entre colonies, avec reliquats fixes et attribution déterministe. Une colonie partage son allowance entre ses ateliers ; ouvrir dix camps n'accroît pas son quota. L'allocation mondiale et ses fractions sont persistées. Le cumul des reliquats ne doit pas permettre d'émettre davantage que le budget mondial arrêté.

Le quota annuel non utilisé expire ; seul un reste de conversion inférieur à une pièce peut passer à l'année suivante, à l'intérieur du budget global. Les nouveaux habitants peuvent changer la répartition annuelle suivante, sans attribuer une allowance complète rétroactive à une colonie fondée en milieu d'année. Toute dotation initiale de colonie politique est une émission distincte enregistrée ; un camp ne reçoit jamais une telle dotation.

Ce plafond borne l'impact même si l'or devient abondant. L'or reste rare et demande du travail : une bonne vente ou une fabrication de bijoux peut lui donner davantage de valeur que la frappe. Le marché fixe cette opportunité, pas un revenu automatique au propriétaire du minerai. La frappe n'est pas conditionnée à l'existence préalable de ventes, afin d'éviter un démarrage circulaire impossible.

À ce stade, une disparition totale de la monnaie n'est pas compensée par une émission gratuite : les réglages initiaux et pertes sont validés pour éviter cet état normal. Un éventuel mécanisme futur de secours monétaire serait une règle distincte à discuter, pas une exception cachée au plafond.

### 10.2 Pseudo-code et comptabilité

```text
AuDébutAnnée(world)
    masse = somme exacte des pièces en stocks, portages, activités et voyages
    budget = calcul fixe du plafond, avec reste inférieur à une pièce
    répartir budget entre colonies vivantes par population, ordre stable et reste déterministe
    enregistrer année, plafond et allowances ; remettre émissions annuelles à zéro

CommencerFrappe(établissement)
    vérifier atelier, travailleur, or disponible et allowance non engagée >= sortie du lot
    réserver allowance et retirer l'or dans le lot de production
    enregistrer intrants et état du lot ; aucune pièce encore créée

AcheverFrappe(lot)
    revalider lot, allowance engagée et achèvement réel
    consommer l'or ; créer exactement les pièces autorisées dans le stock local
    débiter allowance engagée et inscrire émission dans MonetaryLedger
    marquer lot achevé avant tout nouvel appel

AnnulerFrappe(lot)
    rendre or non transformé ou enregistrer perte réelle
    libérer allowance engagée ; aucune pièce créée
```

Un lot traversant la fin d'année conserve sa réservation d'émission dans le budget de son année d'engagement jusqu'à achèvement ou annulation ; ce budget engagé n'est pas redistribué dans la nouvelle année. La somme des émissions et engagements rattachés à une année ne dépasse jamais son plafond.

La frappe est affichée comme émission monétaire, pas comme bénéfice commercial ou bien industriel exporté. `MonetaryLedger` distingue dotations, frappe, transferts et pertes. Un schisme répartit le quota restant et la monnaie existante, sans recevoir une deuxième dotation. La destruction d'un atelier libère seulement les engagements encore récupérables, et enregistre les pertes éventuelles.

## 11. Offrandes, statues et souhaits

### 11.1 Projet collectif concret

Ajouter `OfferingProject`, `OfferingTemplate`, `Monument` et `DivineWish` dans `Colonies/`. Une colonie peut entreprendre un projet dans un établissement choisi. Le projet possède sa recette matérielle figée, ses lots livrés, son travail, sa destination, sa demande associée et son état `Proposed`, `Gathering`, `Building`, `Completed`, `Suspended`, `Cancelled`.

Trois modèles initiaux proposés :

| Offrande | Matériaux indicatifs | Intention possible |
|---|---|---|
| Autel simple | Pierre et bois locaux | Présenter une dévotion accessible, sans pierre précieuse obligatoire |
| Statue des récoltes | Pierre + or + émeraude + saphir | Demander une bénédiction de récolte |
| Statue du champion | Pierre + or + rubis + saphir, diamant facultatif dans un modèle distinct | Demander une faveur durable pour un guerrier précis |

Au moins un modèle prestigieux exige rubis, saphir et émeraude ensemble. Les quantités sont modestes en pierres mais difficiles à réunir par leur rareté. Figées au début du projet, elles ne changent pas quand la colonie découvre un nouveau fournisseur. Un modèle alternatif est un nouveau projet ou une révision explicite avant incorporation, avec restitution exacte des apports récupérables.

Les souhaits sont des données typées : `CropBlessing`, `ChampionBlessing`, puis extensions comme soins/protection seulement quand ces effets sont conçus. Le souhait référence le champ, l'établissement ou le colon par identifiant stable. Un souhait de champion conserve l'intention de durée permanente ; aucun effet de combat n'est implémenté dans cette phase.

```text
EnvisagerOffrande(colonie, intention)
    vérifier absence de projet identique récent ou demande identique déjà active
    vérifier réserves de sécurité et part de budget culturel disponible
    choisir modèle réalisable ou projet prestigieux avec fournisseurs plausibles
    créer projet, réserver emplacement et apports locaux non vitaux
    publier besoins de matériaux manquants pour l'économie et le commerce

ApprovisionnerOffrande(projet)
    recevoir seulement lots réellement livrés à son établissement
    transférer ressources du stock vers inventaire du projet, avec écritures correspondantes
    si crise : suspendre nouvelles dépenses et affectation de travailleurs
    si recette complète et chantier accessible : commencer sculpture/construction

AcheverOffrande(projet)
    vérifier travail achevé et matériaux incorporés une seule fois
    créer Monument conservant matériaux investis et identité du projet
    créer ou enrichir DivineWish associé, avec description de l'effort réel
    notifier la prière au joueur selon mécanismes existants
```

L'achat de gemmes est financé par des pièces disponibles ou des exportations réellement vendues. Un budget religieux n'est pas de la monnaie créée. Les pierres livrées restent séparées du stock disponible ; après incorporation elles appartiennent au monument et ne peuvent être vendues en même temps.

Une offrande achevée peut être montrée et visitée, sans produire chaque jour des gemmes, pièces, faveurs ou miracles. Le même investissement n'est pas présenté comme une nouvelle offrande coûteuse à chaque prière. Un nouveau rituel prestigieux exige un nouveau projet ou des apports supplémentaires explicites. Un monument détruit enregistre matériaux perdus ou récupération partielle définie ; pas de remboursement intégral automatique.

### 11.2 Relation avec `Prayers` et futurs pouvoirs

Réutiliser `PrayerBook` pour le journal et les notifications ; ajouter une catégorie de souhait à la fin de `DecisionKind`, avec une référence typée `DivineWishId`. Les décisions diplomatiques actuelles conservent leur fonctionnement. Adapter les sujets physiques des prières pour qu'ils comprennent l'établissement.

Une prière peut exister **sans `OfferingProjectId`**. L'absence d'offrande ne désactive pas un futur pouvoir. La valeur matérielle de l'offrande est informative pour le joueur ; ne pas créer une formule qui rend mécaniquement obligatoire la réponse ou garantit son succès.

Les souhaits divins n'héritent pas automatiquement de `AutoApprove`. Ne pas exécuter un `Action` vide puis marquer la prière « exaucée ». Dans cette livraison économique, le souhait est présenté comme une demande en attente d'un pouvoir futur ; ignorer n'appelle aucun effet, et un refus explicite suit une réponse enregistrée. Les états du souhait séparent `AwaitingResponse`, `Refused`, `TargetInvalid`, `Fulfilled` ; le dernier exige un effet réellement appliqué.

Le futur service de pouvoirs recevra souhait/cible/contexte et renverra un résultat validé. Une validation réussie puis une application atomique pourront seules marquer `Fulfilled`. Un pouvoir exercé librement sur une cible sans prière restera possible. Ne pas créer maintenant un système complet de mana, des statistiques de héros ou des bonus permanents fictifs.

Si la récolte cible est déjà passée ou le guerrier mort, marquer la cible invalide et conserver le monument/l'histoire. Une nouvelle cible nécessite une nouvelle intention explicite des habitants, pas une substitution silencieuse. Les demandes répétées sont regroupées et espacées ; le silence du joueur ne bloque jamais la vie du village.

## 12. Architecture et responsabilité des fichiers

Les noms suivent les conventions existantes ; le code, ses commentaires et les raisons visibles restent en français. Un fichier par responsabilité réelle suffit. Plusieurs petits enregistrements liés peuvent rester ensemble ; ne pas transformer chaque ligne du tableau en couche d'abstraction.

| Dossier/fichier proposé | Responsabilité / méthodes conceptuelles |
|---|---|
| `Colonies/Settlement.cs` | État physique ; accès aux personnes présentes, stock et services locaux |
| `Colonies/ColonistLocation.cs` | Emplacement local ou voyage, foyer séparé ; changements contrôlés |
| `Colonies/SettlementBrain.cs` | `Sense`, `Think`, `OnDayStart`, priorités et affectations locales |
| `Colonies/ColonyBrain.cs` existant | Coordination politique, objectifs territoriaux, dépendances et investissements |
| `Colonies/ResourceCatalog.cs` | Définitions fixes des ressources ; capacités physiques et usages |
| `Colonies/ProductionPlanner.cs` | Objectifs par filière et remontée des recettes utiles |
| `Colonies/EconomicProfile.cs` | Observations de spécialisation et dépendance, sans bonus caché |
| `Colonies/SupplyNeed.cs` | Besoin daté, priorité, quantité et couverture réelle |
| `Colonies/LogisticsPlanner.cs` | `PlanInternalSupply`, `ReserveDeparture`, choix de trajets directs |
| `Colonies/StockReservation.cs` | Disponibilités et réservation exclusive ; expiration/annulation |
| `Colonies/CargoLot.cs` | Quantité, ressource, âge et propriétés requises au transfert |
| `Colonies/EquipmentState.cs` | Chaussures/prestige individuels et équipement domestique, transfert et usure réelle |
| `Colonies/Trade.cs` existant | Création de missions commerciales, comparaisons et règlements atomiques |
| `Colonies/SupplierMemory.cs` | Offres connues, fiabilité et engagements ; inclure `TradeCommitment` ici si petit |
| `Colonies/Prospection.cs` | Sélection d'expédition, étude locale et retour de connaissance |
| `Colonies/RegionKnowledge.cs` | Indices et estimations propres à une colonie ; pas de référence publique mutable aux réserves |
| `Colonies/ExpansionPlanner.cs` | Comparaison import/exploitation/camp, expédition et évacuation |
| `Colonies/SettlementFoundingProject.cs` | État persistant de l'installation, participants et lots réellement affectés |
| `Colonies/Minting.cs` | Recette spéciale, quota et production de pièces |
| `Colonies/MonetaryLedger.cs` | Masse monétaire, émissions, engagements annuels et pertes |
| `Colonies/OfferingProject.cs` | Modèles, apports, chantier, monument et consommation unique |
| `Colonies/DivineWish.cs` | Intention, cible stable, relation à l'offrande et état de réponse |
| `World/RegionRegistry.cs` | Régions générées/visitées, propriété, conservation des cartes |
| `World/WorldRoadNetwork.cs` | Arêtes améliorées, coût, entretien et révision des routes |
| `Map/Deposit.cs` | Réserves physiques et modes d'exploitation |
| `Map/DepositExtraction.cs` | Vérifications, débit partagé, excavation et production unique |
| `Generation/GeologyGenerator.cs` | Génération régionale déterministe, distincte des tirages de vie |
| `Persistence/WorldSave.cs` et `StateGraph.cs` existants | Nouveau format, types autorisés et reprise exacte des états du nouveau modèle |

`DepositCatalog` décrit les paramètres de matériaux ; il peut rester avec `Deposit`. La géologie persistée appartient à `Map/`, son générateur à `Generation/`. Les recettes fixes peuvent rester dans les fichiers de filière et être indexées par le catalogue, sans créer un chargeur de données externe obligatoire.

Énumérations nouvelles : `SettlementStage`, `ColonistLocationKind`, `DepositMode`, `DepositKnowledgeLevel`, `TransportMissionKind`, `CropKind`, `SupplyPriority`, `OfferingProjectState`, `DivineWishKind`, `DivineWishStatus`. Ne pas recycler un identifiant ancien avec une nouvelle signification.

Ajouter seulement `Logistics` et `Exploration` à `WorkSector` si leur affectation doit être suivie distinctement. `Mining` suffit pour minerai/prospection, `Smithing` pour métaux/frappe/bijoux, `Weaving` pour lin/vêtements ; une nouvelle compétence de céramique ou tannage n'est pas nécessaire à la première version. Si elle est ajoutée ensuite, talents initiaux dérivés par hachage stable, sans décaler les huit métiers actuellement tirés au hasard.

Pour `ActivityKind`, conserver `Mine` et `Craft` avec cibles/variantes explicites ; ajouter `Survey` et `Haul` uniquement pour les gestes locaux réellement distincts. L'activité de voyage global reste portée par la mission. Auditer les correspondances activité/compétence, les listes `WorkSectors.Productive` et les caches indexés ; ajouter un secteur à une énumération ne suffit pas à le rendre assignable.

Propriétés minimales à prévoir, regroupées dans les types ci-dessus :

| Type | Propriétés supplémentaires importantes |
|---|---|
| `WorldState` | Registre régional, réseau routier, compteurs d'identifiants, bilan monétaire, index de population reconstruit |
| `Colony` | `Id`, établissements, principal, renseignements, profil stratégique, fournisseurs, allowance monétaire, projets et souhaits |
| `Settlement` | `Id`, `ColonyId`, `RegionTileIndex`, stade, contexte local déplacé, capteurs, besoins, équipements, engagements |
| `Colonist` | Foyer référencé, localisation, voyage éventuel, équipement ; citoyenneté canonique conservée |
| `FieldPlot` | Culture, date de plantation/installation, maturité et dernière récolte si pérenne |
| `Stockpile` | Lots périssables transférables, quantité réservée, disponibilité ; total existant conservé |
| `Activity` | Cible établissement/site, recette et variante figées, lots d'intrants réellement possédés |
| `Caravan` | Mission, endpoints, participants, chargement/provisions, portefeuille, étapes, engagements et état de livraison |
| `Deposit` | Région, matériau, accès, mode, réserve, crédit borné, débit consommé et jour correspondant |
| `OfferingProject` | Modèle figé, cible, apports livrés, travail, état, monument et souhait associé |

Opérations centrales à exposer conceptuellement : `TryReserve`, `ReleaseReservation`, `TryTransferLots`, `TryStartProduction`, `TryCompleteProduction`, `TryLaunchTrip`, `TryDeliver`, `TryRelocateColonist`, `TryFoundSettlement`, `TryExtract`, `PublishSurvey`, `TryMint` et `SubmitDivineWish`. Elles valident leur contexte et renvoient un résultat/motif exploitable, plutôt qu'un booléen muet ou des mutations partielles. Pas de besoin d'un bus de commandes générique : des méthodes explicites suffisent.

### 12.1 Audit des systèmes existants

| Système | Adaptation obligatoire |
|---|---|
| `ColonistAI`, `Needs`, `Activity` | Contexte du lieu présent ; intrants/repas déjà retirés ; moteur unique par colon |
| `Lifecycle`, `Health`, `Relations` | Vieillissement une fois par citoyen ; soins, infections et rencontres selon présence réelle |
| `Farming`, `Field`, irrigation | Culture explicite, sol et eau locaux, stock de récolte local |
| `Crafting`, `FoodChain`, `ToolChain`, `Cuisine` | Recettes/variantes figées, intrants locaux, lots engagés, maturations sans double tick |
| `Husbandry`, équipements | Animaux présents, reproduction, abattage et usure locaux ; transferts sans duplication |
| `SettlementPlanner`, scheduler et caches | Remplacer le contexte physique `Colony` par `Settlement` ; travaux indexés par établissement |
| `Knowledge` | Découverte collective une fois par colonie, apports de travailleurs sans multiplier le budget par village |
| `Climate`, `Events`, incendies | Événements localisés, avec effets politiques seulement s'ils sont voulus ; pas de tirages multipliés sans contrôle |
| `Migration`, `Schism` | Foyer, citoyenneté, transports, partage territorial et monétaire cohérents |
| `Diplomacy`, `Warfare` | Voisinage territorial, destinations précises, participants non doublés et stocks capturés sur place |
| `Prayers` | Cibles stables par établissement ; souhait distinct d'une décision administrative |
| `WorldMap`, hydrologie | Plusieurs régions d'une colonie, droits et aval territorial ; actualiser chaque région une fois |
| `ResourceAccounting`, `ResourceHistory` | Observer production/consommation locales ; agrégation sans traiter les transferts internes comme du commerce |

L'audit n'autorise pas une refonte de tous ces systèmes : adapter leurs références et invariants au nouveau modèle. Laisser les règles non concernées intactes.

## 13. Ordre d'exécution et pseudo-code du monde

### 13.1 Frontières de temps

Les opérations ci-dessous sont regroupées par fréquence. Le détail de l'ordre existant doit être conservé autant que possible et figé par tests. Une mission ou un projet achevé à un instant n'est pas achevé deux fois après sauvegarde/reprise.

```text
WorldState.Step
    mémoriser ancien jour, heure et année ; avancer Clock une fois
    avancer SettlementPlanningScheduler avec son budget global existant

    si nouveau jour :
        actualiser chaque région matérialisée active une fois
        appliquer vieillissement/péremption selon dates, y compris biens en transit
        pour chaque établissement, dans l'ordre des identifiants :
            eau/climat/ressources locales, cultures, animaux, risques et accès
            entretien des routes et état des stocks/réservations
            SettlementBrain.OnDayStart
        pour chaque colonie, dans l'ordre des identifiants :
            vieillissement et cycle de vie des citoyens, une fois
            Knowledge.Daily une fois ; actualiser connaissances rapportées
            expiration des offres/engagements et état des projets territoriaux/religieux
        si nouvelle année : établir budget monétaire et allowances

    si nouvelle heure :
        pour chaque établissement :
            actualiser risques/soins horaires et activités longues arrivées à échéance
            SettlementBrain.Sense puis Think
        aux heures de planification prévues :
            pour chaque colonie : ColonyBrain.ThinkStrategic
            LogisticsPlanner : priorité aux approvisionnements et retards
            Trade : départs viables, y compris secours
            Prospection et ExpansionPlanner : missions avec bras encore disponibles
            OfferingProject et Minting : travaux compatibles avec priorités
        Diplomacy et Warfare : règles actuelles, endpoints territoriaux adaptés
        avancer les voyages déjà partis, même si les nouveaux départs sont désactivés
        appliquer livraisons et arrivées à cette frontière dans un ordre stable

    pour chaque citoyen vivant exactement une fois :
        si voyage mondial : TickTravelNeeds ; pas d'IA de carte distante
        sinon : ColonistAI.Tick avec établissement réellement présent
    finaliser départs locaux, décès et changements d'emplacement sans double actualisation
    publier faits terminés pour statistiques/interface
```

Les options existantes de cycle de vie, migration et commerce gardent leur sens. Désactiver les **nouveaux** voyages ne fait pas disparaître ceux déjà engagés. Séparer les autorisations de commerce externe, approvisionnement interne et expansion dans les réglages ; un monde de test sans commerce conserve ses ressources physiques et sa comptabilité.

Un événement journalier ne doit pas faire vieillir une cargaison puis la refaire vieillir après transfert au stock destinataire au même instant. Chaque lot conserve son dernier instant traité. Les naissances/décès et arrivées alimentent une liste de changements appliquée à une frontière déterministe, pas une modification incontrôlée de la collection parcourue.

### 13.2 Raisonnement local et politique

```text
SettlementBrain.Think(établissement)
    construire capteurs depuis présence, stock disponible, services et sites connus locaux
    calculer jours de nourriture et risque avant prochaine livraison crédible
    préserver actions individuelles urgentes en cours
    produire SupplyNeed et objectifs locaux selon pyramide
    planifier champs, logements, ateliers et accès avec le planificateur existant
    répartir uniquement travailleurs présents et non engagés ailleurs
    lisser les affectations, sauf nécessité de survie immédiate

ColonyBrain.ThinkStrategic(colonie)
    agréger besoins et capacités locaux sans agréger les droits de consommation
    actualiser risques de fournisseurs et sites bientôt épuisés
    organiser redistribution interne puis demandes commerciales manquantes
    choisir investissements utiles et objectifs de production avec coût complet
    envisager prospection avant rupture estimée de minerai
    comparer camp, importations et réorientation de villages existants
    traiter confort/offrandes avec budget restant
```

Le cerveau local peut demander un ravitaillement sans attendre un nouveau cycle stratégique. Le coordinateur central ne réattribue pas les mêmes travailleurs à un voyage et à un atelier. Une décision en attente d'autorisation politique n'immobilise pas les autres priorités.

### 13.3 Performance et déterminisme

Ne pas scanner toutes les cellules de toutes les régions à chaque `Tick`. Générer les données géologiques par région à la demande ; indexer sources connues, établissements, offres et demandes actives. Les évaluations stratégiques sont quotidiennes ou aux heures prévues ; les besoins individuels restent au rythme actuel. Les scans de placement continuent d'utiliser le scheduler budgété.

Les caches d'inventaire, navigation, présence et services sont invalidés par modification réelle. Les caches sont reconstructibles et non sérialisés ; les engagements, réserves, emplacements et progressions le sont. La fermeture d'une carte ne suspend pas abusivement la péremption ou un événement déjà engagé : rattrapage par dates pour les états inactifs, simulation complète pour les personnes actives.

Utiliser des graines/hachages dédiés à la géologie, tirés du `WorldState.Seed`, de la région et d'un numéro de génération géologique. Conserver `Random`, `Chance` et `Politics` pour leurs responsabilités actuelles. Éviter `GetHashCode()` comme graine, car sa stabilité n'est pas un contrat interprocessus.

Tous les choix à égalité utilisent un ordre stable d'identifiants. Les collections de dictionnaires ne fixent pas implicitement l'ordre des décisions. L'ajout de nouvelles ressources ne doit pas consommer du hasard dans un ancien parcours d'énumération. Les nouveaux systèmes modifieront naturellement les trajectoires des parties ; protéger la reproductibilité et les invariants, sans prétendre conserver toutes les anciennes histoires.

## 14. Sauvegardes du nouveau système

### 14.1 Rupture de format autorisée

Adopter un nouveau format, proposé comme version 3. Il n'y a ni migration des formats 1/2, ni DTO historiques, ni champs de compatibilité à développer pour ce chantier. Les anciens fichiers peuvent être refusés avec un message clair indiquant qu'une nouvelle partie est nécessaire. Ne pas supprimer les fichiers de l'utilisateur et ne pas les accepter partiellement comme s'ils étaient valides.

Les règles existantes de lecture ancienne ne constituent plus une contrainte pour la nouvelle version. Adapter les tests de persistance à cette décision : reprise exacte du nouveau format et refus propre d'un format incompatible. Préserver les autres tests utiles ; ne pas supprimer une catégorie entière de tests seulement pour obtenir un résultat vert.

### 14.2 Données et reprise exacte

Le format contient régions, établissements, identifiants, connaissances, voyages étendus, réservations, lots engagés, projets, équipements et bilan monétaire. Enregistrer les types dans `StateGraph`, préserver ses contrôles de taille, références partagées/cycles et contrôle de schéma. Ajouter une version de génération géologique et de catalogue de règles pour identifier précisément les données écrites.

```text
ChargerNouveauMonde(fichier)
    vérifier version acceptée, schéma, intégrité et limites
    lire état physique et politique complet dans un monde provisoire
    vérifier identifiants, citoyenneté et localisation unique des personnes
    vérifier réserves, lots, engagements, projets et bilans monétaires
    reconstruire index, caches, pathfinders et actions administratives
    restaurer états de hasard sans nouveau tirage
    publier le monde chargé seulement après toutes les validations
```

Les cartes modifiées, réserves déjà extraites, chargements, âges, pièces, progressions et étapes déjà livrées sont conservés. Une reprise ne régénère pas une région, ne recommence pas une frappe et n'ajoute aucune dotation. Les actions de souhait ne reposent pas sur des delegates sérialisés.

Les plafonds actuels de fichiers, état décompressé, objets, collections et profondeur doivent être mesurés avec plusieurs cartes sauvegardées. Augmenter une limite seulement avec un besoin démontré et une borne explicite. Le système de régions paresseux réduit le problème, mais ne dispense pas de conserver toutes les cartes modifiées. Refuser un fichier invalide ou incompatible sans remplacer la partie en mémoire.

## 15. Invariants comptables et états d'échec

### 15.1 Ressources et transformations

Un bien a exactement un emplacement propriétaire : stock, portage, cargaison, lot d'activité, inventaire de projet, équipement ou matériaux incorporés. Une référence de réservation n'est pas un bien supplémentaire. Un animal vivant ne se trouve pas simultanément à l'enclos et dans une cargaison.

Pour chaque ressource, l'évolution du total physique s'explique par : production/extraction, transformations entrantes et sortantes, consommation, pertes et dotations explicitement autorisées. Un transport ne change pas le total. Une recette n'exige pas la conservation du nombre d'unités entre matériaux différents ; elle exige le respect exact de ses intrants et sorties. Un monument retire ses matériaux des stocks disponibles et conserve leur trace d'investissement.

`ResourceAccounting` reste un observateur de flux. Ses six catégories existantes et tableaux indexés doivent être relus avant toute extension. Les transferts internes ne sont ni ventes ni achats au niveau politique. Les statistiques cumulées persistantes nécessaires aux invariants et à la monnaie doivent vivre dans l'état, sans dépendre uniquement d'une table faible ou d'un historique d'interface perdu à la sauvegarde. Conserver un journal récent borné et des totaux, pas une liste infinie d'événements.

Chaque transaction prépare ses changements, vérifie quantités positives, disponibilité, capacité et dépassements numériques, puis applique les débits/crédits ensemble. Ne pas utiliser `Stockpile.Add` avec des valeurs négatives pour simuler un débit sans validation. Réservation, chargement, livraison, transformation, frappe et incorporation ont un identifiant/état empêchant une seconde application.

### 15.2 Monnaie

```text
Pièces physiques actuelles
    = masse initiale du monde
    + dotations politiques enregistrées
    + pièces réellement frappées
    + autres entrées explicitement existantes
    - pertes/destructions enregistrées
    - autres sorties explicitement existantes
```

Les ventes, achats, dons, transports et changements de citoyenneté ne modifient pas la somme. Un portefeuille embarqué appartient à la caravane et n'est plus compté dans le stock d'origine. Les pièces réservées restent dans leur inventaire jusqu'au retrait. Intégrer `CoinsLostToEvents` et les pertes existantes ; ne pas compter une perte dans deux compteurs mondiaux.

Les engagements de frappe ne sont pas des pièces. Les allowances non utilisées ne sont ni un stock ni une richesse disponible. Les bénéfices commerciaux comparent recettes et dépenses réelles ; la monnaie frappée est présentée séparément.

### 15.3 Réactions aux incidents

| Incident | Réaction requise |
|---|---|
| Réserve minière finie | Arrêter rendement, libérer tâches inutiles, réviser estimation et approvisionnement |
| Plusieurs mineurs sur une source permanente | Partager le même débit ; aucune multiplication de quota |
| Fournisseur vendu ailleurs | Réduire livraison possible, actualiser fiabilité, rechercher remplacement |
| Famine imminente | Geler luxe et nouveaux camps, prioriser vivres, maintenir départ de secours viable |
| Route fermée après départ | Recalculer depuis progression réelle, attendre/contourner/revenir ; cargaison conservée |
| Mort d'un participant | Traiter succession/portage/provisions et mission une fois, conserver bilan et citoyenneté des survivants |
| Camp abandonné | Évacuation réelle, maintien terrain/ruines/réserves ; pas de refondation gratuite |
| Colonie change de propriétaire ou disparaît | Revalider droits, cibles, stocks et destinations ; aucune référence pendante silencieuse |
| Projet religieux bloqué | Motif visible, suspension ou annulation définie, apports non incorporés récupérables physiquement |
| Cible d'un souhait disparue | Marquer invalide, conserver offrande et historique, ne pas exaucer sur quelqu'un d'autre |
| Sauvegarde au milieu d'une transaction | Sauvegarder à une frontière atomique ; reprise sans demi-échange ni double livraison |

## 16. Contrat avec l'affichage et observations utiles

Le backend ne référence ni Godot, ni textures, ni scènes. L'affichage représente les faits fournis ; il ne calcule pas une réserve, un prix commercial, un effet de bénédiction ou un stade de village de son côté. Conserver le rendu procédural de secours.

Lectures utiles : liste des établissements d'une colonie, stades, habitants présents/résidents/voyageurs, stocks locaux, synthèse politique, dépendances, commandes, retards, routes, connaissances géologiques, quota de frappe et projets d'offrandes. Une vue de gisement connu fournit estimation et confiance, pas automatiquement la réserve exacte cachée. Les outils de diagnostic peuvent lire la vérité physique séparément.

Faits/notices backend proposés : découverte de site, fondation/évolution/évacuation, gisement proche d'épuisement, livraison urgente, rupture fournisseur, route bloquée, projet d'offrande terminé et prière reçue. Agréger les répétitions ; un ralentissement d'une heure ne produit pas dix alertes identiques. Les descriptions expliquent une cause concrète, par exemple « le camp manque de grain, la livraison prévue est bloquée ».

Les anciennes vues centrées sur `Colony.Map` devront sélectionner un établissement. Prévoir cette adaptation dans le contrat de livraison, sans la faire passer pour un nouveau système de règles côté `Game/`. Les choix du joueur sur une prière passent par une commande validée ; lire une fiche ou sélectionner un camp ne décide pas de son économie.

## 17. Stratégie de réalisation par lots

Chaque lot doit compiler et disposer de sa validation propre. Ne pas empiler tous les changements avant le premier test. Des options temporaires permettent d'activer un système pour les essais ; elles ne doivent pas devenir des branches incohérentes avec des doubles états.

| Lot | Travail et dépendance | Critère de sortie |
|---|---|---|
| 0. Sécuriser le point de départ | État du dossier partagé, lecture ciblée, bilans et graines de référence | Travail existant préservé ; format nouveau défini, sans migration ancienne |
| 1. Établissement principal | `Settlement`, identité, présence, régions, adaptation locale et sauvegarde 3 | Une nouvelle partie à un établissement fonctionne et se sauvegarde/reprend |
| 2. Inventaires et voyages | Lots, réservations, transferts internes, endpoints, besoins de voyageurs | Deux établissements ne peuvent pas consommer les stocks à distance ; conservation aux départs/retours |
| 3. Gisements et prospection | Réserve unique, indices, débit permanent, expéditions, remplacement de l'extraction du fer | Aucun double minerai ; découverte effectivement progressive |
| 4. Expansion autonome | Fondation depuis population/biens existants, évolution, ravitaillement, fermeture | Un camp utile apparaît et peut devenir village sans nouvelle colonie politique |
| 5. Filières et demande | Catalogue, cultures, nouvelles ressources, recettes et usure utile | Production réelle de chaque ressource, demande cohérente et sauvegarde des lots |
| 6. Spécialisation et commerce | Coûts complets, offres connues, engagements, urgences, routes et accès | Scénarios d'importations structurelles et fournisseurs remplaçables, sans verrou de famine |
| 7. Or et frappe | Or déjà réel au lot 5, atelier, allowance partagée et bilan monétaire | Émission plafonnée, sans revenu de camp gratuit ni multiplication par ateliers |
| 8. Offrandes et souhaits | Matériaux rares déjà échangeables, projets, monuments et journal | Offrande composée réelle, prière libre, aucun faux pouvoir appliqué |
| 9. Équilibrage d'ensemble | Mesures, parties longues, sauvegardes en tous états, contrat des vues | Critères ci-dessous vérifiés et valeurs documentées |

Au lot 2, créer deux établissements contrôlés dans des tests pour vérifier la logistique avant que la création autonome du lot 4 existe. Au lot 3, implémenter d'abord le fer, puis enregistrer les autres familles de gisements ; les biens industriels deviennent utilisables au lot 5. Le backend expose les données nécessaires à l'interface à chaque lot, même si sa présentation détaillée est réalisée ultérieurement.

## 18. Validation et critères d'acceptation

### 18.1 Tests de contrats et scénarios

Ajouter des tests centrés sur les comportements, sans recopier les formules dans les assertions :

1. **Contexte local** : village A possède du pain et des outils, B n'en possède pas ; B ne mange ni ne travaille équipé grâce au stock de A avant livraison.
2. **Population unique** : départ, marche locale, trajet, arrivée et retour ; aucun colon n'est actualisé deux fois ou créé par fondation. Citoyenneté et foyer sont conservés.
3. **Transport comptable** : stock + portage + cargaison + activités restent cohérents ; lots réservés non doublés, poids respecté aller/retour, âge des viandes inchangé.
4. **Extraction finie** : plusieurs travailleurs, dernière unité, interruption et reprise ; production cumulée bornée exactement par réserve initiale.
5. **Source permanente** : même débit avec un ou vingt travailleurs, fermeture/réouverture et long arrêt ; pas de crédit accumulé ni terrain creusé indéfiniment.
6. **Connaissance limitée** : un grand site caché ne déclenche pas de camp avant indices/prospection ; l'estimation reste distincte de la réserve vraie.
7. **Fondation autonome viable** : territoire riche connu, coût acceptable et ravitaillement possible ; création dans un délai borné sans dotation ou tirage démographique gratuit.
8. **Développement secondaire** : logements/familles/services apparaissent avec population durable ; même `ColonyId`, budgets locaux et transferts réels.
9. **Spécialisation** : bassin fertile et montagne peu cultivable ; davantage de grain exporté d'un côté, minerai/produits miniers de l'autre, croissance dépendant de ces flux dans le scénario contrôlé.
10. **Industrie importatrice** : ateliers compétents sans minerai local ; achat d'intrants puis vente de produits, coût et intrants correctement imputés.
11. **Importation de secours** : colonie sous seuil `SurvivalAssured`, marchands et provisions encore possibles ; lancement d'achat urgent autorisé, sans retirer les derniers producteurs de nourriture.
12. **Rupture fournisseur/route** : replanification, réserves de sécurité, livraisons retardées visibles ; aucun stock ou paiement téléporté.
13. **Frappe** : plusieurs ateliers et camps ne dépassent pas le quota partagé ; lot traversant l'année, annulation, destruction et sauvegarde sans double pièces.
14. **Offrande composée** : gemmes importées de sources différentes, apports réellement retirés, statue achevée une fois ; impossible de vendre ses matériaux et de les offrir simultanément.
15. **Libre réponse** : prière sans offrande permise ; ignore/refus n'applique aucun effet ; une acceptation sans pouvoir disponible n'est pas marquée comme exaucement.
16. **Persistance** : reprise du nouveau format en aller/retour commercial, fondation, minage, fabrication, frappe et monument en cours ; mêmes états physiques et même suite déterministe après reprise.
17. **Épuisement et réinstallation** : territoire exploité/abandonné/repris ; réserve et modifications ne se régénèrent pas.
18. **Politique et hydrologie** : schisme, mort, frontière ou barrage concernant un village secondaire ; références et budgets valides, région actualisée une seule fois.

Les tests de sauvegarde incluent un fichier corrompu, un format/schéma incompatible, les bornes de collections et une transaction à chaque frontière persistable. Un chargement échoué laisse la partie actuelle intacte.

### 18.2 Mesures d'équilibrage

Exécuter `dotnet test Simulation.Tests` pour les lots de simulation. Si les signatures d'interface ou adaptateurs sont modifiés, exécuter aussi `dotnet build Game/GodColony.csproj`. Les changements graphiques éventuels nécessiteront une vérification visuelle dans leur tâche dédiée.

Conserver les **neuf graines actuelles de `GrowthTests`**, sans remplacement pour masquer une régression. Pour cet ajout majeur, réaliser en plus une campagne de **quinze parties** distinctes et ne lire que son résumé. Aux étapes intermédiaires, suivre la préférence de cinq parties pour un petit réglage, dix pour un réglage moyen, quinze pour un grand changement.

Les essais d'ensemble couvrent au moins vingt années de jeu, avec un essai plus long dédié aux épuisements ; utiliser aussi des scénarios contrôlés qui accélèrent une réserve sans changer la graine des tests de croissance. Inclure terrain fertile, montagne, région pauvre, trajet long, plusieurs colonies et accès politique interrompu. Les parties isolées sans commerce doivent pouvoir conserver une petite subsistance viable : l'interdépendance vise surtout leur croissance et leur industrie.

Mesures résumées : population et établissements, heures de famine soutenue par `StarvationWatch`, décès de faim, nutrition importée, production/consommation par filière, travail productif/transport, marge nette du commerce, fournisseurs actifs, voyages échoués, réserve minière utilisée, délai de découverte/fondation, émissions/pertes/masse monétaire, projets d'offrandes et temps de simulation.

Critères à confirmer pendant l'équilibrage :

- Le démarrage ne se bloque pas faute d'outils nécessaires pour obtenir le premier minerai, ou faute de nourriture avant la première récolte.
- Dans les scénarios spécialisés, importations et exportations réelles permettent une population ou industrie supérieure à leur alternative autarcique avec les mêmes ressources et bras.
- Une rupture longue a un coût visible, sans entraîner systématiquement la disparition immédiate de toutes les colonies ; réserves et fournisseurs alternatifs ont un effet mesurable.
- Un petit potentiel minier ne rivalise pas durablement avec un grand gisement équipé ; les sources permanentes permettent une transition, sans soutenir toute la production industrielle de pointe.
- Les quatre pierres ne sont pas produites en masse partout ; au moins une offrande prestigieuse est réellement complétée par des échanges dans un scénario viable.
- La monnaie créée respecte le plafond annuel exact. Dans les économies commerciales actives, les recettes de ventes dominent la frappe sur plusieurs années ; si l'or seul finance une grande expansion, réduire conversion/capacité ou augmenter ses autres débouchés.
- Les pertes, transformations, biens en transit et investissements expliquent tous les écarts de ressources et monnaie. Aucun solde négatif ni double rémunération.
- Deux exécutions identiques et une exécution sauvegardée/reprise produisent le même état final, aux frontières et règles identiques.
- Les quinze parties fournissent un bilan comparé avant/après ; un résultat défavorable mène à un réglage justifié, pas à un changement opportuniste des graines.

Les objectifs chiffrés de croissance, dépendance et performance ne peuvent pas être garantis avant les mesures. Le développeur doit joindre le résumé observé et les réglages finaux au compte rendu, plutôt que transformer les paramètres initiaux de ce plan en preuves d'équilibrage.

## 19. Livrable attendu de l'agent backend

L'agent réalise les lots dans leur ordre de dépendance, préserve les modifications déjà présentes et ne crée aucun commit sans demande. Il documente uniquement les contrats/réglages utiles, en français, et confronte ce plan aux signatures réellement rencontrées.

Son compte rendu doit indiquer : fonctionnalités réalisées et encore différées, classes/refactorisations significatives, version de sauvegarde et validation de reprise, résultats des tests, résumé des parties d'équilibrage, paramètres ajustés et limites connues. Il doit signaler tout écart matériel aux principes retenus avant de le considérer comme acquis.

Le résultat attendu est une simulation où les habitants développent plusieurs villages, prospectent des ressources finies, organisent leur spécialisation et leur ravitaillement, commercent pour croître, frappent une quantité limitée de monnaie et construisent des offrandes avec des ressources rares. Le joueur conserve entièrement sa liberté de répondre aux prières ; l'application des pouvoirs appartient au chantier suivant.
