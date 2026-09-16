# M09 — Hiboutik paste-order fallback — final Windows/WPF manual acceptance

**Status:** **PASSED — owner Windows/WPF manual acceptance complete; ready for separate merge approval**
**Milestone:** M09
**Owner:** project owner / manual acceptance executor
**Accepted production candidate:** `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d` (FIX-14)
**Accepted artifacts:** EXE SHA-256 `CDC1257A698FE90237316FFE92361BF20EEF97CB44065AFBF70202EE0D0A26BA`; ZIP SHA-256 `00CEFAECB56666FABB03B7D141A4B75D18CF4DAA1A270358E7D3C457D5736319`
**Accepted verification:** exact-head CI run #712 / `35019616149` succeeded; Release evidence `651/651` passed, 0 failed, 0 skipped; Release build 0 warnings / 0 errors
**Result:** **PASSED**

This checklist is the owner-facing manual acceptance for the production Windows/WPF implementation. It does not authorize implementation or merge.

## WP6 candidate-preparation note

WP1–WP5 implementation and automated evidence were accepted by controller review. WP6 prepared the exact self-contained `win-x64` Windows/WPF candidate and reconciled the M09 evidence matrix. The owner then completed the A–N acceptance on the accepted FIX-14 candidate and recorded the final disposition `OWNER_FINAL_ACCEPTANCE: M09 manual acceptance PASSED` in PR #17 comment `5704531105`.

This documentation-only closure adds no new physical/operator observation. The section dispositions and evidence matrix below reconcile already-durable owner, controller and automated evidence; the prepared checkbox controls remain an owner-entry template and are not used to manufacture a new observation. Historical evidence recorded on earlier replacement candidates remains identified as historical and is carried forward only where the later controller/owner records accepted it. Separate project-owner merge approval remains required.

## Active remediation boundary

The owner verified candidate `18ec621d077c1da61994e1cb8657ddb67ab752eb` and found that an unresolved parse left **Réinitialiser / réimporter** disabled after returning to Caisse. That defect was remediated by `M09-MANUAL-A-HIBOUTIK-RESET-ENABLEMENT-FIX-12`, after which owner checklists A–E passed on replacement candidate `a18cca2ad51d3676bc9fc2a99d416ad82a1f1a7a`. Checklist F then found that changing only cumulative CB on a discounted order with an active manual total incorrectly repriced the order and cleared the manual override. The payment-only defect was remediated by `M09-MANUAL-F-MANUAL-TOTAL-PAYMENT-PRESERVATION-FIX-13` at `3269a27f6a5ff41be9085abc36cee39a2258eb32`; controller review then found that a temporary quantity `4 -> 5 -> 4` interaction could still falsely reprice the final unchanged state. The `M09-MANUAL-F-REVIEW-QUANTITY-REVERT-NET-STATE-FIX-14` remediation completed at the accepted production candidate recorded above. These historical defects and their narrow remediations remain part of the evidence trail; the final owner disposition accepted the resulting A–N evidence without claiming that every earlier step was rerun on FIX-14.

Use only the matching durable WP6 `CODEX_DONE` record on PR #17 for the final candidate head, executable/ZIP paths, hashes and sizes. The documentation commit cannot safely embed its own final commit SHA before that commit exists. The M11 `Gestion SUSHI 81` export-exclusion cross-check remains outside this M09 owner checklist's implementation scope.

Use synthetic/sanitized pasted text only during recorded acceptance unless the owner deliberately performs a local unrecorded real-store check. Do not commit screenshots/logs containing real customer/order data.

## Evidence basis and traceability

The section dispositions below are a documentation reconciliation of existing durable PR #17 Conversation evidence, not newly performed Codex observations. The authoritative final result is the PASSED disposition above and the owner final-acceptance record in comment `5704531105`; the original owner-entry checkbox controls are retained unchanged.

| Sections | Durable evidence basis |
|---|---|
| A–E | Existing owner/controller acceptance evidence, including the historical replacement-candidate A–E record and its accepted carry-forward after the narrow remediations. |
| F | FIX-14 completion/controller evidence and owner final disposition, including source/POS-total separation, payment/manual-total preservation and the quantity `4 -> 5 -> 4` net-state regression. |
| G–H | Existing owner/controller evidence for source-total reliability and the known `Livraison (0)` service-line boundary. |
| I–J | Existing owner/controller evidence for ordinary lifecycle/search/reprint behavior, anti-double-counting, and future/due/overdue operational inclusion. |
| K–M | Existing owner/controller evidence for authoritative/read-only enforcement, ordinary M08 printing/reprinting, and FR -> zh-CN localization/data preservation. |
| N | Controller privacy/safety audit in PR #17 comment `5703955265`. |

