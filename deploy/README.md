# Déploiement sur le LXC

Cible : `ely-llm-wake-up.elylan` (Debian 13, CT 106), service systemd, derrière HAProxy qui termine le TLS de `https://monitor.llm.elyspio.fr` et transmet en HTTP sur le port 5000. Décisions : « Topologie de déploiement sur le LXC » (#14).

## Fichiers

- `Dockerfile` : build de l'artefact sur le poste (SPA dans `wwwroot`, API self-contained `linux-x64` en fichier unique, sans ICU).
- `deploy.ps1` : build Docker, scp, extraction dans `/opt/llm-usage-monitor/` (écrasement en place), redémarrage.
- `llm-usage-monitor.service` : unité systemd durcie, compte `llm-monitor`.
- `appsettings.Production.example.json` : modèle de `/etc/llm-usage-monitor/appsettings.Production.json` (jamais commité).

## Mise en service (une fois)

Prérequis hors de ce repo, voir « Infra prod » (#21) : client Keycloak de prod `i-llm-usage-monitor` (redirect URIs `/auth/callback`, `/signin-oidc`, `/swagger/oauth2-redirect.html`), backend HAProxy, user Mongo `llm-usage-monitor` (`readWrite` sur `llm-usage-monitor` et `hangfire`), CA elylan installée sur le LXC.

1. Compte de service et CLIs :
   ```sh
   useradd --system --home-dir /var/lib/llm-monitor --create-home --shell /usr/sbin/nologin llm-monitor
   apt install -y ca-certificates libssl3
   sudo -u llm-monitor -H bash -c 'curl -fsSL https://claude.ai/install.sh | bash'
   # Codex : même installation que pour root, sous le compte llm-monitor.
   sudo -u llm-monitor -H bash -lc 'claude auth login'
   sudo -u llm-monitor -H bash -lc 'codex login --device-auth'
   ```
2. Configuration :
   ```sh
   install -d -o root -g llm-monitor -m 750 /etc/llm-usage-monitor
   install -o llm-monitor -g llm-monitor -m 600 appsettings.Production.json /etc/llm-usage-monitor/
   install -m 644 llm-usage-monitor.service /etc/systemd/system/
   systemctl daemon-reload
   systemctl enable llm-usage-monitor
   ```
   Renseigner le mot de passe Mongo et l'IP de HAProxy (`ForwardedHeaders:KnownProxies`), sans quoi les redirections OIDC partent en `http`.
3. Premier déploiement depuis le poste : `./deploy/deploy.ps1`.
4. Bascule depuis le cron :
   ```sh
   mv /etc/cron.hourly/llm-wake-up /etc/cron.hourly/llm-wake-up.disabled
   ```
5. Vérifications : connexion sur `https://monitor.llm.elyspio.fr`, lectures des deux providers sur le dashboard, un déclenchement manuel, traces du service `llm-usage-monitor` dans Jaeger, notification de test depuis Réglages.

Le déclenchement automatique est actif par défaut en prod (`App:AutoTriggerEnabledByDefault`) ; il se coupe par provider dans Réglages.

## Mises à jour

```sh
./deploy/deploy.ps1
```

Build et tests verts en local avant (voir `AGENTS.md`). Retour arrière : redéployer la version précédente (`git checkout <tag>` puis `./deploy/deploy.ps1`).

## Retour arrière complet vers le cron

```sh
systemctl disable --now llm-usage-monitor
mv /etc/cron.hourly/llm-wake-up.disabled /etc/cron.hourly/llm-wake-up
```

Après deux semaines stables : supprimer `/etc/cron.hourly/llm-wake-up.disabled` et les logins root (`/root/.claude`, `/root/.codex`).

## Notes

- La télémétrie envoie les traces (Hangfire compris) au collector en HTTP/protobuf. `Elyspio.Utils.Telemetry` ajoute aussi un exporteur de métriques non désactivable : ses envois vers `/v1/metrics` échouent en silence sur un collector limité aux traces.
- Les CLIs gardent leur mise à jour automatique : un changement de comportement se verra par les erreurs de lecture, notifiées.
