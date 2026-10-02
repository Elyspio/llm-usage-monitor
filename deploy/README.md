# Déploiement sur le LXC

Cible : `ely-llm-wake-up.elylan` (Debian 13, CT 106), service systemd, derrière HAProxy qui termine le TLS de `https://monitor.llm.elyspio.fr` et transmet en HTTP sur le port 5000. Décisions : « Topologie de déploiement sur le LXC » (#14).

**Mise en service faite le 17 septembre 2026** : le service tourne, le cron est désactivé. La section « Mise en service » ci-dessous reste la procédure de référence pour reconstruire le LXC ; au quotidien, seule « Mises à jour » sert.

## Fichiers

- `Dockerfile` : build de l'artefact sur le poste (SPA dans `wwwroot`, API self-contained `linux-x64` en fichier unique, sans ICU).
- `deploy.ps1` : build Docker, scp, extraction dans `/opt/llm-usage-monitor/` (écrasement en place), redémarrage.
- `llm-usage-monitor.service` : unité systemd durcie, compte `llm-monitor`.
- `appsettings.Production.example.json` : modèle de `/etc/llm-usage-monitor/appsettings.Production.json` (jamais commité).

## Mise en service (une fois)

Prérequis hors de ce repo, voir « Infra prod » (#21) : client Keycloak de prod `i-llm-usage-monitor` (redirect URIs `/auth/callback`, `/signin-oidc`, `/swagger/oauth2-redirect.html`), backend HAProxy, user Mongo `llm-usage-monitor` (`readWrite` sur la base `llm-usage-monitor`, qui contient aussi les collections `hangfire.`), CA elylan installée sur le LXC.

1. Compte de service et CLIs :
   ```sh
   useradd --system --home-dir /var/lib/llm-monitor --create-home --shell /usr/sbin/nologin llm-monitor
   apt install -y ca-certificates libssl3
   sudo -u llm-monitor -H bash -c 'curl -fsSL https://claude.ai/install.sh | bash'
   sudo -u llm-monitor -H bash -c 'curl -fsSL https://chatgpt.com/codex/install.sh | sh'
   # Codex : même installation que pour root, sous le compte llm-monitor.
   sudo -u llm-monitor -H bash -lc 'claude auth login'
   sudo -u llm-monitor -H bash -lc 'codex login --device-auth'
   ```
2. Configuration : remplir une copie locale de `appsettings.Production.example.json` (mot de passe Mongo, IP de HAProxy dans `ForwardedHeaders:KnownProxies` — `10.0.0.20` = `proxy.elylan`, sans quoi les redirections OIDC partent en `http`), puis l'installer depuis le poste :
   ```powershell
   ./deploy/deploy.ps1 -UploadSettings deploy/appsettings.Production.json
   ```
   Le fichier local n'est pas commité (`.gitignore`) ; il arrive en `600 llm-monitor` dans `/etc/llm-usage-monitor/`. Sans `-UploadSettings`, `deploy.ps1` refuse de déployer si le fichier manque sur l'hôte. L'unité systemd, elle, est réinstallée à chaque déploiement.
3. Bascule du cron, une fois l'application déployée et avant de la laisser déclencher :
   ```sh
   mv /etc/cron.hourly/llm-wake-up /etc/cron.hourly/llm-wake-up.disabled
   ```
4. Vérifications : connexion sur `https://monitor.llm.elyspio.fr`, lectures des deux providers sur le dashboard, un déclenchement manuel, traces du service `llm-usage-monitor` dans Jaeger, notification de test depuis Réglages.
5. Après deux semaines de fonctionnement stable : supprimer `/etc/cron.hourly/llm-wake-up.disabled` et les logins `claude` / `codex` de `root` (le service n'utilise que ceux de `llm-monitor`).

Le déclenchement automatique est actif par défaut en prod (`App:AutoTriggerEnabledByDefault`) ; il se coupe par provider dans Réglages.

## Mises à jour

```sh
./deploy/deploy.ps1
```

Build et tests verts en local avant (voir `AGENTS.md`).

Retour arrière d'une version : redéployer la précédente (`git checkout <commit>` puis `./deploy/deploy.ps1`).

Retour arrière complet, vers le cron :

```sh
systemctl disable --now llm-usage-monitor
mv /etc/cron.hourly/llm-wake-up.disabled /etc/cron.hourly/llm-wake-up
```

Le cron tourne sous `root`, avec les logins CLI de `root` : ils doivent donc exister tant que ce retour arrière reste une option.

## Supervision

Toutes les alertes de l'application partent d'elle-même via ntfy : si le process, Hangfire ou MongoDB tombent, elle ne prévient personne. Uptime Kuma la surveille donc de l'extérieur, par deux sondes anonymes dont la réponse est le statut seul (`Healthy`, `Degraded` ou `Unhealthy`) :

| Sonde | Vérifie | Statut HTTP |
| --- | --- | --- |
| `/health/live` | le process répond | 200 |
| `/health/ready` | ping MongoDB ; heartbeat Hangfire de moins de 2 min ; par provider, une lecture réussie depuis moins de 3 intervalles de poll (la fin d'un backoff 429 compte comme point de départ) | 200 `Healthy` ; 200 `Degraded` si un seul provider est en retard (ses échecs sont déjà notifiés par l'app) ; 503 `Unhealthy` si MongoDB, Hangfire ou les deux providers sont KO |

`deploy.ps1` attend `/health/live` après le redémarrage (échec du déploiement sinon) et affiche `/health/ready`.

Moniteur dans l'Uptime Kuma existant (une fois) :

1. Paramètres > Notifications : une notification ntfy sur un topic **distinct** de celui de l'application (canal indépendant : il doit fonctionner quand l'app est morte), priorité haute.
2. Ajouter un moniteur :
   - type **HTTP(s) - Mot-clé**, nom `LLM Usage Monitor`, URL `https://monitor.llm.elyspio.fr/health/ready` ;
   - mot-clé `Healthy` (sensible à la casse) : un `Degraded` ou un 503 passe le moniteur en panne ;
   - intervalle 60 s, 2 nouvelles tentatives à 60 s (un redémarrage ou un poll en cours ne doit pas alerter), délai d'expiration 30 s ;
   - codes HTTP acceptés `200-299` ; notification : celle du point 1.
3. Vérifier : `systemctl stop llm-usage-monitor` sur le LXC fait passer le moniteur en panne et envoie la notification en 3 minutes environ, puis `systemctl start llm-usage-monitor` le rétablit.

Pour ne surveiller que les pannes franches (sans les `Degraded`), un moniteur **HTTP(s)** simple sur la même URL suffit : seul le 503 le fait échouer.


## Rétention des données

Appliquée au démarrage par `MongoStorageInitializer` (index TTL, `collMod` sur l'existant) et par un job Hangfire quotidien :

| Données | Durée | Mécanisme |
| --- | --- | --- |
| `usageSnapshots` (time-series) | 30 jours | `expireAfterSeconds` de la collection, remis à jour par `collMod` si elle a été créée avec une autre valeur |
| `resets` | 90 jours | index TTL sur `detectedAt` |
| `triggerRuns` | 90 jours | index TTL sur `startedAt` (l'index de tri existant devient TTL par `collMod`) |
| jobs Hangfire réussis / supprimés | 1 jour | expiration par défaut de Hangfire |
| jobs Hangfire en échec | 7 jours | job récurrent `purge-failed-jobs` (5 h UTC) : passés en `Deleted`, ils expirent le lendemain |
| `tokenUsage`, `settings`, `providerStates`, `modelPrices`, `usageMachines`, `dataProtectionKeys` | sans expiration | — |

`collMod` demande plus que `readWrite` (rôle `dbAdmin` sur la base). Sans ce droit, l'application démarre quand même et journalise la commande à lancer à la main (`Retention not applied...`). Une fois, avec un compte admin de `rs-shard-a` :

```js
use llm-usage-monitor
db.runCommand({ collMod: "usageSnapshots", expireAfterSeconds: 2592000 })
db.runCommand({ collMod: "triggerRuns", index: { keyPattern: { startedAt: -1 }, expireAfterSeconds: 7776000 } })
```

L'historique est agrégé par MongoDB : un point par fenêtre et par tranche de 5 min sur 24 h, d'une heure sur 7 jours (la dernière lecture de la tranche).

## Notes

- Le shell de `root` sur le LXC est fish : les scripts distants de `deploy.ps1` sont passés à `bash` par l'entrée standard, jamais au shell de connexion.
- La télémétrie envoie les traces (Hangfire compris) au collector en HTTP/protobuf. `Elyspio.Utils.Telemetry` ajoute aussi un exporteur de métriques non désactivable : ses envois vers `/v1/metrics` échouent en silence sur un collector limité aux traces.
- Les CLIs gardent leur mise à jour automatique : un changement de comportement se verra par les erreurs de lecture, notifiées.
