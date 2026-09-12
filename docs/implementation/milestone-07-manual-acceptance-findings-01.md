# M07 manual acceptance finding 01 — fresh-install restart can create local authority

**Status:** Remediated in code; owner retest pending. This finding does not mark Scenario A or M07 Passed.

**Observed candidate:** `79db4bb8a92f401fbc40d464a2f7792d269cdb83` (WP10 production candidate)  
**Review reference:** `5167820845`  
**Affected owner path:** Scenario A — fresh replacement/self-join boundary

## Reproduction

On a genuinely fresh Windows PC B, before launching the self-contained production artifact:

1. Confirm `%LOCALAPPDATA%\Sushi81 POS` is absent.
2. Launch once. The application creates and migrates `Data\live.db`; no authority state,
   M06 bootstrap marker or M06 bootstrap anchor exists, and the UI is Recovery Required/read-only.
3. Configure the existing authoritative OneDrive root and restart B.
4. Observe that startup can persist a new schema-v2 local Authoritative protocol with a new
   lineage, generation 1 and `AuthorityPhase.Authoritative`, even though B has not self-joined,
   acquired a target grant or performed disaster recovery.

The shared OneDrive lineage remains A's lineage, so the final UI is Recovery Required and controls
are disabled. That final UI does not make the intermediate local authority creation acceptable.

## Root cause

The preflight captured the absence of the database before migrations, but the later
`HasLegacyBootstrapEvidenceAsync` check treated any existing `live.db` with
`MAX(schema_migrations.version) >= 5` as legacy evidence. A database created by the first M07
startup therefore became indistinguishable from a genuinely pre-existing M01–M06 database on
the next restart.

## Remediation

The startup preflight now creates a versioned, non-authority provenance pair only when the
beginning-of-startup evidence proves both that `live.db` did not pre-exist and that no established
authority artifacts existed:

- `Config\m07-fresh-install.marker`
- `Data\m07-fresh-install.anchor`

Each file is create-only, written through and flushed. A valid pair makes legacy bootstrap
ineligible regardless of migration history. A missing half or corrupt content is ambiguous and
fails closed. The pair is not authority evidence, does not contain a lineage or grant, and cannot
make the device writable. Existing supported legacy databases without the pair retain the one-time
M01–M06 bootstrap path; existing M06 authority state/marker/anchor behavior is unchanged.

## Automated evidence added

- `InfrastructureIntegrationTests.FreshMigratedDatabaseDoesNotQualifyAsLegacyBootstrapEvidence`
- `InfrastructureIntegrationTests.FreshInstallProvenancePartialOrCorruptEvidenceAlwaysFailsClosed`
- `M06StartupTests.ProductionStartupWithExistingRecoveryShowsMainWindowAndClosesOnSta`

The WPF regression exercises production `CompositionRoot` startup on a real STA dispatcher,
repeated fresh restarts, existing shared-lineage setup, no-grant acquisition rejection, explicit
self-join, exact lineage/generation/device identity persistence and read-only restart behavior.

## Owner retest boundary

The old candidate remains failed/blocked for Scenario A. The replacement self-contained artifact
must be launched on the owner Windows/WPF environment, and Scenario A must be rerun from a genuinely
fresh app-data root. Record the replacement exact production head, artifact path, executable size
and SHA-256 in `milestone-07-final-manual-acceptance.md`. Do not mark Scenario A or overall M07
Passed from automated evidence alone.
