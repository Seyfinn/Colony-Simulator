# GodColony

Sandbox où plusieurs peuples vivent, commercent et grandissent, et où le joueur intervient en dieu.

- `Simulation/` : règles et état du monde, C# sans Godot.
- `Game/` : projet Godot .NET, affichage et interface.
- `Simulation.Tests/` : tests xUnit.
- `Game/Assets/` : illustrations et contrats graphiques.

## Lancer

```powershell
dotnet test Simulation.Tests
dotnet build Game/GodColony.csproj
```

Ouvrir `Game/project.godot` dans Godot .NET. La version du SDK figure dans `Game/GodColony.csproj`.

## Références à consulter selon le besoin

- [Règles partagées des assistants](AGENTS.md).
- [Guide du jeu et options de développement](docs/Guide-jeu.md).
- [Lancement local et contrôles Godot](docs/Developpement-local.md).
- [Cahier graphique et tâches ouvertes](Game/Assets/CAHIER_DES_CHARGES.md).
- [Conception du village organique](docs/Plan-village-organique.md) : propositions à confronter à l'implémentation.

Le code actuel fait foi pour les comportements et paramètres.
