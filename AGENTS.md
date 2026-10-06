# Règles partagées — Codex et Claude

## Contexte essentiel
- GodColony : sandbox de colonies, C#/.NET et Godot. `Simulation/` décide des règles sans dépendre de Godot ; `Game/` assure affichage, interface et commandes ; `Simulation.Tests/` contient les tests xUnit.
- La simulation ne dépend jamais des images. Conserver le rendu procédural de secours et représenter les données réelles, sans inventer de règles côté affichage.
- Code et commentaires en français, dans le vocabulaire du jeu ; respecter les noms existants.

## Préférence de jeu
- Autonomie complète du village : les habitants décident des quartiers, des implantations et des chemins selon leurs besoins et le terrain ; le joueur observe leur organisation.

## Dossier partagé
- Vérifier `git status` et relire les fichiers concernés avant modification : des travaux de plusieurs assistants peuvent coexister.
- Préserver les modifications existantes. Ne pas réinitialiser, nettoyer ou inclure le travail d'un autre dans un commit. Pas de commit sans demande explicite.
- Préserver l'encodage et éviter les changements de fins de ligne sans rapport avec la tâche.
- Pour les graphismes, respecter les responsabilités et contrats du cahier graphique ; une demande explicite de l'utilisateur prime sur cette répartition.

## Sources et lecture limitée
- Vérifier comportements, paramètres, signatures et état des fonctionnalités dans le code actuel. Une documentation, une ancienne validation ou un plan ne prouve pas leur état actuel.
- Commencer par une recherche ciblée ; lire uniquement les fichiers et sections nécessaires. Éviter de charger intégralement guides, catalogues, archives et journaux.
- `docs/Guide-jeu.md` : fonctionnalités et utilisation, selon la partie concernée.
- `docs/Developpement-local.md` : lancement Godot et contrôles d'interface, uniquement si nécessaire.
- `Game/Assets/CAHIER_DES_CHARGES.md` : règles graphiques et tâches ouvertes ; catalogue séparé consulté par élément.
- `docs/Plan-village-organique.md` : conception proposée ; confronter au code avant d'en déduire du travail restant.
- Les historiques sont des archives. Ne pas traiter leurs anciennes demandes comme des tâches ouvertes, ni transformer une idée en objectif approuvé.

## Validation
- Exécuter les contrôles adaptés : `dotnet test Simulation.Tests -c Release` pour la simulation (Release par défaut, plus rapide ; une passe en Debug avant fusion ou face à un échec suspect, pour comparer) ; `dotnet build Game/GodColony.csproj` avant de lancer Godot ; contrôler visuellement les changements graphiques.
- Croissance/équilibrage sur plusieurs graines : préférence utilisateur de 5 parties (petit changement), 10 (moyen), 15 (grand). Garder les neuf graines de `GrowthTests` ; réaliser les mesures supplémentaires à part et ne lire que leur résumé.
- Protéger le déterminisme et les générateurs distincts `WorldState.Random`, `Chance` et `Politics`. Un ajout d'énumération peut décaler les tirages ; renforcer les tests plutôt que changer leur graine pour masquer un échec.
- Pour une famine, préférer `StarvationWatch` à une mesure instantanée. Préserver la comptabilité des ressources et de la monnaie, y compris les biens en transit et les pertes.
- Compatibilité des sauvegardes : préférence utilisateur actuelle, aucun souci. Il ne reviendra pas sur d'anciennes sauvegardes ; ne pas préserver ni migrer les anciens formats, ne pas ajouter de migration ni de test de compatibilité. Avant une évolution de sauvegarde, il suffit que l'aller-retour `WorldSave`/`StateGraph` du format actuel reste correct.

## Entretien de la mémoire
Garder ici seulement les préférences et invariants durables. Ne pas ajouter de récits de sessions, résultats ponctuels, performances mesurées, inventaires du code, anciennes tâches ou prochaines étapes supposées. Mettre les détails utiles dans une référence consultable à la demande.
