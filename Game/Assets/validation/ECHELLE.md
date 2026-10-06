# Validation des grandes colonies — 6 octobre 2026

Les nouveaux onglets se trouvent dans Économie : **Économies d’échelle**, **Village**, **Monnaie et pouvoirs**. Les fiches Territoires présentent les besoins, les productions mesurées, les coûts, les livraisons et leurs attentes ; les informations de gisement restent des observations avec confiance et ancienneté.

| Élément | Avec images | Secours procédural |
|---|---|---|
| Modules, sorties, ramassage, charrette, bénédiction, frappe, sanctuaire, monuments et chantier de raccord | [Galerie](echelle_ateliers_images.png) | [Galerie](echelle_ateliers_secours.png) |
| Production, combustible, ateliers et courbe sur dix jours | [Bilan](bilan_scale.png) | [Bilan](bilan_scale_secours.png) |
| Services, quartiers, ouvrages achevés et croissance | [Village](bilan_village.png) | [Village](bilan_village_secours.png) |
| Monnaie, effets et souhaits | [Pouvoirs](bilan_powers.png), [résultats des souhaits](bilan_powers_resultats.png) | [Pouvoirs](bilan_powers_secours.png) |
| Établissements, renseignements et raccords étudiés | [Territoires](bilan_territories.png) | [Territoires](bilan_territories_secours.png) |
| Routes mondiales de niveaux 1 et 2, six missions territoriales | [Carte](routes_missions.png) | [Carte](routes_missions_secours.png) |

Les panneaux défilent sans recréer leur racine ni perdre leur position. « En mesure » et « Pas assez de données » restent distincts d’une utilisation nulle. Les surfaces achevées excluent les chantiers. Les régions inconnues ne donnent ni chiffres climatiques détaillés ni population ; aucune réserve souterraine réelle n’est affichée.

Les règles et mesures sont fournies par la simulation. Les ajouts nécessaires au backend concernent la série quotidienne d’utilisation, les compteurs des ouvrages achevés, la mesure de la frappe et une lecture de la croissance sans enregistrer une nouvelle raison. Aucun tirage aléatoire ni règle de production ou de croissance n’est ajouté par les vues.

## Contrôles

- `dotnet build Game/GodColony.csproj` : réussite, aucun avertissement ni erreur.
- Suite complète Release : **502 réussites, 2 ignorés**.
- Contrôles Debug des domaines concernés et des sauvegardes : **67 réussites**.
- Après les derniers ajustements des vues et de la connaissance des routes : **26 tests ciblés Release réussis**.
- `ECONOMY_PRODUCTION_UI_OK` : stocks et choix de travail inchangés, onglets et défilement conservés.
- `DIVINE_NOTICES_OK` : deux alertes successives restent dans la file ; une ancienne bénédiction présente avant l’ouverture ne revient pas.
- `SCALE_MAP_OK` : aucune modification des données persistantes entre les images, après initialisation des collections paresseuses des vues existantes.

Godot émet dans cet environnement des messages d’accès concernant son journal, son cache de shaders et les certificats système ; les scènes de validation quittent avec le code 0 et leurs captures sont enregistrées.

## Reproduire

Compiler le jeu avant d’ouvrir une scène. La scène `echelle_ateliers.tscn` lit les véritables vues du village ; `--fallback` désactive les images et `--export-scale` régénère les vingt variantes PNG depuis leurs sources procédurales. La scène `economie_production.tscn` accepte `--preview=scale`, `village`, `powers` ou `territories`, ainsi que `--compact`, `--lower`, `--fallback` et `--capture=<chemin>`. La scène `nature_transport.tscn` accepte `--world --roads` pour la carte des missions.

Ces scènes préparent des données de démonstration en mémoire ; elles ne chargent ni ne modifient de sauvegarde du joueur.
