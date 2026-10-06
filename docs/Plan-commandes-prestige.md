# Commandes de prestige du joueur — plan d'implémentation

Statut : proposition prête à coder, aucune fonctionnalité implémentée par ce document.
Demande : créer une demande rémunérée d'objets difficiles à produire, contenant des matériaux rares, afin d'injecter des pièces dans l'économie. Notifier le joueur de leur fabrication. Le village organise seul ses ateliers, ses approvisionnements et son travail.

## 1. Périmètre retenu

- Trois objets : couronne sertie, armure d'apparat, relique ouvragée.
- Commandes automatiques du joueur-dieu : pas de portefeuille du joueur ni de bouton obligatoire pour payer. Les pièces constituent une émission externe explicite.
- Au plus une commande active par colonie politique, rattachée à un établissement producteur. Les camps ne reçoivent pas chacun une commande supplémentaire.
- Une commande porte sur un seul objet ; elle ne déclenche aucun lot sans besoin réel.
- Fabrication dans les ateliers existants, avec les activités, matières engagées et transports physiques existants.
- Notification à chaque objet fabriqué, puis paiement à sa remise réelle depuis le stock disponible. Le journal conserve ces deux faits distincts.
- Aucun nouveau bâtiment, miracle, bonus de production ou besoin artificiel des ménages.

Ces choix sont des valeurs de départ pour l'implémentation, pas des réglages déjà validés en partie.

## 2. Points d'intégration vérifiés dans le code

| Fichier | Rôle actuel / conséquence |
|---|---|
| `Simulation/Colonies/Stockpile.cs` | `ResourceType` comprend or, quatre gemmes et bijoux ; le dernier identifiant actuel est `Carts = 54`. Ajouter les nouveaux produits sans renuméroter les existants ; revérifier le dernier identifiant au moment de coder. |
| `Simulation/Colonies/ExtendedIndustry.cs` | Catalogue de recettes, objectifs, choix de travaux et construction des ateliers supplémentaires. Les bijoux utilisent l'orfèvre. La protection contre les crises et le contingent d'artisans du confort existent. |
| `Simulation/Colonies/Economy.cs` | Besoins, prix, surplus, déficit et biens échangeables. Les ressources d'identifiant au moins 27 passent par `ExtendedIndustry.Target`. |
| `Simulation/Colonies/ToolChain.cs` | La demande de fer dépend actuellement du manque d'outils. Ajouter du fer à une recette seule ne suffira pas à relancer sa production. |
| `Simulation/Colonies/Crafting.cs` | Résolution des recettes et comptage des fabrications en cours, dont les travaux suspendus. |
| `Simulation/Colonies/ColonistAI.cs` | À la fin de la fabrication, le produit rejoint `Carrying`. Le dépôt en stock intervient ensuite. Le détachement d'un habitant peut aussi déposer son chargement. Ne pas payer au simple lancement du travail. |
| `Simulation/Colonies/ColonyBrain.cs` | `Say` alimente les pensées du journal ; `OnFirstProduct` ne concerne que la première fabrication d'un produit. Il ne couvre pas les notifications demandées à chaque objet. |
| `Simulation/WorldState.cs` | Cadences quotidiennes et horaires avec `UseSettlement`. Placer les décisions politiques une seule fois par colonie, et traiter les stocks dans le bon établissement. |
| `Simulation/Colonies/MonetaryLedger.cs` | La masse monétaire explique actuellement dotations + frappe − pertes. Les paiements du joueur doivent être une source séparée. |
| `Simulation/Persistence/StateGraph.cs` | Sauvegarde du graphe par champs et liste de types autorisés. Tout nouveau type d'état persistant doit être enregistré. |
| `Game/Scripts/Main.Menus.cs` | `Notify` affiche déjà un encart pendant cinq secondes ; il remplace le texte précédent. Prévoir une file pour les achèvements simultanés. |
| `Game/Scripts/Hud.cs` | Journal de la colonie observée. Il ne suffit pas à prévenir de la fabrication dans une autre colonie. |
| `Game/Scripts/View/ResourceIcons.cs` | Icônes et secours procédural des ressources. Les nouveaux objets doivent rester reconnaissables sans PNG. |

Relire les sections concernées et vérifier `git status` avant toute modification : beaucoup de ces fichiers contiennent déjà des travaux non validés d'autres assistants. Respecter `AGENTS.md` et les contrats graphiques ; ne pas inclure leurs changements dans un commit. Aucun commit n'est demandé.

## 3. Objets et prix de départ

