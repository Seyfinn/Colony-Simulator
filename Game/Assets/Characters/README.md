# Apparences des peuples

Les elfes, orques et nains sont prêts dans `Scripts/View/PeoplesSprites.cs` et `PeopleCostumes.cs`.
Ils utilisent des canevas transparents de **32 × 32 pixels**, avec les pieds au centre du bord inférieur.
Les silhouettes gardent leurs proportions : elfes élancés, orques larges, nains courts et trapus.

## Utilisation dans le jeu

```csharp
var biome = BiomeVisuals.At(colony.Map, colonist.TileX, colonist.TileY);
ImageTexture[] walkingFrames = SpriteFactory.Colonist(colonist, biome);
var appearance = PeoplesSprites.Describe(colonist, biome);
ImageTexture portrait = PeoplesSprites.Portrait(appearance);
ImageTexture sleeping = PeoplesSprites.Rest(appearance);
```

L'espèce vient de `Colonist.Species`, l'âge de `Colonist.Stage`, les accessoires de `Colonist.Sector`.
La sélection suit la personne, même lorsqu'elle rejoint une colonie d'un autre peuple.
L'apparition de nouvelles colonies ne nécessite aucun branchement supplémentaire dans les visuels.
Cette préparation ne modifie ni le nombre de colonies au démarrage ni les règles de simulation.

Pour dessiner un personnage debout, utiliser `(-texture.GetWidth() / 2f, -texture.GetHeight())`
depuis la position des pieds, avec le filtre `Nearest`.
Les anciens humains de 16 × 24 sont conservés ; les portraits normalisent tous les peuples sur un canevas de 32 pixels.
`ColonistsView` adapte déjà les ombres, les chargements, les bulles et la pose de sommeil à la silhouette.

## Variantes disponibles

- 24 apparences par peuple, avec des combinaisons de peau, cheveux, coiffure et ornements.
- Les deux sexes, les enfants, adolescents, adultes et anciens. Les enfants sont affichés à 70 % dans le monde.
- Quatre étapes du cycle de marche, plus une pose au repos avec les yeux fermés.
- Cinq milieux : plaine tempérée, terres sèches, forêt fraîche, altitude, berges humides.
- Cinq équipements : quotidien, champs, forêt, pierre/construction, ateliers. Les enfants gardent leur tenue quotidienne.
- Les pointes d'oreilles, défenses et barbes restent visibles avec les accessoires de métier.

Pour préparer un aperçu sans modifier le moteur :

```csharp
var frames = SpriteFactory.Colonist(7, PeopleLook.Elf, WoodlandBiome.Highland,
    LifeStage.Adult, Sex.Female, OutfitTrade.Woodland);
```

Les textures sont créées au premier usage et réutilisées. Les 24 apparences répétables limitent la croissance du cache
pour les peuples fantastiques ; la génération ne consomme aucun hasard de la simulation.
