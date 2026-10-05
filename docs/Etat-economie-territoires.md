# Économie et territoires : contrats disponibles et dépendances

Le plan de référence reste `Plan-economie-territoires-offrandes.md`. Les lots 1 à 8 disposent d’une implémentation jouable : établissements secondaires, voyages internes, gisements et prospection progressive, nouvelles filières, expansion et logistique, spécialisation, routes mondiales, frappe monétaire, offrandes et souhaits. Les cartes et implantations restent organisées par les habitants. Les contrats et limites actuelles figurent dans [Territoires-filieres.md](Territoires-filieres.md). Les anciennes campagnes ci-dessous précèdent ces ajouts et ne valident pas leur équilibrage.

## Ajouts utilisables

- **Stocks et promesses (partie du lot 2)** : `StockReservation` conserve propriétaire, quantité, priorité et échéance. `Get` lit le stock physique ; `Available` exclut les engagements. Un retrait ordinaire ne consomme pas une promesse. Les réservations expirent à la frontière horaire ; une perte réelle les annule si elles ne peuvent plus être honorées. `Active` permet au demandeur de constater cette annulation.
- **Transferts (partie du lot 2)** : `TryTransferTo` conserve les âges des lots de viande et prépare les limites numériques avant retrait. `LoadReservation` ne charge une promesse qu'une fois. `CargoLot` représente les portions réellement transférées, sans constituer un deuxième stock persistant.
- **Fabrications (partie du lot 5)** : chaque activité garde sa recette choisie et ses intrants physiques. Une interruption rend les types réellement prélevés (y compris la viande salée de remplacement) et conserve l'âge de la viande fraîche. Les intrants vieillissent à l'atelier ; une perte interrompt la recette et seuls les matériaux restants reviennent au stock. L'usage est compté à l'achèvement, les pertes à leur occurrence. Les activités anciennes v1 conservent leur restitution historique faute de lots sauvegardés.
- **Portage (socle du lot 5 et partie du lot 2)** : `ResourceCatalog` centralise les poids et la nutrition des ressources existantes. La cargaison et les provisions sont deux inventaires physiques. `Caravan.Cargo` est l'adaptateur existant des quantités de `Inventory`, avec le même dictionnaire ; les deux vues ne représentent pas deux exemplaires des biens. Pour écrire une cargaison de viande, utiliser les opérations d'inventaire et de transfert.
- **Voyages commerciaux (parties des lots 2 et 6)** : départ préparé avec biens, portefeuille et provisions réels ; contrôles de trajet, guerre, participants, poids et réserve de survie. Les besoins des voyageurs hors carte évoluent et leurs provisions sont consommées. Les colons encore dans la marche locale conservent leur actualisation locale unique. Les invendus limitent les achats possibles au retour.
- **Règlement (partie du lot 6)** : `TrySellTo` vérifie ensemble les marchandises, le paiement et les plafonds des inventaires avant application. Chaque transaction et le retour sont appliqués une fois. Si le stock d'arrivée dépasse la limite numérique, le voyage conserve sa cargaison et expose `BlockedReason` jusqu'à pouvoir la déposer.
- **Secours alimentaire (partie du lot 6)** : sous deux jours de nutrition adulte, une colonie peut rechercher des achats de nourriture même si `SurvivalAssured` est faux. Ce voyage n'exige pas la marge commerciale ordinaire, mais doit rapporter plus de nutrition que ses provisions. Il conserve les producteurs de nourriture, les agriculteurs et les bûcherons sur place, ainsi qu'un jour de nutrition pour les habitants restants.

Les recettes, la salaison, les surplus négociables, les cadeaux diplomatiques et l'armement des expéditions respectent maintenant les engagements lors des débits. Les pertes restent physiques, même si les biens avaient été promis.

## Sauvegarde et réglages

Le format écrit est désormais **10** (frappe monétaire, offrandes et souhaits, prospection progressive, routes mondiales, règles territoriales ; les formats 7, 8 et 9 ne sont pas lus). Les schémas figés v1/v3/v4/v5/v6 restent chargeables ; les formats 7, 8 et 9 ne sont pas lus. Les données locales sont déplacées vers l’établissement principal pendant la lecture, sans nouvelle dotation ni nouveau terrain. Les vrais bilans v5 sont conservés. Le format courant conserve aussi les coûts au départ et les bilans réalisés des voyages ; les anciens gains prévisionnels ne sont plus intégrés au cumul réalisé. Il conserve les promesses, les recettes et intrants des fabrications, les âges des cargaisons, les provisions, la dernière actualisation des voyageurs et le motif de blocage. Le chargement contrôle les inventaires, les réservations, les offres datées et les itinéraires. Les renseignements, budgets publiés, positions et attentes des voyages sont persistés. La migration v1 déjà réalisée par Claude est conservée et testée ; les anciennes sauvegardes v2 ne correspondent pas au nouveau schéma.

