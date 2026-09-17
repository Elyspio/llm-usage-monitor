# LLM Usage Monitor

Application web qui surveille l'usage des abonnements Claude Code et Codex, et relance une fenêtre d'usage après un reset.

- API ASP.NET Core 10 (`LlmUsageMonitor.Api/`), SPA Vite+ / React (`LlmUsageMonitor.Front/`), orchestration Aspire (`LlmUsageMonitor.AppHost/`).
- Lecteurs d'usage TypeScript d'origine et leur outillage : `LlmUsageMonitor.Scripts/`.
- Déploiement sur le LXC : `deploy/`.

```sh
aspire run
```

Spécification : [PRD](https://github.com/Elyspio/llm-usage-monitor/issues/19). Structure, tests et déploiement : [AGENTS.md](AGENTS.md). Vocabulaire du domaine : [CONTEXT.md](CONTEXT.md).
