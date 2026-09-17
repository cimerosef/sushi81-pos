# Post-M09 Hiboutik daily payment dashboard — Windows/WPF manual acceptance

**Status:** PASSED — owner Windows/WPF manual acceptance completed
**Owner:** project owner / manual acceptance executor  
**Executable candidate source checkout:** `dbab706a3f535b521a4a0fc67c318bf0c14b60bb`
**EXE:** `Sushi81.Pos.Desktop.exe` — 162816 bytes — SHA-256 `5DA02D47C0854F993FC461AF1936300BB1DB455B9EABAC30CACABE31FC4FB7AB`
**ZIP:** `post-m09-dashboard-win-x64.zip` — 67042116 bytes — SHA-256 `97D9ACC23C6356A68158E749762EEAF4689BA5803BCF0E237AC73A6E64B48145`
**Result:** PASSED
**Owner final acceptance record:** PR #19 top-level Conversation comment `5715414940` (`OWNER_FINAL_ACCEPTANCE`); all sections A–F passed on the exact candidate above.

This checklist verifies only the approved post-M09 dashboard enhancement. It does not reopen M09 and does not authorize merge or M10+.

Use synthetic/test orders only for recorded evidence.

## A. Top Caisse presentation

- [x] The existing top Caisse daily dashboard still shows the ordinary POS metrics and operational counters as before.
- [x] Exactly two additional passive values are visible: `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`.
- [x] The two new values are text/read-only values, not buttons or editable controls.
- [x] No Hiboutik turnover, total-received, order-count, discrepancy or dedicated workflow appears.

## B. Source separation and same-day payments

Using a synthetic non-Cancelled Hiboutik paste-created order:

- [x] adding/changing same-day cumulative CB updates `Hiboutik CB aujourd'hui` by the corresponding signed effective-date delta;
- [x] adding/changing same-day cumulative Espèce updates `Hiboutik Espèce aujourd'hui` by the corresponding signed effective-date delta;
- [x] the existing ordinary POS `Encaissé`, `CB`, `Espèce` and operational turnover values do not increase because of the Hiboutik payment;
- [x] ordinary POS orders do not contribute to either new Hiboutik value.

## C. Cancellation boundary

- [x] After cancelling a Hiboutik order that has retained payment facts, that order no longer contributes to either Hiboutik daily value.
- [x] The order remains viewable as Cancelled with its retained payment facts; the dashboard exclusion does not delete history.

## D. Effective business date and corrections

- [x] A payment/correction assigned to another effective date does not appear in today's Hiboutik values.
- [x] A later-entered payment back-dated to today appears in today's Hiboutik value even though its technical recorded timestamp is later.
- [x] A negative correction reduces the corresponding Hiboutik CB/Espèce daily value rather than being ignored or converted to a positive amount.

## E. Localization

- [x] Switching FR -> zh-CN changes the two new labels to the approved localized equivalents while amounts/business data remain unchanged.
- [x] Switching back to FR restores the French labels.

## F. Read-only device boundary

- [x] On a non-authoritative/read-only paired device, the two values remain visible from locally available committed data.
- [x] The enhancement adds no write action and does not weaken the existing non-authoritative/stale warning or authority rules.

## Final disposition

- [x] All applicable sections A–F passed on the exact candidate.
- [x] No regression requiring remediation was found.
- [x] Project owner declares this enhancement manual acceptance PASSED.

A PASSED disposition is not merge approval. Merge remains a separate explicit project-owner decision.