| Objet | Atelier | Intrants par objet | Travail de base proposé |
|---|---|---|---|
| Couronne sertie | Orfèvre | 2 or, 1 saphir, 1 diamant | 24 secondes |
| Armure d'apparat | Forge | 8 fer, 1 or, 1 rubis | 24 secondes |
| Relique ouvragée | Orfèvre | 12 pierres, 2 or, 1 émeraude, 1 rubis | 24 secondes |

La difficulté doit surtout venir des chaînes et de la rareté. Ne pas imposer un travail continu excessivement long : le catalogue actuel mentionne déjà le risque de jeûne de l'artisan. Utiliser les mécanismes de pause et reprise existants.

Prix proposé : arrondir au supérieur la somme des coûts de référence des intrants, augmentée du travail calculé avec la conversion existante, puis multiplier par 2. Figer ce prix dans la commande à son ouverture, l'afficher au joueur et l'utiliser exactement à la livraison. Donner aux trois produits un coût de référence cohérent avec leur recette. Vérifier par scénario que la récompense couvre effectivement l'achat des matières rares ; ajuster ces constantes sur mesure plutôt que supposer la rentabilité.

Ne pas indexer le paiement sur les coûts observés après fabrication : cela permettrait de récompenser davantage un travail inefficace. Ne pas créer un deuxième système de prix intercolonial.

## 4. Cycle d'une commande

1. Lors de la revue quotidienne, une colonie hors crise et connaissant la métallurgie peut prendre une commande. Une seule par colonie, sans tirage aléatoire supplémentaire.
2. Choisir un établissement actif non camp où la chaîne est réalisable. Privilégier les modèles dont les matériaux rares sont en stock, dans des gisements connus non épuisés ou proposés par un fournisseur connu et encore pertinent. Réutiliser les vérifications d'accessibilité des offrandes quand elles conviennent ; ne pas déduire l'accessibilité de la carte entière non explorée.
3. Si plusieurs modèles conviennent, choisir celui dont les intrants manquants sont les moins coûteux ; départager dans un ordre stable. Ne pas exiger l'atelier final déjà construit : la commande doit pouvoir motiver sa construction autonome.
4. Figer identifiant, modèle, établissement, date et récompense. Décrire dans le journal la commande acceptée.
5. État actif : obtenir les matières, construire les ateliers nécessaires et produire. Les commandes passent après la survie, les outils essentiels et les besoins ordinaires. Inclure ces trois produits dans le contingent existant d'artisans consacrés au confort.
6. À la fin effective de la recette, enregistrer un événement de production et écrire au journal « [Colonie / établissement] a fabriqué une couronne sertie pour votre commande. Remise en attente : N pièces. » Aucun paiement tant que l'objet est porté.
7. À une cadence horaire de simulation, remettre un objet depuis le stock disponible de l'établissement désigné : retrait de l'objet, ajout exact de la récompense, mise à jour du registre monétaire et clôture de la commande dans la même opération. Écrire « Couronne sertie remise : N pièces versées. »
8. Attendre sept jours de jeu après la remise avant une nouvelle commande. Au plus une remise par colonie et par période de sept jours ; sauvegarder cette échéance. Ce délai et le contingent d'artisans bornent le débit initial. Mesurer l'émission totale avant d'ajouter un plafond mondial plus complexe.

Une crise suspend le lancement de nouveaux travaux de prestige et les achats non essentiels, mais n'efface ni les matières engagées ni le produit déjà fabriqué. Un objet déjà disponible peut être remis et payé pendant la crise. Reprendre ensuite la même commande. Ne pas remettre à zéro le délai par chargement, transfert de capitale ou schisme : une commande conserve un propriétaire unique, ne se duplique pas, et les héritiers du schisme conservent au minimum l'échéance de renouvellement existante.

Si le site disparaît ou ses approvisionnements deviennent durablement inaccessibles, expliquer le blocage. Réévaluer quotidiennement, puis abandonner une commande sans fabrication en cours après sept jours consécutifs de blocage prouvé et revenir au choix de modèle. Une fabrication engagée ou un objet porté conserve sa commande jusqu'à restitution ou livraison ; ne pas le supprimer pour changer de modèle. Ne pas annuler pour le seul manque de pièces ou de matières que les colonies sont en train de réunir.

L'état minimal peut être une commande persistante attachée à la colonie avec son établissement, ses dates et son état, plus des compteurs cumulatifs de remises. Éviter un moteur générique de contrats et une nouvelle logistique du joueur. La remise au dépôt représente directement la vente au joueur.

