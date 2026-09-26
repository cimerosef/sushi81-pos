# M13 — Full business-data reset contract

**Status:** Approved — V1 specification amendment
**Decision date:** 2026-09-26
**Owner evidence:** PR #26 comment `5844832700`
**Milestone:** M13 — Installer, localization completion and final V1 acceptance

## Approved behavior

Sushi81 POS provides a protected in-application maintenance action that resets the entire operational business dataset to the empty pre-operation state. This is a maintenance operation, not ordinary order cancellation, an installer operation, or a troubleshooting step. It is not a privacy or secure-wipe function.

The reset deletes categories, products, option groups/options; all orders, items, adjustments, tax rows and payment adjustments; order-reference sequences; Gestion batches, links and emissions including `PREPARED`; annual-archive completion/proof rows; and active application-managed local annual archive files. The resulting Catalogue, order/history, Gestion and archive views are empty.

It preserves A/B device identities and pairing; lineage, generation and authority ownership; OneDrive System membership; GitHub handoff configuration and credential target; Windows Credential Manager secrets; printer selections; UI language/local settings; all `business_settings` fields; schema/migrations; and the application installation.

Before mutation the application creates a private local maintenance backup under the app-managed `MaintenanceBackups` location. The database backup uses SQLite `BackupDatabase` to include committed WAL state and is validated for integrity, foreign keys, schema and business revision. Active Archive files are copied with a manifest and hashes. This backup is retained until an operator explicitly removes it outside V1. It is never exposed in ordinary UI or uploaded to OneDrive, GitHub, logs, tests or repository artifacts.

The localized Settings danger section shows counts for orders, catalogue records, Gestion batches including `PREPARED`, archive records/years/files, and order-reference counters without exposing free-text order/customer content. It is enabled only for an authoritative writable device outside transfer, recovery or transition states. The operator types the exact locale-independent token `RESET`, then confirms in a separate final destructive dialog. Cancel performs no mutation. The database changes in one transaction and advances `business_data_revision` exactly once through the normal transaction runner; the standard durable-change/recovery pathway runs once. A successful reset refreshes all affected views without restart.

If any stage fails, the operation leaves the original data intact or restores the database and archive from the private backup; if automatic restoration cannot finish, it fails visibly into a safely recoverable state. The reset may be used again as an explicit whole-dataset maintenance operation, never as a row-level/history-pruning tool.

## Relationship to V1 lifecycle and installer behavior

- Ordinary cancellation continues to retain an order and its recorded payment facts. It is not a reset and no individual order-delete action is added.
- Install, upgrade, repair, uninstall, startup recovery, authority transfer and troubleshooting never trigger a reset implicitly.
- Codex automated validation uses synthetic data only and never runs the reset against a real business profile.

This is the approved specification amendment authorized by `OWNER_DECISION: M13-IN-APP-BUSINESS-DATA-RESET-20260926` (PR #26 comment `5844832700`). It is effective for the exact M13 handoff named in the active PR mailbox; later changes still require a separate approved decision.
