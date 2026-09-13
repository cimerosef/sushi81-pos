# M08 final Windows/WPF manual acceptance — printing and reprinting

**Status:** Prepared checklist — **NOT EXECUTED / NOT PASSED**  
**Prepared:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Implementation authorization:** AUTHORIZED for M08 only; this does not authorize merge or M09+
**Execution gate:** OPEN only for M08-MANUAL-ACCEPTANCE-REMEDIATION-11; owner acceptance is blocked pending the Kitchen-only B-PC physical retest
**Owner visual decision:** `../decisions/m08-print-layout-and-receipt-identity.md`

This checklist is project-owner acceptance only. Codex/CI may prepare evidence and an exact artifact but must never check owner-manual items or declare M08 Passed on the owner's behalf.

Archive printing (`AC-PRINT-009`) is not part of M08 acceptance; it belongs to M12.

The owner-selected visual targets are:

- kitchen: page 1 of the supplied `modèle impression.pdf` reference;
- customer: page 2 of that reference, following the current Hiboutik receipt structure/thermal appearance without copying Hiboutik branding.

## 1. Exact candidate identity — fill before testing

- exact production implementation head: pending final R11 push
- exact docs/evidence record: `docs/implementation/milestone-08-worklog.md` §17 and the matching PR #14 `CODEX_DONE: M08-MANUAL-ACCEPTANCE-REMEDIATION-11`
- M08 PR number: #14 (OPEN / unmerged)
- self-contained `win-x64` publish location: `artifacts/m08-win-x64-r11-final`
- executable SHA-256: `715167295642AAFADE0356644066C891893B6004ED04DA47A0793962F0C1E79C`
- Release test result: **575/575 Passed**, 0 failed, 0 skipped
- Release build warnings/errors: **0 / 0**
- exact-head implementation-commit CI: pending final R11 push
- physical Windows PC/device used:
- authority state at start:
- Kitchen configured Windows queue:
- Customer configured Windows queue:
- real printer model(s), if useful for diagnosis:
- approved receipt identity block verified: `Sushi 81 / 12 Rue Gaston Darley / 77140 Nemours - FRA / SIRET 90805211100014 / TVA FR03908052111 / APE 5610C`
- owner page-1/page-2 print reference available for side-by-side comparison: Yes / No
- only synthetic/non-sensitive acceptance orders used: Yes / No

The R09 A-PC Customer PDF/layout review passed the targeted Customer presentation, including far-right option amounts. The R10 B-PC `GP-C200 Series` comparison accepted the Customer output as-is and found the Kitchen output materially successful, with only ordinary Kitchen metadata typography and item/option list breathing room requiring the R11 refinement. Run the Kitchen-only physical retest on the R11 exact candidate; no Customer retest is requested, and all owner boxes below remain unchecked.

## 2. Environment and safety rules

- Preserve existing M07 authoritative business data/authority state; do not rerun destructive DR merely for M08.
- Use the production WPF application and production Windows print-queue path.
- A Windows PDF/XPS software queue may supplement deterministic checks, but final layout/latency/practicality must use the actual intended physical printer path where available.
- Do not manually edit `live.db`, authority state or print output data.
- Printer failure must never be induced by corrupting business data.
- Do not start M09 during M08 acceptance.
- Page 3/page 4 of the owner-supplied PDF are not the selected M08 target layouts.

## 3. Scenario A — local printer configuration and receipt identity

Starting state: application installed with M08 candidate; order data available.

- [ ] Open the local printer configuration surface.
- [ ] Installed Windows queues are listed with understandable names.
- [ ] Select Kitchen queue and Customer queue independently.
- [ ] Configure the same physical queue for both and confirm this is accepted.
- [ ] Save and restart the app; both selections persist.
- [ ] Temporarily make one configured queue unavailable/rename/remove it as safely practical; the application reports the affected destination clearly and allows re-selection.
- [ ] Printer configuration is available while the app is non-authoritative/read-only and does not change business authority.
- [ ] FR and zh-CN printer-setup labels/status are usable.
- [ ] On an authoritative device, inspect/edit the receipt-identity settings surface if exposed by the implementation; the approved default values are present.
- [ ] On a non-authoritative/read-only device, receipt identity is readable/usable for printing but not editable as a business setting.
- [ ] Changing only local printer queues does not alter receipt identity; changing receipt identity through the authorized business-settings path does not alter local printer queues.

Result: Pending.

## 4. Scenario B — normal new-order automatic printing / durable-first

Use an ordinary same-day synthetic order.

