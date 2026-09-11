# M07 manual-acceptance finding 03 — deterministic post-relinquishment probe

## Finding

The R20 project-owner retest passed the same-process presentation refresh scenarios D and E and
the production newest-three retention convergence scenario H. Mandatory scenario G was still not
executable safely because the owner could not deterministically stop a normal handoff after the
source had durably entered `RelinquishedPendingGrant` but before the target-releasing grant was
created. Random network interruption was rejected because it could hit the wrong side of the
irreversible boundary.

## R21 remediation

R21 adds `INormalHandoffFaultProbe` at the exact post-persistence/pre-grant boundary. The desktop
composition uses `EnvironmentNormalHandoffFaultProbe`, which is disabled unless the owner
explicitly starts the process with:

`SUSHI81_M07_MANUAL_FAULT_AFTER_RELINQUISH=1`

The probe is not persisted in authority state, SQLite, OneDrive, GitHub metadata, logs or
screenshots. It is invoked only when the current operation crosses into the newly persisted
`RelinquishedPendingGrant` phase. A restart/resume from an already pending phase therefore does
not re-trigger merely because the process environment still contains the variable.

The injected exception is handled as an ordinary post-irreversible failure: the guard remains
non-writable, the exact transfer remains pending, no grant upload occurs, and retry accepts only
the already durable transfer identity and target. The production default is a no-op probe.

## Automated evidence

`NormalHandoffTests.ManualPostRelinquishmentProbeFailsClosedAndExactRetryCannotRetarget` proves:

- durable pending state and non-writable guard precede the injected fault;
- no grant asset is created on the injected attempt;
- restart reconstructs the same transfer, target, version, business revision and snapshot receipt;
- retry with a different requested target cannot retarget the pending transfer;
- exactly one matching grant is created on retry and the source remains read-only.

Existing `TargetAcquisitionTests.ExactTargetAcquisitionInstallsDataBeforeDurableAuthority` and
`TargetAcquisitionTests.NonTargetGrantDoesNotCreateAuthority` continue to cover exact-target
acquisition and non-target read-only behavior.

## Owner boundary

Project-owner Windows/WPF/manual acceptance remains **not executed / not passed**. R21 makes
Scenario G executable; it does not mark G or overall M07 Passed. Scenario A, F, I, J, K, L, M and
N remain pending as applicable. WP7 proof assets, M08+ and merge remain unauthorized.
