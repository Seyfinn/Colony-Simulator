# Commerce observé, commandes et voyages

Le commerce utilise les colonies actuelles, sans modifier l'implantation des quartiers ni les chemins locaux du chantier village organique.

## Informations réellement échangées

`SupplierMemory` garde les offres publiées, leur date, le budget d'achat annoncé, les échanges entièrement ou partiellement tenus, les retards et le coût observé. Les prix et les quantités cachés du partenaire ne sont plus relus pour préparer un échange. Les paiements et les biens réels sont contrôlés à la rencontre.

Une première caravane peut partir pour prendre contact, avec ses propres provisions et sans gain commercial fictif. Elle rapporte les offres au retour. L'hôte reçoit les informations emportées par les visiteurs à leur départ. Une observation ne remplace jamais des renseignements plus récents. Les offres expirent après trente jours ; la confiance diminue avec leur âge. Les fournisseurs nouveaux restent considérés, avec une incertitude modérée.

## Prévisions et commandes

`Trade.Forecast` expose stock physique, quantité disponible, réservations, intrants à l'atelier, sorties des fabrications engagées, charges des habitants vers le stock, bière en fermentation attendue sous cinq jours, retours chargés, achats commandés et besoin d'achat restant. Son horizon ordinaire est de cinq jours.

`TradeCommitment` est une lecture des voyages existants, identifiée par sa caravane : une commande à l'aller n'est pas une livraison confirmée. Au retour, seuls les biens réellement chargés comptent comme arrivée confirmée. Un blocage ou un retard rend cette arrivée incertaine et la retire du calcul des commandes supplémentaires. Ces prévisions ne créditent jamais le stock comestible. Les achats de survie restent fondés sur la nourriture disponible.

La planification des achats soustrait les fabrications commencées et les arrivées crédibles. Un acheteur rencontré contrôle également ses besoins pour éviter de payer deux fois un même manque. La demande d'exportation additionne les besoins publiés des partenaires, sous un plafond, en retranchant les quantités déjà expédiées.

## Paiements et bilans réalisés

Un lot de moins d'une pièce est refusé, sans transfert gratuit. Les autres paiements sont arrondis une seule fois au plus proche, avec les demi-pièces vers le haut ; le prix unitaire conservé dans le compte rendu correspond au paiement effectivement réalisé.

Le départ conserve les quantités chargées et les coûts unitaires connus à cette date. Au retour, le bilan valorise les biens et les pièces réellement revenus, déduit les biens cédés, les pertes, les vivres consommés et le temps des voyageurs. Une réserve de vivres intacte et rapportée n'est pas facturée comme consommée. Les achats perdus en route restent payés mais ne procurent aucun bien au retour. Les valeurs en heures utilisent les coûts connus au départ, qui peuvent être estimés ; le gain annoncé lors de la planification reste affiché séparément.

Les livraisons vers un chantier ne réduisent pas le besoin d'achat du stock. Un même habitant n'est compté qu'une fois. Un fût sans taverne ou dont la maturation dépasse l'horizon ne constitue pas une arrivée prochaine disponible.

## Positions, détours et attentes

La caravane conserve son itinéraire, son segment, la distance parcourue sur ce segment, sa dernière actualisation et ses échéances. Un passage fermé invalide les chemins mondiaux. Les territoires des colonies ennemies sont évités ; les droits par région attendent le futur modèle des établissements.

Les voyages revalident à la prochaine étape. Ils peuvent trouver un détour, attendre avec leur chargement, ou rentrer si la guerre empêche l'échange ou si les provisions rendent l'attente trop risquée. L'arête déjà empruntée est terminée avant le changement de route. Le retour parcourt réellement son chemin et ne verse rien au stock avant l'arrivée.

`WorldMap.SetPassageClosed` est le contrat backend pour fermer ou rouvrir un passage. Il n'ajoute pas de pouvoir au joueur ni de destruction automatique d'une route. Les routes construites et les travaux mondiaux restent un chantier distinct.

## Vues disponibles

- **Stocks et besoins** : quantité totale, jauge de disponibilité, allocations réservées et intrants à l'atelier ; arrivées et achats supplémentaires restent séparés du stock.
- **Commerce** : trajets propres à la colonie observée, cargaison réelle, charge maximale, provisions, retour estimé ou incertain, motif de blocage et commandes confirmées ou à confirmer.
- **Fournisseurs** : offres datées, confiance, livraisons, refus, retards et dernier coût d'acquisition (achats, vivres et absence des voyageurs).
- **Production** : livraisons locales illustrées, fûts en fermentation, chopes attendues, délai du prochain fût et progression ; recette retenue pour le travail commencé et intrants réellement engagés ; taverne et fût rejoignent les autres ateliers. Les substitutions autorisées sont prises en compte dans les manques.
- **Carte mondiale** : itinéraire réellement emprunté, passage fermé et badge d'attente.

Les composants lisent la simulation ; ils ne modifient ni stocks ni affectations. Les contrôles sont conservés lors du rafraîchissement pour garder le défilement.

## Captures et vérification

Captures Godot dans `Game/Assets/validation/` :

- `commerce-logistique.png` : caravane bloquée, charge et provisions, fenêtre compacte.
- `commerce-fournisseurs.png` : renseignements rapportés et historique des échanges.
- `stocks-logistique.png` : quantités disponibles et allocations, fenêtre compacte.
- `fabrications-intrants.png` : fabrication commencée et matières effectivement engagées.
- `commerce-carte.png` : position physique et passages fermés.
- `commerce-fermentation-bilan.png` : maturation réelle des fûts.
- `commerce-deliveries-bilan.png` : charges réellement transportées par les habitants.
- `commerce-suppliers-bilan.png` : gain annoncé et bilan réalisé, y compris une perte.

La scène `economie_production.tscn` propose les modes `economy`, `commerce`, `suppliers`, `active`, `fermentation`, `deliveries` et `routes`, avec `--capture=<chemin>` et éventuellement `--compact`. Sa démonstration fait effectuer une vraie rencontre puis bloque un autre voyage dans un monde distinct des sauvegardes du joueur.

`SupplierTests` couvre rencontre et retour d'informations, absence d'actualisation à distance, commandes, fabrications, détours, attentes, guerre, conservation et sauvegarde/reprise. Les contrôles ciblés complètent les cas existants de secours alimentaire, de conservation commerciale et de migrations v1/v3. Le format écrit est désormais 9 ; les schémas figés v1/v3/v4/v5/v6 restent acceptés. Le format v5 conserve ses bilans réalisés lors de la migration vers les établissements. Un ancien voyage en cours sans coûts de départ conserve son trajet et son chargement mais son gain réalisé reste inconnu. Le cumul des anciens gains prévisionnels repart à zéro à la migration, sans modifier stocks ni pièces.

Quatorze cas distincts ont réussi dans les contrôles ciblés, dont les paiements fractionnaires, les bilans négatifs, la reprise déterministe d'un voyage, la lecture v3/v4 et les prévisions de livraison/fermentation. Les trois nouveaux aperçus compacts ont été exécutés et contrôlés visuellement. Aucun équilibrage sur une longue campagne n'est affirmé pour ce changement.
