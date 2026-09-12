# M07 manual-acceptance findings — Remediation 22

**Handoff:** `M07-MANUAL-ACCEPTANCE-REMEDIATION-22`  
**Scope:** Scenario M localization/presentation-state remediation and owner-evidence bookkeeping only  
**Status:** Candidate prepared for ChatGPT review and project-owner retest; M07 remains not passed

## Findings addressed

The owner’s R21 retest exposed four presentation defects in the production WPF surface:

1. `RefreshResources()` rebuilt the `Languages` collection while the ComboBox was bound to
   object-valued `SelectedItem`; the selected language could therefore become blank.
2. M07 Disaster Recovery/reinitialize command visibility could lose its notification after a
   localization or authority-state refresh even when authority had not changed.
3. The zh-CN Disaster Recovery failure dialog displayed the infrastructure diagnostic
   `Online activation did not produce a proven winner; this device remains read-only.`.
4. The French stale-generation reinitialize success dialog displayed the English infrastructure
   diagnostic `This device was reinitialized into the current generation and remains read-only.`.

The narrow M07 presentation audit also found raw `result.Diagnostic` and raw exception-message
display in join/Disaster Recovery/reinitialize handlers.

## Remediation

- The language ComboBox now binds to the stable `CultureName` key through
  `SelectedLanguageCultureName`. `RefreshResources()` rebinds `_selectedLanguage` to the newly
  created option and raises both selection properties after rebuilding the collection.
- M07 command-state notifications are centralized in `RefreshM07CommandState()` and are emitted
  from resource and business-refresh paths. The existing phase/guard predicates remain unchanged.
- `DisasterRecoveryResult` carries a presentation-only `DisasterRecoveryOutcome` classification
  for proven stable outcomes. The service’s authority protocol, persistence order, activation
  primitive and write guard are unchanged.
- MainWindow maps those outcomes to localized resources and uses safe localized failure text for
  join/operation exceptions. Infrastructure diagnostics remain internal and are no longer shown
  directly to the operator.

## Automated evidence

- Focused localization and M07 WPF/STA tests: `13/13` Passed before the final full-suite run.
- The focused regression set covering M05/M06/M07 WPF and localization paths: `62/62` Passed.
- The final full Release solution count, build result, publish SHA and exact-head CI result are
  recorded in the matching PR completion comment and the final manual-acceptance document.

## Owner-evidence bookkeeping preserved

The following owner evidence remains unchanged and is recorded without reinterpreting automated
results:

- Scenarios D, E, G, H, J, K and N: Passed in the owner evidence ledger.
- Scenario A: caveat remains; the C/C2 membership/target identity evidence is useful, but the
  Windows Sandbox `0x80370106` failure prevents a clean running post-join/restart UI claim.
- Scenario B: C/C2 membership and target-identity evidence is preserved with the same Sandbox
  caveat.
- Scenario I: partial only; valid-unit retention and changed-data checkpoint evidence passed, but
  delayed publication/normal 15-minute rate-limit proof remains unproven.
- Scenario L: WP7 proof remains frozen and is not rerun or modified.
- Scenario M: blocked pending a focused owner retest of the fresh R22 artifact, including the
  language ComboBox, DR action visibility and French/zh-CN outcome dialogs.

M07 is not marked Passed. The PR remains open, merge is unauthorized, and M08+ work is not
authorized.
