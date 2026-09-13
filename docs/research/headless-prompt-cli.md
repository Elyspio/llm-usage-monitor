# Prompt headless via les CLIs `claude` et `codex`

> Ticket : [#4](https://github.com/Elyspio/llm-usage-monitor/issues/4) — carte [#1](https://github.com/Elyspio/llm-usage-monitor/issues/1).
> Recherche du 2026-09-13. Versions installées et analysées : **Claude Code 2.1.270**, **codex-cli 0.153.4** (source lue au tag `rust-v0.153.4`).
> Aucun prompt réel n'a été exécuté : tout vient de `--help`, du schéma généré localement (`codex app-server generate-json-schema`), de la documentation officielle, du code source et du changelog. Les affirmations issues de la communauté sont marquées **[communauté]**.

## Réponse courte

**Claude** : processus lancé avec `cwd` = un dossier vide dédié (hors dépôt git), stdin = `/dev/null` :

```sh
claude -p "1+1=?" \
  --model haiku \
  --tools "" \
  --output-format json \
  --no-session-persistence \
  --safe-mode --strict-mcp-config \
  --permission-mode dontAsk \
  --max-turns 1
```

- **Surtout pas `--bare`** : ce mode ne lit jamais l'OAuth ni le keychain et exige `ANTHROPIC_API_KEY`, donc il ne consomme pas l'abonnement.
- Succès = code de sortie `0` et JSON avec `is_error: false`. Sinon, classer l'erreur via `is_error`, `api_error_status` et le texte de `result`.

**Codex** : recommandé = passer par l'**app-server**, déjà utilisé par `src/codex.ts`. Séquence JSON-RPC :

1. `initialize`, puis `initialized`.
2. `thread/start` avec `{ ephemeral: true, cwd, model: "gpt-5.6-luna", sandbox: "read-only", approvalPolicy: "never" }`.
3. `turn/start` avec `{ threadId, input: [{ type: "text", text: "1+1=?" }], effort: "low" }`.
4. Attendre `turn/completed`, puis relire `account/rateLimits/read`.

Les erreurs y sont typées (`codexErrorInfo` : `usageLimitExceeded`, `unauthorized`…).

Repli plus simple : `codex exec`, avec `cwd` = le même dossier vide et stdin = `/dev/null` (**obligatoire**, voir « Linux sans TTY ») :

```sh
codex exec --skip-git-repo-check --ephemeral --sandbox read-only \
  -C /var/lib/llm-usage-monitor/prompt \
  -m gpt-5.6-luna -c model_reasoning_effort="low" -c web_search="disabled" \
  --json --color never "1+1=?" < /dev/null
```

Code de sortie `0` = tour terminé ; `1` = tout échec. En JSONL, les erreurs n'ont qu'un champ `message`, sans code.

**Ce qui est sûr et ce qui ne l'est pas**

- **Coût** : non documenté en % pour Claude. Estimation : bien moins de 1 % de la fenêtre 5h avec Haiku, à mesurer une fois en comparant l'usage avant et après. Pour Codex, la doc donne 250–2 000 messages locals par 5h sur Luna (plan Plus), soit environ 0,05–0,4 % par prompt.
- **Démarrage de fenêtre** :
  - *Claude 5h* : fenêtre glissante qui démarre au premier message **[communauté, rapports concordants]**.
  - *Claude hebdo* : reset à heure **fixe** attribuée au compte (officiel). Le prompt ne l'« ouvre » donc pas.
  - *Codex 5h / hebdo* : démarrent au premier usage après expiration **[communauté]**. Depuis le 25/08/2026, la limite 5h n'est **pas active pour Pro 100 $ / 200 $**.
  - Dans tous les cas, le service doit **vérifier** après coup que `resetsAt` est apparu ou a bougé.
- **Tokens OAuth** : oui, les deux CLIs rafraîchissent et réécrivent leurs credentials (`~/.claude/.credentials.json`, `~/.codex/auth.json`). Les refresh tokens sont à usage unique : il faut sérialiser les appels et éviter que le même compte tourne en parallèle ailleurs sur la même copie de credentials.
- **Sans TTY** : les deux fonctionnent, à condition de fermer stdin et d'imposer un timeout avec kill.

## Détails

### 1. Claude Code (`claude -p`)

#### Flags

| Besoin | Flag | Source / remarque |
|---|---|---|
| Non interactif | `-p` / `--print` | En mode non interactif (`-p`, ou stdout non TTY), le dialogue de confiance du workspace est ignoré. Les fichiers de settings invalides sont ignorés silencieusement (`claude --help`, 2.1.270). |
| Modèle le moins cher | `--model haiku` | L'alias `haiku` pointe vers le dernier Haiku ([model-config](https://code.claude.com/docs/en/model-config)). « Opus costs several times more per turn than Sonnet, and Sonnet more than Haiku » ([support, 2026-04-15](https://support.claude.com/en/articles/14552983-models-usage-and-limits-in-claude-code)). Par défaut, Max utilise Opus 5 et Pro Sonnet 5 : **toujours forcer `haiku`**. |
| Aucun outil | `--tools ""` | `--help` : « Use "" to disable all tools ». Avec Node `spawn` sans shell, passer l'argument `''` tel quel. |
| Pas de contexte parasite | `--safe-mode`, `--strict-mcp-config` | `--safe-mode` désactive CLAUDE.md, skills, plugins, hooks, MCP et auto-memory, mais garde l'auth et le choix du modèle ([cli-reference](https://code.claude.com/docs/en/cli-reference)). C'est l'inverse de `--bare`, qui **coupe l'OAuth** ([headless](https://code.claude.com/docs/en/headless#start-faster-with-bare-mode)). Sans ces flags, `-p` exécute les hooks et `.mcp.json` du cwd sans demander ([headless](https://code.claude.com/docs/en/headless)). |
| Pas d'écriture de session | `--no-session-persistence` | Le transcript n'est pas sauvegardé ([cli-reference](https://code.claude.com/docs/en/cli-reference)). **Il y a quand même des écritures** : `~/.claude.json` (état) et `.credentials.json` en cas de refresh. |
| Permissions | `--permission-mode dontAsk` | Tout ce qui demanderait une permission est refusé ([headless](https://code.claude.com/docs/en/headless#auto-approve-tools)). Redondant avec `--tools ""`, mais c'est une ceinture de sécurité. |
| Borne | `--max-turns 1` | Mode print uniquement ; sort en erreur si la limite est atteinte. |
| cwd | *(pas de flag)* | C'est le cwd du processus. Utiliser un dossier vide dédié pour éviter qu'un `.claude/settings.json` ou `.mcp.json` soit chargé. |
| JSON | `--output-format json` | Un objet unique avec `result`, `session_id`, `usage`, `total_cost_usd` (**estimation client, sans rapport avec la facturation de l'abonnement**) ([headless](https://code.claude.com/docs/en/headless#get-structured-output)). |
| Git | *(rien)* | Claude n'exige pas de dépôt git. |

Variables d'environnement utiles dans un service :

- `DISABLE_AUTOUPDATER=1`, `DISABLE_TELEMETRY=1`, `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` : ces variables jouent sur leur seule présence ([env-vars](https://code.claude.com/docs/en/env-vars)).
- `HOME` ou `CLAUDE_CONFIG_DIR` doit pointer vers le compte connecté ([authentication](https://code.claude.com/docs/en/authentication#credential-management)).

À valider au premier essai manuel : que `--safe-mode` et `--max-turns 1` se combinent bien avec `-p`. Non testé, pour ne pas consommer de quota.

#### Coût et démarrage de fenêtre

- **Coût** : Anthropic ne publie aucun coût en % par message. Le support officiel indique seulement que l'usage dépend de la longueur, du modèle et de l'effort ([support 11647753, 2026-07-13](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work)). Même avec `--tools ""` et `--safe-mode`, le prompt système de Claude Code ajoute des milliers de tokens d'entrée. Avec Haiku, on s'attend à moins de 1 % de `five_hour`. **Non vérifié : à mesurer** en lisant `/api/oauth/usage` avant et après le premier envoi.
- **Fenêtre 5h** : l'aide officielle dit seulement que la limite de session « will reset every five hours » ([Max plan, 2026-08-07](https://support.claude.com/en/articles/11049741-what-is-the-max-plan)). Qu'elle démarre au **premier message** est un comportement rapporté par la communauté et utilisé par des outils de « warm-up » (`claude --model haiku -p "ok"` en cron) ([dev.to](https://dev.to/ibrahimdans/claude-wake-up-start-your-claude-session-at-5-am-without-getting-up-yourself-4ecn), [dev.to](https://dev.to/avsi/how-to-make-best-of-claude-codes-5-hour-limits-4j32)) **[communauté]**.
- **Fenêtre hebdo** : officiel. « The weekly limit resets at a fixed time each week that is assigned to your account. Your reset day and time stay the same regardless of when you start using Claude » ([Max plan, 2026-08-07](https://support.claude.com/en/articles/11049741-what-is-the-max-plan)). Conséquence pour le déclencheur : **ne déclencher le prompt que sur le reset de `five_hour`**, pas sur `seven_day*`.
- Haiku compte dans `five_hour` et dans la limite hebdo « tous modèles ». Il ne touche pas les seaux hebdo propres à Opus ou Sonnet. C'est une déduction : aucune source officielle ne détaille ce point.

#### Codes de sortie et erreurs

- **Codes de sortie** :
  - `0` en cas de succès, non nul en cas d'échec.
  - Un flag invalide produit une erreur sur **stderr** avant le démarrage.
  - Un échec **pendant** le run (auth manquante, limite…) est imprimé **comme résultat sur stdout** ([headless](https://code.claude.com/docs/en/headless#basic-usage)).
  - SIGTERM donne `143` ([headless](https://code.claude.com/docs/en/headless#stop-a-run-with-sigterm)).
- **JSON d'erreur** : les champs utiles sont `is_error`, `subtype` et `api_error_status`. Un exemple communautaire montre `subtype: "success"` avec `is_error: true` : **se fier à `is_error`**, pas à `subtype` ([issue SDK Python #1031](https://github.com/anthropics/claude-agent-sdk-python/issues/1031)) **[communauté]**.
- **Messages officiels** ([errors](https://code.claude.com/docs/en/errors)) :
  - `Not logged in · Please run /login`
  - `Login expired · Please run /login`
  - `OAuth token revoked / OAuth token has expired`
  - `You've hit your session limit`
  - `You've hit your weekly limit`
  - `Request rejected (429)`
  - `API Error: Repeated 529 Overloaded errors`
- Depuis 2.1.108, les rate limits serveur sont distinguées des limites du plan. Depuis 2.1.199, les 429 transitoires sont retentées automatiquement ([CHANGELOG](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md)).
- **Classement proposé** :
  - `api_error_status` 401 ou 403, ou texte « Login expired / Not logged in » : `AUTH_EXPIRED`.
  - Texte « hit your … limit » : `USAGE_LIMIT`.
  - 429 : `RATE_LIMITED`.
  - 5xx ou 529 : `TRANSIENT`.
- **Diagnostic sans consommer de quota** : `claude auth status --json` sort en `0` si connecté, `1` sinon ([cli-reference](https://code.claude.com/docs/en/cli-reference)).
- **Événements en `stream-json`** (option plus riche) :
  - `system/api_retry` porte un champ `error` ∈ `authentication_failed`, `rate_limit`, `billing_error`, `overloaded`… (officiel, [headless](https://code.claude.com/docs/en/headless#handle-api-retries)).
  - Un événement `rate_limit_event` porte `rateLimitType: "five_hour"`, `status` et `resetsAt`, mais aucun pourcentage. Il n'est pas documenté ([issue #78476](https://github.com/anthropics/claude-code/issues/78476), fermée *not planned*) **[communauté]**.

#### Effet de bord sur le refresh OAuth

- **Oui, `claude -p` rafraîchit le token et réécrit `~/.claude/.credentials.json`**. Sur Linux, le fichier est en mode `0600`. Sur Windows, il se trouve dans `%USERPROFILE%\.claude\` ([authentication](https://code.claude.com/docs/en/authentication#credential-management)).
  - Si le login expire **et** ne peut plus être rafraîchi, chaque requête échoue avec `Login expired · Please run /login` ([authentication](https://code.claude.com/docs/en/authentication#renew-an-expiring-login)).
  - 2.1.117 : « the token is now refreshed reactively on 401 ».
  - 2.1.133 : correction de sessions parallèles bloquées en 401 après une course au refresh.
  - 2.1.248 : un verrou inter-processus sur le refresh ; la requête échoue en erreur retentable au lieu de renvoyer au login ([CHANGELOG](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md)).
- **Risque** : l'[issue #12447](https://github.com/anthropics/claude-code/issues/12447) (OAuth en autonome) est toujours **ouverte**. Un commentaire de septembre 2026 décrit des agents headless sous systemd qui meurent la nuit avec « OAuth session expired and could not be refreshed » quand le même compte Max est aussi connecté à l'app desktop **[communauté]**. En prod, le LXC doit avoir **son propre login** (`claude auth login`), pas une copie du fichier d'un autre poste.
- **Intérêt pour le monitor** : `src/claude.ts` refuse un `accessToken` expiré (`AUTH_EXPIRED`) et ne rafraîchit rien. Lancer le prompt, ou n'importe quelle commande qui fait une requête, remet le token à jour. Savoir si `claude auth status` rafraîchit aussi n'a **pas été vérifié**.
- **Alternative** : `claude setup-token` crée un token d'un an via `CLAUDE_CODE_OAUTH_TOKEN`, qui ne sert **qu'aux requêtes modèle** ([authentication](https://code.claude.com/docs/en/authentication#generate-a-long-lived-token)). Il fait sans doute l'affaire pour le prompt, mais son usage par `/api/oauth/usage` n'est **pas vérifié** : il n'a probablement pas les bons scopes.

#### Linux sans TTY

- `-p` est prévu pour les pipes et saute le dialogue de confiance quand stdout n'est pas un TTY (`--help`). L'ancien bug « hang sans TTY » ([#9026](https://github.com/anthropics/claude-code/issues/9026), 2025) a été fermé *not planned*.
- Si stdin est un pipe, Claude attend l'entrée pendant `CLAUDE_STDIN_WAIT_MS`, 5000 ms par défaut ([env-vars](https://code.claude.com/docs/en/env-vars)). Un stdin illisible produit un warning, puis le run continue ; le crash correspondant sous Windows a été corrigé en 2.1.211 ([headless](https://code.claude.com/docs/en/headless#pipe-data-through-claude)).
- **Recommandation** :
  - `spawn(..., { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true })`.
  - Timeout d'environ 120 s, puis SIGINT, puis SIGTERM (code `143`).
  - Unité systemd avec `User=` égal au compte connecté et `HOME` correct.

### 2. Codex (`codex exec` et app-server)

#### Flags de `codex exec` (0.153.4, `codex exec --help`)

| Besoin | Flag | Source / remarque |
|---|---|---|
| Modèle le moins cher | `-m gpt-5.6-luna` | La doc le décrit ainsi : « Fast and affordable… lowest cost in the family » ([models](https://learn.chatgpt.com/docs/models)). Tableau officiel : Luna = 250–2 000 messages locals par 5h sur Plus, contre 25–200 pour Terra ([pricing](https://learn.chatgpt.com/docs/pricing)). Le nom exact dépend du catalogue du compte : le vérifier via `model/list` (app-server, sans tour). |
| Effort minimal | `-c model_reasoning_effort="low"` | Valeurs possibles : `minimal \| low \| medium \| high \| xhigh` ([config-reference](https://learn.chatgpt.com/docs/config-file/config-reference)). Le schéma dit « value advertised by the model » : `minimal` n'est peut-être pas accepté par Luna, d'où `low`. |
| Pas d'outils | `--sandbox read-only`, `-c web_search="disabled"` ; optionnel : `-c features.shell_tool=false` | Il n'existe **pas** d'équivalent à `--tools ""`. En `read-only`, aucune écriture n'est possible ([non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode)). La doc mentionne `features.shell_tool` ([config-reference](https://learn.chatgpt.com/docs/config-file/config-reference)), mais ce n'est pas vérifié en 0.153.4. |
| Pas d'écriture disque | `--ephemeral` (+ `-c history.persistence="none"`) | `--ephemeral` évite les fichiers de session. **Il reste des écritures** : `$CODEX_HOME/log`, la base d'état sqlite, et `auth.json` en cas de refresh ([config-reference](https://learn.chatgpt.com/docs/config-file/config-reference)). |
| Hors dépôt git | `--skip-git-repo-check` | Sans ce flag, hors dépôt : « Not inside a trusted directory and --skip-git-repo-check was not specified. » et exit 1 ([lib.rs L803-808](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/lib.rs#L803-L808)). |
| cwd | `-C <dir>` | « Tell the agent to use the specified directory as its working root ». |
| JSON | `--json` (+ `--color never`) | JSONL : `thread.started`, `turn.started`, `turn.completed {usage}`, `turn.failed {error:{message}}`, `item.*`, `error {message}` ([exec_events.rs](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/exec_events.rs#L11-L94)). |
| Config reproductible (optionnel) | `--ignore-user-config`, `--ignore-rules` | L'auth continue d'utiliser `CODEX_HOME`. Attention : si `config.toml` définit `cli_auth_credentials_store = "keyring"`, l'ignorer peut changer la façon dont l'auth est trouvée. |

#### Variante app-server (recommandée)

Dans le schéma généré par 0.153.4 :

- `ThreadStartParams` accepte `ephemeral`, `model`, `cwd`, `sandbox` (`read-only | workspace-write | danger-full-access`), `approvalPolicy` (`untrusted | on-request | never`), `config`, `baseInstructions` et `developerInstructions`.
- `TurnStartParams` accepte `input`, `model`, `effort`, `sandboxPolicy`, `cwd` et `outputSchema`.
- La doc en ligne ([app-server](https://learn.chatgpt.com/docs/app-server)) écrit les valeurs de sandbox en camelCase (`workspaceWrite`), alors que le schéma 0.153.4 utilise `read-only`. **Se caler sur le schéma généré par la version installée** (`codex app-server generate-json-schema` ou `generate-ts`).

Pourquoi l'app-server :

1. C'est le même canal JSON-RPC que le lecteur `src/codex.ts` : même binaire, même auth.
2. Les erreurs sont **typées**. `turn/completed` porte `turn.status ∈ completed | interrupted | failed` et `error.codexErrorInfo ∈ usageLimitExceeded | rateLimitExceeded | unauthorized | serverOverloaded | internalServerError | …`, plus `httpStatusCode`. La notification `error` porte `willRetry`. `RateLimitSnapshot.rateLimitReachedType` indique le type de limite atteinte.
3. Le serveur pousse `account/rateLimits/updated` : on peut confirmer tout de suite que la fenêtre a démarré (`resetsAt` non nul ou changé), puis tuer le processus.

La commande porte le label `[experimental]` dans `codex app-server --help`. C'est le compromis à accepter. `codex exec` est lui-même construit sur un app-server en process (`InProcessServerEvent` dans `lib.rs`).

#### Coût et démarrage de fenêtre

- **Coût** : Luna donne 250–2 000 messages locals par 5h sur Plus, 1 250–10 000 sur Pro 5x et 5 000–40 000 sur Pro 20x ([pricing](https://learn.chatgpt.com/docs/pricing)). Un « 1+1=? » coûterait donc environ 0,05–0,4 % de la fenêtre 5h sur Plus, et beaucoup moins en hebdo. C'est une déduction à partir d'estimations officielles, à mesurer via `usedPercent`.
- **Fenêtre 5h** :
  - Glissante ; elle démarre au premier usage après expiration **[communauté]** ([sessionwatcher, 2026-08](https://sessionwatcher.com/guides/codex-rate-limits-explained)).
  - Suspendue le 12/07/2026, puis **rétablie le 25/08/2026 pour Plus**. D'après une citation d'OpenAI : « keeping the 5h limit not enabled for Pro $100 and Pro $200 subscriptions » ([9to5Mac, 2026-08-24](https://9to5mac.com/2026/08/24/openai-restores-5-hour-codex-and-work-limits-for-chatgpt-plus-users/)).
  - Le déclencheur doit donc gérer une fenêtre `primary` absente ou nulle.
- **Fenêtre hebdo** : les utilisateurs rapportent qu'elle démarre au premier message et dure 7 jours. Les resets exceptionnels d'OpenAI la remplacent parfois par une nouvelle fenêtre ([#38332](https://github.com/openai/codex/issues/38332), [#38900](https://github.com/openai/codex/issues/38900)) **[communauté]**.
- **`resetsAt`** : il est *nullable* dans le schéma (`RateLimitWindow.resetsAt: integer | null`). L'interprétation « null = fenêtre non démarrée » est plausible mais **non documentée**.
- **Risque de seau séparé** : la « Luna Reserve » a sa propre allocation, utilisée en repli quand les limites normales sont épuisées ([codexusage.dev](https://www.codexusage.dev/luna-reserve)) **[communauté]**. En temps normal, Luna consomme le quota du plan (tableau officiel). Il faut quand même **vérifier après le prompt** que le seau visé (`rateLimitsByLimitId`) a bien démarré, et sinon retenter avec le modèle par défaut.

#### Codes de sortie et erreurs de `codex exec`

D'après le code source :

- `exit(1)` dans chacun de ces cas :
  - erreur `-c` ;
  - `CODEX_HOME` introuvable ;
  - règles invalides ;
  - restriction de login ;
  - absence de dépôt git sans le flag ;
  - échec de lecture de stdin ;
  - notification `error` non retentée sur le tour ;
  - tour `failed` ou `interrupted` ([lib.rs L1080-L1143](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/lib.rs#L1080-L1143)).
- Sinon `0`.
- Le JSONL n'expose **que des messages texte** (`ThreadErrorEvent { message }`). Il faut donc classer les erreurs par texte, ce qui est fragile ; c'est une raison de préférer l'app-server.
- Messages de refresh officiels (source) : « …refresh token has expired / was already used / was revoked. Please log out and sign in again. » ([manager.rs L191-L196](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/login/src/auth/manager.rs#L191-L196)).
- `codex login status` sert de diagnostic sans consommer de quota. Son code de sortie n'est **pas vérifié**.

#### Effet de bord sur le refresh OAuth

- **Oui.** Le refresh a lieu dans deux cas ([manager.rs](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/login/src/auth/manager.rs#L2936-L2945)) :
  - l'access token expire dans moins de 5 min (`CHATGPT_ACCESS_TOKEN_REFRESH_WINDOW_MINUTES = 5`) ;
  - le dernier refresh date de plus de 8 jours (`TOKEN_REFRESH_INTERVAL = 8`).
- En cas de 401, le CLI recharge d'abord `auth.json` depuis le disque si le compte est le même, puis tente un refresh.
- Les tokens rafraîchis sont réécrits dans `auth.json` et `last_refresh` est mis à jour (`persist_tokens`, [L1555-L1577](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/login/src/auth/manager.rs#L1555-L1577)). Doc : « Codex refreshes tokens automatically during use before they expire » ([auth](https://learn.chatgpt.com/docs/auth)).
- Le lecteur actuel (app-server avec `account/rateLimits/read`) passe par le même gestionnaire et peut donc déjà rafraîchir.
- Refresh token **à usage unique** (`refresh_token_reused`) : ne pas lancer en parallèle le lecteur et le prompt sur le même `CODEX_HOME` ; les sérialiser, idéalement dans un seul processus app-server. Sur le LXC, utiliser un login propre (`codex login --device-auth`) plutôt qu'un `auth.json` copié d'un autre poste.

#### Linux sans TTY

- **Piège confirmé par le code** : quand un prompt est passé en argument et que stdin **n'est pas un terminal**, `codex exec` lit stdin **jusqu'à EOF** pour l'ajouter en contexte (`OptionalAppend`, [lib.rs L2049-L2093](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/lib.rs#L2049-L2093), [L2122-L2132](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/lib.rs#L2122-L2132)). Avec un `spawn` Node par défaut (`stdio: 'pipe'`, stdin jamais fermé), **le processus bloque indéfiniment**.
- Avec `stdio: ['ignore', …]` (`/dev/null`), EOF arrive tout de suite. Le buffer vide est ignoré : pas d'erreur, le prompt reste l'argument.
- L'app-server en `stdio://` ne pose pas ce problème : on garde stdin ouvert pour le JSON-RPC, comme `src/codex.ts`.
- Timeout avec kill obligatoire (déjà fait dans `src/codex.ts`).

### 3. Conséquences pour le déclencheur (à reprendre dans #10)

1. Déclencher **uniquement sur le reset de la fenêtre 5h** : `five_hour` pour Claude, `*/primary` pour Codex. Pour Claude, la fenêtre hebdo a une heure fixe. Pour Codex, un prompt envoyé au bon moment ouvre de toute façon aussi l'hebdo s'il est expiré.
2. Après chaque prompt, **relire l'usage** et considérer le déclenchement réussi seulement si `resetsAt` est désormais environ maintenant + 5h. Cette boucle de vérification compense les zones non documentées : démarrage de fenêtre, seaux par modèle, plans sans 5h.
3. Un seul processus CLI à la fois par provider (verrou), stdin fermé, timeout, un compte connecté localement sur le LXC.
4. Mapper les erreurs sur les codes existants de `src/shared.ts` (`AUTH_EXPIRED`, `RATE_LIMITED`, …) et ajouter `USAGE_LIMIT`.

## Non vérifié

- Combinaison exacte des flags Claude (`--safe-mode` + `--max-turns` + `--tools ""` avec `-p`) et coût réel en % : aucun prompt n'a été lancé.
- Démarrage de la fenêtre 5h au premier message, pour les deux providers : non documenté officiellement, seulement des rapports concordants de la communauté.
- `resetsAt == null` ⇒ fenêtre non démarrée : interprétation.
- Nom exact du modèle Codex le moins cher pour ce compte (`gpt-5.6-luna` d'après la doc) : à confirmer via `model/list`.
- Si `claude auth status` rafraîchit le token ; si le token `setup-token` fonctionne sur `/api/oauth/usage` ; code de sortie de `codex login status`.
- `features.shell_tool=false` en 0.153.4.
- L'aide OpenAI [help.openai.com/…/11369540](https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan) et la page [chatgpt.com/codex/pricing](https://chatgpt.com/codex/pricing/) ont répondu 403. J'ai utilisé [learn.chatgpt.com/docs/pricing](https://learn.chatgpt.com/docs/pricing) à la place.

## Sources

Sources primaires :

- Aide locale : `claude --help`, `claude auth status --help` (2.1.270) ; `codex exec --help`, `codex app-server --help`, `codex login --help`, `codex debug --help` (0.153.4) ; schéma `codex app-server generate-json-schema` (0.153.4).
- Claude Code docs : [headless](https://code.claude.com/docs/en/headless), [cli-reference](https://code.claude.com/docs/en/cli-reference), [authentication](https://code.claude.com/docs/en/authentication), [errors](https://code.claude.com/docs/en/errors), [model-config](https://code.claude.com/docs/en/model-config), [env-vars](https://code.claude.com/docs/en/env-vars).
- Claude Code [CHANGELOG](https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md) : 2.1.108, 2.1.117, 2.1.133, 2.1.199, 2.1.211, 2.1.225, 2.1.248.
- Aide Claude : [What is the Max plan? (2026-08-07)](https://support.claude.com/en/articles/11049741-what-is-the-max-plan), [How do usage and length limits work? (2026-07-13)](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work), [Models, usage, and limits in Claude Code (2026-04-15)](https://support.claude.com/en/articles/14552983-models-usage-and-limits-in-claude-code), [Use Claude Code with your Pro or Max plan](https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan).
- Codex docs : [non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode), [app-server](https://learn.chatgpt.com/docs/app-server), [auth](https://learn.chatgpt.com/docs/auth), [models](https://learn.chatgpt.com/docs/models), [pricing](https://learn.chatgpt.com/docs/pricing), [config-reference](https://learn.chatgpt.com/docs/config-file/config-reference).
- Code source Codex au tag `rust-v0.153.4` : [exec/src/lib.rs](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/lib.rs), [exec/src/exec_events.rs](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/exec/src/exec_events.rs), [login/src/auth/manager.rs](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/login/src/auth/manager.rs), [app-server/README.md](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md).

Presse et communauté :

- [9to5Mac 2026-08-24](https://9to5mac.com/2026/08/24/openai-restores-5-hour-codex-and-work-limits-for-chatgpt-plus-users/), [sessionwatcher (2026-08)](https://sessionwatcher.com/guides/codex-rate-limits-explained), [codexusage.dev — Luna Reserve](https://www.codexusage.dev/luna-reserve).
- [dev.to — Claude Wake Up](https://dev.to/ibrahimdans/claude-wake-up-start-your-claude-session-at-5-am-without-getting-up-yourself-4ecn), [dev.to — 5-hour limits](https://dev.to/avsi/how-to-make-best-of-claude-codes-5-hour-limits-4j32).
- Issues : [anthropics/claude-code#12447](https://github.com/anthropics/claude-code/issues/12447), [#28827](https://github.com/anthropics/claude-code/issues/28827), [#37402](https://github.com/anthropics/claude-code/issues/37402), [#9026](https://github.com/anthropics/claude-code/issues/9026), [#78476](https://github.com/anthropics/claude-code/issues/78476), [claude-agent-sdk-python#1031](https://github.com/anthropics/claude-agent-sdk-python/issues/1031), [openai/codex#38332](https://github.com/openai/codex/issues/38332), [#38900](https://github.com/openai/codex/issues/38900), [#20395](https://github.com/openai/codex/issues/20395).
