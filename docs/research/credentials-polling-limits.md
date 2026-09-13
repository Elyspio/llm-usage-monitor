# Credentials et limites de polling

> Ticket : [#5](https://github.com/Elyspio/llm-usage-monitor/issues/5) (enfant de la map [#1](https://github.com/Elyspio/llm-usage-monitor/issues/1)).
> Recherche du 2026-09-13. Versions locales : Claude Code `2.1.270`, `codex-cli 0.153.4`. Code Codex lu sur `openai/codex@516f278` (main du 2026-09-13).
> Légende : **[officiel]** = doc / code / changelog de l'éditeur ; **[communautaire]** = issue ou outil tiers, non confirmé par l'éditeur ; **[déduction]** = raisonnement à valider par prototype.

## Réponse courte

| Provider | Intervalle recommandé | Plancher | En cas d'échec |
| --- | --- | --- | --- |
| Claude (`/api/oauth/usage`) | **10 min** (+ jitter ±1 min), plus lecture à la demande (bouton « rafraîchir » limité à 1/5 min) | 5 min | 429 → ne **pas** réessayer tout de suite ; backoff 15 → 30 → 60 min, afficher le dernier snapshot « au … » |
| Codex (`account/rateLimits/read`) | **5 min** (1 min si une fenêtre ≥ 75 %) | 60 s | erreur → backoff exponentiel plafonné à 15 min |

- **Token Claude** : l'access token de `.credentials.json` vit ~8 h [communautaire] et **seule la CLI `claude` le rafraîchit**, quand elle s'exécute (verrou inter‑processus) [officiel]. Le service **ne doit pas** utiliser le refresh token lui‑même : rotation du refresh token → la CLI se retrouve déconnectée [communautaire], et c'est hors du cadre d'usage des credentials Claude.ai [officiel]. Le service relit le fichier à chaque poll et, si le token est expiré, signale `AUTH_EXPIRED` (ou fait tourner une commande CLI qui rafraîchit, cf. §1.4).
- **`claude setup-token`** donne un token d'**un an** [officiel] mais limité au scope `user:inference` : il sert pour lancer le prompt « 1+1=? », **pas** pour lire `/api/oauth/usage` (403 `user:profile`) [communautaire, cohérent avec la doc officielle].
- **Login headless** : Claude → `claude auth login` sur le LXC, ouvrir l'URL ailleurs puis coller le code dans le terminal [officiel]. Codex → `codex login --device-auth` (à activer dans les réglages de sécurité ChatGPT) ou copie de `~/.codex/auth.json` [officiel]. Préférer **un login propre au LXC** plutôt que partager le même fichier entre Windows et le LXC.
- **429 Claude** : l'endpoint est non documenté et limite très agressivement (quelques requêtes puis 429 persistants, `retry-after: 0` ou absent) [communautaire, nombreux reports depuis mars 2026] ; la CLI officielle elle‑même prévoit le cas (affiche les dernières valeurs connues quand l'endpoint est rate‑limité) [officiel].
- **Codex** : chaque `account/rateLimits/read` fait un appel HTTP réel à `chatgpt.com/backend-api/wham/usage` (+ un 2ᵉ appel « reset credits » sauf `excludeResetCreditDetails: true`), sans cache, et peut rafraîchir le token au passage [officiel, code]. Ne consomme pas de quota modèle. Le TUI officiel poll lui‑même toutes les **60 s** (jusqu'à 5 s près de l'épuisement) [officiel, code] → 5 min est très conservateur.

## Détails

### 1. Token OAuth Claude (`.credentials.json`)

#### 1.1 Stockage

- Linux : `~/.claude/.credentials.json`, mode `0600`. Windows : `%USERPROFILE%\.claude\.credentials.json`. macOS : Keychain (fallback fichier). `CLAUDE_CONFIG_DIR` déplace le fichier [officiel — Authentication].
- Le fichier est géré par `/login` et `/logout` ; la doc ne décrit pas son format (le lecteur actuel lit `claudeAiOauth.accessToken` / `expiresAt`).

#### 1.2 Durée de vie