The owner’s final disposition is recorded in PR #17 comment `5704531105`; the accepted candidate, FIX-14 evidence and exact-head verification are preserved in the preceding durable PR records and matching completion/controller records. PR #17 remains OPEN / unmerged, and M10+ remain unauthorized.

## A. Entry point and no-write boundary

- [ ] From the normal Caisse/new-order workflow, a compact Hiboutik paste action is available without entering a separate emergency-order application area.
- [ ] The paste UI explains that the Hiboutik product-detail block can be pasted directly and that source `Total` lines are handled automatically.
- [ ] Pasting/parsing alone does not create a visible durable order in Commandes/order search.
- [ ] Closing/abandoning the parsed draft before confirmation leaves no durable order.
- [ ] Malformed/unsupported text reports a clear problem and creates no durable order.

## B. Normal whole-block paste

Using a sanitized block equivalent to:

```text
1 x AA1 Produit Exemple Alpha (5.50)
Total : 5.5
2 x BB2 Produit Exemple Beta (5.00)
Total : 10
TOTAL 15.5
```

- [ ] Product quantities/codes resolve correctly when matching active catalogue products exist.
- [ ] Interleaved `Total : ...` lines do not create cart products or user-facing parser errors.
- [ ] Final `TOTAL ...` does not create a cart product.
- [ ] The source display names/prices do not override current Sushi81 catalogue product definitions/pricing.

## C. Unknown/unresolved line handoff

- [ ] A source product with an unknown current code becomes visibly unresolved; it is not silently dropped.
- [ ] A product-like line with no reliable code becomes unresolved; no name-based automatic guess occurs.
- [ ] Unknown material text that is not one of the specifically supported ignored source lines becomes unresolved rather than disappearing.
- [ ] Each unresolved line exposes the original source line/context needed for operator judgment.
- [ ] **Select product** allows the operator to choose a current active catalogue product through the ordinary product-selection/search capability.
- [ ] A reliably parsed quantity is carried into the manually selected product by default.
- [ ] If quantity could not be trusted, the operator can/cannot proceed until quantity is corrected as required by the implemented workflow.
- [ ] **Ignore / not a product** requires an explicit operator action and resolves that line without creating a cart product.
- [ ] Final order confirmation remains unavailable/rejected while at least one unresolved line has no explicit disposition.
- [ ] Once every unresolved line is explicitly resolved/ignored, normal confirmation becomes possible subject to ordinary validation/options.

## D. Options

- [ ] Imported product with no enabled option groups can enter the ordinary cart directly.
- [ ] Imported product with enabled required option groups invokes the ordinary option workflow.
- [ ] Imported product with an optional option group still requires explicit review.
- [ ] The operator can explicitly confirm **no selection** for an optional group when appropriate.
- [ ] No separate Hiboutik-specific option editor appears.

## E. Fulfilment/date/customer fields

- [ ] Paste import does not attempt to populate Retrait/Livraison, planned date/time, telephone, address or comment from full-email text.
- [ ] The operator completes Retrait/Livraison through the ordinary POS field.
- [ ] The operator completes planned date/time through the ordinary POS fields.
- [ ] Telephone/address/comment remain ordinary editable fields.
- [ ] A future planned date entered manually behaves as an ordinary future order and receives the normal future-order/printing treatment.

## F. Source amount versus authoritative POS amount

Use a synthetic/sanitized case in which the Hiboutik source total and final POS amount diverge, preferably through the normal Retrait discount.

Example target behavior:

- source total = €35.90;
- ordinary POS Retrait discount applies;
- authoritative POS total = €32.31.

Verify:

