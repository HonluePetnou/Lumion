# Lumion

Application mobile de signalement communautaire des coupures d'électricité à Douala.
Né comme examen pratique (Atelier de développement mobile, Niveau 5 Génie Logiciel, ENSPD, 2026/2027).

Les habitants signalent une coupure, le quartier passe à l'orange puis au rouge sur la carte dès
que trois habitants la confirment, les abonnés sont notifiés, et l'historique alimente des
statistiques mensuelles par quartier.

## Structure

| Projet | Rôle |
| --- | --- |
| `Lumion` | Application .NET MAUI (.NET 9) : écrans XAML, modèles de vue, carte, graphiques, thèmes |
| `Lumion.Api` | Serveur ASP.NET Core : API REST `/api/v1`, base SQLite côté serveur, tâche planifiée |
| `Lumion.Core` | Cœur métier sans dépendance à MAUI : modèles, règles RG01 à RG14, statistiques, routes de l'API |
| `Lumion.Tests` | Tests xUnit des critères d'acceptation CA01 à CA19 du cahier des charges |

L'application ne lit jamais la base directement : elle passe par l'API, soit le serveur
`Lumion.Api`, soit un **serveur embarqué** qui exécute les mêmes routes dans l'application
(mode autonome, sans réseau). Le choix se fait dans Profil → Connexion.

Hors ligne, les dernières réponses sont lues en cache et les signalements attendent dans une file
envoyée au retour du réseau.

## Lancer

Le serveur (écoute sur le port 5080, toutes interfaces) :

```bash
dotnet run --project Lumion.Api
```

L'application sous Windows, qui se connecte par défaut à `http://localhost:5080` :

```bash
dotnet build Lumion -t:Run -f net9.0-windows10.0.19041.0
```

L'application sous Android (émulateur : `http://10.0.2.2:5080` ; téléphone : l'adresse IP du PC
sur le même Wi-Fi, à saisir dans Profil → Connexion) :

```bash
dotnet build Lumion -t:Run -f net9.0-android "-p:AndroidSdkDirectory=C:\Program Files (x86)\Android\android-sdk"
```

Le paramètre `AndroidSdkDirectory` n'est nécessaire qu'en ligne de commande sur ce poste, où
`ANDROID_HOME` pointe vers un SDK sans la plateforme API 35.

Les tests :

```bash
dotnet test Lumion.Tests
```

## Comptes de démonstration

La connexion se fait par numéro de téléphone et code à 6 chiffres. Aucun fournisseur SMS n'est
branché : le code est affiché à l'écran en mode démonstration (et dans la console du serveur).

| Rôle | Numéro |
| --- | --- |
| Administrateur (Alex Tchatchoua) | 6 00 00 00 01 |
| Modérateur (Marie Nkodo) | 6 00 00 00 02 |
| Habitant (Jean P. Ekwalla) | 6 00 00 00 05 |

Au premier lancement, la base est remplie avec 20 quartiers, 55 utilisateurs et 12 mois de
coupures. Bonamoussadi est en coupure confirmée, Deido et Bépanda à confirmer, Makepe sous
délestage annoncé. Administration → Export → « Réinitialiser la démo » remet cet état à neuf.

## Interface

- Maquettes : fichier Figma « Lumion — Redesign », page « Écrans v2 » (clair et sombre).
- Thèmes clair et sombre (Profil → Apparence : Clair, Sombre ou Auto). Les couleurs sont dans
  `Lumion/Resources/Styles/ThemeClair.xaml` et `ThemeSombre.xaml`, avec les mêmes clés.
- Police Plus Jakarta Sans, sous licence SIL Open Font License (`Lumion/Resources/Fonts/OFL.txt`).
- Fond de carte : tuiles OpenStreetMap (© contributeurs OpenStreetMap), atténuées selon le thème.

## Limites connues

- Pas encore de serveur en ligne : en mode serveur, le PC qui exécute `Lumion.Api` doit être
  joignable par le téléphone. Les notifications sont produites par l'application (pas de push).
- La politique d'usage des tuiles OpenStreetMap limite leur emploi à un faible trafic : pour une
  diffusion large, il faudra un fournisseur de tuiles (ou un serveur de tuiles) dédié.
- Les périmètres des quartiers sont des approximations autour d'un point central ; un
  administrateur peut les ajuster.
- Les données de démonstration sont fictives et ne décrivent pas la situation réelle des quartiers.
- L'interface n'existe qu'en français.