- **Access token** : `expiresAt` ≈ 8 h après émission [communautaire — #68398, #37402 et doublons]. Non documenté officiellement.
- **Login (refresh)** : durée non publiée. La CLI avertit « Your login expires in 3 days · run /login to renew » (depuis v2.1.203 ; 5 jours avant v2.1.217) ; une fois expiré et non rafraîchissable, les requêtes échouent avec `Login expired · Please run /login` [officiel — Authentication, CHANGELOG]. → Il faut prévoir un **re‑login manuel périodique** et une notification côté app.
- **`claude setup-token`** : token OAuth d'**un an**, imprimé une seule fois, jamais sauvegardé, à passer via `CLAUDE_CODE_OAUTH_TOKEN` ; il « ne peut faire que des requêtes modèle » [officiel — Authentication]. Il ne demande que `user:inference`, alors que `/api/oauth/usage` exige `user:profile` → 403 [communautaire — #22450].

#### 1.3 Qui rafraîchit, quand

- **La CLI `claude`**, pendant son exécution (session interactive, `claude -p`, SDK…). Le CHANGELOG officiel confirme un refresh automatique avec **verrou de refresh inter‑processus** (v2.1.248 : un process qui trouve le verrou tenu renvoie une erreur réessayable), et plusieurs correctifs de courses entre processus (v2.1.265, v2.1.269) [officiel — CHANGELOG].
- Historique de bugs de refresh (token non rafraîchi en `--print`, fichier vidé, re‑login quotidien) — tous fermés/stale, versions 2.1.81 à 2.1.181 [communautaire — #37402, #68398, #71757, #50743]. À surveiller mais la version actuelle (2.1.270) contient de nombreux correctifs.
- **Un poll du service n'exécute pas la CLI → il ne rafraîchit rien.** Si aucune session `claude` ne tourne pendant > ~8 h, le poll finira en `AUTH_EXPIRED`.

#### 1.4 Le service peut‑il rafraîchir lui‑même ? — **Non recommandé**

- Techniquement possible (POST sur l'endpoint OAuth avec le `client_id` de Claude Code — détails publiés dans des issues) mais les **refresh tokens sont à usage unique** : le service écrirait une nouvelle paire que la CLI (qui garde des infos en mémoire) ne connaît pas → la CLI se déconnecte / re‑login toutes les quelques minutes [communautaire — commentaires de #30930 et #31637].
- La page *Legal and compliance* réserve l'OAuth Claude.ai à l'usage ordinaire de Claude Code et des apps Anthropic, et interdit aux développeurs tiers de collecter, stocker ou relayer ces tokens [officiel]. Un service perso qui **lit** le token de sa propre CLI reste dans une zone grise (endpoint non documenté) ; **se substituer à la CLI pour le refresh** en sort clairement.
- **Recommandation** [déduction] :
  1. Le lecteur reste en lecture seule et **relit le fichier à chaque poll** (jamais de token gardé en mémoire).
  2. Si `expiresAt` est proche/dépassé : déclencher un process `claude` qui rafraîchit. Candidat sans coût : `claude auth status` — **non vérifié** qu'il déclenche un refresh (à prototyper). Repli garanti : le prompt « 1+1=? » via `claude -p`, **mais il démarre une fenêtre 5 h** → c'est une décision produit, pas un simple keep‑alive.
  3. Sinon : état `AUTH_EXPIRED` + notification « relancer `claude auth login` sur le LXC ».

### 2. Login headless sur Linux

#### Claude

- `claude auth login` (options `--claudeai` par défaut, `--console`, `--sso`, `--email`) ou `/login` dans `claude` [officiel — `claude auth login --help`].
- Si le navigateur ne peut pas joindre le callback local (SSH, conteneurs, WSL2), la page affiche un code à coller au prompt « Paste code here if prompted » [officiel — Authentication]. → Sur le LXC : lancer la commande en SSH, ouvrir l'URL sur le poste, coller le code.
- Pas de device‑code flow Claude documenté.
- Copier `.credentials.json` Windows → LXC : même format, rien d'officiel dessus. Deux machines partageant la même paire de tokens se piétinent au refresh (rotation) [déduction à partir des reports communautaires]. → **Faire un login dédié sur le LXC.**
- `claude auth status --json` / `--text` permet au service de vérifier l'état du login sans lire le fichier [officiel — `--help`].

#### Codex

- `codex login --device-auth` (beta) : il faut d'abord activer la connexion par device code dans les réglages de sécurité ChatGPT (compte perso) [officiel — Codex Auth].
- Alternative documentée : copier `~/.codex/auth.json` sur la machine headless (ex. via `scp`) ; le fichier est à traiter comme un mot de passe [officiel — Codex Auth].
- Stockage configurable par `cli_auth_credentials_store` = `file` | `keyring` | `auto` | `ephemeral` [officiel]. Sur un LXC sans keyring, `file` (défaut effectif) convient.
- Refresh : Codex rafraîchit automatiquement pendant l'usage [officiel]. Dans le code : refresh proactif si l'access token expire dans < 5 min ou si `last_refresh` date de > 8 jours ; `auth()` est appelé avant chaque lecture des rate limits [officiel — `login/src/auth/manager.rs`, `account_processor.rs`]. → **Poller Codex garde son token à jour tout seul**, via le binaire officiel.
- Copie partagée entre 2 machines : le serveur OpenAI tolère la réutilisation d'un refresh token pendant ~1 h, au‑delà `refresh_token_reused` → re‑login [officiel — réponse d'un mainteneur OpenAI dans openai/codex#10332 ; reports persistants dans #19803]. → Après copie, n'utiliser le fichier que sur une machine, ou préférer `--device-auth`. Ne pas lancer `codex logout` sur la machine source en pensant « nettoyer » : un endpoint de révocation existe dans le code (`auth.openai.com/oauth/revoke`) et pourrait invalider la copie [déduction — non vérifié].

### 3. 429 de `api.anthropic.com/api/oauth/usage`

- Endpoint **non documenté** ; aucune limite officielle publiée.
- Reports depuis début mars 2026 : 429 `rate_limit_error` persistants, `retry-after: 0` ou absent, qui durent des heures même avec backoff jusqu'à 5–30 min ; poll à 10 min → 429 en moins d'une heure pour un utilisateur ; limite estimée « ~5 requêtes par access token » [communautaire — #30930, #31021, #31637 ; tous fermés « inactifs », sans réponse d'Anthropic].
- Contournement signalé : envoyer `User-Agent: claude-code/<version>` ferait passer dans un bucket moins restrictif [communautaire, non confirmé — artem-from-ua/claude-plugins#329]. Usurper l'UA de la CLI est discutable ; à décider (et à tester) plutôt qu'à adopter par défaut.
- Côté officiel, la CLI traite le cas : `/usage` affiche les dernières valeurs avec une mention « as of » quand l'endpoint est rate‑limité (v2.1.208), et gère la perte d'une ligne en cas de rate‑limit (v2.1.261) [officiel — CHANGELOG]. C'est le comportement à reproduire.
- `Retry-After` : **ne pas s'y fier** (0 ou absent). Utiliser son propre backoff.
- Source alternative sans appel réseau : la CLI passe `rate_limits.five_hour` / `seven_day` (`used_percentage`, `resets_at`) au script de status line, pour Pro/Max, après la première réponse API de la session [officiel — Status line]. Utile seulement si une session `claude` tourne ; pas une source de polling pour un service de fond.

### 4. `codex app-server` → `account/rateLimits/read`

- Par appel : `auth()` (refresh proactif éventuel) puis `GET {chatgpt_base_url}/wham/usage` ; en parallèle un 2ᵉ appel aux « reset credits » sauf si `excludeResetCreditDetails: true` ; aucun cache dans l'app‑server [officiel — `app-server/src/request_processors/account_processor.rs`, `backend-client/src/client/rate_limit_resets.rs`].
- Le paramètre `excludeResetCreditDetails` est documenté comme destiné « aux polls d'usage en arrière‑plan » ; le TUI officiel le passe à `true` sur ses polls périodiques [officiel — `GetAccountRateLimitsParams.ts`, `tui/src/app/background_requests.rs`]. → **Le lecteur `src/codex.ts` devrait envoyer `params: { excludeResetCreditDetails: true }`.**
- Le TUI officiel poll `account/rateLimits/read` toutes les **60 s** (30 s ≥ 75 %, 15 s ≥ 90 %, 5 s ≥ 99 %) [officiel — `tui/src/chatwidget/rate_limits.rs`]. Aucune limite de débit publiée pour `wham/usage` ; aucun report de 429 trouvé.
- Coût réel côté service : **le spawn du process** `codex app-server` à chaque poll (démarrage, lecture config, éventuelle écriture d'`auth.json`). Alternative : garder un app‑server stdio vivant et réutiliser la connexion ; `account/rateLimits/updated` n'envoie que des mises à jour partielles (pendant les tours), à fusionner avec le dernier `read` [officiel — schéma `AccountRateLimitsUpdatedNotification`]. Il existe aussi `codex app-server daemon` (start/stop/bootstrap) [officiel — `--help`].
- Aucune consommation de quota modèle (pas de thread ni de turn).

### 5. Intervalles — justification

- **Claude** : aucun chiffre officiel. Les reports montrent des 429 dès 30–60 s et parfois à 10 min ; un commentateur conseille ≤ 1 appel / 5 min avec cache jusqu'au `resets_at` [communautaire]. 10 min laisse une marge ; les fenêtres Claude font 5 h et 7 j, donc 10 min de latence est sans impact sur le dashboard. Déclencheur post‑reset : lire une fois juste après `resets_at` (+1 min) plutôt que resserrer le polling.
- **Codex** : 60 s est la cadence du client officiel ; 5 min suffit pour un dashboard et limite les spawns.
- Les deux : jitter, un seul poll en vol par provider, snapshot persisté avec horodatage, pas de retry immédiat sur 401/403 (problème de login, pas transitoire).

### Non vérifié / incertain

- Durée exacte de l'access token Claude (~8 h) et du login : uniquement observée par la communauté.
- Le quota exact et le mécanisme du rate‑limit de `/api/oauth/usage` (par token ? par UA ?) : pas de source officielle.
- Si `claude auth status` déclenche un refresh du token : à prototyper sur le LXC.
- Effet d'un `codex logout` sur une copie d'`auth.json` : déduit du code, non testé.
- Comportement sur un compte Max vs Pro, et évolution de l'endpoint (non documenté, peut changer sans préavis).

## Sources

Officielles
- Claude Code — Authentication : https://code.claude.com/docs/en/authentication
- Claude Code — Legal and compliance (usage des credentials OAuth) : https://code.claude.com/docs/en/legal-and-compliance
- Claude Code — Status line (`rate_limits`) : https://code.claude.com/docs/en/statusline
- Claude Code — Errors (login expiré, refresh) : https://code.claude.com/docs/en/errors
- Claude Code — CHANGELOG (v2.1.203, 2.1.208, 2.1.217, 2.1.248, 2.1.261, 2.1.265, 2.1.269) : https://github.com/anthropics/claude-code/blob/main/CHANGELOG.md
- `claude setup-token --help`, `claude auth login --help`, `claude auth status --help` (v2.1.270, exécutés localement)
- Codex — Authentication : https://learn.chatgpt.com/docs/auth (redirigé depuis https://developers.openai.com/codex/auth)
- `codex login --help`, `codex app-server --help`, `codex app-server daemon --help` (codex-cli 0.153.4)
- openai/codex @516f278 :
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/app-server/src/request_processors/account_processor.rs
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/backend-client/src/client/rate_limit_resets.rs
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/login/src/auth/manager.rs
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/app-server-protocol/schema/typescript/v2/GetAccountRateLimitsParams.ts
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/tui/src/chatwidget/rate_limits.rs
  - https://github.com/openai/codex/blob/516f2780fd227a80cd9fe89488f5039245090b71/codex-rs/tui/src/app/background_requests.rs
- openai/codex#10332 (réponse mainteneur sur la tolérance de réutilisation du refresh token) : https://github.com/openai/codex/issues/10332

Communautaires
- anthropics/claude-code#30930 (429 persistants, `retry-after: 0`) : https://github.com/anthropics/claude-code/issues/30930
- anthropics/claude-code#31021 : https://github.com/anthropics/claude-code/issues/31021
- anthropics/claude-code#31637 (poll 10 min → 429, backoff inefficace) : https://github.com/anthropics/claude-code/issues/31637
- anthropics/claude-code#37402, #68398, #71757, #50743 (expiration ~8 h, bugs de refresh) : https://github.com/anthropics/claude-code/issues/37402 · https://github.com/anthropics/claude-code/issues/68398 · https://github.com/anthropics/claude-code/issues/71757 · https://github.com/anthropics/claude-code/issues/50743
- anthropics/claude-code#22450 (`setup-token` = `user:inference`, usage exige `user:profile`) : https://github.com/anthropics/claude-code/issues/22450
- artem-from-ua/claude-plugins#329 (hypothèse User-Agent) : https://github.com/artem-from-ua/claude-plugins/issues/329
- openai/codex#19803 (`refresh_token_reused`) : https://github.com/openai/codex/issues/19803
