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

**Verrou**:
Exclusion mutuelle par provider : une seule lecture, un seul déclenchement ou keep-alive à la fois, parce qu'un seul process CLI peut tourner par provider.
_Avoid_: lock, mutex, sémaphore

### Supervision

**Santé**:
État courant d'un provider : dernière lecture réussie, dernier échec, et, par type d'erreur, s'il est sain ou KO.
_Avoid_: statut, health check

**Keep-alive**:
Commande CLI gratuite lancée dans les cinq minutes qui précèdent l'expiration du token Claude, seule fenêtre où le CLI le rafraîchit ; le service n'utilise jamais le refresh token lui-même.
_Avoid_: refresh, renouvellement, ping

**Backoff**:
Pause des lectures d'un provider après une réponse 429 : 15, puis 30, puis 60 minutes. Un 429 n'est pas une lecture en échec.
_Avoid_: retry, throttling, rate limit

**Alerte**:
Notification envoyée une fois par série d'échecs — déclenchement KO, login expiré, lectures en échec au-delà du seuil — suivie d'un « rétabli » au retour à la santé. Un déclenchement manuel n'alerte jamais.
_Avoid_: erreur, incident, warning

**Réglages**:
Paramètres de comportement édités depuis l'app et stockés en base : période de lecture par provider (1 à 60 min), déclenchement automatique et modèle du prompt par provider, ntfy. L'infrastructure reste dans les fichiers de configuration.
_Avoid_: configuration, settings, options
