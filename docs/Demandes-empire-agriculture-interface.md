# Changements demandés — empire, agriculture et interface

Document de transmission à un agent chargé de réaliser les changements dans GodColony.

## Objectif et statut

Faire évoluer le jeu autour de cinq demandes : de grands champs, une terminologie « empire / colonies », la suppression des tombes, une interface mieux organisée et des céréales utilisées surtout comme matière première.

Les exigences ci-dessous viennent de l'utilisateur. Les propositions sont explicitement identifiées : elles servent à préciser les idées, sans constituer des choix déjà approuvés. Ce document demande une mise en œuvre ; il ne constate pas que ces changements sont déjà réalisés.

## 1. De vrais grands champs

### Résultat attendu

Les habitants doivent aménager de grandes surfaces agricoles, visibles comme de véritables champs à l'échelle du village. Agrandir seulement les images ne suffit pas : la surface cultivable doit exister dans la simulation.

L'implantation et le développement restent autonomes. Les habitants choisissent les emplacements selon leurs besoins, le terrain et leurs capacités de travail ; le joueur observe leur organisation.

### Travail à réaliser

- Faire évoluer les dimensions ou l'organisation des champs pour permettre des surfaces nettement supérieures aux petits carrés actuels.
- Adapter la recherche d'emplacement, les accès, les chemins, le travail agricole et le rendu aux surfaces réelles.
- Conserver les contraintes de terrain et les effets agricoles existants ; vérifier que les habitants peuvent effectivement semer et récolter ces surfaces.
- Équilibrer le développement agricole avec la main-d'œuvre disponible, sans multiplier gratuitement la production.

### Choix confirmé : évolution progressive

Les champs s'agrandissent progressivement avec le développement de la colonie, ses besoins et ses capacités de travail. Un petit campement ne reçoit pas immédiatement une très grande surface agricole. Les dimensions exactes et la forme restent à choisir dans ce cadre.

### Critères de réussite

Les champs évoluent progressivement : un village développé possède des champs clairement plus vastes que ceux d'un petit campement, avec des parcelles réellement cultivées. Leur implantation reste praticable et leur exploitation ne bloque pas l'organisation du village.

## 2. Empire et colonies : vocabulaire et navigation

### Résultat attendu

Le terme **empire** désigne désormais l'ensemble qui était appelé « colonie » dans la demande de l'utilisateur. Le terme **colonie** désigne uniquement un lieu habité : village ou campement. Un empire comprend sa colonie d'origine et peut comprendre plusieurs colonies secondaires.

L'interface propose un **menu déroulant des colonies de l'empire**. Cliquer sur une entrée conduit directement à la vue locale du lieu choisi.

### Travail à réaliser

- Uniformiser les libellés, titres, descriptions et messages concernés pour distinguer clairement l'empire du lieu observé.
- Afficher le nom de l'empire et celui de la colonie observée de manière lisible.
- Inclure dans le menu la colonie d'origine et les colonies secondaires appartenant à cet empire.
- Actualiser la liste quand les lieux apparaissent ou changent d'appartenance ; traiter clairement les lieux fermés, sans navigation invalide.
- Faire suivre le changement de lieu par la carte, les habitants, les stocks et les informations locales affichées.

### Attention au modèle actuel

Le code distingue déjà `Colony`, plusieurs `Settlement` par `Colony`, et un niveau `Realm` qui réunit plusieurs `Colony`. Il faut établir la correspondance avec les mots demandés avant de modifier le modèle. Ne pas assimiler automatiquement « empire » à `Realm` uniquement parce que cette classe représente aujourd'hui un royaume.

La demande ne fixe pas le nouveau nom du niveau « royaume », ni de nouvelles règles de gouvernement. Elle ne demande pas non plus de renommer systématiquement toutes les classes internes. Privilégier une correspondance cohérente avec les comportements actuels et expliquer toute évolution structurelle nécessaire.

### Critères de réussite

Depuis un empire possédant plusieurs lieux habités, le joueur choisit une colonie dans le menu et arrive sur sa vue locale. L'interface distingue les informations de l'empire et celles de la colonie sélectionnée.

## 3. Supprimer les tombes et conserver le nombre de décès

