# Établissement principal — reprise après le village organique

Chaque colonie politique possède un établissement principal et peut développer des établissements secondaires. Voir [Territoires-filieres.md](Territoires-filieres.md) pour les missions et filières ajoutées ensuite.

## Autorités conservées

- `Colony` garde identité politique, citoyens, connaissances, relations, prières et historique commercial.
- `Settlement` possède stocks, travail, bâtiments, champs, canaux, troupeaux, climat et plan du village. Les adaptateurs `Colony.Stock`, `Map`, `Layout` et autres données physiques désignent le contexte local courant ; hors rendez-vous local ils désignent l’établissement principal. Les mises à jour, recherches et lectures des vues ferment leur contexte avant de rendre la main.
- `RegionState` conserve le terrain de la région. Le terrain du village et celui du registre régional sont la même référence, sans génération supplémentaire.
- Les identités des colonies, établissements et voyages sont attribuées par compteurs persistants, sans tirage aléatoire. Les voyages mémorisent leurs établissements de départ et d'arrivée.

## Citoyenneté et présence

`Colony.Members` possède les citoyens vivants, y compris en mission. `PresentPopulation` est une vue filtrée, sans seconde collection propriétaire. Les habitants en voyage ou en approche ne sont pas affectés aux métiers locaux ni actualisés par leur ancien village.

Les opérations locales utilisent `PresentMembers`. Le départ conserve la citoyenneté et la place en hutte ; le retour remet la personne sur sa carte puis reprend sa marche locale. La migration politique et la mort retirent la citoyenneté. Les expéditions militaires respectent la même distinction.

Les colons portent un foyer, un établissement courant et un identifiant de voyage. `Colonist.Location` décrit leur emplacement actuel depuis ces données. Les coordonnées ne sont pas réinitialisées au départ.

## Sauvegarde

Le format courant 10 conserve également gisements, équipements, cultures, missions internes, emprises de bâtiments et extensions accolées. Les formats v1/v3/v4/v5/v6 sont lus (les formats 7, 8 et 9 ne sont pas lus) avec leurs schémas figés. Le lecteur transfère les anciens champs physiques vers l'établissement principal en conservant objets, références partagées, plan, réservations et inventaires. Il n'ajoute aucune monnaie, ressource, personne ou carte.

Les voyageurs des anciennes parties rejoignent la collection politique canonique sans double allocation ; le nombre de citoyens peut donc dépasser l'ancien compteur de présents. Les compteurs, liens de citoyenneté et localisations sont contrôlés au chargement. Les bilans commerciaux réellement calculés en v5 restent conservés.

## Vérification et interface

Les contrôles ciblés couvrent reprise complète sur 250 ticks, identités et autorités locales, lectures v1/v3/v4/v5, familles, citoyenneté des marchands, échanges physiques et sauvegarde en guerre. La compilation du jeu est propre. Aucun nouvel équilibrage sur une campagne longue n'est affirmé.

Le panneau Économie présente citoyens, présents, personnes en mission et en approche ; les réserves locales utilisent le nombre de présents. Aperçu : `Game/Assets/validation/etablissement-principal.png`.

## Établissements secondaires

Le moteur met à jour chaque établissement actif avec ses propres services, plan, champs et réserves. Les citoyens restent dans la collection politique unique, tandis que le travail quotidien utilise la présence locale. L’observation depuis Économie → Territoires ou la carte mondiale sélectionne la carte réelle du lieu ; la sauvegarde garde ce choix.
