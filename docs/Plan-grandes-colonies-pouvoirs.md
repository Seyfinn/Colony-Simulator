# Grandes colonies, économies d'échelle et pouvoirs divins

Date : 6 octobre 2026.
Statut : liste des ajouts retenus pour la prochaine évolution ; aucune implémentation livrée par ce document. Les paramètres numériques et effets précis restent à définir dans chaque lot.

## Objectif

Favoriser de gros villages autonomes organisés en quartiers, entourés de camps spécialisés. Les habitants choisissent leurs implantations, leurs métiers, leurs approvisionnements et leurs chemins. Les avantages économiques viennent d'équipements réellement utilisés et de flux physiques, sans multiplicateur de production accordé à la seule population.

La population concernée par un atelier ou un service est celle de son établissement et de ses usagers accessibles ; additionner les membres dispersés d'un empire ne donne pas un avantage local gratuit.

## Point de départ à revérifier avant chaque lot

- `Schism.Daily` ouvre actuellement la possibilité d'une demande de départ à partir de 30 habitants, même sans crise ; un départ reste soumis à une prière au joueur.
- Les compétences progressent déjà par la pratique. L'ajout porte sur la continuité des affectations, pas sur un second système de compétences.
- Les camps, le ravitaillement, les charrettes, les routes mondiales, les services locaux et l'aménagement des accès existent déjà : les étendre selon les besoins du lot.
- `DivineWishes.OnAnswered` enregistre un accord sans appliquer de pouvoir. Les souhaits concernent actuellement une récolte ou un champion.
- Les vues montrent déjà une partie des missions et des indices de gisements. Compléter leurs informations au lieu de reconstruire ces panneaux.
- Les fichiers du projet contiennent des travaux partagés. Relire le code actuel et préserver ces changements avant toute modification.

## Liste des ajouts

| ID | Ajout | Comportement attendu | Critère de réussite |
|---|---|---|---|
| A-01 | Croissance sur place et schismes motivés | Privilégier les nouveaux logements et quartiers quand le village peut les desservir. Fonder une colonie distincte pour des raisons durables : manque de place ou de ressources, mécontentement, ambition ou opportunité concrète. | Un village prospère peut devenir grand ; sa population seule ne provoque plus régulièrement une demande de séparation. Les départs motivés restent possibles. |
| A-02 | Camps spécialisés au service du village | Comparer agrandissement local, importation et camp spécialisé avec leurs coûts réels. Un camp approvisionne le village et peut se développer si son activité le justifie. | Les camps ont une fonction économique identifiable ; les fondateurs, leurs réserves et les transports viennent du monde existant. |
| A-03 | Production par lots | Partager les coûts de préparation et une partie du combustible pour plusieurs unités réellement demandées. Commencer par une filière existante, puis étendre aux filières où le gain a un sens. | Le coût par unité diminue avec un lot utile ; les matières sont réservées, les produits sont physiques et la colonie ne fabrique pas des surplus sans débouché. |
| A-04 | Spécialistes plus stables | Maintenir un artisan dans son métier lorsque la demande et l'approvisionnement sont réguliers ; réutiliser les compétences actuelles et limiter les changements inutiles d'affectation. | Une grande colonie entretient davantage de spécialistes par leur pratique réelle, tout en répondant aux urgences et aux besoins individuels. |
| A-05 | Services partagés entre quartiers | Mutualiser les services existants lorsque leur capacité et leur accessibilité le permettent. Construire un service supplémentaire lorsque la fréquentation ou les trajets le justifient. | Le coût des services par habitant peut diminuer sans rendre un service distant ou saturé artificiellement disponible. |
| A-06 | Transport groupé | Regrouper les livraisons compatibles et mieux remplir les charrettes sur les flux réguliers. Respecter les destinations, priorités et délais des besoins urgents. | Réduire le travail de transport par unité livrée, sans bloquer un ravitaillement urgent pour attendre un chargement complet. |
| A-07 | Extensions d'ateliers | Agrandir un atelier durablement utilisé pour accueillir davantage de travail ou des lots plus importants. Réutiliser les extensions existantes quand elles conviennent. | L'extension est décidée et construite par les habitants ; elle a un coût, une capacité concrète et ne crée pas de rendement lorsqu'elle reste inutilisée. |
| A-08 | Raccourcis et boucles de chemins | Détecter les détours coûteux entre quartiers, ateliers et dépôts ; proposer un raccordement lorsque les trajets économisés justifient les travaux. | Les habitants créent des raccourcis praticables et utiles, avec travail réel, budget borné et suspension des aménagements facultatifs en crise. |
| A-09 | Bénédiction des récoltes | Appliquer un effet agricole concret à la cible valide d'un souhait accepté. Définir amplitude, durée, expiration et règle de cumul avant de coder. | Le souhait est exaucé seulement après application ; l'effet intervient dans la simulation, reste lisible et se conserve correctement après sauvegarde. |
| A-10 | Bénédiction du champion | Appliquer une faveur concrète au champion concerné, selon un effet et une durée à définir. | La cible est validée ; le combat ou la protection utilise réellement l'effet ; aucune faveur n'est appliquée à un habitant mort ou absent du périmètre prévu. |
| A-11 | Interface de monnaie et d'offrandes | Montrer plafond et parts de frappe, pièces frappées et engagées, projets d'offrande, matériaux investis, cible et état des souhaits, ainsi que les effets divins actifs. | Le joueur comprend les investissements et les réponses divines à partir des données de simulation. |
| A-12 | Visuels de frappe, sanctuaires et monuments | Donner une silhouette propre aux bâtiments de frappe et de culte ; représenter les autels et statues réellement présents. | Lecture claire de la carte avec images et rendu procédural de secours ; respecter le cahier graphique et ses tâches T-033/T-034. |
| A-13 | Territoires et routes lisibles | Afficher routes mondiales aménagées, types de missions, progression de prospection, indices et confiance, priorités et blocages de ravitaillement. Montrer les productions, besoins et coûts disponibles de chaque établissement. | Le joueur peut comprendre le rôle d'un camp et la cause d'un approvisionnement bloqué. Compléter T-035/T-036 sans refaire les parties déjà présentes. |
| A-14 | Bilan des économies d'échelle | Exposer les mesures utiles : production par adulte, coût par unité, utilisation des ateliers, travail de transport et disponibilité des services. | Les gains et les limites des grandes colonies sont mesurables ; les valeurs affichées viennent du backend. |