### Résultat attendu

Un habitant qui meurt ne génère plus de tombe. Aucune tombe n'apparaît sur la carte. Le nombre de morts reste visible **en haut à gauche de l'interface**, même si aucune tombe n'existe.

### Travail à réaliser

- Supprimer la création et le placement d'une tombe lors d'un décès, ainsi que son affichage sur la carte.
- Conserver le traitement normal de la mort : retrait de l'habitant vivant et conséquences existantes associées au décès.
- Enregistrer les décès indépendamment des tombes pour alimenter le compteur et les statistiques utiles.
- Remplacer les libellés « tombes » utilisés comme mesure de mortalité par des libellés « décès » ou « morts ».
- Sauvegarder les données de mortalité nécessaires dans le format actuel.

### Choix confirmé : cumul de l'empire

Afficher le cumul des décès de l'empire depuis le début de la partie, avec une indication explicite de ce périmètre. Changer de colonie observée au sein du même empire ne change pas ce cumul. Le détail local peut rester disponible dans les statistiques.

### Critères de réussite

Un décès ne crée aucun objet de tombe ni aucun marqueur sur le terrain. Le compteur cumule correctement les décès de l'empire depuis le début de la partie, reste visible en haut à gauche lors d'un changement de colonie et conserve sa valeur après sauvegarde et chargement.

## 4. Revoir l'interface et regrouper les minéraux

### Résultat attendu

Réorganiser globalement les informations affichées pour rendre l'observation du jeu plus claire. Les minéraux doivent être regroupés au lieu d'être dispersés parmi les autres ressources.

### Travail à réaliser

- Réexaminer la hiérarchie des informations : empire, colonie observée, population, décès, réserves et ressources.
- Créer un regroupement identifiable pour les ressources minérales, avec un ordre cohérent.
- Donner accès aux quantités détaillées sans surcharger l'affichage permanent.
- Distinguer les réserves alimentaires des matières premières agricoles ; présenter les céréales comme une matière première consommable seulement en dernier recours, avec leur très faible apport nutritif.
- Rendre explicite le périmètre des chiffres : local ou empire. Les stocks doivent rester ceux réellement simulés.

### Proposition d'organisation, à ajuster

Un bloc principal présente le lieu observé et les indicateurs essentiels ; le menu déroulant permet de changer de colonie. Les ressources sont réparties entre nourriture, cultures, matériaux, minéraux et produits transformés. Dans le groupe minéral, distinguer au besoin minerais bruts, métaux et pierres précieuses.

La liste exacte des éléments permanents, le classement de la pierre, de l'argile, du sel et des combustibles, ainsi que la disposition graphique restent à définir. La demande ne prescrit pas une maquette précise.

### Critères de réussite

Les minéraux se consultent dans un groupe commun. Population, décès et état alimentaire se lisent rapidement. Les quantités détaillées restent accessibles et reflètent le lieu ou l'ensemble annoncé.

## 5. Céréales : matière première et nouveaux usages

### Résultat attendu

Les céréales doivent devenir surtout une matière première à transformer. Elles restent consommables directement en dernier recours, avec une valeur nutritive très fortement réduite. En contrepartie, ajouter de nouvelles manières de les utiliser.

### Choix confirmé : consommation de dernier recours

Conserver une consommation directe des céréales uniquement en dernier recours, lorsque les autres aliments disponibles ne permettent pas de se nourrir. Leur valeur nutritive doit être très faible, mais supérieure à zéro. Les produits transformés restent nourrissants. La valeur exacte reste à choisir et à vérifier lors de l'équilibrage.

### Travail à réaliser

- Adapter la consommation et les décisions des habitants pour privilégier les autres aliments et ne manger des céréales brutes qu'en dernier recours.
- Adapter le calcul des réserves, les provisions de voyage, les caravanes et le ravitaillement : compter les céréales selon leur très faible valeur nutritive réelle, sans les assimiler à des repas ordinaires.
- Rendre les transformations alimentaires accessibles assez tôt pour que les habitants puissent survivre de manière autonome.
- Réexaminer les réserves gardées pour les recettes et les priorités de production avec la nouvelle valeur alimentaire.
- Ajouter de nouveaux usages réellement pris en charge par la simulation : consommation d'intrants, travail nécessaire, résultat ou effet, et décisions autonomes des habitants.
- Mettre à jour les descriptions et les affichages alimentaires.