Poids initiaux : ressource ordinaire 1, pierre/minerai de fer/fer 2, poule 2, mouton 8, vache 16, pièce 0,01. La capacité existante de 36 devient une capacité de poids, avec ses modificateurs existants. Prévoir une marge de provisions d'un jour. La nutrition adulte quotidienne de 0,96 correspond aux besoins individuels actuels (0,04 par heure), sans modifier les constantes du calendrier ni les graines de croissance. Ces réglages sont des valeurs initiales, pas une validation de l'interdépendance finale.

## État des lots 1 à 8

| Lot | Implémentation actuelle |
|---|---|
| 1. Établissements | Autorités locales, identités, citoyenneté et présence, régions persistantes, simulation et observation de chaque lieu. |
| 2. Inventaires et voyages | Réservations, intrants, provisions et cargaisons physiques ; extrémités locales ; prospection, fondation, ravitaillement et évacuation. |
| 3. Gisements | Réserves régionales uniques, quotas journaliers partagés, sources permanentes limitées et renseignements rapportés au retour. Le fer extrait débite ce registre. |
| 4. Expansion | Décisions autonomes de départ, camp fondé sans dotation, ravitaillement, retour de surplus, évolution et fermeture avec conservation du terrain. |
| 5. Filières | Vingt ressources supplémentaires, quatre ateliers, cuivre/or, poterie, lin, cuir, chaussures, vigne/vin, gemmes/bijoux, équipement et usure. |
| 6. Commerce | Offres datées, fournisseurs remplaçables, importations urgentes, engagements réels, échanges industriels et accès aux régions ennemies. |
| 7. Frappe | Atelier de frappe, quota annuel partagé (5 % de la monnaie présente), engagements par lot, registre des émissions. Voir [Monnaie-offrandes.md](Monnaie-offrandes.md). |
| 8. Offrandes | Sanctuaire, projets à matériaux réellement livrés, sculpture, monuments, souhaits typés sans faux exaucement. Voir [Monnaie-offrandes.md](Monnaie-offrandes.md). |

Les contrats des ajouts du 5 octobre 2026 (prospection progressive, expansion, logistique, spécialisation, routes, passage, hydrologie, schisme d'un établissement, finitions des filières) sont dans [Territoires-filieres.md](Territoires-filieres.md).

**Encore différé :** application des pouvoirs divins (les souhaits attendent un futur service), monuments détruits (pas de récupération partielle), modèles de souhait de soins et de protection, ouvrages de franchissement des routes, droits de passage payants, visuels de l'atelier de frappe, du sanctuaire et des monuments (tâches T-033 à T-036 du cahier graphique), interface détaillée de la monnaie, des offrandes et des routes. Le budget culturel est borné par le travail (un sculpteur par dizaine d'adultes) et par le coût en pièces des pierres à la création du projet (25 % des pièces disponibles), non par un plafond annuel de dépenses. L'équilibrage de longue durée reste à poursuivre ; les seuils territoriaux et coûts nouveaux sont des réglages initiaux.

## Vérification reproductible

Ajouts du 5 octobre 2026 : `MintingTests`, `OfferingTests`, `ProspectionTests`, `ExpansionTests`, `LogisticsPlannerTests`, `SpecializationTests`, `TerritoryPoliticsTests`, `FinishingTests` et `ResumeTests` (reprise à plusieurs instants, déterminisme de deux exécutions). Les tests de contrat de population (citoyens en voyage) et de commerce ont été mis à jour : les voyageurs restent dans `Members`, les missions internes ne sont pas du commerce.

`dotnet test Simulation.Tests` vérifie la simulation et les neuf graines de `GrowthTests` sans les remplacer. `LogisticsTests` couvre réservations, expirations, pertes, transports périssables, échanges atomiques, échecs de départ, achats de secours, surcharge, alimentation des voyageurs et sauvegarde/reprise. `FabricationInventoryTests` vérifie la restitution après sauvegarde, le vieillissement et la consommation unique. `dotnet build Game/GodColony.csproj` vérifie les adaptateurs d'affichage conservés.

La sonde séparée se lance avec `dotnet run --configuration Release --project tools/LogisticsProbe -- 15 20`. Un troisième argument permet éventuellement d'enregistrer le résumé dans un fichier. Elle utilise quinze graines distinctes (101 à 115), deux colonies et vingt années par partie. Elle affiche uniquement le résumé : population, voyages, famines soutenues par `StarvationWatch`, décès de faim, stocks négatifs, conservation monétaire et dépassement de portage. Aucun bilan avant/après n'est affirmé sans référence antérieure mesurée sur le même code du village organique.

### Résultats observés le 5 octobre 2026