## Ordre de réalisation proposé

1. **Croissance et référence de mesure** : établir la comparaison de départ, puis A-01 et A-02. Distinguer schisme du village et sécession d'un établissement éloigné.
2. **Premiers gains productifs** : A-03 et A-04, avec une première filière pour vérifier le gain avant généralisation.
3. **Circulation et territoires** : A-08, A-06 et A-13. Les nouvelles décisions réutilisent les budgets de planification et le transport existant.
4. **Capacités et lecture économique** : A-05, A-07, A-11, A-12 et A-14. Ajouter les informations nécessaires à la vérification dès les lots précédents, puis compléter leur présentation ici.
5. **Pouvoirs divins** : A-09 et A-10, avec affichage des effets et de leur expiration dans les vues précédentes. Ce lot peut être avancé si la priorité de jeu change.

Chaque lot doit rester jouable et vérifiable séparément. Aucun seuil de population, taille de lot, entretien supplémentaire ou pourcentage de bénédiction n'est fixé par cette liste.

## Validation

- Contrôler les changements de simulation avec `dotnet test Simulation.Tests -c Release` ; une passe Debug avant fusion ou en cas d'échec suspect.
- Construire `Game/GodColony.csproj` avant lancement et vérifier visuellement les interfaces, les chemins et les bâtiments, y compris en secours procédural.
- Pour cette évolution importante, comparer sur 15 graines une grosse implantation et plusieurs petites à population totale et conditions initiales comparables. Garder les neuf graines de `GrowthTests` ; produire les mesures supplémentaires séparément avec un résumé.
- Mesurer production par adulte, combustible et travail par unité, temps de trajet, pertes, utilisation des ateliers, confort et famines suivies avec `StarvationWatch`. Distinguer gain local et coût des camps satellites.
- Vérifier qu'une grande colonie bien organisée gagne en efficacité, que les gains plafonnent avec les capacités et trajets, et que les petites implantations proches des ressources gardent un intérêt.
- Protéger comptabilité des ressources et de la monnaie, biens en transit, réservations, pauses d'artisans, priorités de survie et déterminisme des générateurs distincts.
- Vérifier l'aller-retour du format courant et la reprise déterministe des lots, transports, travaux et effets divins. Ne pas ajouter de migration des anciennes sauvegardes.
- Mesurer le coût de simulation des grandes populations et des nouvelles recherches avant d'annoncer un objectif de taille ou de vitesse.

## Hors de cette liste

Les commandes de prestige restent dans leur plan séparé. Les nouveaux pouvoirs de soins et de protection, les péages et la récupération des monuments détruits ne font pas partie de ces premiers lots.