- [ ] Enter a valid new order and confirm it.
- [ ] The order becomes visibly committed/stable before output failure/success can change persistence semantics.
- [ ] Exactly one Kitchen document is submitted automatically.
- [ ] Exactly one Customer document is submitted automatically.
- [ ] Kitchen content matches the committed order ID/reference, fulfilment, planned time, telephone/address/comment where entered, ordered lines/options/adjustments and authoritative total.
- [ ] Customer content matches the committed items/prices, total, persisted VAT and applicable payment state plus the approved Sushi 81 identity block.
- [ ] The two documents use the same committed order ID.
- [ ] Restart application and retrieve the order; it still exists independent of printing.

Result: Pending.

## 5. Scenario C — kitchen visual/layout match to owner page 1

Using the intended physical printer path and the page-1 owner reference:

- [ ] Centered `CUISINE` heading has the same compact operational feel as the reference.
- [ ] `Cmd`/order reference and original confirmation time are immediately readable near the top.
- [ ] Two upper dashed separators visibly define the customer/order-information block.
- [ ] Telephone, delivery address, fulfilment/planned date-time and full comment/remarks appear between those upper separators when applicable, as required by the owner.
- [ ] Blank optional values are omitted cleanly rather than leaving confusing labels/empty placeholders.
- [ ] Product lines retain the compact `1x CODE Name` style of the reference.
- [ ] Structured options/custom adjustments are visually attached to the correct parent product and cannot be confused with a separate item.
- [ ] Long comments/addresses wrap without clipping; long Customer product/option labels stay single-line and trim with price columns preserved.
- [ ] Final total is prominent at the bottom in the same compact ticket hierarchy.
- [ ] Kitchen ticket remains readable at practical working distance.

Result: Pending.

## 6. Scenario D — customer visual/layout match to owner page 2

Using the intended physical printer path and the page-2 Hiboutik reference:

- [ ] Top identity block is centered and contains exactly: Sushi 81; 12 Rue Gaston Darley; 77140 Nemours - FRA; SIRET 90805211100014; TVA FR03908052111; APE 5610C.
- [ ] Ticket/order reference and original date/time use the accepted left-aligned ticket row beneath the identity block in the same overall hierarchy as the reference.
- [ ] Customer/order information block uses existing committed fields only; no invented customer-name field appears merely to mimic Hiboutik.
- [ ] Item rows preserve a dense thermal-receipt table feel with quantity, product/code/description and saved price information.
- [ ] Long product and option descriptions remain single-line and are deterministically ellipsis-trimmed before they can overlap or displace the right-aligned price columns.
- [ ] HT/VAT section is compact and derived from persisted tax snapshots, not current Catalogue VAT reconstruction.
- [ ] Final `Total`/EUR amount is visually much more prominent than surrounding rows, resembling the reference hierarchy.
- [ ] Payment information is readable and reflects the committed Card/Cash state.
- [ ] No Hiboutik version/marketing/website/vendor footer is copied or falsely attributed to this application.
- [ ] If a small technical footer is shown, it truthfully identifies Sushi81 POS only.
- [ ] The typeface/character density/line spacing look sufficiently close to the current Hiboutik thermal receipt for the owner to consider the visual transition natural.
- [ ] No essential content is outside the printer's imageable area.

Result: Pending.

## 7. Scenario E — future order prominence

Create a synthetic order whose planned fulfilment date is later than the current business date.

- [ ] Confirm order.
- [ ] Both automatic documents show the exact planned future date/time very prominently.
- [ ] On Kitchen, future information remains in/adjacent to the upper customer/order-information block without destroying the page-1 layout.
- [ ] It is operationally difficult to mistake either output for a same-day order.
- [ ] Order ID/content remains ordinary; no new order subtype is created.
- [ ] On a later explicit reprint, persisted planned date/time remains present.

Result: Pending.

## 8. Scenario F — independent failure and retry

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

## 9. Scenario G — ambiguous/duplicate-safety behavior

Use deterministic/manual acceptance support only if the implementation exposes a safe test seam for unknown submission outcome. Do not intentionally create uncontrolled duplicate production print jobs.

- [ ] An unknown/ambiguous submission state is not silently retried as an indistinguishable “first” ticket.
- [ ] UI explains uncertainty safely.
- [ ] Any operator-requested additional copy follows explicit reprint semantics/marking.

If this cannot be induced safely on a physical printer, accept exact deterministic automated evidence plus review the resulting WPF state manually.

Result: Pending.

## 10. Scenario H — existing-order modification does not auto-reprint

Choose an existing non-cancelled live order.