- Avant le dernier ajout aux fabrications : **260 tests réussis, un ignoré** ; les neuf graines de croissance sont conservées. Projet Godot : compilation réussie, sans avertissement ni erreur.
- Quinze parties de vingt années, graines 101 à 115, deux colonies initiales : population finale moyenne de **109,5 habitants par monde**, de 95 à 129, avec les personnes en voyage et les transients comptés une seule fois.
- **998 voyages terminés**, dont 12 sans échange ; travail économique épargné estimé par le registre existant : environ 110 471 heures.
- **Aucun écart monétaire**, stock négatif ou dépassement de charge observé. La comptabilité inclut les pièces embarquées et `CoinsLostToEvents`.
- `StarvationWatch` a détecté une famine soutenue dans **deux colonies**, sans décès de faim. L'équilibrage global n'est donc pas déclaré terminé ; ces situations devront être comparées au village organique stabilisé avant d'imputer un effet aux transports ou de changer ses réglages.

La campagne complète a fini en mode développement, avant la relance optimisée envisagée ; son résumé a été récupéré dans la console. L'écriture du fichier optionnel dans `.artifacts/` a été refusée par l'environnement après les mesures. Les chiffres ci-dessus sont conservés ici et ne dépendent pas de ce fichier absent. Les essais de sauvegarde/reprise incluent promesses actives, cargaison de viande âgée et poursuite déterministe ; ils refusent aussi un fichier qui fait posséder le même stock à un village et une caravane.

Après la demande de validations plus légères : seuls les **quatre cas ciblés de `FabricationInventoryTests`** ont été exécutés pour le dernier ajout, tous réussis. La campagne longue n’a pas été relancée.

### Campagne du 5 octobre 2026 (après les lots 7 et 8 et les ajouts territoriaux)

Quinze parties de vingt années, graines 101 à 115, deux colonies initiales, version optimisée de la sonde (6 minutes) :

- Population finale moyenne **109,6** (de 76 à 159), contre 109,5 pour la campagne précédente (de 95 à 129) ; famine soutenue détectée dans **une** colonie (deux auparavant), **aucun décès de faim**, aucun stock négatif, aucun dépassement de charge commerciale, **aucun écart monétaire** (frappe comprise : pièces présentes + pertes = masse initiale + frappe).
- Territoire : 33 camps fondés (cinq établissements actifs au maximum par monde en moyenne), 3 hameaux, aucun village secondaire ni camp fermé en vingt ans ; 411 prospections (27 par partie), 1 148 livraisons internes (76 par partie ; 163 avant le seuil de charge minimale et le délai de quatre jours), 14 déménagements de familles, 41 chantiers de route pour 40 niveaux posés.
- Monnaie et offrandes : 34 pièces frappées au total (l'or est rare et le quota modeste) ; 22 monuments dressés et 22 souhaits en attente d'un pouvoir.
- Commerce : 643 voyages terminés, dont 233 sans échange (prises de contact : coût sans gain). Le cumul de travail épargné (−30 400 h) ne compte que le point de vue de l'expéditeur : le plan additionne le gain des deux parties, l'expéditeur n'en réalise qu'une partie. Cette mesure n'est pas comparable à la campagne précédente (+110 000 h) ; elle signale un commerce d'expédition peu rentable pour de petits peuples proches, à régler (durée de validité des offres, seuil de rentabilité).
- Réglages à surveiller : fréquence des prospections, rareté de la frappe, absence de villages secondaires.

## Commerce observé et interface

Les quatre ajouts indépendants sont réalisés : mémoire commerciale, prévision des achats, incidents de voyage et affichage des allocations. Voir [Commerce-observe.md](Commerce-observe.md) pour les contrats, limites actuelles et captures.

La vérification de ce précédent ajout commercial comportait **neuf cas ciblés réussis**. Douze cas distincts ont été vérifiés au cours de cet ajout, en incluant les anciens formats et la conservation des échanges. La compilation Godot réussit sans avertissement ni erreur. Cinq aperçus des panneaux et de la carte ont été contrôlés, en 1100 × 700 et 1600 × 900. Aucune campagne de croissance n'a été relancée : les anciennes mesures ci-dessus précèdent le commerce observé et ne constituent pas son bilan d'équilibrage.

## Vérification des territoires et filières

Contrôles ciblés : fondation sans dotation, présence unique, reprise déterministe, livraison interne et retour de surplus après sauvegarde en route, connaissance au retour de prospection, quota commun puis épuisement, combustible substitué et chargement v6. Les tests des fournisseurs et de la chaîne du fer restent utilisés lors de l’intégration. Compilation du jeu et aperçus Godot des territoires, du camp et du catalogue procédural vérifiés séparément. Aucune campagne longue supplémentaire n’est revendiquée.
