# Crédits de reset

Le dashboard affiche le nombre annoncé par le fournisseur, les détails disponibles, leur expiration, la prochaine sélection et le journal.

## Consommation

`POST /api/providers/{provider}/reset-credits/consume` exige un identifiant de crédit et une clé UUID ; la confirmation manuelle réutilise cette clé après une erreur HTTP.

Réglages : `/api/settings/reset-credits`, indépendants des prompts de relance, désactivés par défaut, marge de 60 minutes.

- Avant expiration, tenter chaque crédit éligible dans l'ordre d'expiration, même à quota nul ; un refus ne consomme rien.
- Expiration inconnue : manuel uniquement.
- Aucun prompt après consommation.

## Journal

La collection Mongo `resetCreditRuns` conserve les tentatives et clés d'idempotence avant l'appel externe. Les reprises réutilisent la clé. Les entrées terminées expirent après 90 jours ; les demandes en cours restent conservées.

## Fournisseurs

- Codex : `account/rateLimitResetCredit/consume`.
- Claude : bloc `cedar_ember` demandé sur l'endpoint OAuth et flux de consommation repéré dans Claude Code 2.1.289. `Claude:AccountConfigPath` peut désigner le `.claude.json` contenant l'organisation OAuth. L'accès Claude a été vérifié en lecture seule ; les tests de consommation utilisent des adapters factices.
