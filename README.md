# Lumion

Application mobile de signalement communautaire des coupures d'électricité à Douala.
Examen pratique — Atelier de développement mobile, Niveau 5 Génie Logiciel, ENSPD, 2026/2027.

Les habitants signalent une coupure, le quartier passe à l'orange puis au rouge sur la carte dès
que trois habitants la confirment, les abonnés sont notifiés, et l'historique alimente des
statistiques mensuelles par quartier.

## Structure

| Projet | Rôle |
| --- | --- |
| `Lumion` | Application .NET MAUI (.NET 9) : écrans XAML, modèles de vue, carte, graphiques |
| `Lumion.Core` | Cœur métier sans dépendance à MAUI : modèles, base SQLite, règles de gestion RG01 à RG14, statistiques |
| `Lumion.Tests` | Tests xUnit des critères d'acceptation CA01 à CA19 du cahier des charges |

## Lancer

```bash
dotnet test Lumion.Tests
```

```bash
dotnet build Lumion -t:Run -f net9.0-windows10.0.19041.0
```

```bash
dotnet build Lumion -t:Run -f net9.0-android "-p:AndroidSdkDirectory=C:\Program Files (x86)\Android\android-sdk"
```

Le paramètre `AndroidSdkDirectory` n'est nécessaire qu'en ligne de commande sur ce poste, où
`ANDROID_HOME` pointe vers un SDK sans la plateforme API 35. Visual Studio utilise déjà le bon.

## Comptes de démonstration

La connexion se fait par numéro de téléphone et code à 6 chiffres. Aucun fournisseur SMS n'est
branché : le code est affiché à l'écran en mode démonstration.

| Rôle | Numéro |
| --- | --- |
| Administrateur (Alex Tchatchoua) | 6 00 00 00 01 |
| Modérateur (Marie Nkodo) | 6 00 00 00 02 |
| Habitant (Jean P. Ekwalla) | 6 00 00 00 05 |

Au premier lancement, la base est remplie avec 20 quartiers, 55 utilisateurs et 12 mois de
coupures. Bonamoussadi est en coupure confirmée, Deido à un signalement de la confirmation,
Makepe sous délestage annoncé. Administration → Export → « Réinitialiser la démo » remet cet état à neuf.

## Limites connues

- Les données sont locales à l'appareil (SQLite). Il n'y a pas encore de serveur : deux téléphones
  ne partagent pas leurs signalements, et les notifications sont locales. Les services sont isolés
  dans `Lumion.Core` pour pouvoir brancher une API plus tard.
- Les périmètres des quartiers sont des approximations générées autour d'un point central ; un
  administrateur peut les ajuster.
- Les données de démonstration sont fictives et ne décrivent pas la situation réelle des quartiers.
- L'interface n'existe qu'en français ; la version anglaise n'est pas traduite.
