# LLM Usage Monitor

Surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset. Production : <https://monitor.llm.elyspio.fr>.

Toute évolution repart d'une nouvelle issue. Trace des décisions : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19), [map Wayfinder](https://github.com/Elyspio/llm-usage-monitor/issues/1).

## Lancer et tester

`aspire run` lance l'application en local ; les CLIs `claude` et `codex` doivent être connectés.

Build et tests verts en local avant chaque merge : les PRs n'ont pas de CI. Docker démarré requis.

- Backend : `dotnet build LlmUsageMonitor.slnx`, puis `dotnet test --solution LlmUsageMonitor.slnx`.
- `LlmUsageMonitor.Front/` et `LlmUsageMonitor.Collector/` : `pnpm install`, `pnpm check`, `pnpm test`, `pnpm build`.

## Règles

- Auth : la `FallbackPolicy` exige le rôle admin ; un endpoint anonyme se déclare par `AllowAnonymous`.
- Changement d'API : le build de `WebApi` réécrit `LlmUsageMonitor.Front/openapi/llm-usage-monitor.json`, `pnpm gen:api` régénère le client ; commiter les deux.
- Ce build démarre l'application sans MongoDB : tout service qui se connecte à sa construction s'exclut via `OpenApiGeneration.IsRunning`.
- CSP de prod : un script inline ou un appel vers un nouvel hôte s'ajoute dans `ProductionHosting.ContentSecurityPolicy`.
- Le paquet `typescript` du front reste en 6.x : `@hey-api/openapi-ts` utilise l'API JavaScript du compilateur, absente de TypeScript 7.
- Fixtures des adapters CLI anonymisées (aucun token, id de compte ni email). Après une mise à jour des vrais CLIs, valider leur comportement à la main.

## Releases

Après le squash-merge, taguer le composant touché (fonctionnalité = mineure, correctif = patch) :

- serveur ou front : `vX.Y.Z` ;
- collecteur : monter `version` de son `package.json` dans la PR, puis `collector-vX.Y.Z` ;
- doc ou scripts de déploiement seuls : aucun tag.

Le tag déclenche `.github/workflows/release.yml`.

## Déploiement

`./deploy/deploy.ps1 -Version X.Y.Z`. Détails : [`deploy/README.md`](deploy/README.md).

## Docs

- Crédits de reset : [`docs/reset-credits.md`](docs/reset-credits.md).
- Collecteur : [`LlmUsageMonitor.Collector/README.md`](LlmUsageMonitor.Collector/README.md).
