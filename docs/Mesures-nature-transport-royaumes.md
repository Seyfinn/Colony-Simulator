# Mesures de faune, transport et royaumes

## Protocole reproductible

Depuis la racine du dépôt : `dotnet run --project tools/NatureProbe -c Release -- 15 20 6`.
Quinze parties de vingt ans, graines 301 à 315, six colonies initiales de douze fondateurs chacune. Le joueur de mesure accepte toutes les prières. Ce choix favorise les fondations et les conflits ; les résultats ne représentent pas un joueur refusant ces demandes. Les neuf graines de `GrowthTests` restent inchangées.

Les voyages et activités de chasse sont observés chaque heure ; royaumes, stocks, abondance du gibier et monnaie chaque jour à midi. Les nombres de royaumes ci-dessous sont des observations quotidiennes, pondérées par leur durée de vie, et non une distribution des tailles à la fondation. Résumé brut : [Mesures-nature-transport-royaumes.json](Mesures-nature-transport-royaumes.json).

## Résultats

| Mesure | Résultat |
|---|---|
| Population finale moyenne par monde | 335,13 habitants |
| Royaumes formés | 75 ; présents dans les 15 parties |
| Observations de royaumes de 1 à 3 membres | 14 317 sur 15 386, soit 93,05 % |
| Taille maximale observée | 5 membres |
| Conquêtes et pensées de sécession | 0 et 0 |
| Voyages commerciaux observés | 12 334 |
| Voyages interceptés au moins une fois | 39, soit 0,316 % |
| Charrettes en stock / demandes quotidiennes | 0 / 0 |
| Plus long aller-retour moyen au dépôt agricole | 2,424 secondes |
| Observations d'écart de monnaie | 0 |
| Décès attribués à la faim | 32 |
| Colonies ayant connu un jeûne soutenu détecté | 20 |
| Abondance du gibier, minimum / moyenne | 0 / 34,89 % |
| Observations de gibier sous 30 % | 31 115 |
| Pression maximale des prédateurs | 100 % |
| Heures de chasse cumulées par habitant | 102 643 |

## Interprétation et limites

Les petits royaumes dominent et aucun royaume ne dépasse six membres. Les interceptions sont non nulles et restent sous 2 %. Les conquêtes et sécessions n'ont pas été exercées par ces parties ; leur rareté ne permet pas de valider leurs effets à partir de ce lot. Le compteur de sécession recherche les pensées correspondantes et ne constitue pas un journal exhaustif des transitions politiques.

L'absence de charrette correspond à l'absence de demande observée : les trajets agricoles restent sous le seuil de huit secondes. Le test ciblé `NatureFollowupTests` vérifie qu'un champ éloigné déclenche `Carts.Wanted`, puis que la forge consomme les matières et fabrique la charrette. Les mesures quotidiennes des demandes et des trajets portent sur l'établissement principal ; les stocks de charrettes couvrent tous les établissements actifs.

Les trente-deux décès de faim et les épisodes de gibier très bas empêchent de conclure à un équilibre alimentaire satisfaisant. Ce lot ne sépare pas l'effet des gués, du terrain, de la croissance et de la chasse. Il ne justifie donc pas à lui seul de modifier les coûts des gués, la croissance animale ou le seuil de chasse : ces paramètres restent à 3/6 et 30 %. Une comparaison contrôlée de ces facteurs reste nécessaire avant un ajustement.

## Robustesse et contrôles

La comptabilité monétaire inclut désormais les pièces pillées transportées par les bandes de guerre. La pause de fabrication conserve progression et matières ; une disparition de l'atelier rembourse les matières restantes et les ingrédients périssables continuent de vieillir. `StarvationWatch` tient compte des repas réellement pris entre ses observations pour éviter le faux positif du petit déjeuner.

Validation : 431 tests réussis, deux ignorés, aucune erreur ; compilation de `Game/GodColony.csproj` sans erreur ni avertissement. Les tests ajoutés couvrent reprise, mort, départ, atelier disparu, aliments périssables, sauvegarde avec pont en chantier, charrette et monnaie en transit. Captures Godot : `Game/Assets/validation/nature_transport.tscn`, y compris secours procédural et infobulles.

Les bâtiments optionnels rucher et pavillon de chasse ne sont pas ajoutés. Les pensées déjà présentes dans les modules de nature ne sont pas dupliquées.