## 5. Faire réellement remonter la demande

- Objectif du produit fini : exactement un objet pour la commande active, corrigé du stock, des produits portés et des lots en cours ou suspendus. Ne pas démarrer deux fabrications pour une commande.
- Objectifs des intrants : quantités manquantes de la recette active. Retirer la demande des intrants déjà engagés physiquement pour éviter un double approvisionnement pendant le travail.
- Propager or → minerai d'or → combustible, et fer → minerai de fer → combustible → bois de charbonnage. Réutiliser les recettes et conversions du jeu.
- Additionner les besoins réellement simultanés (outils, bijoux, frappe, offrandes et commande), sans compter deux fois les mêmes matières. Éviter toute récursion circulaire entre `Economy.Need`, `ExtendedIndustry.Target` et `ToolChain.Demand`.
- Une commande d'armure doit alimenter la fusion et le charbon même lorsque tous les habitants ont des outils. Revérifier les choix de travaux et les décisions de construction, pas seulement les prix.
- Les manques de matières rares doivent passer par le commerce, la prospection, l'extraction et la logistique territoriale existants. Respecter découvertes, solvabilité, disponibilité des fournisseurs, stocks réservés et nourriture des voyageurs.
- Les produits finis sont dédiés au joueur dans cette première version : ne pas les ajouter automatiquement à `Economy.Tradable`. Leurs intrants sont déjà échangeables.
- Ne pas demander toutes les gemmes pour chaque modèle : seul le modèle commandé ouvre le besoin de ses pierres précises.

Points à relire avec les fonctions précédentes : `Trade.cs` et ses fichiers de logistique, `Prospection.cs`, `ExpansionPlanner.cs`, `LogisticsPlanner.cs`. Suivre le trajet d'une commande jusqu'au chantier et à l'artisan avant de considérer cette étape terminée.

## 6. Monnaie et conservation

Ajouter au registre monétaire un total cumulé explicite des paiements du joueur. Nouvelle identité :

`masse présente = dotations + frappe + paiements du joueur − pertes comptabilisées`.

Les pièces reçues entrent dans le stock de l'établissement vendeur et utilisent les flux économiques adaptés. Le retrait du produit utilise le flux de vente ; ce produit sort de l'économie simulée vers le joueur. Conserver le total des objets remis par modèle, sans créer d'inventaire interactif du joueur.

Le quota annuel existant de frappe reste distinct et continue de suivre sa règle actuelle. Comme cette règle dépend de la masse monétaire, les recettes du joueur pourront augmenter le quota lors du budget suivant : mesurer cet effet cumulé dans le bilan.

Garanties : pas de pièce au lancement ou à l'achèvement seul, pas de remise depuis un stock réservé, pas de retrait partiel sans paiement, pas de deuxième paiement au même tick ou après rechargement. Traiter aussi décès, interruption et restitution d'intrants via les chemins actuels.

## 7. Notifications et interface

- La simulation émet un fait typé à l'achèvement, avec identifiant stable, colonie, établissement, modèle, quantité et date. L'affichage n'invente ni la production ni le paiement.
- Rendre ces faits consultables dans un historique borné de simulation, avec séquence croissante. Le journal peut garder le texte lisible ; ne pas analyser ses phrases pour détecter une fabrication.
- Côté jeu, lire les nouveaux faits de toutes les colonies et réutiliser `Notify`. Une file conserve chaque notification pendant cinq secondes, y compris si plusieurs objets sont terminés au même tick ; ne pas écraser une alerte déjà visible.
- Au chargement ou à l'ouverture d'une partie, initialiser le curseur au dernier fait existant : aucun ancien achèvement ne réapparaît comme nouvelle alerte. L'historique et le journal restent consultables. Vider les notifications de l'ancienne partie lorsque le monde change.
- La pause arrête la production mais laisse le joueur lire les alertes ; le délai d'affichage utilise le temps réel. Fonctionner aussi à vitesse accélérée et lorsque la carte ou un panneau est ouvert.
- Notification de fabrication : nom de la colonie et de l'objet, quantité, remise encore en attente. Notification de remise : paiement réellement effectué. Ne pas annoncer un gain anticipé comme déjà acquis.
- Dans le panneau économique existant, montrer la commande, les intrants acquis/manquants, la récompense, le blocage éventuel et le délai de renouvellement. Montrer séparément le cumul de pièces reçu du joueur et les objets remis.
- Ajouter des noms français et des icônes procédurales distinctes aux nouveaux objets. Respecter le cahier graphique ; aucun PNG nécessaire pour activer la mécanique.

