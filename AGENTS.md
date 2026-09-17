# LLM Usage Monitor

Application web qui surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset. **En service en production depuis le 17 septembre 2026** sur [`https://monitor.llm.elyspio.fr`](https://monitor.llm.elyspio.fr), où elle remplace le cron `llm-wake-up`.

Spec : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19) — fermé, les 14 issues d'implémentation sont livrées. Décisions : [map Wayfinder](https://github.com/Elyspio/llm-usage-monitor/issues/1) — fermée, les tickets restent la trace des choix. Vocabulaire du domaine : [CONTEXT.md](CONTEXT.md). Toute évolution repart d'une nouvelle issue.

## Structure

- `LlmUsageMonitor.slnx`, `global.json` (SDK .NET 10, runner de tests Microsoft.Testing.Platform), `aspire.config.json`.
- `LlmUsageMonitor.AppHost/` : AppHost Aspire 13.5 (C#). MongoDB, Keycloak de dev (realm importé depuis `Realms/`, comptes `admin`/`admin` avec le rôle et `norole`/`norole` sans rôle), API, front sur `https://localhost:3000`.
- `LlmUsageMonitor.Api/` : ASP.NET Core 10.
  - `Abstractions` (contrats, config), `Core` (règles de cycle et de reset, lecture, déclenchements, keep-alive, santé, notifications, réglages), `WebApi` (contrôleurs, auth, OpenAPI).
  - Adapters : `Claude` (`/api/oauth/usage`, prompt `claude -p`, rafraîchissement par `claude mcp list`), `Codex` (JSON-RPC `codex app-server`), `MongoDB` (5 collections + clés Data Protection), `Hangfire` (jobs dans le process de l'API, collections préfixées `hangfire.` dans la base de l'application), `Ntfy`.
  - `Core.Tests` (services réels sur stockage en mémoire, `FakeTimeProvider`), `Adapters.Tests` (fixtures anonymisées dans `Fixtures/`, repositories sur Mongo Testcontainers), `WebApi.Tests` (`WebApplicationFactory` sur Mongo Testcontainers, JWT signés localement, Hangfire désactivé).
  - Toutes les routes sont sous `/api` et exigent le rôle client `llm-usage-monitor:admin` ; `/hangfire` passe par cookie + OIDC avec le même rôle.
- `LlmUsageMonitor.Front/` : SPA Vite+ (`@elyspio/vite-eslint-config` v6, React Router 8, MUI 9, TanStack Query, `oidc-client-ts`).
  - `openapi/llm-usage-monitor.json` : document OpenAPI écrit par le build de `WebApi`, commité.
  - `src/core/apis/generated/` : client `@hey-api/openapi-ts` généré depuis ce document, commité, exclu du lint et du formatage.
- `LlmUsageMonitor.Scripts/` : lecteurs d'usage TypeScript d'origine (`src/`, `examples/`) et leur outillage (pnpm, Oxlint, Oxfmt, TypeScript 7). Projet indépendant du front, portés en C# dans les adapters ; les fixtures des tests d'adapters viennent de ces lecteurs.

## Lancer

```sh
aspire run
```

Front sur `https://localhost:3000`, Swagger UI sur `https://localhost:3000/swagger`, jobs sur `https://localhost:3000/hangfire`, dashboard Aspire à l'URL affichée.

En développement (`appsettings.Development.json`), le déclenchement automatique est **désactivé par défaut** : une exécution locale lit l'usage des vrais comptes mais n'envoie jamais de prompt toute seule. L'activer dans Réglages si besoin. Les CLIs `claude` et `codex` doivent être connectés sur le poste.

## Tests

Pas de CI : **build et tests se lancent en local sur le poste de dev, et doivent être verts avant chaque déploiement**.

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
- Scripts TypeScript (dans `LlmUsageMonitor.Scripts/`) :
  ```sh
  pnpm install
  pnpm check    # Oxfmt, Oxlint, tsc
  ```
- Contrat API : le build de `WebApi` réécrit `LlmUsageMonitor.Front/openapi/llm-usage-monitor.json`, puis `pnpm gen:api` régénère le client. Après un changement d'API, commiter les deux ; `git status` ne doit plus montrer de diff.

La génération du document OpenAPI au build démarre l'application sans MongoDB : Hangfire et `AppInitializer` y sont exclus (`OpenApiGeneration.IsRunning`). Tout service qui se connecte à sa construction doit l'être aussi.

Le paquet `typescript` du front reste en 6.x : `@hey-api/openapi-ts` utilise l'API JavaScript du compilateur, que TypeScript 7 n'expose pas encore. Le typecheck de `vp check` passe par tsgolint (TypeScript 7).

Les adapters CLI sont testés sur des fixtures capturées et anonymisées (aucun token, id de compte ni email). La gestion du process CLI se valide à la main.

## Déploiement

En production : LXC `ely-llm-wake-up.elylan` (Debian 13, CT 106), service systemd `llm-usage-monitor` sous le compte dédié `llm-monitor`, Kestrel en HTTP sur `:5000` derrière HAProxy qui termine le TLS de `https://monitor.llm.elyspio.fr`. Keycloak (`auth.elyspio.fr`, realm `internal`, client `i-llm-usage-monitor`), MongoDB `rs-shard-a` et le collector de traces sont externes. Le cron qu'elle remplace est désactivé sur le LXC (`/etc/cron.hourly/llm-wake-up.disabled`) : à supprimer, avec les logins CLI de `root`, après deux semaines de fonctionnement stable.

Mettre à jour :

```sh
./deploy/deploy.ps1
```

Build self-contained `linux-x64` dans Docker sur le poste (`deploy/Dockerfile`), scp vers `ely-llm-wake-up.elylan`, extraction dans `/opt/llm-usage-monitor/` (écrasement en place) puis redémarrage de `llm-usage-monitor.service`. Config de prod : `/etc/llm-usage-monitor/appsettings.Production.json` (modèle `deploy/appsettings.Production.example.json`, jamais commitée), chargée via `LLM_USAGE_MONITOR_SETTINGS`. En prod l'API sert aussi la SPA (`wwwroot`) et `/conf.js`. Mise en service et retour arrière : `deploy/README.md`.
