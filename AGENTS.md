# LLM Usage Monitor

Application web qui surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset. Spec : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19), décisions : [map Wayfinder](https://github.com/Elyspio/llm-usage-monitor/issues/1). Vocabulaire du domaine : [CONTEXT.md](CONTEXT.md).

## Structure

- `LlmUsageMonitor.slnx`, `global.json` (SDK .NET 10, runner de tests Microsoft.Testing.Platform), `aspire.config.json`.
- `LlmUsageMonitor.AppHost/` : AppHost Aspire 13.5 (C#). MongoDB, Keycloak de dev (realm importé depuis `Realms/`, comptes `admin`/`admin` avec le rôle et `norole`/`norole` sans rôle), API, front sur `https://localhost:3000`.
- `LlmUsageMonitor.Api/` : ASP.NET Core 10.
  - `Abstractions` (contrats, config), `Core` (règles de cycle et de reset, lecture, déclenchements, keep-alive, santé, notifications, réglages), `WebApi` (contrôleurs, auth, OpenAPI).
  - Adapters : `Claude` (`/api/oauth/usage`, prompt `claude -p`, rafraîchissement par `claude mcp list`), `Codex` (JSON-RPC `codex app-server`), `MongoDB` (5 collections + clés Data Protection), `Hangfire` (jobs dans le process de l'API, base `hangfire`), `Ntfy`.
  - `Core.Tests` (services réels sur stockage en mémoire, `FakeTimeProvider`), `Adapters.Tests` (fixtures anonymisées dans `Fixtures/`, repositories sur Mongo Testcontainers), `WebApi.Tests` (`WebApplicationFactory` sur Mongo Testcontainers, JWT signés localement, Hangfire désactivé).
  - Toutes les routes sont sous `/api` et exigent le rôle client `llm-usage-monitor:admin` ; `/hangfire` passe par cookie + OIDC avec le même rôle.
- `LlmUsageMonitor.Front/` : SPA Vite+ (`@elyspio/vite-eslint-config` v6, React Router 8, MUI 9, TanStack Query, `oidc-client-ts`).
  - `openapi/llm-usage-monitor.json` : document OpenAPI écrit par le build de `WebApi`, commité.
  - `src/core/apis/generated/` : client `@hey-api/openapi-ts` généré depuis ce document, commité, exclu du lint et du formatage.
- `src/*.ts` : lecteurs d'usage TypeScript d'origine, à supprimer une fois portés en C# avec tests de parité.

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
- Contrat API : le build de `WebApi` réécrit `LlmUsageMonitor.Front/openapi/llm-usage-monitor.json`, puis `pnpm gen:api` régénère le client. Après un changement d'API, commiter les deux ; `git status` ne doit plus montrer de diff.

La génération du document OpenAPI au build démarre l'application sans MongoDB : Hangfire et `AppInitializer` y sont exclus (`OpenApiGeneration.IsRunning`). Tout service qui se connecte à sa construction doit l'être aussi.

Le paquet `typescript` du front reste en 6.x : `@hey-api/openapi-ts` utilise l'API JavaScript du compilateur, que TypeScript 7 n'expose pas encore. Le typecheck de `vp check` passe par tsgolint (TypeScript 7).

Les adapters CLI sont testés sur des fixtures capturées et anonymisées (aucun token, id de compte ni email). La gestion du process CLI se valide à la main.

## Déploiement

Build self-contained `linux-x64` dans Docker sur le poste, puis scp vers `ely-llm-wake-up.elylan` (`/opt/llm-usage-monitor/`, écrasement en place) et `systemctl restart llm-usage-monitor`. Config de prod : `/etc/llm-usage-monitor/appsettings.Production.json`, jamais commitée.