- [ ] Modify an ordinary field and Save.
- [ ] No Kitchen document prints automatically.
- [ ] No Customer document prints automatically.
- [ ] Modify payment values and Save; no automatic print occurs.
- [ ] Close/reopen lifecycle as applicable; no automatic print occurs solely because of lifecycle save.
- [ ] Order retains same stable ID.

Result: Pending.

## 11. Scenario I — selective latest-committed reprint

Using the order from Scenario H:

- [ ] Reprint Kitchen only; Customer is not also forced.
- [ ] Kitchen output carries `RÉIMPRESSION` prominently without making the page-1 content unreadable.
- [ ] Reprint Customer only; Kitchen is not also forced.
- [ ] Customer output carries `DUPLICATA` prominently without making the page-2 content unreadable.
- [ ] Both copies retain original stable order ID.
- [ ] Customer reprint reflects latest committed Card/Cash/payment correction and current committed total.
- [ ] Kitchen/customer reprint content reflects the latest saved modification, not the older state.

Result: Pending.

## 12. Scenario J — unsaved edits never print as committed data

Start editing an existing order but do not Save.

- [ ] Change one clearly visible value in the editor.
- [ ] Explicit reprint is disabled/blocked or otherwise cannot use the unsaved editor value.
- [ ] UI directs the operator to Save or abandon the edit first; it does not silently print the draft as business truth.
- [ ] Abandon/cancel edit and reprint; output matches the persisted state.
- [ ] Repeat with Save first; output then matches the newly committed state.

Result: Pending.

## 13. Scenario K — Cancelled printing/reprinting

Use a synthetic order that can safely be cancelled.

- [ ] Cancel order through ordinary lifecycle UI.
- [ ] Cancellation itself does not auto-print.
- [ ] Reprint Kitchen: output remains same order and shows both `ANNULÉ` and `RÉIMPRESSION` prominently.
- [ ] Reprint Customer: output remains same order and shows both `ANNULÉ` and `DUPLICATA` prominently.
- [ ] Markings remain visually obvious even though they alter the normal reference spacing.
- [ ] Printing does not reactivate or duplicate the order.

Result: Pending.

## 14. Scenario L — non-authoritative/read-only printing

Use a legitimate M07 paired non-authoritative/read-only device with a committed local copy. Do not change generation/perform DR merely for this scenario.

- [ ] Persistent M07 warning clearly states non-authoritative/read-only/stale risk.
- [ ] Select an existing locally available committed order.
- [ ] Kitchen reprint remains available.
- [ ] Customer reprint remains available.
- [ ] Print succeeds from the local committed copy using the locally available committed receipt identity.
- [ ] Warning does not disappear after print.
- [ ] Device does not become writable/authoritative.
- [ ] No handoff, generation, recovery, freshness or synchronization claim is created by printing.
- [ ] Business write controls remain blocked exactly as before.

Result: Pending.

## 15. Scenario M — control-state/localization preservation

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
- [ ] verify M07 authority/read-only state remains unchanged;
- [ ] verify printed business data/identity is not translated by UI-language switching.

Result: Pending.

## 16. Scenario N — restart and missing-printer recovery

- [ ] Commit an order while one output has failed.
- [ ] Close/restart without modifying the order.
- [ ] Retrieve the committed order successfully.
- [ ] Correct printer configuration.
- [ ] Perform the appropriate independent retry/reprint without creating a new order.
- [ ] Existing `local-settings.json` upgraded from a pre-M08 shape remains readable and other M07 settings survive.
- [ ] Existing SQLite database upgrades additively to the M08 receipt-identity/settings schema without losing orders/catalogue/payments/authority-relevant data.
- [ ] Approved receipt identity survives restart/handoff/reopen through the business database path.

Result: Pending.

## 17. Scenario O — regression boundary

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

## 18. Owner final disposition

- [ ] Scenario A Passed
- [ ] Scenario B Passed
- [ ] Scenario C Passed
- [ ] Scenario D Passed
- [ ] Scenario E Passed
- [ ] Scenario F Passed
- [ ] Scenario G Passed / accepted deterministic substitute where noted
- [ ] Scenario H Passed
- [ ] Scenario I Passed
- [ ] Scenario J Passed
- [ ] Scenario K Passed
- [ ] Scenario L Passed
- [ ] Scenario M Passed
- [ ] Scenario N Passed
- [ ] Scenario O Passed

**Overall M08 result:** NOT EXECUTED / NOT PASSED.

Passing this checklist does not authorize PR merge. Merge requires a separate explicit project-owner merge approval after final exact-head controller review.
