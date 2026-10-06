# Équilibrage de l'économie et des peuples

Réglages du 6 octobre 2026, fondés sur les chaînes de production actuelles. Les coûts ci-dessous sont des références en heures de travail, pas des prix imposés : les coûts locaux mesurés, la rareté, les besoins et les bourses continuent de déterminer les échanges.

## Objets et production

Les produits transformés coûtent désormais les intrants nécessaires et le travail de leur recette, divisés par le nombre d'unités produites. Lorsqu'il existe plusieurs filières, la moins coûteuse donne la référence. Les coûts locaux restent mesurés pendant la partie. Un intrant reçu ou acheté conserve une valeur même si le village ne l'a jamais produit ; auparavant, son travail pouvait compter pour zéro.

| Produit | Ancienne référence | Nouvelle référence approximative |
| --- | ---: | ---: |
| Farine | 4,60 | 3,28 |
| Pain | 6,70 | 4,18 |
| Charbon de bois | 5,00 | 3,13 |
| Fer | 48,00 | 32,37 |
| Outils | 129,00 | 80,67 |
| Toile de lin | 12,00 | 5,98 |
| Chaussures | 25,00 | 29,07 |
| Or affiné | 72,00 | 44,20 |
| Charrette locale | 30,00 | 43,77 |

La référence couvre les 55 ressources, y compris les aliments, les plats, les animaux et les nouvelles récoltes sauvages. La monnaie reste à une heure par pièce ; elle n'est pas une marchandise. Les rubis, saphirs et émeraudes valent 24 heures de référence, les diamants 36. Les poids des chevaux, bœufs et chiens suivent désormais leur charge réelle.

La fusion du fer passe de 24 à 18 secondes, la forge d'un outil de 32 à 24 secondes. Un outil supporte 240 usages au lieu de 150, avec le même gain de vitesse. La cuisson passe de 14 à 12 secondes avec farine et de 20 à 16 secondes avec la meule à bras ; les rendements physiques restent identiques.

La réserve de farine augmente avec les besoins du village. Le grain réservé suit le pain à fabriquer, le rendement du four et la farine disponible, plutôt qu'un nombre de jours de repas crus. La poterie donne deux objets pour trois argiles et le tissage deux toiles pour trois lins : les objectifs d'intrants tiennent maintenant compte de ces rendements.

## Prime selon la profondeur de production

La valeur d'échange reçoit une prime de 8 % par transformation successive dans la filière de référence : `coût local × (1 + 0,08 × étapes) × rareté`. La référence reste la filière la moins coûteuse ; à coût égal, la plus courte est retenue. On compte la branche d'intrants la plus profonde, et non le nombre d'intrants ni le nombre d'unités du lot. Un intrant acheté garde sa profondeur de production.

| Profondeur | Exemples | Prime |
| --- | --- | ---: |
| 0 | Bois, minerai, grain, pièces | 0 % |
| 1 | Charbon de bois, farine, cuir, viande salée | +8 % |
| 2 | Fer, pain, chaussures, vêtements de référence en lin | +16 % |
| 3 | Outils, charrettes, bijoux | +24 % |

Ainsi, le bois devient charbon de bois (une étape), le charbon permet la fusion du fer (deux), puis le fer est forgé en outil (trois). Le pain utilise la référence moulin puis four, même dans un village qui travaille encore à la meule à bras. Les variantes d'un même produit partagent sa valeur de référence ; le surcroît de travail d'une filière moins efficace reste mesuré localement.

Cette prime entre dans les offres d'achat et de vente, les négociations et la rentabilité comparée des filières. Les coûts physiques et les heures des intrants restent séparés : la prime n'est pas composée à nouveau à chaque atelier. Les pièces conservent leur valeur d'une heure. La rareté, les bourses, le transport et les besoins continuent de limiter les échanges.

## Bâtiments

Les emprises, implantations autonomes, extensions et capacités de service suivent les règles existantes.

| Construction | Bois | Pierre | Travail à vitesse ×1 |
| --- | ---: | ---: | ---: |
| Mine de 6 × 4 | 72, auparavant 120 | 72, auparavant 100 | 160 s, auparavant 260 |
| Marché de 4 × 3 | 24, auparavant 42 | 12, auparavant 18 | 48 s, auparavant 72 |
| Extension du marché de 2 × 3 | 12, auparavant 21 | 6, auparavant 9 | 24 s, auparavant 36 |
| Entrepôt de 4 × 3 | 36, auparavant 54 | 24, auparavant 30 | 60 s, auparavant 84 |
| Four de poterie | 8 | 16 | 18 s |
| Tannerie | 12 | 4 | 16 s |
| Orfèvrerie | 10 | 12 | 24 s |

Chaque entrepôt protège 240 unités supplémentaires au lieu de 150. Le four de poterie, la tannerie et l'orfèvrerie ont des coûts propres à leur fonction ; ils utilisaient auparavant le coût par défaut d'une hutte. Le logement et les services vitaux gardent leurs réglages.

## Échanges et monnaie

Le miel, les herbes médicinales et les charrettes entrent dans les offres. Les objectifs suivent leurs usages : soins, alimentation et transport, avec une protection des charrettes déjà en service. La réserve de bière commerciale suit celle du brassage.

Le bénéfice attendu d'une caravane est celui de l'expéditeur, selon ses besoins et les prix négociés. Le gain du partenaire ne sert plus à justifier le coût du voyage. Les provisions, charges, bourses et réserves de survie restent vérifiées. Les prises de contact restent un investissement sans recette immédiate ; le bilan réalisé valorise les biens au coût de production connu au départ et peut différer du gain attendu selon leur rareté.

