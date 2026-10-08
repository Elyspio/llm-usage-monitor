# LLM Usage Monitor

Application web qui surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset. **En service en production depuis le 17 septembre 2026** sur [`https://monitor.llm.elyspio.fr`](https://monitor.llm.elyspio.fr), où elle remplace le cron `llm-wake-up`.

Spec : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19) — fermé, les 14 issues d'implémentation sont livrées. Décisions : [map Wayfinder](https://github.com/Elyspio/llm-usage-monitor/issues/1) — fermée, les tickets restent la trace des choix. Toute évolution repart d'une nouvelle issue.

## Structure

- `LlmUsageMonitor.slnx`, `global.json` (SDK .NET 10, runner de tests Microsoft.Testing.Platform : ni `Microsoft.NET.Test.Sdk` ni `xunit.runner.visualstudio`), `aspire.config.json`. Pas de gestion centralisée des paquets : chaque `.csproj` porte les versions de ses paquets NuGet, ceux dont tous les modules ont besoin sont dans `Abstractions` ; une prerelease y est justifiée en commentaire.
- `LlmUsageMonitor.AppHost/` : AppHost Aspire 13.5 (C#). MongoDB, Keycloak de dev (realm importé depuis `Realms/`, comptes `admin`/`admin` avec le rôle et `norole`/`norole` sans rôle), API, front sur `https://localhost:3000`.
- `LlmUsageMonitor.Api/` : ASP.NET Core 10.
  - `Abstractions` (contrats, config ; sans ASP.NET Core, les modules se chargent par `AddModule` dans `WebApi`), `Core` (règles de cycle et de reset, lecture, déclenchements, keep-alive, santé, notifications, réglages), `WebApi` (contrôleurs, auth, OpenAPI).
  - Adapters : `Claude` (`/api/oauth/usage`, prompt `claude -p`, rafraîchissement par `claude mcp list`), `Codex` (JSON-RPC `codex app-server`), `MongoDB` (8 collections, Data Protection : clés et chiffrement du token ntfy), `Hangfire` (jobs dans le process de l'API, collections préfixées `hangfire.` dans la base de l'application, même client Mongo que l'adapter `MongoDB`), `Ntfy`, `LiteLlm` (table de prix publique, rechargée chaque jour).
  - Usage en tokens : `POST /api/token-usage` reçoit les totaux horaires (poste × fournisseur × modèle) envoyés par le collecteur `LlmUsageMonitor.Collector/`, qui lit les journaux de session locaux : importé par l'app desktop Elytools ([Elyspio/elytools](https://github.com/Elyspio/elytools)), ou installé seul (CLI `llm-usage`). Le coût est calculé à l'envoi et stocké ; un modèle sans prix reste « Unpriced ».
  - `Core.Tests` (services réels sur stockage en mémoire, `FakeTimeProvider`), `Adapters.Tests` (fixtures anonymisées dans `Fixtures/`, repositories sur Mongo Testcontainers, CLIs remplacés par `LlmUsageMonitor.FakeCli`, endpoints HTTP par un handler factice), `WebApi.Tests` (`WebApplicationFactory` sur Mongo Testcontainers, JWT signés localement, Hangfire désactivé ; le dashboard `/hangfire` est testé sur un stockage Hangfire en mémoire, sans serveur de jobs, et `HangfireIntegrationTests` fait tourner le vrai Hangfire sur Mongo avec des adapters de providers factices).
  - Erreurs : les exceptions applicatives deviennent des ProblemDetails (`HttpExceptionFilter`, exception enregistrée en événement de l'activité), toute autre erreur un 500 ProblemDetails (`UseExceptionHandler`). Logs : Serilog seul, une ligne par événement sur la console (journald en prod), niveaux dans la section `Serilog:MinimumLevel` (la section `Logging` n'est pas lue).
  - Toutes les routes sont sous `/api` et exigent le rôle client `llm-usage-monitor:admin` ; le token doit être émis pour `Oidc:ClientId` ou un client de `Oidc:AuthorizedParties` (claim `azp` : collecteur `i-llm-usage-collector`, Elytools `i-elytools`). `/hangfire` passe par cookie + OIDC avec le même rôle (cookie de 8 h non glissant, antiforgery sur les actions du dashboard). La politique d'autorisation par défaut (`FallbackPolicy`) est la politique admin : un nouvel endpoint est protégé sans attribut. Seules exceptions, anonymes et déclarées par `AllowAnonymous` : les sondes `/health/live` et `/health/ready` (supervision Uptime Kuma, voir `deploy/README.md`), `/conf.js` et la SPA. Swagger UI et `/openapi` ne sont servis que hors Production.
- `LlmUsageMonitor.Front/` : SPA Vite+ 1.0 (`@elyspio/vite-eslint-config` v8, React Router 8, MUI 9, TanStack Query, `oidc-client-ts` avec les tokens en sessionStorage : un nouvel onglet se reconnecte par la session Keycloak).
  - `openapi/llm-usage-monitor.json` : document OpenAPI écrit par le build de `WebApi`, commité.
  - `src/core/apis/generated/` : client `@hey-api/openapi-ts` généré depuis ce document, commité, exclu du lint et du formatage.
- `LlmUsageMonitor.Collector/` : package npm public `@elyspio/llm-usage-collector` (pnpm, vite-plus). Cœur du collecteur importé par Elytools, et CLI `llm-usage` livré en exécutable Node SEA win-x64 / linux-x64 / linux-arm64 (`vp pack -F exe`). Device flow sur le client Keycloak public `i-llm-usage-collector`, dossier de données partagé avec Elytools. Détails : [`LlmUsageMonitor.Collector/README.md`](LlmUsageMonitor.Collector/README.md).

Crédits de reset : le dashboard affiche le nombre annoncé par le fournisseur, les détails disponibles, leur expiration, la prochaine sélection et le journal. `POST /api/providers/{provider}/reset-credits/consume` exige un identifiant de crédit et une clé UUID ; la confirmation manuelle réutilise cette clé après une erreur HTTP. Réglages : `/api/settings/reset-credits`, indépendants des prompts de relance, désactivés par défaut, marge de 60 minutes. Avant expiration, tenter chaque crédit éligible dans l'ordre d'expiration, même à quota nul ; un refus ne consomme rien. Expiration inconnue : manuel uniquement. Aucun prompt après consommation. Le journal Mongo `resetCreditRuns` conserve les tentatives et clés d'idempotence avant l'appel externe ; les reprises réutilisent la clé, les entrées terminées expirent après 90 jours, les demandes en cours restent conservées. Codex : `account/rateLimitResetCredit/consume`. Claude : bloc `cedar_ember` demandé sur l'endpoint OAuth et flux de consommation repéré dans Claude Code 2.1.289 ; `Claude:AccountConfigPath` peut désigner le `.claude.json` contenant l'organisation OAuth. L'accès Claude a été vérifié en lecture seule ; les tests de consommation utilisent des adapters factices.

## Lancer

```sh
aspire run
```

Front sur `https://localhost:3000`, Swagger UI sur `https://localhost:3000/swagger`, jobs sur `https://localhost:3000/hangfire`, dashboard Aspire à l'URL affichée.

En développement (`appsettings.Development.json`), le déclenchement automatique est **désactivé par défaut** : une exécution locale lit l'usage des vrais comptes mais n'envoie jamais de prompt toute seule. L'activer dans Réglages si besoin. Les CLIs `claude` et `codex` doivent être connectés sur le poste.

## Tests

**Build et tests se lancent en local sur le poste de dev, et doivent être verts avant chaque merge.** Pas de CI sur les PRs : seul le workflow de release (`.github/workflows/release.yml`) les relance, sur le tag, avant de publier (voir « Releases »).

Prérequis : Docker démarré (MongoDB via Testcontainers dans les tests backend).

- Backend :
  ```sh
  dotnet build LlmUsageMonitor.slnx
  dotnet test --solution LlmUsageMonitor.slnx
  ```
- Front (dans `LlmUsageMonitor.Front/`) :
  ```sh
  pnpm install
  pnpm check    # formatage, lint, typecheck
  pnpm test     # Vitest + Testing Library + MSW
  pnpm build
  ```
- Collecteur (dans `LlmUsageMonitor.Collector/`) :
  ```sh
  pnpm install
  pnpm check    # Oxfmt, Oxlint, types
  pnpm test
  pnpm build    # lib npm ; pnpm build:exe pour les exécutables (depuis PowerShell)
  ```
  Publication (npm + GitHub Release `collector-vX.Y.Z`) : par le workflow de release, voir « Releases ».
- Contrat API : le build de `WebApi` réécrit `LlmUsageMonitor.Front/openapi/llm-usage-monitor.json`, puis `pnpm gen:api` régénère le client. Après un changement d'API, commiter les deux ; `git status` ne doit plus montrer de diff.

La génération du document OpenAPI au build démarre l'application sans MongoDB : Hangfire et `AppInitializer` y sont exclus et le trousseau Data Protection y reste en mémoire (`OpenApiGeneration.IsRunning`). Tout service qui se connecte à sa construction doit l'être aussi.

Le paquet `typescript` du front reste en 6.x : `@hey-api/openapi-ts` utilise l'API JavaScript du compilateur, que TypeScript 7 n'expose pas encore. Le typecheck de `vp check` passe par tsgolint (TypeScript 7).

Les adapters CLI sont testés sur des fixtures capturées et anonymisées (aucun token, id de compte ni email), issues des lecteurs TypeScript d'origine (`LlmUsageMonitor.Scripts/`, supprimé depuis, retrouvable dans l'historique git). La gestion du process CLI est testée avec `LlmUsageMonitor.FakeCli` (copié dans `fake-cli-bin/` à côté des tests) : scénario lu dans `fake-cli.json` de son dossier de travail, appels et messages JSON-RPC journalisés à côté ; le comportement des vrais CLIs après une mise à jour se valide à la main.

## Releases

Publiées par `.github/workflows/release.yml` à la poussée d'un tag, chaque série testée par les tests de son composant :

- `vX.Y.Z` (serveur et front) : build et tests backend, contrat OpenAPI (le client régénéré ne doit pas différer), check/test/build du front ; exécutables `llm-usage-monitor-linux-x64` et `-linux-arm64` (build `docker-bake.hcl`, version du tag dans l'assembly, affichée en bas du menu via `/conf.js`), smoke test du binaire x64 seul dans un dossier vide (`deploy/scripts/smoke-test.sh`), release avec `appsettings.Production.example.json`, l'unité systemd et `SHA256SUMS`. Ces releases portent le badge « Latest ».
- `collector-vX.Y.Z` : le tag doit égaler la version de `LlmUsageMonitor.Collector/package.json` ; check/test, package npm (trusted publishing, provenance) et exécutables win-x64 / linux-x64 / linux-arm64 avec `SHA256SUMS`.
- Un tag à suffixe (`v1.2.0-rc.1`) publie une prerelease (npm : tag `next`). Notes générées par GitHub depuis le tag précédent de la même série. Une exécution relancée saute ce qui est déjà publié.

**Après le squash-merge d'une PR, taguer le composant touché** : `vX.Y.Z` si le serveur ou le front changent ; si le collecteur change, monter `version` dans son `package.json` (dans la PR) puis taguer `collector-vX.Y.Z`. Rien pour une PR qui ne touche que la doc ou les scripts de déploiement. Semver : fonctionnalité = mineure, correctif = patch.

## Déploiement

En production : LXC `ely-llm-wake-up.elylan` (Debian 13, CT 106), service systemd `llm-usage-monitor` sous le compte dédié `llm-monitor`, Kestrel en HTTP sur `:5000` derrière HAProxy qui termine le TLS de `https://monitor.llm.elyspio.fr`. Keycloak (`auth.elyspio.fr`, realm `internal`, client `i-llm-usage-monitor`), MongoDB `rs-shard-a` et le collector de traces sont externes. Le cron qu'elle remplace est désactivé sur le LXC (`/etc/cron.hourly/llm-wake-up.disabled`) : à supprimer, avec les logins CLI de `root`, après deux semaines de fonctionnement stable.

Mettre à jour :

```sh
./deploy/deploy.ps1 -Version X.Y.Z
```

Télécharge l'exécutable `linux-x64` et l'unité systemd de la release `vX.Y.Z` (vérifiés par `SHA256SUMS`), les copie sur `ely-llm-wake-up.elylan`, remplace `/opt/llm-usage-monitor/` (l'exécutable seul : la SPA et l'`appsettings.json` par défaut sont embarqués dedans) puis redémarre `llm-usage-monitor.service`. Sans `-Version`, l'exécutable est construit dans Docker sur le poste depuis le checkout (`deploy/docker/Dockerfile` via `docker-bake.hcl`, cible `artifact`, version `0.0.0-dev`), pour tester une modification non publiée. Étapes dans `deploy/scripts/`. Config de prod : `/etc/llm-usage-monitor/appsettings.Production.json` (modèle `deploy/config/appsettings.Production.example.json`, jamais commitée), chargée via `LLM_USAGE_MONITOR_SETTINGS`. En prod l'API sert aussi la SPA (embarquée dans l'exécutable par `ProductionHosting.UseEmbeddedSpa`, `wwwroot` hors build de release) et `/conf.js` (`Cache-Control: no-store`), avec une CSP stricte sur toutes les réponses hors `/hangfire` (`ProductionHosting.ContentSecurityPolicy` : aucun script inline ; `style-src 'unsafe-inline'` pour Emotion/MUI ; `connect-src` limité à l'application et à Keycloak). Un script inline dans `index.html` ou un appel vers un autre hôte doit l'y ajouter. Mise en service et retour arrière : `deploy/README.md`. Raspberry Pi 4 : même installation, exécutable `linux-arm64` (cible bake `artifact-arm64`), `./deploy/deploy.ps1 -Platform arm64 -Target root@<pi> -Version X.Y.Z`.
