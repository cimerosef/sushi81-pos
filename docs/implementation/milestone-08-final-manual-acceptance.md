# M08 final Windows/WPF manual acceptance — printing and reprinting

**Status:** Prepared checklist — **NOT EXECUTED / NOT PASSED**  
**Prepared:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Implementation authorization:** NOT YET GRANTED  
**Execution gate:** CLOSED during preparation

This checklist is project-owner acceptance only. Codex/CI may prepare evidence and an exact artifact but must never check owner-manual items or declare M08 Passed on the owner's behalf.

Archive printing (`AC-PRINT-009`) is not part of M08 acceptance; it belongs to M12.

## 1. Exact candidate identity — fill before testing

- exact production implementation head:
- exact docs/evidence head:
- M08 PR number:
- self-contained `win-x64` publish location:
- executable SHA-256:
- Release test result:
- Release build warnings/errors:
- exact-head CI run/job:
- physical Windows PC/device used:
- authority state at start:
- Kitchen configured Windows queue:
- Customer configured Windows queue:
- real printer model(s), if useful for diagnosis:
- receipt identity decision/version used:
- only synthetic/non-sensitive acceptance orders used: Yes / No

If code or the artifact changes after a blocking finding, rerun the affected scenarios on the replacement exact candidate and record the new identity.

## 2. Environment and safety rules

- Preserve existing M07 authoritative business data/authority state; do not rerun destructive DR merely for M08.
- Use the production WPF application and production Windows print-queue path.
- A Windows PDF/XPS software queue may supplement deterministic checks, but at least the final layout/latency/practicality check should use the actual intended physical printer path where available.
- Do not manually edit `live.db`, authority state or print output data.
- Printer failure must never be induced by corrupting business data.
- Do not start M09 during M08 acceptance.

## 3. Scenario A — local printer configuration

Starting state: application installed with M08 candidate; order data available.

- [ ] Open the local printer configuration surface.
- [ ] Installed Windows queues are listed with understandable names.
- [ ] Select Kitchen queue and Customer queue independently.
- [ ] Configure the same physical queue for both and confirm this is accepted.
- [ ] Save and restart the app; both selections persist.
- [ ] Temporarily make one configured queue unavailable/rename/remove it as safely practical; the application reports the affected destination clearly and allows re-selection.
- [ ] Printer configuration is available while the app is non-authoritative/read-only and does not change business authority.
- [ ] FR and zh-CN printer-setup labels/status are usable.

Result: Pending.

## 4. Scenario B — normal new-order automatic printing / durable-first

Use an ordinary same-day synthetic order.

- [ ] Enter a valid new order and confirm it.
- [ ] The order becomes visibly committed/stable before output failure/success can change persistence semantics.
- [ ] Exactly one Kitchen document is submitted automatically.
- [ ] Exactly one Customer document is submitted automatically.
- [ ] Kitchen content matches the committed order ID/reference, fulfilment, planned time, telephone/address/comment where entered, ordered lines/options/adjustments and authoritative total.
- [ ] Customer content matches the committed items/prices, total, persisted VAT and applicable payment state plus approved Sushi 81 identity block.
- [ ] The two documents use the same committed order ID.
- [ ] Restart application and retrieve the order; it still exists independent of printing.

Result: Pending.

## 5. Scenario C — real layout/readability

Using the intended physical printer path:

- [ ] Kitchen ticket is readable at practical working distance and line/order hierarchy is clear.
- [ ] Quantity / code / name and option/custom-adjustment instructions are not ambiguous.
- [ ] Long comment/address/product/option content wraps without clipping or disappearing.
- [ ] Customer ticket item prices, TTC total, VAT breakdown and identity block are readable.
- [ ] No essential content is outside the printer's imageable area.
- [ ] Printing latency does not freeze the WPF UI; operator can see truthful busy/status feedback.

Result: Pending.

## 6. Scenario D — future order prominence

Create a synthetic order whose planned fulfilment date is later than the current business date.

- [ ] Confirm order.
- [ ] Both automatic documents show the exact planned future date/time very prominently.
- [ ] It is operationally difficult to mistake the output for a same-day order.
- [ ] Order ID/content remains ordinary; no new order subtype is created.
- [ ] On a later explicit reprint, persisted planned date/time remains present.

Result: Pending.

## 7. Scenario E — independent failure and retry

Safely make exactly one output destination fail (for example select an unavailable test queue for Kitchen while Customer remains valid).

- [ ] Confirm a new valid order.
- [ ] Order remains committed and retrievable.
- [ ] UI identifies Kitchen as failed/unavailable.
- [ ] Customer still attempts and succeeds; it is not suppressed by Kitchen failure.
- [ ] Restore/reselect Kitchen queue.
- [ ] Retry only Kitchen; no new order ID is allocated and no Customer retry is forced.
- [ ] Repeat the inverse vector if practical: Customer fails while Kitchen succeeds.
- [ ] Failure/retry messages are clear in FR and zh-CN.

Result: Pending.

## 8. Scenario F — ambiguous/duplicate-safety behavior

Use deterministic/manual acceptance support only if the implementation exposes a safe test seam for unknown submission outcome. Do not intentionally create uncontrolled duplicate production print jobs.

- [ ] An unknown/ambiguous submission state is not silently retried as an indistinguishable “first” ticket.
- [ ] UI explains uncertainty safely.
- [ ] Any operator-requested additional copy follows explicit reprint semantics/marking.

