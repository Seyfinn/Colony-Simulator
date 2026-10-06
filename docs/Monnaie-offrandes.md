# Monnaie frappée, offrandes et souhaits

Contrats des lots 7 et 8 du plan économique, confrontés au code actuel. Les réglages courants et leur campagne de mesure sont décrits dans [Equilibrage.md](Equilibrage.md).

## Frappe monétaire (`MonetaryLedger`, `Minting`)

- L'atelier de frappe (`BuildingType.Mint`) demande le savoir `Coinage`. Sa recette : 1 or → 40 pièces, 24 secondes, par une activité de fabrication ordinaire (intrants engagés, compétence de forge). Ce rendement est proche du coût de référence de l'or affiné ; les pièces naissent à l'achèvement, directement dans le stock local de l'atelier.
- Le quota est une règle de capacité : au début de chaque année de 20 jours, `WorldState.Money` établit un plafond mondial de 20 % de la monnaie présente (stocks de tous les établissements et caravanes), avec un reste de conversion inférieur à une pièce reporté à l'année suivante. Il est réparti entre les colonies vivantes selon leur population citoyenne (plus fort reste, puis identifiant). Un quota inutilisé expire ; une colonie fondée en cours d'année n'a pas de part ; ouvrir plusieurs ateliers ou camps n'augmente pas le quota.
- Un lot engage son quota au départ (`Activity.MintCoins`, `MintYear`). Interrompu, il rend l'or et l'engagement (si l'année est encore celle du budget) ; un lot qui traverse la fin d'année garde son engagement dans le budget de son année et ne réduit pas le nouveau.
- La frappe ne dépend d'aucune vente. Une colonie ne veut frapper que lorsqu'elle a moins de 40 pièces par habitant, de l'or non réservé aux bijoux, du quota, et qu'elle n'est pas en crise ; l'or à affiner pour la frappe reste borné (les gîtes d'or sont finis).
- Comptabilité : `MonetaryLedger.Imbalance` = pièces présentes − (dotations + frappe − pertes). Une dotation de fondation d'une colonie politique est enregistrée ; aucun camp n'en reçoit. Un schisme partage le quota restant et la monnaie existante sans créer de pièce.
- `Colony.Ledger` désigne ce registre ; hors d'un monde (colonie bâtie à la main) il est nul et la frappe est impossible.

## Offrandes (`OfferingProject`, `Offerings`, `Monument`)

- Une colonie d'au moins 14 habitants présents, hors crise, qui connaît la maçonnerie, envisage une offrande (au plus une à la fois, 30 jours entre deux). Cinq modèles : autel simple (pierre et bois), statue des récoltes, statue du champion, grande statue des moissons (rubis, saphir **et** émeraude), statue du champion couronnée (avec diamant). Chaque modèle n'est dressé qu'une fois par colonie ; un modèle prestigieux n'est lancé que si ses pierres sont en stock ou offertes par un fournisseur connu et si leur coût reste raisonnable en pièces (25 % des pièces disponibles).
- Un projet suit : `Proposed` (un sanctuaire `BuildingType.Shrine` est bâti par le planificateur) → `Gathering` (les unités réellement présentes au stock de l'établissement, au-delà des réserves de pierre et de bois, passent à l'inventaire du projet : elles ne sont plus vendables) → `Building` (un sculpteur au plus par dizaine d'adultes travaille la pierre ; activité `Sculpt`) → `Completed` (un `Monument` garde les matériaux incorporés une seule fois). Une crise `Suspended` le projet, qui reprend ensuite ; au-delà de 90 jours sans sanctuaire ou en crise, il est abandonné (`Cancelled`) et rend ses apports non incorporés au stock. Les matériaux manquants sont publiés comme besoins aux échanges (`Offerings.Need`).
- Un monument n'accorde rien : il n'engendre ni pièce, ni gemme, ni faveur.

## Souhaits (`DivineWish`, `DecisionKind.Wish`)

- Un monument achevé crée un souhait : bénédiction de récolte (champ, identifiant stable, valide tant qu'une récolte pousse) ou faveur pour un guerrier (colon vivant). La prière correspondante est distincte d'une décision administrative : l'accord d'office ne s'y applique jamais.
- États : `AwaitingResponse`, `Refused`, `TargetInvalid`, `Fulfilled`. Ignorer ne change rien ; refuser est enregistré ; accorder est retenu (`AcceptedTicks`) sans rien exaucer. `Fulfilled` n'est atteint que par `DivineWishes.TryFulfill(…, effectApplied: true)` d'un futur service de pouvoirs, sur une cible encore valide. Une cible disparue invalide le souhait (le monument reste) et retire la prière sans réponse (`PrayerStatus.Withdrawn`). Aucun pouvoir n'est appliqué dans cette livraison.