## 8. Ordre d'implémentation

1. Relire les modifications présentes et les chemins ci-dessus ; relever l'état initial des tests appropriés et des mesures économiques, sans modifier les travaux d'autrui.
2. Ajouter modèles, ressources, recettes, coûts et état persistant minimal de commande. Enregistrer les types nécessaires dans `StateGraph`.
3. Ajouter le cycle déterministe, puis intégrer la remontée des besoins et les priorités de travail/construction. Vérifier un scénario local complet avant l'interface.
4. Ajouter remise atomique, comptabilité monétaire et historique d'événements d'achèvement. Vérifier stocks, intrants engagés, produits portés, pause et reprise.
5. Brancher notification globale, file d'affichage, panneau économique et secours des icônes. Contrôler visuellement.
6. Exécuter les validations, ajuster les constantes si les scénarios le justifient, puis documenter le fonctionnement final dans les sections concernées du guide du jeu et de la monnaie.

Ne pas considérer une phase terminée sur la seule présence d'une recette : une colonie doit décider et accomplir toute la chaîne de façon autonome.

## 9. Validation et critères d'acceptation

Tests ciblés à ajouter dans un fichier dédié aux commandes de prestige :

- Une commande crée exactement les besoins de son modèle et motive la chaîne de fer même sans manque d'outils ; absence de demande après remise ou pendant le délai.
- Production réelle des trois modèles avec consommation exacte des matières, en passant par les activités existantes. Aucun objet obtenu par simple incrément artificiel dans les scénarios d'intégration.
- Une commande ne lance qu'un lot en incluant travail suspendu et produit porté ; interruption, restitution et décès conservent la comptabilité.
- Fabrication : un seul fait d'achèvement par objet, journal renseigné, zéro paiement tant que le produit reste porté.
- Remise : retrait exact, récompense exacte, compteur augmenté une seule fois et `Money.Imbalance(world) == 0` dans un monde sans stocks monétaires modifiés artificiellement.
- Une colonie ne possédant pas la gemme peut l'acheter à un fournisseur connu, fabriquer, remettre et enrichir le fournisseur. Un scénario sans fournisseur accessible reste bloqué avec une raison, sans créer de matériau.
- Crise : survie prioritaire, aucun nouveau lot de prestige, reprise correcte, remise possible du produit déjà disponible.
- Plusieurs établissements, changement de capitale et schisme : une commande n'est ni multipliée ni payée deux fois.
- Aller-retour de sauvegarde avant engagement, pendant travail suspendu, pendant portage et après paiement ; continuation déterministe identique avec/sans rechargement. Aucun travail de migration des anciennes sauvegardes.
- Nouvelle ressource : vérifier les boucles sur `ResourceType` qui pourraient décaler les tirages. Préserver les générateurs `Random`, `Chance` et `Politics`, sans changer de graines pour faire passer les tests.

Contrôles de jeu : fabrication dans une colonie non observée ; deux achèvements rapprochés restent tous deux lisibles ; pause/vitesse accélérée ; aucune alerte ancienne rejouée après chargement ; encart sans débordement et icônes lisibles en secours. Étendre le contrôle d'interface existant seulement si ces cas sont représentables par ses outils.

Exécuter `dotnet test Simulation.Tests -c Release` et `dotnet build Game/GodColony.csproj` avant le contrôle visuel Godot. Comparer en Debug si un échec est suspect ; aucune fusion ni aucun commit dans cette demande.

Changement économique important : mesurer 15 parties, garder les neuf graines de `GrowthTests` intactes et réaliser les mesures supplémentaires à part. Inclure des scénarios avec plusieurs colonies et des ressources rares réellement accessibles, car un test de croissance seul ne garantit pas l'activation de la nouvelle filière. Comparer scénario initial et scénario modifié ; ne lire que les résumés des mesures supplémentaires.

Résumé attendu : population, famine suivie par `StarvationWatch`, première commande/production/remise, commandes bloquées et motifs, échanges de matières rares, volume et circulation des pièces, paiements du joueur, frappe et pertes, bilan monétaire. Contrôler que les objets sont effectivement produits et que la survie ne se dégrade pas. Ne pas annoncer une économie stimulée si seules les réserves de pièces augmentent sans davantage d'échanges.

Livraison de l'agent codeur : fichiers modifiés, comportement obtenu, résultats des tests et mesures, captures du panneau et des notifications, limites observées et constantes finalement retenues. Préserver tous les autres changements présents dans le dossier partagé.