If this cannot be induced safely on a physical printer, accept exact deterministic automated evidence plus review the resulting WPF state manually.

Result: Pending.

## 9. Scenario G — existing-order modification does not auto-reprint

Choose an existing non-cancelled live order.

- [ ] Modify an ordinary field and Save.
- [ ] No Kitchen document prints automatically.
- [ ] No Customer document prints automatically.
- [ ] Modify payment values and Save; no automatic print occurs.
- [ ] Close/reopen lifecycle as applicable; no automatic print occurs solely because of lifecycle save.
- [ ] Order retains same stable ID.

Result: Pending.

## 10. Scenario H — selective latest-committed reprint

Using the order from Scenario G:

- [ ] Reprint Kitchen only; Customer is not also forced.
- [ ] Kitchen output carries `RÉIMPRESSION` prominently.
- [ ] Reprint Customer only; Kitchen is not also forced.
- [ ] Customer output carries `DUPLICATA` prominently.
- [ ] Both copies retain original stable order ID.
- [ ] Customer reprint reflects latest committed Card/Cash/payment correction and current committed total.
- [ ] Kitchen/customer reprint content reflects the latest saved modification, not the older state.

Result: Pending.

## 11. Scenario I — unsaved edits never print as committed data

Start editing an existing order but do not Save.

- [ ] Change one clearly visible value in the editor.
- [ ] Explicit reprint is disabled/blocked or otherwise cannot use the unsaved editor value.
- [ ] UI directs the operator to Save or abandon the edit first; it does not silently print the draft as business truth.
- [ ] Abandon/cancel edit and reprint; output matches the persisted state.
- [ ] Repeat with Save first; output then matches the newly committed state.

Result: Pending.

## 12. Scenario J — Cancelled printing/reprinting

Use a synthetic order that can safely be cancelled.

- [ ] Cancel order through ordinary lifecycle UI.
- [ ] Cancellation itself does not auto-print.
- [ ] Reprint Kitchen: output remains same order and shows both `ANNULÉ` and `RÉIMPRESSION` prominently.
- [ ] Reprint Customer: output remains same order and shows both `ANNULÉ` and `DUPLICATA` prominently.
- [ ] Printing does not reactivate or duplicate the order.

Result: Pending.

## 13. Scenario K — non-authoritative/read-only printing

Use a legitimate M07 paired non-authoritative/read-only device with a committed local copy. Do not change generation/perform DR merely for this scenario.

- [ ] Persistent M07 warning clearly states non-authoritative/read-only/stale risk.
- [ ] Select an existing locally available committed order.
- [ ] Kitchen reprint remains available.
- [ ] Customer reprint remains available.
- [ ] Print succeeds from the local committed copy.
- [ ] Warning does not disappear after print.
- [ ] Device does not become writable/authoritative.
- [ ] No handoff, generation, recovery, freshness or synchronization claim is created by printing.
- [ ] Business write controls remain blocked exactly as before.

Result: Pending.

## 14. Scenario L — control-state/localization preservation

Establish representative state before print action:

- Catalogue search/category/status selection;
- Caisse/order-entry harmless draft state where appropriate;
- Commandes search/date/selected order;
- M07 warning/authority status;
- selected printer queues.

Then:

- [ ] perform a successful reprint;
- [ ] perform a controlled failed print;
- [ ] switch FR → zh-CN → FR;
- [ ] verify unrelated search/date/selection/draft values remain unchanged;
- [ ] verify printer selections remain unchanged;
- [ ] verify failure/retry status remains truthful/localized;
- [ ] verify M07 authority/read-only state remains unchanged.

Result: Pending.

## 15. Scenario M — restart and missing-printer recovery

- [ ] Commit an order while one output has failed.
- [ ] Close/restart without modifying the order.
- [ ] Retrieve the committed order successfully.
- [ ] Correct printer configuration.
- [ ] Perform the appropriate independent retry/reprint without creating a new order.
- [ ] Existing `local-settings.json` upgraded from a pre-M08 shape remains readable and other M07 settings survive.

Result: Pending.

## 16. Scenario N — regression boundary

Perform a short smoke sweep on the same accepted candidate:

- [ ] create one ordinary order;
- [ ] retrieve/search existing order;
- [ ] save a modification;
- [ ] payment/lifecycle controls still work as authorized;
- [ ] catalogue/settings writes still obey authority guard;
- [ ] non-authoritative business writes remain blocked;
- [ ] normal M07 close/authority presentation remains intact;
- [ ] no M09/M10/M11/M12/M13 feature appears unexpectedly.

Result: Pending.

## 17. Owner final disposition

- [ ] Scenario A Passed
- [ ] Scenario B Passed
- [ ] Scenario C Passed
- [ ] Scenario D Passed
- [ ] Scenario E Passed
- [ ] Scenario F Passed / accepted deterministic substitute where noted
- [ ] Scenario G Passed
- [ ] Scenario H Passed
- [ ] Scenario I Passed
- [ ] Scenario J Passed
- [ ] Scenario K Passed
- [ ] Scenario L Passed
- [ ] Scenario M Passed
- [ ] Scenario N Passed

**Overall M08 result:** NOT EXECUTED / NOT PASSED.

Passing this checklist does not authorize PR merge. Merge requires a separate explicit project-owner merge approval after final exact-head controller review.
