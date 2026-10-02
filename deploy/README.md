# Déploiement sur le LXC

Cible : `ely-llm-wake-up.elylan` (Debian 13, CT 106), service systemd, derrière HAProxy qui termine le TLS de `https://monitor.llm.elyspio.fr` et transmet en HTTP sur le port 5000. Décisions : « Topologie de déploiement sur le LXC » (#14).

**Mise en service faite le 17 septembre 2026** : le service tourne, le cron est désactivé. La section « Mise en service » ci-dessous reste la procédure de référence pour reconstruire le LXC ; au quotidien, seule « Mises à jour » sert.

## Fichiers

- `Dockerfile` : build de l'artefact sur le poste (SPA dans `wwwroot`, API self-contained `linux-x64` en fichier unique, sans ICU).
- `deploy.ps1` : build Docker, scp, extraction dans `/opt/llm-usage-monitor/` (écrasement en place), redémarrage.
- `llm-usage-monitor.service` : unité systemd durcie (voir « Durcissement »), compte `llm-monitor`.
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
   Le fichier local n'est pas commité (`.gitignore`) ; il transite par un dossier temporaire `700` (`umask 077`, `mktemp -d`) et arrive en `600 llm-monitor` dans `/etc/llm-usage-monitor/`. Sans `-UploadSettings`, `deploy.ps1` refuse de déployer si le fichier manque sur l'hôte. L'unité systemd, elle, est réinstallée à chaque déploiement.
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

## Exposition réseau

Le LXC n'a qu'une interface, `eth0` en `10.0.10.221/24` (passerelle `10.0.10.254`) ; HAProxy (`proxy.elylan`, `10.0.0.20`) l'atteint par routage, et c'est la seule adresse autorisée à poser les en-têtes `X-Forwarded-*` (`ForwardedHeaders:KnownProxies`).

**Liaison de Kestrel** : l'unité écoute par défaut sur `http://0.0.0.0:5000` (`ASPNETCORE_URLS`). La clé `Urls` du fichier de prod la remplace :

```json
"Urls": "http://10.0.10.221:5000;http://127.0.0.1:5000"
```

La boucle locale reste nécessaire : `deploy.ps1` sonde `http://127.0.0.1:5000/health/live`. Une interface unique rend cette liaison peu restrictive : un hôte du LAN joint toujours `10.0.10.221:5000` sans passer par HAProxy (pas de TLS, `X-Forwarded-*` ignorés car il n'est pas un proxy connu, mais l'API reste appelable avec un token).

**Pare-feu (la vraie barrière)** : n'accepter le port 5000 que depuis HAProxy et la boucle locale. Au choix :

- pare-feu Proxmox du CT 106 : règle `IN ACCEPT -source 10.0.0.20 -p tcp -dport 5000`, puis `IN DROP -p tcp -dport 5000` ;
- nftables dans le LXC (aucune règle aujourd'hui, politique `accept`) :
  ```sh
  cat > /etc/nftables.conf <<'NFT'
  #!/usr/sbin/nft -f
  flush ruleset
  table inet filter {
  	chain input {
  		type filter hook input priority filter; policy accept;
  		iif "lo" accept
  		tcp dport 5000 ip saddr 10.0.0.20 accept
  		tcp dport 5000 drop
  	}
  }
  NFT
  systemctl enable --now nftables
  ```

Vérifier ensuite que `https://monitor.llm.elyspio.fr/health/live` répond et qu'un `curl http://10.0.10.221:5000/health/live` depuis un autre hôte du LAN échoue.

## Durcissement

L'unité systemd isole le service et les CLIs, qui tournent en processus enfants avec les mêmes restrictions : système en lecture seule sauf `/var/lib/llm-monitor`, `/home` et `/root` masqués, `/dev` privé, noyau (tunables, modules, logs) et cgroups protégés, familles d'adresses `AF_UNIX`/`AF_INET`/`AF_INET6`/`AF_NETLINK`, aucun namespace, aucune capability, appels système limités à `@system-service` (un appel filtré échoue en `EPERM` sans tuer le process), `umask 077`, `MemoryMax=900M` (pic observé : 640 Mo), arrêt en 30 s au plus. Pas de `MemoryDenyWriteExecute` : .NET et le CLI `claude` (exécutable Bun) ont un JIT.

`systemd-analyze security llm-usage-monitor` : **8.5 EXPOSED** avant (relevé sur le LXC le 2 octobre 2026), **1.5 OK** après (même systemd 257, analyse hors ligne de l'unité).

À chaque modification de l'unité, vérifier après le déploiement que les CLIs fonctionnent toujours : une lecture réussie de chaque provider sur le dashboard, puis un déclenchement manuel de chacun (Claude lance `claude -p`, Codex `codex app-server`), et rien de suspect dans `journalctl -u llm-usage-monitor` (`EPERM`, `Operation not permitted`). Si une directive bloque un CLI, la lever par un drop-in, conservé par `deploy.ps1` qui ne réinstalle que l'unité :

```sh
systemctl edit llm-usage-monitor   # par exemple [Service] puis RestrictNamespaces=false
systemctl restart llm-usage-monitor
```

Le fichier de prod (`LLM_USAGE_MONITOR_SETTINGS`) passe au-dessus des `appsettings*.json` et des variables `ASPNETCORE_*`, mais une variable d'environnement non préfixée (`Oidc__Authority=...`) ou un argument de ligne de commande le remplacent toujours : un drop-in `Environment=` suffit pour une surcharge temporaire.

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


## Sauvegardes

### Base MongoDB

La base `llm-usage-monitor` de `rs-shard-a` contient tout l'état : réglages (token ntfy chiffré), état des providers, historique, déclenchements, usage en tokens, **clés Data Protection** (`dataProtectionKeys`, sans elles le token ntfy est illisible) et jobs Hangfire (`hangfire.*`). Le LXC ne garde rien d'autre que les identifiants CLI.

Si la sauvegarde du shard ne couvre pas déjà cette base, un dump quotidien depuis un hôte qui a `mongodump` (mongodb-database-tools) et un compte `backup` (ou `read` sur la base) :

```sh
# /etc/cron.daily/llm-usage-monitor-dump : 14 dumps conservés
set -e
dir=/var/backups/llm-usage-monitor
mkdir -p "$dir"
mongodump --uri "$MONGO_BACKUP_URI" --db llm-usage-monitor --gzip --archive="$dir/$(date +%F).archive.gz"
find "$dir" -name '*.archive.gz' -mtime +14 -delete
```

Les collections `hangfire.*` peuvent être exclues (`--excludeCollectionsWithPrefix=hangfire`) : les jobs récurrents sont recréés au démarrage, seuls les jobs planifiés en cours seraient perdus (le prochain poll les replanifie).

**Vérification par restauration** (à faire une fois après la mise en place, puis à chaque changement de procédure), dans une base jetable du même cluster ou un Mongo local :

```sh
mongorestore --uri "$MONGO_RESTORE_URI" --gzip --archive=/var/backups/llm-usage-monitor/<date>.archive.gz \
  --nsFrom 'llm-usage-monitor.*' --nsTo 'llm-usage-monitor-restore.*'
mongosh "$MONGO_RESTORE_URI/llm-usage-monitor-restore" --eval '
  ["settings","providerStates","dataProtectionKeys","triggerRuns","tokenUsage"].forEach(c => print(c, db[c].countDocuments()))'
```

Puis lancer l'API en local (`ConnectionStrings:MongoDB` sur la base restaurée, `Hangfire:Enabled=false`) : les réglages s'affichent, `tokenDefined` est vrai et une notification de test part (la clé Data Protection déchiffre le token). Supprimer ensuite la base `llm-usage-monitor-restore`.

Hangfire.Mongo copie ses collections (`CollectionMongoBackupStrategy`, suffixe `migrationbackup`) avant toute migration de schéma, lors d'une mise à jour du paquet : à supprimer à la main une fois la nouvelle version validée.

### Identifiants des CLIs

Les logins vivent dans le home du compte de service et se rafraîchissent en place :

- Claude : `/var/lib/llm-monitor/.claude/.credentials.json` (et `/var/lib/llm-monitor/.claude.json`) ;
- Codex : `/var/lib/llm-monitor/.codex/auth.json`.

Ce sont des secrets (refresh tokens) : pas de copie hors du LXC sans chiffrement. Les tokens tournent (le refresh token Claude est à usage unique) : une copie ancienne ne sert souvent plus, la reconnexion reste la procédure de référence. L'application prévient N jours avant l'expiration du login Claude (Réglages > Notifications) et dès qu'une lecture échoue en `AUTH_EXPIRED`.

Reconnexion (LXC reconstruit, login expiré ou révoqué), depuis une session sur le LXC :

```sh
systemctl stop llm-usage-monitor
sudo -u llm-monitor -H bash -lc 'claude auth login'          # ouvre une URL à valider dans le navigateur
sudo -u llm-monitor -H bash -lc 'codex login --device-auth'  # code à saisir sur la page affichée
sudo -u llm-monitor -H bash -lc 'claude auth status && codex login status'
systemctl start llm-usage-monitor
```

Puis vérifier sur le dashboard une lecture réussie de chaque provider (ou `/health/ready` à `Healthy`).

### Boucle de crash

L'unité systemd redémarre le service 10 s après un échec, au plus 5 démarrages en 10 minutes (`StartLimitIntervalSec`/`StartLimitBurst`) : au-delà, l'unité passe en échec et y reste, `/health/ready` ne répond plus et Uptime Kuma alerte. Après correction : `systemctl reset-failed llm-usage-monitor && systemctl start llm-usage-monitor` (`deploy.ps1` le fait).

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
