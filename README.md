# LLM Usage Monitor

Application web qui surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset.

En service en production depuis le 17 septembre 2026 sur [`https://monitor.llm.elyspio.fr`](https://monitor.llm.elyspio.fr) (LXC `ely-llm-wake-up.elylan`, service systemd derrière HAProxy), en remplacement du cron `llm-wake-up`.

- API ASP.NET Core 10 (`LlmUsageMonitor.Api/`), SPA Vite+ / React (`LlmUsageMonitor.Front/`), orchestration Aspire (`LlmUsageMonitor.AppHost/`).
- Lecteurs d'usage TypeScript d'origine et leur outillage : `LlmUsageMonitor.Scripts/`.
- Déploiement sur le LXC : `deploy/`.

```sh
aspire run
```

Spécification : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19) (fermé, livré). Structure, tests et déploiement : [AGENTS.md](AGENTS.md). Mise en service et retour arrière : [deploy/README.md](deploy/README.md).