- [ ] The ordinary POS total is calculated from current Sushi81 catalogue/business rules.
- [ ] The pasted/source amount does not overwrite the POS calculated total.
- [ ] The order can retain/show the source reference amount €35.90 when reliably parsed.
- [ ] The read-only source amount remains visibly distinct from the authoritative POS total.
- [ ] Customer/kitchen selling amount follows the ordinary authoritative POS total, not the source reference amount.
- [ ] CB/Espèce Close arithmetic uses the authoritative POS total, not the source reference amount.
- [ ] Ordinary manual total override still works exactly as for a normal POS order.

## G. Source total reliability

- [ ] A final parseable `TOTAL ...` line produces the source-reference total.
- [ ] When no final total exists but a complete/unambiguous set of per-item `Total : ...` lines exists, the source amount is derived correctly if that fallback is implemented.
- [ ] When source-total information is incomplete/ambiguous, the UI shows no false precise source amount; the order remains usable with the source amount absent.
- [ ] The POS never substitutes its own pre-discount/current-catalogue total as a missing Hiboutik source total.

## H. Known Livraison service line

Using a sanitized delivery-style block containing:

```text
1 x Livraison (0)
Total : 0
```

- [ ] The known Hiboutik service/technical line does not create a Sushi81 catalogue product/cart line.
- [ ] It does not force the POS fulfilment mode automatically; the operator still chooses ordinary Livraison explicitly.
- [ ] Its source `0` amount does not override current Sushi81 delivery-fee/minimum rules.

## I. Durable source identification and ordinary lifecycle

After confirming a paste-created order:

- [ ] It receives a normal Sushi81 order reference/ID.
- [ ] Ordinary order list/detail shows a compact passive `Hiboutik` indication.
- [ ] If source total exists, ordinary order list/detail exposes it read-only for matching/reconciliation.
- [ ] There is no dedicated emergency-order screen, status, counter or special reconciliation panel.
- [ ] Search works normally.
- [ ] Modification uses the same order ID.
- [ ] Source indication remains Hiboutik after modification.
- [ ] Source total remains the original reference amount after ordinary later modification.
- [ ] Payment entry, Close/reopen and Cancel behave like ordinary orders.
- [ ] Reprinting behaves like ordinary M08 reprinting.

## J. Anti-double-counting behavior

- [ ] A confirmed Hiboutik paste order does **not** increase ordinary POS-originated operational turnover.
- [ ] Payment recorded on it does **not** increase ordinary POS-originated received-payment/CB/Espèce summaries.
- [ ] It still appears in ordinary search and appropriate future/due/overdue operational views.
- [ ] No user control exists to change the source classification from Hiboutik to POS or vice versa.

The final `Gestion SUSHI 81` export exclusion remains a dedicated M11 cross-check and is not required to prove the M11 implementation here.

## K. Authority/read-only enforcement

- [ ] On an authoritative device, a valid fully resolved Hiboutik draft can be confirmed normally.
- [ ] On a non-authoritative/read-only device, final durable confirmation is blocked by the same ordinary authority guard.
- [ ] The paste path does not bypass M06/M07 authority semantics.

## L. Printing boundary

- [ ] Successful confirmation durably creates/reloads the order before initial printing.
- [ ] Normal M08 kitchen/customer initial printing is invoked.
- [ ] Print failure does not remove/roll back the committed Hiboutik paste order.
- [ ] Normal selective retry/reprint remains available.
- [ ] No Hiboutik-specific printer workflow appears.

## M. Localization and usability

- [ ] French UI labels/messages are clear for paste, unresolved state, product selection, ignore, source label and source amount.
- [ ] Simplified Chinese UI exposes equivalent controls/messages.
- [ ] Switching language does not translate/alter pasted source text or catalogue/order business data.
- [ ] The workflow remains fast enough for live-store use: operator can copy the entire product block, paste once, resolve only exceptional lines/options, complete normal order fields and confirm.

## N. Privacy/safety check

- [ ] No real customer/order sample is added to Git as part of M09 evidence.
- [ ] Diagnostic/log output does not retain the full pasted production text unnecessarily.
- [ ] The committed application does not auto-read email, scrape Hiboutik, call Hiboutik API or monitor clipboard in the background.

## Final owner disposition

- [ ] All required manual checks Passed on the accepted exact build/head.
- [ ] Any failures/remediation are recorded in the M09 worklog/PR before final acceptance.
- [ ] Owner declares M09 manual acceptance **PASSED**.
- [ ] Separate owner merge approval still required after controller review.
