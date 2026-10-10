# SmartPlanif — Allocation Bus / Chauffeur / Receveur (C# / .NET 8, Clean Architecture, Blazor)

Allocation de bus, chauffeurs et receveurs aux voyages avec **contraintes choisies par l'utilisateur**
(désactivée / dure / souple) et **heuristique + métaheuristique** (construction par phases + ALNS avec recuit simulé).

## Démarrage
```powershell
dotnet build
dotnet test
dotnet run --project src\SmartPlanif.Web        # http://localhost:5080
```
Sans chaîne de connexion, l'application fonctionne en **mode démonstration** (36 voyages générés par jour, 12 bus, 28 agents).
Pour MySQL : exécuter `sql/001_allocation_tables.sql`, puis renseigner dans `src/SmartPlanif.Web/appsettings.json`
`ConnectionStrings:TransportAi` et `Qualifications:Map` (valeur de `dragent.DECQUAL` -> `Driver` / `Receiver`).
ATTENTION : le commentaire de `DECQUAL` dans le dump est « Qualification (H=homme) » ; confirmez avec la société
quelle colonne distingue réellement chauffeur et receveur.

## Écrans
| Page | Rôle |
|---|---|
| Contraintes | Pour chaque règle : Désactivée / Dure / Souple + poids. Les impossibilités physiques (chevauchement, bus inactif) sont verrouillées. |
| Types de ligne | Catégories de bus autorisées (B06) et préférées (BP03) par type de ligne (ex. confort pour l'interurbain). |
| Paramètres | 8 h, amplitude, repos, remise en service, km... et réglages ALNS. Validation avant sauvegarde. |
| Allocation | Aperçu + diagnostic de faisabilité, choix de méthode, progression en direct, plan, causes d'échec, contrôle indépendant. |

Les réglages sont enregistrés dans `src/SmartPlanif.Web/App_Data/allocation-settings.json`.

## Architecture (les dépendances pointent vers le Domain)
```
Web (Blazor) -> Infrastructure (MySQL/Dapper, démo, JSON) -> Application (cas d'usage, ports) -> Domain
```
| Dossier Domain | Contenu |
|---|---|
| Entities, Scheduling | Voyage, bus, agent ; `ScheduleBook` (plannings, Clone/Remove pour la métaheuristique) |
| Rules | Règles dures `IHardRule<T>` et souples `ISoftRule<T>` (une classe par règle) |
| Constraints | `ConstraintCatalog` : liste de TOUTES les contraintes, modes supportés, poids par défaut. Point central d'extension |
| Allocation | `TripAllocator` (décision pour un voyage), constructions, `ObjectiveFunction`, `PlanValidator`, `FeasibilityAnalyzer` |
| Alns | `AlnsImprover`, opérateurs de destruction/réparation |

## Algorithme
1. **Diagnostic** : demande maximale simultanée vs ressources (bus par type de ligne, chauffeurs, receveurs, heures).
2. **Construction par phases** : types de ligne triés par tension (demande / bus compatibles) ; dans chaque phase, voyages les plus contraints d'abord ; plannings partagés entre phases.
3. **ALNS + recuit simulé** : détruire 5-15 % du plan, réparer (toujours faisable), accepter selon l'objectif ; poids des opérateurs adaptatifs ; seule la faisabilité dure est visitée.
4. **Passe de complétion** puis **contrôle indépendant** : un plan qui viole une règle dure n'est jamais enregistré.

Objectif (à minimiser) : `pénalité x voyages non affectés + déficit de score souple + poids x écart-type de charge`.
La pénalité domine : un meilleur score n'est jamais obtenu en perdant un voyage.

## Étendre
* **Nouvelle règle** : créer la classe (`Rules/Hard` ou `Rules/Soft`), l'ajouter dans `ConstraintCatalog` (une ligne), écrire un test. L'interface la propose automatiquement.
* **Nouvel opérateur ALNS** : implémenter `IDestroyOperator` / `IRepairOperator` et l'ajouter dans `AlnsImprover`.
* **Autre source de données** : implémenter les ports de `Application/Abstractions/Ports.cs`.

## Vérifications effectuées lors de la génération
Domain, Application, Infrastructure et Web ont été compilés sans erreur ; les 25 tests ont été exécutés avec succès
(avec un lanceur de remplacement, faute d'accès à NuGet) ; les pages Blazor répondent (HTTP 200) ; sur les données de
démonstration : glouton 34/36, construction par phases 35/36, ALNS 36/36 voyages affectés, 0 violation.
Non vérifiés : les requêtes MySQL (aucune base disponible) et les interactions dans le navigateur (clics, saisie).

## Limites et points à valider avec la société
* Requêtes MySQL écrites d'après le schéma fourni : non testées sur vos données (valeurs de `trips.ETAT`, définition d'un voyage « complet »).
* Heure d'arrivée dérivée de `stop_times` ; aller-retour liés (`HAVERET`) traités comme des voyages indépendants.
* B05 (capacité) inactive tant que le nombre de voyageurs prévu n'existe pas ; `drdentr` ignorée (aucune relation avec `dragent`).
* Scores pendant la recherche = scores à l'insertion (approximation pour la vitesse) ; scores exacts recalculés en fin d'allocation.
* Valeurs par défaut (8 h, 12 h d'amplitude, 30 min de repos, 15 min de remise en service) à valider.