La référence d'une poule passe de 100 à 80 pièces, d'un mouton de 250 à 180, d'une vache de 600 à 360. La rareté régionale et le seuil de richesse avant un achat restent appliqués : les troupeaux ne deviennent pas une ressource gratuite.

Un or affiné donne 40 pièces au lieu de cinq, une valeur proche de sa référence de production. Le plafond mondial annuel de 20 %, son partage entre colonies, les engagements de lots et la comptabilité monétaire demeurent appliqués.

## Natalité

La fécondité vaut `1 / longévité relative`. Le rythme humain sert de référence ; la prospérité module toujours les conceptions. Les âges de maturité et de vieillesse suivent leur échelle existante.

| Peuple | Fécondité relative | Chance de conception par jour à pleine prospérité | Gestation | Intervalle minimal entre naissances |
| --- | ---: | ---: | ---: | ---: |
| Orques | 1,67 | 16,67 % | 3 jours | 0,6 an |
| Humains | 1 | 10 % | 5 jours | 1 an |
| Nains | 0,5 | 5 % | 10 jours | 2 ans |
| Elfes | 0,25 | 2,5 % | 20 jours | 4 ans |

Les taux concernent un couple éligible. Les naissances observées d'un peuple dépendent aussi des couples, de leurs âges, du logement, des ressources, des arrivées et des décès.

## Validation

Les neuf graines de `GrowthTests` sont conservées. La suite Release passe avec 505 tests réussis et deux sondes volontairement ignorées. Les contrôles ciblés passent aussi en Debug, et le projet du jeu compile.

La sonde `tools/BalanceProbe` compare 15 mondes distincts (graines 1001 à 1015), avec huit fondateurs pour chacun des quatre peuples, sur douze ans, sans réponse automatique aux prières. Les observations quotidiennes utilisent `StarvationWatch` et vérifient la monnaie et les stocks. Les résultats sont dans [le relevé initial](Mesures-equilibrage-avant.json) et [le relevé après équilibrage](Mesures-equilibrage-apres.json).

### Résultats de la comparaison

| Mesure sur les 15 mondes | Avant | Après |
| --- | ---: | ---: |
| Premier doublement humain médian, en années | 3,02 | 2,93 |
| Population totale moyenne à douze ans | 114,67 | 120,93 |
| Pains produits | 62714 | 85286 |
| Outils produits | 1660 | 1387 |
| Voyages terminés | 1602 | 1245 |
| Colonies avec famine observée | 4 | 3 |
| Décès de faim relevés dans les villages principaux | 48 | 17 |
| Écarts monétaires | 0 | 0 |
| Stocks négatifs | 0 | 0 |

| Peuple | Population moyenne avant → après | Naissances moyennes avant → après |
| --- | ---: | ---: |
| Humain | 28,07 → 32,73 | 12,73 → 14,60 |
| Nain | 35,33 → 33,47 | 11,53 → 10,07 |
| Elfe | 34,33 → 34,67 | 8,67 → 7,67 |
| Orque | 16,93 → 20,07 | 8 → 10,87 |

La nourriture produite augmente et moins de travail est consacré au remplacement des outils. La croissance humaine reste proche de trois ans pour le premier doublement. Les naissances elfes sont moins fréquentes, celles des orques plus nombreuses dans cette campagne.

Le risque de famine subsiste : trois colonies ont connu une famine prolongée, avec 17 décès de faim relevés au village principal, et une population orque atteint zéro dans un monde. Les observations de `StarvationWatch` portent sur tous les citoyens ; les décès chiffrés ici sont ceux du registre du village principal. La campagne ne démontre donc pas une absence de famine dans tous les établissements.

Le bilan commercial cumulé au coût de production passe de -101235,65 à -82217,20 heures. Il reste négatif : il inclut les prises de contact et les voyages qui répondent à une rareté locale, sans valoriser les connaissances acquises. Ce bilan doit être distingué du bénéfice attendu selon les besoins, utilisé pour choisir un échange.

Commande pour reproduire la campagne sur les règles courantes :

```powershell
dotnet run --project tools/BalanceProbe -c Release -- docs/Mesures-equilibrage-courant.json 15 12
```

Cette campagne couvre douze ans et les conditions indiquées ; elle ne constitue pas une validation à cent ans, avec de grands empires ou avec tous les pouvoirs divins activés.

### Validation de la prime de transformation

Une campagne supplémentaire de dix mondes sur douze ans (graines 1001 à 1010, quatre peuples et huit fondateurs chacun) est conservée dans [le relevé de la prime](Mesures-prime-transformation.json). La comparaison ci-dessous utilise uniquement ces mêmes dix graines du relevé après équilibrage précédent, avant l'ajout de la prime.

| Mesure sur les dix mondes | Avant la prime | Avec la prime |
| --- | ---: | ---: |
| Population totale à douze ans | 1198 | 1290 |
| Pains produits | 57128 | 59104 |
| Outils produits | 912 | 968 |
| Voyages terminés | 799 | 844 |
| Colonies avec famine observée | 2 | 1 |
| Décès de faim dans les villages principaux | 17 | 11 |
| Écarts monétaires | 0 | 0 |
| Stocks négatifs | 0 | 0 |

Le bilan commercial réalisé reste négatif et proche du précédent (−53590,83 contre −53954,88 heures). Cette campagne ne prouve donc pas une amélioration globale du bénéfice commercial ; les tests contrôlés vérifient directement la hausse du prix négocié et de l'intérêt de production à travail égal. Une colonie orque connaît encore une famine et atteint zéro habitant. Les limites de mesure des décès et de durée de campagne indiquées plus haut restent applicables.

```powershell
dotnet run --project tools/BalanceProbe -c Release -- docs/Mesures-prime-transformation.json 10 12
```
