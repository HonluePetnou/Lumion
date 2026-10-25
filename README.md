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

### Déploiement HTTPS

Les versions Release utilisent `https://31.97.76.16` par défaut et refusent toute
adresse HTTP réseau avant d'envoyer une requête. Une autre adresse HTTPS peut être
saisie dans Profil → Connexion. HTTP reste disponible dans les builds Debug pour
le développement local ; le mode autonome ne passe pas par le réseau.

Avant de déployer avec Docker Compose, fournir un certificat PFX signé par une
autorité reconnue par les appareils, contenant sa clé privée et sa chaîne de
certification. Son identité doit correspondre à l'adresse utilisée : l'IP
`31.97.76.16` pour l'adresse par défaut, ou le domaine configuré dans l'application.
Définir `LUMION_CERT_PATH` (chemin absolu du fichier sur l'hôte) et
`LUMION_CERT_PASSWORD` dans l'environnement du déploiement, puis lancer
`docker compose up -d`. Ne pas enregistrer le certificat ni son mot de passe dans Git.
Le port HTTPS public est 443 ; le port HTTP 5080 est limité à la boucle locale du
serveur. Sans certificat valide, la connexion Release reste indisponible : aucun
repli HTTP ni contournement de validation du certificat n'est prévu.

### Données hors ligne

Le cache est isolé par serveur et session, et la file des signalements par serveur
et compte. Une reconnexion au même compte permet de reprendre les envois ; les
erreurs temporaires (session expirée, suspension, limite horaire, panne serveur)
conservent les signalements en attente. Les refus définitifs ne sont pas comptés
comme des envois réussis.

Les anciens fichiers `api/` et `signalements-en-attente.json` ne sont plus lus.
Ils restent sur l'appareil, mais ne peuvent pas être migrés automatiquement car
ils ne permettent pas d'identifier leur serveur et leur propriétaire. Les nouvelles
données utilisent `api-v2/` et `signalements-v2/`.

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

- En mode serveur, l'API doit être joignable par le téléphone (HTTPS en Release).
  Les notifications sont produites par l'application (pas de push).
- La politique d'usage des tuiles OpenStreetMap limite leur emploi à un faible trafic : pour une
  diffusion large, il faudra un fournisseur de tuiles (ou un serveur de tuiles) dédié.
- Les périmètres des quartiers sont des approximations autour d'un point central ; un
  administrateur peut les ajuster.
- Les données de démonstration sont fictives et ne décrivent pas la situation réelle des quartiers.
- L'interface n'existe qu'en français.
