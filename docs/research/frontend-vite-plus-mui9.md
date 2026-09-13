# Recherche : base frontend Vite+ récent + MUI 9 vs référence haproxy-virtualizer

- **Ticket** : [#2](https://github.com/Elyspio/llm-usage-monitor/issues/2) (map [#1](https://github.com/Elyspio/llm-usage-monitor/issues/1))
- **Date** : 2026-09-13
- **Référence** : `P:\own\desktop\linux\haproxy-virtualizer\Haproxy.Editor.Front` (lue, non modifiée)
- **Méthode** : sources primaires (registre npm, notes de version GitHub, docs officielles MUI / Vite+ / React Router / Aspire, code source Aspire et React Compiler, tarballs npm). Les inférences non testées sont marquées **(inférence)**.

## Réponse courte

1. **MUI 7 → 9 (il n'y a pas de v8)** : le moteur de style reste Emotion, le thème `createTheme` / `ThemeProvider` / `styleOverrides` de la référence passe tel quel. En revanche, les API dépréciées sont **supprimées**, dont les *system props*. Dans la référence, cela casse **54 lignes `<Stack alignItems/justifyContent=…>`**, 6 `<Typography fontWeight/textAlign=…>`, les props `InputProps` / `inputProps` / `SelectProps` de `TextField` (8 usages), `renderTags` (1), `params.inputProps` d'`Autocomplete` (2) et l'icône `DeleteOutline` (14 imports/usages). `GridLegacy` est supprimé, mais la référence n'utilise pas `Grid`. `@mui/x-data-grid` 8 est incompatible (il faut la 9.x). Des codemods officiels couvrent tout cela.
2. **React Compiler + MUI 9** : aucune incompatibilité connue. MUI et Emotion ne figurent pas dans la liste des modules incompatibles du compilateur (seuls react-hook-form, @tanstack/react-table et @tanstack/react-virtual y sont), et MUI n'a pas de position officielle. On peut garder la voie Babel de la référence (`reactCompilerPreset` + `@rolldown/plugin-babel`). La voie native `react({ compiler: true })` est encore **expérimentale**. Point d'attention : `vp lint` utilise Oxlint, donc les règles « compiler » d'`eslint-plugin-react-hooks` ne tournent pas.
3. **Vite+** : il est en **Beta depuis v0.2.2** (2026-07-02) ; la dernière version est **0.3.1** (2026-09-08), qui embarque Vite 8.2, Vitest 4.1.11, Oxlint 1.81 et Oxfmt 0.66. Les commandes `dev`, `build`, `check`, `lint`, `fmt` et `test` sont toutes documentées ; le lint *type-aware* est « stable » depuis v0.2.6. Entre 0.1.15 et 0.3.1, il y a des ruptures : Vitest n'est plus wrappé, les variables `VITE_*` sont renommées en `VP_*` et la disposition d'installation change. **Principal écart** : `@elyspio/vite-eslint-config@5.0.3` (dernière version) déclare `peer vite-plus ^0.1.15`, donc exclut 0.2 et 0.3, et épingle `vite: 8.0.0`. Il faut une nouvelle version de ce paquet ou une config inline.
4. **Aspire `AddViteApp().WithPnpm()`** : Aspire lance `pnpm run dev --port <port>`, qui exécute `vp dev --port …` ; c'est compatible sans changement. Le HTTPS Aspire est opt-in et, s'il est activé, repose sur un wrapper qui fait `import 'vite'`. Or, dans un projet Vite+ sans dépendance `vite` directe, ce paquet est introuvable **(inférence)**. Il faut donc garder l'approche de la référence (mkcert + endpoint `https`) ou ajouter l'alias `vite` comme le fait `vp migrate`.
5. **React Router** : la version actuelle est **8.3.1** (v8.0.0 publiée le 2026-06-17). Le data mode (`createBrowserRouter`, `createRoutesFromChildren`, `RouterProvider`) est inchangé. La référence n'utilise ni loaders ni actions, donc la migration se limite aux imports : `react-router-dom` est **supprimé** en v8, `RouterProvider` vient de `react-router/dom` et le reste de `react-router`. Nouveaux minimums : React ≥ 19.2.7 et Node ≥ 22.22.0.
6. **react-query** : 5.102.8, `peer react ^18 || ^19` ; aucun écart.

## Versions (au 2026-09-13, source : `npm view`)

| Paquet | Référence (installée) | Dernière | Date de la dernière |
| --- | --- | --- | --- |
| `vite-plus` | 0.1.15 (2026-03-31) | **0.3.1** | 2026-09-08 |
| `vp` global local | 0.1.20 (2026-04-29) | 0.3.1 | 2026-09-08 |
| `vite` (upstream) | 8.0.0 (via `@elyspio/vite-eslint-config`) | 8.3.0 | — |
| `@mui/material` / `icons-material` | 7.3.9 | **9.4.0** | 2026-08-27 (9.0.0 : 2026-04-07) |
| `@mui/x-data-grid` | 8.28.0 | 9.13.0 | — |
| `react-router` | 7.13.1 | **8.3.1** | 2026-08-28 (7.18.3 publié le même jour) |
| `react-router-dom` | 7.13.1 | 7.18.3 (plus de v8) | 2026-08-28 |
| `@tanstack/react-query` | 5.101.4 | 5.102.8 | — |
| `react` | 19.2.4 | 19.3.0 | — |
| `babel-plugin-react-compiler` | 1.0.0 | 1.0.0 | — |
| `@vitejs/plugin-react` | 6.0.1 | 6.1.1 | — |
| `@rolldown/plugin-babel` | 0.2.x | 0.2.4 | — |
| `vitest` | 4.1.2 (devDep direct) | 5.0.0 (Vite+ 0.3.1 embarque 4.1.11) | — |
| `@elyspio/vite-eslint-config` | 5.0.3 | 5.0.3 | 2026-03-31 |
| `Aspire.Hosting.JavaScript` | 13.4.6 | 13.5.3 (stable) | — |

---

## 1. MUI 7 → 9 : ruptures et patrons de la référence qui cassent

### Contexte

- Material UI passe directement de v7 à v9 pour s'aligner sur MUI X v9. Le billet de blog dit en substance qu'il n'y a pas de v8 ([blog v9](https://mui.com/blog/introducing-mui-v9/), 2026-04-08).
- Les paquets compagnons doivent aussi passer en 9.x : `@mui/icons-material` 9.4.0 déclare `peer @mui/material ^9.4.0`, et `@mui/x-data-grid` 8.29.3 déclare `peer @mui/material ^5.15.14 || ^6 || ^7`, donc **incompatible**. `@mui/x-data-grid` 9.13.0 accepte `^7.3.0 || ^9.0.0` (`npm view`).
- Navigateurs ciblés : Chrome 117 (au lieu de 109), Edge 121, Firefox 121 (au lieu de 115), Safari 17.0 (au lieu de 15.4) ([guide v9](https://mui.com/material-ui/migration/upgrade-to-v9/#supported-browsers-and-versions)).

### Moteur de style et CSS variables

- **Emotion reste le moteur** : `@mui/material@9.4.0` a pour peers `@emotion/react ^11.5.0` et `@emotion/styled ^11.3.0`, avec `@mui/material-pigment-css` en peer optionnel (`npm view`). Se passer d'Emotion reste un objectif de roadmap, pas une réalité v9 ([blog v9](https://mui.com/blog/introducing-mui-v9/)).
- CSS theme variables : v9 étend les variables avec `color-mix()` pour les couleurs dérivées ([blog Material UI v9](https://mui.com/blog/introducing-material-ui-v9/)). Le guide de migration v9 ne mentionne **aucun changement de défaut** de `cssVariables`. Je considère donc qu'elles restent opt-in comme en v7 **(inférence, non testé)**. La doc 9.4 ajoute des pages *Cascade layers*, *Native color* et *Container queries* ([index llms 9.4.0](https://llms.mui.com/material-ui/9.4.0/llms.txt)).
- Thème de la référence (`src/view/theme/cockpit.theme.ts`) : il combine `createTheme({ palette: { mode } })`, `responsiveFontSizes`, `alpha()` et des `components.MuiCssBaseline/MuiPaper/MuiCard/MuiButton/MuiOutlinedInput/MuiListItemButton` (`styleOverrides` / `defaultProps`), et le thème est recréé au changement de mode dans `App.tsx`. **Rien de tout cela n'est retiré par le guide v9.** Seuls `MuiTouchRipple` et `MuiGridLegacy` disparaissent des types `components`, et la référence ne les utilise pas. Pour la cible, la voie moderne documentée est `colorSchemes` + `cssVariables` + `useColorScheme` ; ce n'est pas obligatoire.

### Grid

- `GridLegacy` est **supprimé** ; il faut utiliser `Grid` (ex-Grid v2 : `size={{ xs, sm }}`, sans `item`). `Grid` n'accepte plus `direction="column"` ni `"column-reverse"`, il faut utiliser `Stack` à la place ([guide v9, Grid](https://mui.com/material-ui/migration/upgrade-to-v9/#grid)).
- Référence : **0 usage de `Grid`**, donc aucun impact.

### API dépréciées supprimées : impact mesuré sur la référence

J'ai compté par grep sur `Haproxy.Editor.Front/src` :

| Rupture v9 | Remplacement | Occurrences dans la référence |
| --- | --- | --- |
| *System props* supprimées sur `Box`, `Stack`, `Typography`, `Grid`, `Link`, `DialogContentText` ([guide](https://mui.com/material-ui/migration/upgrade-to-v9/#system-props)) | `sx={{ … }}` ; codemod `npx @mui/codemod@latest v9.0.0/system-props <dir>` | **54 lignes `<Stack … alignItems/justifyContent/… =>`**, 6 lignes `<Typography fontWeight/textAlign/…=>` |
| `TextField` : `InputProps`, `inputProps`, `SelectProps`, `InputLabelProps`, `FormHelperTextProps` | `slotProps.input/htmlInput/select/inputLabel/formHelperText` ; codemod `deprecations/text-field-props` | `InputProps` ×1, `inputProps` ×4, `SelectProps` ×3 |
| `Autocomplete` : `renderTags`, `ListboxProps`, `PaperComponent`… ; `params.inputProps` dans `renderInput` | `renderValue`, `slots` / `slotProps` ; `params.slotProps.htmlInput` ; codemod `deprecations/autocomplete-props` | `renderTags` ×1, `params.inputProps` ×2 |
| Icônes `*Outline` (sans « d ») supprimées, par exemple `DeleteOutline` | `DeleteOutlined` | `DeleteOutline` ×14 |
| `Tooltip` : `components`, `componentsProps`, `PopperProps`, `TransitionComponent`… | `slots` / `slotProps` | 0 |
| `Typography paragraph` | `sx={{ mb: 2 }}` | 0 |
| `Dialog` / `Modal` `disableEscapeKeyDown` | tester `reason` dans `onClose` | 0 |

Vérification : l'API `Stack` 9.4 ne liste plus que `children`, `component`, `direction`, `divider`, `spacing`, `sx` et `useFlexGap` ([api/stack 9.4](https://llms.mui.com/material-ui/9.4.0/api/stack.md)). La page 7.3.11 disait encore que Stack « supporte toutes les system properties comme props » ([api/stack 7.3.11](https://llms.mui.com/material-ui/7.3.11/api/stack.md)). `Typography` conserve sa prop `color` ([api/typography 9.4](https://llms.mui.com/material-ui/9.4.0/api/typography.md)), donc `color="text.secondary"` reste valide.

### Changements de comportement à surveiller

Ces changements ne cassent pas la compilation mais peuvent changer le comportement ([guide v9](https://mui.com/material-ui/migration/upgrade-to-v9/#breaking-changes)) :

- `ButtonBase` : Entrée et Espace font désormais *bubbler* le `click`. Il y a une nouvelle prop `nativeButton` et un avertissement en dev si l'élément rendu via `component` ne correspond pas. À vérifier sur les `ListItemButton` / `Button` rendus avec `component={Link}` **(non testé)**.
- `ListItemIcon` : `min-width` par défaut passe à 36px (au lieu de 56). La référence force déjà `minWidth: 36`, donc c'est neutre.
- `Menu` / `MenuList` / `Tabs` / `Stepper` : *roving tabindex*. Un `MenuItem` hors `Menu` et un `Tab` hors `Tabs` **lèvent maintenant une erreur**.
- `TablePagination` formate les nombres via `Intl.NumberFormat`.
- **jsdom** : MUI détecte maintenant jsdom et happy-dom par *user-agent* au lieu de `NODE_ENV === 'test'`, ce qui peut changer le résultat des tests unitaires. La référence teste en `environment: "jsdom"`.

### Conclusion MUI

Pour un projet **neuf**, ce ne sont pas des migrations mais des patrons à ne pas recopier : `alignItems` / `justifyContent` en props de `Stack`, `InputProps` / `inputProps` et `renderTags`. Le thème de la référence est réutilisable tel quel.

---

## 2. React Compiler + MUI 9

- **Aucune incompatibilité documentée.** La liste des modules connus comme incompatibles (interior mutability) dans le compilateur ne contient que `react-hook-form`, `@tanstack/react-table` et `@tanstack/react-virtual` ([DefaultModuleTypeProvider.ts](https://github.com/facebook/react/blob/main/compiler/packages/babel-plugin-react-compiler/src/HIR/DefaultModuleTypeProvider.ts)). La page de la règle `incompatible-library` cite react-hook-form `watch`, TanStack Table et MobX, sans mention de MUI ni d'Emotion ([react.dev](https://react.dev/reference/eslint-plugin-react-hooks/lints/incompatible-library)). `@tanstack/react-query` n'y figure pas non plus.
- Côté MUI, il n'y a pas de déclaration officielle. L'issue interne [#44336 « Adopt react compiler? »](https://github.com/mui/material-ui/issues/44336) (ouverte) porte sur la compilation *du code de MUI*, pas sur la compatibilité des consommateurs. Les peers MUI 9 acceptent React 19.
- **Intégration** avec `@vitejs/plugin-react` 6.1.1 (README du tarball npm, [dépôt](https://github.com/vitejs/vite-plugin-react/tree/main/packages/plugin-react)) :
  - Voie **Babel**, celle de la référence : `babel({ presets: [reactCompilerPreset()] })` via `@rolldown/plugin-babel`, avec `babel-plugin-react-compiler` et `@babel/core` en peers. Le preset filtre sur le code et ne s'applique qu'à l'environnement `client`.
  - Voie **native** `react({ compiler: true })` via `oxc-transform-react` (portage Rust) : le README la marque « experimental ».
  - La référence a besoin de Babel de toute façon, pour les décorateurs `inversify` (`plugin-proposal-decorators`, `transform-typescript-metadata`). Si la cible n'utilise pas de décorateurs, Babel ne servira qu'au compilateur.
- **Lint** : `vp lint` = Oxlint, pas ESLint ([docs Vite+ lint](https://viteplus.dev/guide/lint)). Les règles « compiler » d'`eslint-plugin-react-hooks` v7 (purity, refs, incompatible-library…) ne sont donc **pas exécutées** par `vp check`. La référence garde un `eslint.config.js`, que Vite+ n'utilise pas (il sert à l'IDE). Oxlint propose des *JS plugins* pour réutiliser des plugins ESLint ; je n'ai pas vérifié si ces règles y fonctionnent.

---

## 3. Vite+ : statut et intégration Aspire

### Statut (voidzero-dev/vite-plus)

- **v0.2.2 (2026-07-02) : « Vite+ is now in Beta »**, présentée comme stable et prête pour la production, sous licence MIT ([release v0.2.2](https://github.com/voidzero-dev/vite-plus/releases/tag/v0.2.2)). La dernière est **v0.3.1 (2026-09-08)** ([releases](https://github.com/voidzero-dev/vite-plus/releases)).
- `vite-plus@0.3.1` : `vite` → `npm:@voidzero-dev/vite-plus-core@0.3.1` (Vite 8.2.x), `vitest 4.1.11`, `oxlint 1.81.0`, `oxfmt 0.66.0`, `oxlint-tsgolint 7.0.2001`. Binaires : `vp`, `vpr`, `oxfmt`, `oxlint`. `engines.node` vaut `^20.19.0 || ^22.18.0 || >=24.11.0` (`npm view`). Les notes v0.2.0 annoncent pourtant un minimum `^22.18.0 || >=24.11.0` : il y a une divergence, le plus sûr est de viser Node ≥ 22.18. Le Node local est 24.21.

| Commande | Statut / comportement (docs Vite+ 0.3.1) |
| --- | --- |
| `vp dev` | Serveur de dev Vite standard. Il exécute toujours le serveur intégré, pas le script `dev` ([guide/dev](https://viteplus.dev/guide/dev)). |
| `vp build` | Build Vite/Rolldown. Cache zéro-config sous le runner depuis v0.2.2. |
| `vp check` | fmt (Oxfmt) + lint (Oxlint) + type-check (tsgolint) si `lint.options.typeCheck`. Un bloc `check` permet de désactiver fmt ou lint ([guide/check](https://viteplus.dev/guide/check)). |
| `vp lint` | Oxlint, config dans le bloc `lint` de `vite.config.ts`. `typeAware` + `typeCheck` sont recommandés ([config/lint](https://viteplus.dev/config/lint)). Le *type-aware* est « stable » depuis v0.2.6 ([release v0.2.6](https://github.com/voidzero-dev/vite-plus/releases/tag/v0.2.6)). |
| `vp fmt` | Oxfmt, bloc `fmt`. Les nouvelles versions d'Oxfmt peuvent signaler du code auparavant accepté (notes 0.2.8 / 0.3.0). |
| `vp test` | **Vitest upstream** depuis v0.2.0 (plus de wrapper). On importe depuis `vite-plus/test`, sans installer `vitest`. Pas de watch par défaut (`vp test watch`). Config dans le bloc `test` ([guide/test](https://viteplus.dev/guide/test)). |

### Ruptures entre la référence (0.1.15) et 0.3.1

- **v0.2.0** : `@voidzero-dev/vite-plus-test` est supprimé, et `vp test` exécute Vitest upstream. La référence déclare encore `vitest ^4.1.2` en devDep ; c'est à retirer, ou à épingler sur la version embarquée via overrides ([release v0.2.0](https://github.com/voidzero-dev/vite-plus/releases/tag/v0.2.0), [guide/migrate](https://viteplus.dev/guide/migrate)).
- **v0.2.2** : `vp migrate` sait mettre à niveau un projet Vite+ entre versions. Avec pnpm, il ajoute une devDep `vite` aliasée vers `@voidzero-dev/vite-plus-core`.
- **v0.2.8** : `VITE_LOG` → `VP_LOG` (et deux autres), sans alias ([release v0.2.8](https://github.com/voidzero-dev/vite-plus/releases/tag/v0.2.8)).
- **v0.3.0** : nouvelle disposition d'installation (XDG, et `%LOCALAPPDATA%` / `%APPDATA%` sous Windows) pour les installations neuves. `vp upgrade` laisse une installation existante en place ([release v0.3.0](https://github.com/voidzero-dev/vite-plus/releases/tag/v0.3.0)).

### Écart bloquant : `@elyspio/vite-eslint-config`

Source : `node_modules/@elyspio/vite-eslint-config` de la référence et `npm view`.

- `getDefaultConfig({ basePath, port, useMkcert })` renvoie `{ fmt, lint, plugins: [svgr(), react(), babel({ presets: [reactCompilerPreset()], plugins: [decorateurs…] }), mkcert()], resolve.alias (paths tsconfig), server/preview { port, host: "0.0.0.0" } }`, avec `lint.options = { typeAware: true, typeCheck: true }` et `fmt` en tabs, largeur 180.
- **5.0.3 est la dernière version (2026-03-31)**, avec `peerDependencies: { "vite-plus": "^0.1.15" }`. Un caret sur une version 0.x donne `<0.2.0`, donc **0.2.x et 0.3.x sont exclues**. Le paquet dépend aussi de `"vite": "8.0.0"` en version exacte, ce qui installe un second Vite upstream à côté de vite-plus-core (visible dans le `.pnpm` de la référence), ainsi que d'`eslint 9` et `prettier`, que Vite+ n'utilise pas.
- Il faut donc soit publier une v6 de ce paquet (peer `vite-plus ^0.3`, sans `vite` épinglé, sans ESLint/Prettier), soit reproduire ces quelques lignes dans le `vite.config.ts` de la cible.

### Intégration Aspire `AddViteApp().WithPnpm()`

Sources : [doc aspire.dev](https://aspire.dev/integrations/frameworks/javascript/) et [JavaScriptHostingExtensions.cs](https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs), branche `main`, qui peut être légèrement plus récente que 13.5.3.

- `AddViteApp(name, dir, runScriptName = "dev")` enregistre un endpoint `http` (env `PORT`) et lance `<pm> run dev` en ajoutant `--port <targetPort>`, plus `--config <path>` si `WithViteConfig` est utilisé. Pour pnpm, `CommandSeparator = null` : Aspire n'ajoute pas `--`, car pnpm ne le retire pas. Cela donne `pnpm run dev --port 3000`, puis `vp dev --port 3000`. `vp dev` étant le serveur Vite standard, il accepte `--port` **(inférence cohérente avec le fonctionnement actuel de la référence)**.
- `WithPnpm()` : `pnpm install --frozen-lockfile` si `pnpm-lock.yaml` existe. En publish, Aspire utilise le script `build`, qui vaut `vp check --fix && vp build` dans la référence.
- La doc demande de ne pas appeler `.WithHttpEndpoint()` sur une ressource Vite. La référence modifie l'endpoint existant via `WithEndpoint("http", a => { Port = TargetPort = 3000; UriScheme = "https"; IsProxied = false; })`, ce qui est conforme.
- **HTTPS** : il est opt-in (`.WithoutHttpsCertificate()` par défaut). Avec `WithHttpsDeveloperCertificate()`, Aspire génère `node_modules/.aspire/<id>/<name>/aspire.vite.config.ts`, qui fait `import { defineConfig } from 'vite'` puis enveloppe la config utilisateur (`server.https` via `TLS_CONFIG_PFX`). La référence **n'a pas de `node_modules/vite` de premier niveau** (vite est seulement aliasé ou transitif), donc cet import échouerait probablement **(inférence, non testé)**. Deux options :
  - garder l'approche de la référence (`vite-plugin-mkcert` + `UriScheme = "https"`, sans `WithHttpsDeveloperCertificate`) ;
  - ajouter la devDep `"vite": "npm:@voidzero-dev/vite-plus-core@<ver>"`, comme le fait `vp migrate`.
- Aspire stable le plus récent : `Aspire.Hosting.JavaScript` 13.5.3 ; la référence est en 13.4.6 ([NuGet](https://api.nuget.org/v3-flatcontainer/aspire.hosting.javascript/index.json)).

---

## 4. React Router : version actuelle et impact sur le data mode

- La version actuelle est **react-router 8.3.1 (2026-08-28)** ; v8.0.0 date du 2026-06-17. La branche 7.x est toujours maintenue : 7.18.3 est sortie le même jour ([CHANGELOG](https://github.com/remix-run/react-router/blob/main/packages/react-router/CHANGELOG.md), `npm view`).
- Ruptures v8.0.0 qui concernent un SPA en data mode :
  - **Paquet `react-router-dom` supprimé** : `RouterProvider` et `HydratedRouter` viennent de `react-router/dom`, tout le reste de `react-router`.
  - Paquets **ESM-only**, cible TS ES2022.
  - Minimums : **React ≥ 19.2.7** et **Node ≥ 22.22.0** (`peerDependencies` et `engines` de 8.3.1).
  - Middleware toujours actif : le `context` des loaders et actions est un `RouterContextProvider`, et le flag `future.v8_middleware` disparaît.
  - `hasErrorBoundary` n'est plus accepté sur `RouteObject` ni sur `<Route>` ; `meta` utilise `loaderData` au lieu de `data`. Les flags `v8_trailingSlashAwareDataRequests` et `v8_passThroughRequests` deviennent le comportement par défaut, ce qui concerne surtout le framework mode.
- **Référence** : `createBrowserRouter(createRoutesFromChildren(<Route …/>))` et `RouterProvider`, avec 6 fichiers qui importent `react-router-dom`, **aucun loader ni action** et une garde via `ProtectedRoute` en `element`. `createRoutesFromChildren` est toujours exporté par `react-router@8.3.1` (vérifié dans `dist`). L'impact se limite donc aux imports et aux minimums React/Node.
- À venir : React Router v9 exigera `node@24+`, sans *future flags* v9 pour l'instant ([upgrading/future](https://reactrouter.com/upgrading/future)).

---

## 5. Recommandations pour la base de la cible

À valider par le ticket de décision front, [#9](https://github.com/Elyspio/llm-usage-monitor/issues/9) :

1. Utiliser `vite-plus ^0.3.1` en local et mettre à jour le `vp` global (`vp upgrade`, de 0.1.20 vers 0.3.x). Écrire la config avec `defineConfig` de `vite-plus`, et les tests avec `vite-plus/test`, sans devDep `vitest`.
2. Remplacer `@elyspio/vite-eslint-config@5` : nouvelle version compatible 0.3, ou config inline (plugins `react()` + `babel({ presets: [reactCompilerPreset()] })` + mkcert, alias, blocs `lint` / `fmt`).
3. Installer `@mui/material` / `@mui/icons-material` 9.4 et, si besoin, `@mui/x-*` 9.x. Écrire dès le départ `sx` et `slotProps`, sans *system props*. Le thème de la référence est réutilisable.
4. Importer `react-router@8` depuis `react-router` et `react-router/dom`. Le data mode peut rester, avec `createBrowserRouter` et un tableau de routes ; `createRoutesFromChildren` reste possible.
5. Pour Aspire, garder `AddViteApp(...).WithPnpm()` avec le port fixe et HTTPS via mkcert, comme la référence. Si on veut le certificat Aspire, ajouter l'alias `vite` en devDep et tester.
6. Node ≥ 22.22 partout (en dev, dans l'image de build et sur le LXC), à cause du minimum de React Router 8. Viser Node 24 anticipe React Router v9.

## Non vérifié / incertain

- Valeur par défaut de `cssVariables` en MUI 9 : non mentionnée dans le guide, je la suppose inchangée (opt-in).
- Avertissements `nativeButton` de MUI 9 avec `component={Link}` de React Router : non testé.
- Échec du wrapper HTTPS d'Aspire (`import 'vite'`) dans un projet Vite+ sans dépendance `vite` directe : déduit du code, non exécuté.
- Prise en charge par Oxlint (JS plugins) des règles React Compiler d'`eslint-plugin-react-hooks` : non vérifié.
- Exclusion de `node_modules` par `@rolldown/plugin-babel` (le preset filtre sur le code, pas sur l'id) : non vérifié. MUI est de toute façon distribué précompilé.
- `engines.node` de vite-plus 0.3.1 (`^20.19`) contredit les notes v0.2.0 (`^22.18`).
- Source Aspire lue sur `main` (dépôt `dotnet/aspire`), qui peut différer de 13.5.3.
- L'annonce Beta de Vite+ (voidzero.dev) n'a pas été lue ; seules les notes de release l'ont été.

## Sources

- MUI :
  - [Upgrade to v9](https://mui.com/material-ui/migration/upgrade-to-v9/) (lu via [llms 9.4.0](https://llms.mui.com/material-ui/9.4.0/migration/upgrade-to-v9.md))
  - [Introducing MUI v9](https://mui.com/blog/introducing-mui-v9/)
  - [Material UI v9](https://mui.com/blog/introducing-material-ui-v9/)
  - [API Stack 9.4](https://llms.mui.com/material-ui/9.4.0/api/stack.md)
  - [API Stack 7.3.11](https://llms.mui.com/material-ui/7.3.11/api/stack.md)
  - [API Typography 9.4](https://llms.mui.com/material-ui/9.4.0/api/typography.md)
  - [CSS theme variables](https://llms.mui.com/material-ui/9.4.0/customization/css-theme-variables/overview.md)
  - [Issue #44336](https://github.com/mui/material-ui/issues/44336)
- React Compiler :
  - [DefaultModuleTypeProvider.ts](https://github.com/facebook/react/blob/main/compiler/packages/babel-plugin-react-compiler/src/HIR/DefaultModuleTypeProvider.ts)
  - [incompatible-library](https://react.dev/reference/eslint-plugin-react-hooks/lints/incompatible-library)
  - [@vitejs/plugin-react README](https://github.com/vitejs/vite-plugin-react/tree/main/packages/plugin-react) (tarball 6.1.1)
- Vite+ :
  - [Releases](https://github.com/voidzero-dev/vite-plus/releases) (v0.2.0, v0.2.2, v0.2.6, v0.2.8, v0.3.0, v0.3.1)
  - Docs (tarball `vite-plus@0.3.1/docs`, publiées sur [viteplus.dev](https://viteplus.dev)) : guide/dev, guide/check, guide/lint, config/lint, guide/test, guide/migrate
- Aspire :
  - [JavaScript integration](https://aspire.dev/integrations/frameworks/javascript/)
  - [JavaScriptHostingExtensions.cs](https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs)
  - [NuGet index](https://api.nuget.org/v3-flatcontainer/aspire.hosting.javascript/index.json)
- React Router :
  - [CHANGELOG react-router](https://github.com/remix-run/react-router/blob/main/packages/react-router/CHANGELOG.md) (tarball 8.3.1)
  - [Future changes](https://reactrouter.com/upgrading/future)
- Registre npm (`npm view <pkg> time|peerDependencies|dependencies|engines`, 2026-09-13) : vite-plus, @mui/material, @mui/x-data-grid, @mui/icons-material, react-router, react-router-dom, @tanstack/react-query, @elyspio/vite-eslint-config, vite, vitest, react
- Référence (lecture seule) :
  - `Haproxy.Editor.Front/package.json`, `vite.config.ts`, `src/view/App.tsx`, `src/view/theme/cockpit.theme.ts`
  - `node_modules/@elyspio/vite-eslint-config/lib/vite.config-*.mjs`
  - `Haproxy.Editor.AppHost/AppHost.cs`