### Usages existants et propositions nouvelles

Le jeu utilise déjà les céréales pour la filière farine/pain, la bière et le ragoût. Ces usages doivent être adaptés et valorisés ; les présenter comme de nouveaux usages ne répondrait pas à la demande.

Pistes nouvelles à choisir : alimentation du bétail avec un effet simulé, ou nouvelle préparation alimentaire à base de céréales transformées. Aucun choix précis ni nombre de nouvelles recettes n'a été fixé. Ces pistes sont des propositions, pas des exigences supplémentaires approuvées.

### Critères de réussite

Les habitants privilégient les autres aliments et ne consomment les céréales brutes qu'en dernier recours, pour un apport nutritif très faible mais non nul. Les réserves et les provisions ne surestiment pas la nourriture disponible. Les habitants utilisent les transformations et les nouveaux débouchés de façon autonome, sans famine systématique provoquée par une filière inaccessible.

## Ordre de réalisation conseillé

1. Fixer la correspondance empire/colonie et les choix ouverts qui conditionnent l'implémentation.
2. Supprimer les tombes en préservant les données de décès.
3. Adapter ensemble les champs, l'alimentation et les transformations des céréales, puis vérifier l'équilibrage.
4. Finaliser l'interface, les regroupements de ressources et la navigation entre colonies.

## Repères dans le code actuel

Repérage effectué lors de la préparation du document ; relire ces fichiers avant toute modification, car le dossier contient des travaux en cours.

| Sujet | Points de départ |
| --- | --- |
| Dimensions des champs | `Simulation/Colonies/Field.cs` : `Field.Size` vaut actuellement 4, soit 16 parcelles. Rechercher ensuite les usages de cette dimension. |
| Empire et lieux habités | `Simulation/Colonies/Colony.cs`, `Settlement.cs`, `Realm.cs`, `Simulation/WorldState.Settlements.cs` |
| Navigation locale | `Game/Scripts/Main.cs` : `ObserveSettlement` / `ObserveLocation` ; `Game/Scripts/View/TerritoryOverview.cs` |
| Mortalité et tombes | `Simulation/Colonies/Lifecycle.cs`, `Settlement.cs`, `Game/Scripts/View/ColonistsView.cs`, `Game/Scripts/Hud.cs`, `StatsPanel.cs` |
| Céréales et nutrition | `Simulation/Colonies/Stockpile.cs` : `GrainMealValue` vaut actuellement 0,6 ; `ResourceCatalog.cs` inclut les céréales dans `TravelFood` ; `Cuisine.cs` contient des recettes et des réserves de céréales. |
| Présentation des ressources | `Game/Scripts/Hud.cs`, `Game/Scripts/View/ResourceIcons.cs`, `Simulation/Colonies/ResourceCatalog.cs` |

## Consignes pour l'agent et validation

Respecter `AGENTS.md`, préserver les modifications des autres assistants et ne faire aucun commit sans demande. Garder les règles dans `Simulation/`, le rendu et l'interface dans `Game/`, et le rendu procédural de secours opérationnel.

Tester les comportements modifiés : grands champs exploitables, navigation entre lieux, comptage des décès sans tombe, consommation et transformation des céréales. Vérifier l'aller-retour de sauvegarde du format actuel ; aucune migration d'anciennes sauvegardes n'est demandée.

Exécuter `dotnet test Simulation.Tests` pour les changements de simulation et `dotnet build Game/GodColony.csproj` avant le contrôle visuel dans Godot. Pour l'ensemble agriculture/alimentation, prévoir 15 parties sur des graines distinctes compte tenu de l'ampleur, conserver les neuf graines de `GrowthTests` et réaliser les mesures supplémentaires à part. Utiliser `StarvationWatch` pour observer les famines et fournir un résumé des résultats.

Dans le compte rendu final, distinguer les demandes réalisées, les choix retenus pour les points ouverts et les limites éventuelles.
