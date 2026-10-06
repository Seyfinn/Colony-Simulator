# Plan : nature sauvage, transport, royaumes

Conception validée avec l'utilisateur (octobre 2026). Le joueur observe ; les habitants décident.
Spécification technique pour le développement : [Spec-faune-transport-royaumes.md](Spec-faune-transport-royaumes.md).
Ordre de réalisation : **1. Nature sauvage → 2. Transport → 3. Royaumes** (chaque chantier sert le suivant).

## Existant à réutiliser (vérifié dans le code)

| Système | Fichier | Ce qu'il fait déjà |
|---|---|---|
| Élevage | `Colonies/Husbandry.cs` | Enclos de poules, moutons, vaches ; reproduction par paires, abattage, abondance par région, troupeau de départ. |
| Saisons | `Time/GameClock.cs` | 4 saisons de 5 jours ; l'hiver affame les troupeaux et freine les voyageurs. |
| Stock | `Colonies/Stockpile*.cs` | Stock commun de la colonie, promesses, transferts atomiques, pertes. |
| Logistique interne | `Colonies/LogisticsPlanner.cs` | Ravitaillement entre établissements d'une colonie, par priorité et charge réelle. |
| Caravanes | `Colonies/Trade*.cs`, `TerritorialTravel.cs` | Colons qui portent biens et pièces de colonie en colonie ; routes mondiales et passages interdits. |
| Routes | `RoadWorks`, `RoadDevelopment`, `WorldRoadNetwork` | Aménagement des routes selon les passages. |
| Diplomatie, guerre | `Diplomacy.cs`, `Warfare.cs` | Opinions, alliances, trêves, bandes de guerriers ; victoire = pillage seulement. |
| Colonies filles | `Schism.cs` | Fondation d'une fille (`Parent` = mère). |

Absents : chasse, faune sauvage, prédateurs, ponts, chefs, territoires politiques, royaumes, conquête.

## 1. Nature sauvage

- **Faune libre** par biome : gibier (cerf, lapin, sanglier…) et prédateurs (loup, ours). Déplacements, reproduction, équilibre proies/prédateurs.
- **Tous les animaux sont sauvages au départ**, y compris poules, moutons et vaches. Plus de troupeau de départ : il faut capturer puis apprivoiser.
  À surveiller : les premières années ne doivent pas s'effondrer (mesure sur plusieurs graines).
- **Apprivoisement progressif** : nourrir une bête capturée un certain temps ; les jeunes s'apprivoisent mieux. Le loup apprivoisé devient chien (garde de troupeau, aide à la chasse).
- **Sélection** : les générations nées en enclos sont plus dociles et productives ; races locales propres à chaque colonie.
- **Bêtes de trait** : bœufs et chevaux apprivoisés servent aux caravanes (voir 2).
- **Chasse** décidée par les habitants (besoin de vivres, gibier abondant) : viande et peaux. La surchasse éloigne le gibier.
- **Prédateurs** : blessent en général, tuent rarement. Attaquent troupeaux mal gardés, voyageurs isolés et caravanes ; plus pressants en hiver ou quand le gibier manque. Réponse des habitants : chasse, enclos renforcés, palissades.
- **Herbivores sauvages** qui mangent les cultures non protégées.
- **Ressource et menace** dans une proportion propre au biome.
- **Ressources sauvages épuisables** : ruches (miel, cire), cueillette (baies, champignons, plantes médicinales), poisson.
- **Alphas et bêtes légendaires** rares : grande chasse collective, prestige ou trophée.
- **Recul et retour de la faune** : défrichement et chasse la repoussent ; une zone tranquille se repeuple.
- **Hivers rudes et migrations** saisonnières du gibier.

Écartés : maladies animales, croyances liées aux animaux.

## 2. Transport et logistique

- **Stock commun conservé**, mais l'accès dépend de la distance : un besoin est servi par l'entrepôt le plus proche qui a le bien, au prix du trajet (temps, pas de matière).
- **Porteurs et charrettes visibles** ; au survol, contenu et destination (lus dans la simulation).
- **Charrettes** quand distance et volume le justifient ; plus de charge, mais liées aux routes.
- **Ponts** construits par les habitants quand un trajet utile est coupé par l'eau (prolongement de l'aménagement des routes).
- **Caravanes améliorables** par la colonie qui en a les moyens et le besoin : charrette de bois → renforts de fer → bêtes de trait. Effets : capacité et vitesse.
- **Interception très rare**, selon le risque de route : faible sur route entretenue ou près d'une colonie, plus fort en terrain sauvage (prédateurs, bandits). Les pertes restent comptées (biens en transit).

## 3. Politique et territoires

- **Chef élu** par tous les adultes de la colonie. Sa personnalité oriente les choix habituels (prudence, construction, guerre, commerce). Fiche au survol.
- **Pas de lois ni de taxes.**
- **Territoires et frontières** dessinés sur la carte, colorés par royaume.
- **Alliances et échanges** seulement entre colonies ; pas de mariages ni de rapprochement culturel : chaque colonie garde son identité.
- **Royaume** :
  - une colonie fille naît dans le royaume de sa mère (simple appartenance, aucun dû) ;
  - la conquête, rare et coûteuse, y ajoute une colonie étrangère ;
  - le **roi est élu par les chefs** des colonies du royaume.
- **Conquête** : une partie des habitants meurt, une partie fuit, le reste change d'allégeance.
- **Empires** possibles mais rares et très coûteux. L'influence baisse avec la distance à la capitale et la taille du royaume : les colonies lointaines finissent par faire sécession.

## Décisions

- Alliance, guerre et paix restent des **prières au joueur** : c'est lui qui tranche. Le chef élu ne fait qu'influencer quand la colonie les demande.
