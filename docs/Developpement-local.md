# Lancement local et contrôles Godot

Compiler d'abord `Game/GodColony.csproj` : Godot charge la DLL compilée.

Exécutable trouvé sur cette machine lors du nettoyage : vérifier son existence avant utilisation.

```powershell
$godotExe = 'E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build Game/GodColony.csproj
& $godotExe --path Game --headless --fixed-fps 60 -- --smoke-menu
& $godotExe --path Game --headless --fixed-fps 60 -- --smoke-saves
```

Pour une vérification visuelle, retirer `--headless`. `--smoke-captures=<dossier-existant>` enregistre les étapes. Les préférences de `user://settings.cfg` peuvent modifier le plein écran : vérifier le rendu obtenu.

Les options de capture et de mesure sont décrites dans [Guide-jeu.md](Guide-jeu.md), section « Options de développement » ; leur implémentation actuelle se trouve dans `Game/Scripts/Main*.cs`.
