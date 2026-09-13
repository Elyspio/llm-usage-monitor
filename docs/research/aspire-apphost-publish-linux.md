# Aspire 13.5 : AppHost, worker de fond, publish sur hôte Linux

> Ticket de recherche [#3](https://github.com/Elyspio/llm-usage-monitor/issues/3), enfant de la map [#1](https://github.com/Elyspio/llm-usage-monitor/issues/1).
> Rédigé le 2026-09-13. Versions : CLI Aspire locale **13.5.3** (commit `b5f1433`, publiée le 2026-08-25) ; sources lues au tag `v13.5.3` de `microsoft/aspire`.
> Méthode : docs aspire.dev (via `aspire docs get`), notes de version GitHub, code source au tag, et une **expérience jetable** (`aspire publish` sur un AppHost C# 13.5.3 hors du repo, avec et sans dashboard). Les points non vérifiés sont signalés par ⚠️.

## Réponse courte

1. **AppHost : C# par défaut, TypeScript viable.** L'AppHost TypeScript est GA depuis 13.5 (le diagnostic `ASPIREATS001` a disparu). Les quatre intégrations dont on a besoin (Docker, MongoDB, JavaScript, Keycloak) sont exportées vers TypeScript. Le C# garde l'avantage : continuité avec la référence, API expérimentales parfois C#-only, tests d'intégration `Aspire.Hosting.Testing` documentés côté .NET uniquement. De toute façon, l'orchestration tourne sur un hôte .NET dans les deux cas.
2. **Versions 13.5.3 :** `Aspire.Hosting.Docker`, `.MongoDB` et `.JavaScript` sont stables en 13.5.3 ; `Aspire.Hosting.Keycloak` est **toujours en preview** (`13.5.3-preview.1.26425.3`). Images par défaut : `quay.io/keycloak/keycloak:26.6`, `docker.io/library/mongo:8.3`. **Ne pas mélanger 13.4.x et 13.5.x** : c'est un problème connu, avec des `TypeLoadException` sur `Aspire.Hosting.JavaScript`.
3. **Worker de fond :** un **projet/process dédié** est recommandé, parce que c'est le seul composant qui a besoin des CLIs et des credentials. Pour Aspire, c'est une ressource à part entière (logs, traces, `OTEL_SERVICE_NAME` et restart qui lui sont propres). Un `BackgroundService` hébergé dans l'API reste possible, mais il couple l'image de l'API aux CLIs.
4. **Publish sur hôte unique :** avec `AddDockerComposeEnvironment`, `aspire publish` produit `docker-compose.yaml`, un `.env` à remplir et un Dockerfile par app JS. `aspire deploy` fait en plus le build des images et le remplissage du `.env`, puis lance `docker compose up -d` **sur le runtime Docker/Podman local**. Aspire ne génère **ni unité systemd ni rien pour un process hôte** : un `AddExecutable` disparaît silencieusement de la sortie (vérifié).
5. **Accès aux CLIs `claude`/`codex` :** Aspire ne sait pas publier un process hôte. Deux voies :
   - **(B, recommandée)** le worker tourne en **process hôte systemd** sur le LXC, exclu du publish (`ExcludeFromManifest()`), et le reste tourne en compose ;
   - **(A)** le worker tourne en conteneur avec les CLIs installées dans l'image et `~/.claude` / `~/.codex` montés en bind.
6. **OpenTelemetry :**
   - **En dev**, DCP injecte `OTEL_EXPORTER_OTLP_ENDPOINT` (dashboard), `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` et des délais courts.
   - **En publish, le cœur n'injecte rien.** Seul l'environnement compose ajoute un conteneur dashboard, et il **écrase** tout endpoint OTLP personnalisé (endpoint `http://<env>-dashboard:18889`, protocole `grpc`, nom du service = nom de la ressource ; vérifié).
   - **Pour le collector de prod**, il faut `WithDashboard(enabled: false)` et poser explicitement `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL` et `OTEL_SERVICE_NAME` en mode publish.

## Détails

### 1. AppHost C# vs TypeScript

| Critère | C# (`Aspire.AppHost.Sdk/13.5.3`) | TypeScript (`apphost.mts`) |
|---|---|---|
| Statut | Stable, historique | **GA en 13.5** (plus besoin de `ASPIREATS001`) [R1][D1] |
| Architecture | Process .NET | Code « invité » TS qui parle en JSON-RPC à un serveur d'orchestration **.NET** ; intégrations et publishers restent en .NET [D2] |
| Intégrations Docker / MongoDB / JavaScript / Keycloak | Oui | Oui : attributs `[AspireExport]` dans les 4 packages (dont `withRealmImport`, `publishAsStaticWebsite`) [S1] ; la doc donne des exemples TS pour chacune [D3][D4][D5][D6] |
| Écarts connus | Référence de fait | MongoDB : `withInitFiles` (copie) au lieu de `WithInitBindMount` [D4]. `withTerminal()` sans options (dimensions C# uniquement) [D1]. API expérimentales exposées « quand l'export existe » |
| Outillage | `dotnet run` / `aspire run`, IDE .NET | `aspire run` ; toolchain détectée via `packageManager`/lockfile (pnpm ≥ 10 supporté) ; `tsc --noEmit` avant démarrage ; Node `^20.19 \|\| ^22.13 \|\| >=24` [D7] |
| Tests d'intégration | `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`) [D8] | ⚠️ la doc de test ne décrit que le package .NET ; aucun équivalent TS n'est documenté |
| Régressions 13.5 | Aucune pertinente | 13.5.1 corrige un crash d'AppHost polyglotte quand le SDK 13.5 est lancé par une CLI 13.4 [R2] |

**Lecture :** le choix de langage d'AppHost n'est pas bloquant, les deux savent orchestrer un projet .NET (`addProject('api', '../Api/Api.csproj')`) et une app Node/Vite. Le C# reste le plus sûr : parité complète, tests d'intégration documentés, et même base que la référence (13.4.6). Pour migrer la référence vers 13.5, il faut monter **tous** les packages `Aspire.Hosting.*` d'un coup [D1 « Known issues »].

### 2. Intégrations : état et versions (NuGet, 2026-09-13)

| Package | Version | Statut | Points utiles |
|---|---|---|---|
| `Aspire.Hosting.Docker` | 13.5.3 | stable | Cible compose. `WithDashboard`, `ConfigureComposeFile`, `ConfigureEnvFile`, `PublishAsDockerComposeService` [D3] |
| `Aspire.Hosting.MongoDB` | 13.5.3 | stable | Image par défaut `mongo:8.3` (`MongoDBContainerImageTags.cs`) ; `WithDataVolume` → volume nommé en compose ; mot de passe → paramètre `${MONGO_PASSWORD}` [D4][S2] |
| `Aspire.Hosting.JavaScript` | 13.5.3 | stable (API de publish **expérimentales**, `ASPIREJAVASCRIPT001`) | `AddViteApp(name, dir, runScriptName="dev")`, `WithPnpm(install, installArgs)` ; à ne pas combiner avec `WithHttpEndpoint()` (endpoint `http` déjà créé) [D5] |
| `Aspire.Hosting.Keycloak` | 13.5.3-preview.1.26425.3 | **preview** | Image `keycloak:26.6` ; `start-dev` en run, `start` en publish, toujours `--import-realm` [S3] |
| `Aspire.Hosting.Yarp` | 13.5.3 | stable | Utilisé implicitement par `PublishAsStaticWebsite` |

**Keycloak `WithRealmImport` :**
- **En run**, les fichiers du dossier sont *copiés* dans `/opt/keycloak/data/import` (`WithContainerFiles`) [S3][S4].
- **En publish**, `WithContainerFiles` devient un **bind mount en lecture seule** [S4]. Vérifié dans l'expérience : volume `bind` → `/opt/keycloak/data/import`, source `${KEYCLOAK_BINDMOUNT_0}`, et la commande `start --import-realm`.
- La doc aspire.dev dit au contraire que `WithRealmImport` n'est « pas supporté » en publish/deploy et conseille de cuire les realms dans une image [D6] : **la doc et le code divergent**.
- Peu importe pour nous : la prod utilise `auth.elyspio.fr`. On ajoute donc Keycloak **uniquement en run mode** (`if (builder.ExecutionContext.IsRunMode)`) et on passe l'autorité OIDC par paramètre en publish.
- ⚠️ Non vérifié : un Keycloak publié en `start` (mode production) exigerait de toute façon une config hostname/TLS que l'intégration ne pose pas.

**Vite en publish :**
- Le serveur de dev Vite n'existe qu'en run. En publish, il faut choisir qui sert le build [D9] :
  - `PublishAsStaticWebsite("/api", api)` : conteneur YARP qui sert `dist/` et proxifie `/api` ;
  - `PublishWithContainerFiles` : copie dans le conteneur de l'API ;
  - une passerelle YARP explicite.
- Expérience, avec `WithPnpm()` + `PublishAsStaticWebsite` :
  - Dockerfile généré : `node:22-slim` → `npm i -g pnpm@10.30.1` → `pnpm install` (`--frozen-lockfile` si `pnpm-lock.yaml` existe) → `pnpm run build`, puis runtime `mcr.microsoft.com/dotnet/nightly/yarp:2.3-preview` ;
  - service `front` avec routes `REVERSEPROXY__*` vers `https+http://api`.
- ⚠️ Pour Vite+ (`vp`), le script `build` du `package.json` doit appeler `vp build` : non testé.
- Le port fixe 3000 du front en dev (redirect URIs Keycloak) reste un sujet dev uniquement.

### 3. Héberger un worker de fond

| Option | Pour | Contre |
|---|---|---|
| `BackgroundService` dans l'API (`AddHostedService<T>()`) [M1] | Un seul process/image ; accès direct aux services de l'API | L'API hérite des CLIs, des credentials et des mounts ; un crash du worker arrête l'hôte (voir ci-dessous) ; pas de restart séparé |
| **Projet dédié** (template Worker, `Microsoft.NET.Sdk.Worker`) [M1], `AddProject<Projects.Worker>("worker")` | Ressource distincte dans le dashboard (logs/traces/`OTEL_SERVICE_NAME` propres) ; `WaitFor(mongo)` ; service compose séparé avec sa propre `restart:` (vérifié : `svc.Restart = "unless-stopped"` via `PublishAsDockerComposeService`) ; peut sortir du compose (option B §5) | Deux projets ; l'API doit communiquer avec le worker (via Mongo, voir ticket « robustesse du déclencheur ») |
| Process Node (`AddNodeApp` / `AddJavaScriptApp`) si les lecteurs `src/*.ts` sont gardés tels quels | Réutilise `src/claude.ts` / `src/codex.ts` sans portage | Instrumentation OTel Node à écrire soi-même [D10] ; en publish, doit être containerisé ou exclu |

Depuis .NET 6, une exception non gérée dans `BackgroundService.ExecuteAsync` est **loggée et arrête l'hôte** par défaut (`HostOptions.BackgroundServiceExceptionBehavior`, valeur alternative `Ignore`) [M2]. C'est un argument de plus pour un process dédié avec une politique de redémarrage (compose `restart:` ou systemd `Restart=`).

**Recommandation :** un worker dédié, parce que c'est lui qui porte la contrainte « tourne là où les CLIs sont connectés ». En dev, un `AddProject` ou un `AddNodeApp` tourne **en process hôte** sous DCP [D11], donc il voit les CLIs du poste Windows sans rien faire de plus.

### 4. `aspire publish` / `aspire deploy` pour un hôte Linux unique

**Mécanique** [D12][D13] :
- ajouter `builder.AddDockerComposeEnvironment("env")` (package `Aspire.Hosting.Docker`) ; toutes les ressources compatibles deviennent alors des services compose, sans opt-in ;
- `aspire publish` produit `aspire-output/docker-compose.yaml`, un `.env` (paramètres **vides**) et un `<ressource>.Dockerfile` pour les apps JS. Il ne build pas les images. C'est un « handoff » à sens unique ;
- `aspire do prepare-env --environment production` build les images et écrit un `.env.production` rempli ;
- `aspire deploy` = génération + résolution des paramètres + build des images + `docker compose up -d --remove-orphans`. Le code lance `ComposeUpAsync` sur le runtime résolu **localement** (message « running with Docker Compose locally ») [S5]. En pratique, on exécute donc `aspire deploy` **sur** le LXC (SDK .NET, CLI Aspire et sources nécessaires), ou on publie en CI puis on copie le compose et le `.env`. ⚠️ Cibler un démon distant (`DOCKER_HOST`, `docker context`) n'est pas documenté par Aspire et n'a pas été testé.
- Les projets .NET n'ont pas de Dockerfile : leur image est construite par le SDK .NET et référencée par `${API_IMAGE}`. Pour pousser vers un registre : `AddContainerRegistry` (expérimental `ASPIRECOMPUTE003`) + `aspire do push` [D12].
- Podman ≥ 5.0 est aussi supporté, détecté automatiquement, et forçable avec `ASPIRE_CONTAINER_RUNTIME` [D12].

**Constats de l'expérience** (AppHost jetable : compose + Mongo + Keycloak `WithRealmImport` + API + Worker + conteneur avec bind mount + `AddExecutable` + Vite) :
- `.env` généré : `API_IMAGE`, `API_PORT`, `WORKER_IMAGE`, `FRONT_IMAGE`, `MONGO_PASSWORD`, les paramètres `KC_USER`/`KC_PASS` et les placeholders `<RESSOURCE>_BINDMOUNT_<n>`.
- **`AddExecutable("host-agent", …)` est absent** du compose, sans erreur ni avertissement. Le code filtre les ressources de calcul sur « conteneur ou projet » (`GetComputeResources`) [S6]. Pour qu'un exécutable soit publié, il faut `PublishAsDockerFile()` (qui en fait un conteneur) [D11].
- Les bind mounts déclarés via `WithBindMount` deviennent des placeholders `.env` (ex. `CLI_RUNNER_BINDMOUNT_0`) [S7]. Ceux ajoutés à la main dans `PublishAsDockerComposeService` restent littéraux.
- `WithExternalHttpEndpoints()` sur l'API donne `ports: - "${API_PORT}"`, c'est-à-dire un port conteneur seul, donc un port hôte aléatoire. Pour un port hôte stable, il faut personnaliser `service.Ports` via `PublishAsDockerComposeService`. Sans endpoint externe, un service n'a que `expose:` (Mongo, front).
- Avec le dashboard activé (défaut), le compose ajoute `env-dashboard` avec l'image `mcr.microsoft.com/dotnet/nightly/aspire-dashboard:13.5` (dépôt *nightly*, observé), port UI 18888 publié et OTLP 18889/18890 exposés. La télémétrie y reste en mémoire [D14].
- Le modèle `Service` expose `Ports`, `User`, `ExtraHosts`, `NetworkMode`, `Privileged`, `Restart` et `Healthcheck`, tous personnalisables [S8].

**LXC Proxmox :** Proxmox recommande toujours d'imbriquer les conteneurs applicatifs dans une VM QEMU quand on veut une isolation maximale ou la migration à chaud. L'option LXC « Nesting » expose procfs/sysfs aux conteneurs imbriqués [P1]. ⚠️ Les prérequis exacts pour Docker dans un LXC non privilégié (nesting, keyctl, AppArmor) n'ont pas été vérifiés : c'est à valider sur l'hôte cible.

### 5. Faire tourner un service qui utilise `claude` / `codex` et leurs credentials

Ce que les lecteurs actuels exigent (repo) :
- `src/claude.ts` lit `$CLAUDE_CONFIG_DIR/.credentials.json`, sinon `~/.claude/.credentials.json` (jeton OAuth) ;
- `src/codex.ts` lance `codex app-server --listen stdio://` : il faut le binaire `codex` **et** son état de connexion (⚠️ emplacement exact côté Codex non vérifié ici, probablement sous `~/.codex`) ;
- le prompt « 1+1=? » passera lui aussi par les CLIs.

Ce qu'Aspire fournit :
- en dev, tout projet ou exécutable tourne en process hôte, donc l'accès est naturel ;
- en publish, rien pour les process hôtes. `ExcludeFromManifest()` exclut explicitement une ressource du publish [S9].

| | **A — Worker en conteneur + bind mounts** | **B — Worker en process hôte (systemd), le reste en compose** |
|---|---|---|
| Modélisation Aspire | `AddProject<Worker>` + `PublishAsDockerComposeService` : `Volumes` (bind de `~/.claude`, `~/.codex`), `User` (UID aligné), `Restart` | Même `AddProject<Worker>` en dev ; `.ExcludeFromManifest()` en publish ; déploiement par `dotnet publish` (ou Node) + unité systemd écrite à la main/CI |
| CLIs | À installer **dans l'image** : deux copies (hôte pour le login, image pour l'usage) qui peuvent diverger de version | Les CLIs **de l'hôte**, exactement celles qui sont connectées |
| Credentials | Montage **rw** obligatoire (le refresh OAuth réécrit le fichier) ; risque d'écritures concurrentes hôte/conteneur ; permissions et UID à aligner | Aucun montage ; le service tourne sous l'utilisateur connecté |
| Réseau | Mongo accessible par nom de service compose | Mongo doit publier un port hôte (ex. `127.0.0.1:27017`) via personnalisation compose. Si l'API doit joindre le worker : `ExtraHosts` `host.docker.internal: host-gateway`, ou passer seulement par Mongo |
| OTel | Injecté comme pour les autres services (voir §6) | Variables posées dans l'unité systemd |
| Tout via `aspire deploy` | Oui | Non : une étape hors Aspire pour le worker |

**Recommandation :** **B**. Elle respecte littéralement la contrainte de la map (process là où les CLIs sont connectés), évite les copies de CLIs et les problèmes d'UID ou de refresh concurrent, et reste identique au dev, où le worker est déjà un process hôte. A est acceptable si l'on tient à tout déployer par `aspire deploy`. Dans les deux cas, garder l'API et le front dans le compose.

### 6. OpenTelemetry : dev vs artefact publié

**Dev (`aspire run`)** [D11][S10] :
- toute ressource portant `OtlpExporterAnnotation` (projets .NET automatiquement, conteneurs via `WithOtlpExporter()`) reçoit `OTEL_EXPORTER_OTLP_ENDPOINT` et `OTEL_EXPORTER_OTLP_PROTOCOL` (endpoint gRPC ou HTTP du dashboard), `OTEL_SERVICE_NAME` et `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=…` (valeurs injectées par DCP) ;
- en environnement Development, s'y ajoutent `OTEL_BSP_SCHEDULE_DELAY`, `OTEL_BLRP_SCHEDULE_DELAY` et `OTEL_METRIC_EXPORT_INTERVAL` à 1000 ms, `OTEL_TRACES_SAMPLER=always_on` et un filtre d'exemplars ;
- les apps Node/Vite ne sont **pas** instrumentées automatiquement : il faut un SDK OTel qui lit ces variables [D10].

**Publish** [S10][S11] :
- `RegisterOtlpEnvironment` **ne fait rien** en publish mode (retour anticipé sur `IsPublishMode`) ;
- avec un environnement compose **dashboard activé**, un callback ajoute à chaque ressource instrumentée `OTEL_EXPORTER_OTLP_ENDPOINT=http://env-dashboard:18889`, `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` et `OTEL_SERVICE_NAME=<nom de ressource>` ;
- vérifié : ce callback **écrase** un `WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", …)` posé par nos soins. Keycloak reçoit en plus `KC_FEATURES=opentelemetry` ;
- avec `WithDashboard(enabled: false)`, **aucune** variable `OTEL_*` n'est injectée (vérifié) : seul reste ce qu'on pose soi-même, plus `OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=in_memory` sur les projets .NET ;
- côté app, les ServiceDefaults n'activent l'exporteur OTLP que si `OTEL_EXPORTER_OTLP_ENDPOINT` est défini [D15][D16].

**Motif conseillé pour notre prod** (collector OTLP HTTP, traces uniquement) :

```csharp
var compose = builder.AddDockerComposeEnvironment("prod").WithDashboard(enabled: false);
var otlp = builder.AddParameter("otlp-endpoint", "http://10.0.1.121:4318", publishValueAsDefault: true);

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlp)
       .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
       .WithEnvironment("OTEL_SERVICE_NAME", "llm-usage-monitor-api");
    // idem pour le front YARP si on veut ses traces ; le worker hôte (option B) les reçoit via systemd
}
```

⚠️ Le collector n'accepte que les traces. Couper l'export des logs et métriques côté SDK (configuration par signal) reste à trancher dans le ticket OTel. Non vérifié ici.

## Incertitudes / non vérifié

- Déploiement `aspire deploy` vers un démon Docker distant (`DOCKER_HOST` / `docker context`) : non documenté, non testé.
- Configuration requise par Keycloak en mode `start` s'il était publié (hostname, TLS) : non vérifiée, et sans objet si Keycloak reste dev-only.
- Divergence doc/code sur `WithRealmImport` en publish : c'est le code 13.5.3 qui fait foi (bind mount en lecture seule).
- `vp build` (Vite+) dans le Dockerfile généré ; tests `Aspire.Hosting.Testing` pour un AppHost TS ; prérequis Docker dans un LXC Proxmox non privilégié ; emplacement des credentials Codex.
- L'expérience a tourné sous Windows, sans Docker, en `publish` uniquement : aucune image n'a été construite, aucun `deploy` exécuté.

## Sources

Notes de version
- [R1] Aspire 13.5.0, notes de version (2026-08-18) : https://github.com/microsoft/aspire/releases/tag/v13.5.0
- [R2] Aspire 13.5.1 / 13.5.2 / 13.5.3 : https://github.com/microsoft/aspire/releases/tag/v13.5.1 · https://github.com/microsoft/aspire/releases/tag/v13.5.2 · https://github.com/microsoft/aspire/releases/tag/v13.5.3

Docs aspire.dev (lues via `aspire docs get <slug>` le 2026-09-13)
- [D1] What's new in Aspire 13.5 (dont Breaking changes et Known issues) : https://aspire.dev/whats-new/aspire-13-5/
- [D2] Multi-language architecture : https://aspire.dev/architecture/multi-language-architecture/
- [D3] Docker integration : https://aspire.dev/integrations/compute/docker/
- [D4] Set up MongoDB in the AppHost : https://aspire.dev/integrations/databases/mongodb/mongodb-host/
- [D5] Set up JavaScript apps in the AppHost : https://aspire.dev/integrations/frameworks/javascript/
- [D6] Keycloak integration : https://aspire.dev/integrations/security/keycloak/
- [D7] TypeScript AppHost project structure : https://aspire.dev/app-host/typescript-apphost/
- [D8] Testing overview : https://aspire.dev/testing/overview/
- [D9] Deploy JavaScript apps : https://aspire.dev/deployment/javascript-apps/
- [D10] Node.js OpenTelemetry with the Aspire dashboard : https://aspire.dev/dashboard/standalone-for-nodejs/
- [D11] OpenTelemetry and distributed tracing / Host external executables : https://aspire.dev/fundamentals/telemetry/ · https://aspire.dev/app-host/executable-resources/
- [D12] Deploy to Docker Compose : https://aspire.dev/deployment/docker-compose/
- [D13] How Aspire deployment works : https://aspire.dev/deployment/deploy-with-aspire/
- [D14] Telemetry after deployment : https://aspire.dev/fundamentals/telemetry-after-deployment/
- [D15] C# Service Defaults : https://aspire.dev/get-started/csharp-service-defaults/
- [D16] Même source que D14, section « Export to an OpenTelemetry-compatible backend »

(Les URL aspire.dev sont celles indiquées dans les pages ; les slugs `aspire docs` correspondants sont `whats-new-in-aspire-135`, `multi-language-architecture`, `docker-integration`, `set-up-mongodb-in-the-apphost`, `set-up-javascript-apps-in-the-apphost`, `keycloak-integration`, `typescript-apphost-project-structure`, `testing-overview`, `deploy-javascript-apps`, `nodejs-opentelemetry-with-the-aspire-dashboard`, `opentelemetry-and-distributed-tracing-in-aspire`, `host-external-executables-in-aspire`, `deploy-to-docker-compose`, `how-aspire-deployment-works`, `telemetry-after-deployment`, `c-service-defaults`. ⚠️ Certaines URL canoniques peuvent différer légèrement : le slug fait foi.)

Code source `microsoft/aspire` au tag `v13.5.3`
- [S1] Exports ATS : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Keycloak/KeycloakResourceBuilderExtensions.cs · https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs
- [S2] https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.MongoDB/MongoDBContainerImageTags.cs
- [S3] Keycloak (`start-dev` / `start`, `--import-realm`, `WithRealmImport`) : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Keycloak/KeycloakResourceBuilderExtensions.cs · image : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Keycloak/KeycloakContainerImageTags.cs
- [S4] `WithContainerFiles` (copie en run, bind mount en publish) : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting/ContainerResourceBuilderExtensions.cs
- [S5] `DockerComposeUpAsync` : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs
- [S6] `GetComputeResources` : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting/ApplicationModel/DistributedApplicationModelExtensions.cs
- [S7] Placeholders de bind mount : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Docker/DockerComposeEnvironmentContext.cs
- [S8] Modèle `Service` compose : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Docker/Resources/ComposeNodes/Service.cs
- [S9] `ExcludeFromManifest` : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting/ResourceBuilderExtensions.cs
- [S10] Injection OTLP (dev vs publish) : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting/OtlpConfigurationExtensions.cs
- [S11] `ConfigureOtlp` de l'environnement compose : https://github.com/microsoft/aspire/blob/v13.5.3/src/Aspire.Hosting.Docker/DockerComposeEnvironmentResource.cs

Microsoft Learn / Proxmox / NuGet
- [M1] Background tasks with hosted services in ASP.NET Core (aspnetcore-10.0) : https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- [M2] .NET 6 breaking change : Exception handling in hosting : https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling
- [P1] Proxmox VE, Linux Container : https://pve.proxmox.com/wiki/Linux_Container
- Versions NuGet (index `api.nuget.org/v3-flatcontainer`, 2026-09-13) : https://www.nuget.org/packages/Aspire.Hosting.Keycloak · https://www.nuget.org/packages/Aspire.Hosting.MongoDB · https://www.nuget.org/packages/Aspire.Hosting.JavaScript · https://www.nuget.org/packages/Aspire.Hosting.Docker

Référence interne : `P:\own\desktop\linux\haproxy-virtualizer\Haproxy.Editor.AppHost` (Aspire 13.4.6, lecture seule) ; lecteurs du repo `src/claude.ts`, `src/codex.ts`.
