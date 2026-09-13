# LLM Usage Monitor

Surveille l'usage des abonnements LLM (Claude Code, Codex) d'un compte par provider, et relance une fenêtre d'usage dès qu'elle est réinitialisée.

## Language

### Usage

**Provider**:
Service LLM surveillé, avec un compte unique : Claude ou Codex.
_Avoid_: service, compte, fournisseur

**Fenêtre**:
Quota d'usage glissant d'un provider, identifié par son `windowId` (ex. `five_hour`, `seven_day`), avec un pourcentage consommé et une heure de reset.
_Avoid_: bucket, limite, période

**Lecture**:
Interrogation de l'usage d'un provider à un instant donné ; réussie, elle renvoie l'état de toutes ses fenêtres.
_Avoid_: fetch, check

**Snapshot**:
État d'une fenêtre relevé par une lecture réussie : pourcentage consommé, heure de reset, durée.
_Avoid_: point, mesure, sample

**Reset**:
Baisse du pourcentage consommé d'une fenêtre entre deux lectures, signe que le provider l'a réinitialisée.
_Avoid_: remise à zéro, renouvellement

### Déclenchement

**Fenêtre déclencheuse**:
Fenêtre la plus courte d'un provider ; son passage à 0 % autorise le déclenchement automatique.
_Avoid_: fenêtre principale, fenêtre 5 h (elle n'est pas toujours de 5 h)

**Déclenchement**:
Envoi d'un prompt minimal à un provider pour ouvrir ses fenêtres inactives ; automatique (après reset) ou manuel (forcé depuis l'app).
_Avoid_: prompt, réveil, wake-up, run

**Cycle**:
Période entre deux resets de la fenêtre déclencheuse, identifiée par l'heure de reset qui l'ouvre ; au plus un déclenchement automatique par provider et par cycle.
_Avoid_: période, fenêtre (une fenêtre est un quota, pas une occurrence)

### Supervision

**Santé**:
État courant d'un provider : dernière lecture réussie, dernier échec, et, par type d'erreur, s'il est sain ou KO.
_Avoid_: statut, health check
