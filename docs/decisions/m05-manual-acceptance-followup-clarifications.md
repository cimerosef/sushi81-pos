# M05 manual-acceptance follow-up clarifications

**Status:** Approved  
**Approved by:** project owner  
**Approval date:** 2026-09-02  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Scope:** manual-acceptance localization/layout corrections plus restoration of the frozen V1 optional planned-fulfilment-time rule

## Context

During project-owner Windows/WPF acceptance of remediation `M05-MANUAL-ACCEPTANCE-SEARCH-LAYOUT-FIX-05`, the search, Commandes top-list/bottom-detail composition, Caisse cleanup and human-reference feedback behaved as expected. Three follow-up findings remained:

1. an already-visible red validation message stayed in the previous UI language after FR/zh-CN switching;
2. the fixed-width Commandes columns left a large meaningless filler region to the right of `Adresse` on a wide window;
3. Caisse still blocked confirmation until a planned fulfilment time was selected.

The third finding exposed an existing specification inconsistency rather than a new product change. The frozen V1 product requirement `FR-017` says planned fulfilment **date and optional time** are stored separately, and `docs/data-model.md` marks `planned_fulfilment_date` required and `planned_fulfilment_time` not required. The later M04 implementation contract section 4.5 incorrectly strengthened time to mandatory for new POS confirmation. That M04 clause conflicts with the frozen product/data semantics and is superseded by D3 below.

## D1 — Visible validation feedback follows the active UI language

When the operator changes between French and Simplified Chinese, currently visible application validation/help/error text owned by the application UI must be rendered in the newly selected language without requiring the user to change another business field first.

This includes, at minimum, the Caisse validation message shown when fulfilment mode is still unselected.

Requirements:

- switching FR → zh-CN or zh-CN → FR updates an already-visible validation message immediately or as part of the language-change refresh;
- the underlying validation condition remains unchanged;
- language switching must not mutate cart contents, fulfilment choice, planned date/time, telephone, address, comment, discount request, manual total or other unrelated editor state;
- user-entered business text is never translated;
- implementation should preserve structured validation state or deterministically re-render it rather than treating a previously localized display string as authoritative business state.

## D2 — Commandes uses available width instead of leaving a filler region

The approved Commandes business columns remain exactly the existing columns plus the three additions:

- `Mode`;
- `Commentaire`;
- `Adresse`.

No financial columns are added.

At normal and wide window sizes, the grid should not leave a large empty filler area after the last business column when that width can usefully improve `Commentaire` and/or `Adresse` readability. Allocate the remaining horizontal viewport primarily to those long-text columns using a stable WPF sizing strategy.

At narrower supported widths, preserve horizontal scrolling instead of crushing all columns below practical readable widths. Exact proportions/minimum widths are a technical choice.

The visual filler area is not a tenth business column and must not be represented as one in the data/view model.

## D3 — Planned fulfilment time is optional in V1

The frozen V1 rule is:

- `planned_fulfilment_date` is required for a confirmed order;
- `planned_fulfilment_time` is optional;
- leaving the time unset must not block otherwise-valid Retrait or Livraison confirmation;
- when the operator does choose a time through the ordinary Caisse UI, it remains a structured approved slot using hours `11, 12, 13, 14, 18, 19, 20, 21, 22` and minutes `00, 05, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55`;
- a persisted null planned time remains valid for current as well as historical orders;
- future/due-today/overdue derivation and turnover attribution depend on planned fulfilment **date**, not on the presence of a time;
- list/detail/printing/export code must tolerate an absent planned time and render it as empty/unspecified according to each existing presentation contract.

This decision restores the already-approved semantics in `docs/product-requirements.md` FR-017 and `docs/data-model.md` section 6. It explicitly supersedes the contradictory statements in `docs/implementation/milestone-04-order-entry.md` section 4.5 that described planned fulfilment time as required for new POS confirmation.

This is a specification-correction amendment; it does not authorize a new order model, migration or alternative scheduling workflow.

## Non-changes

This clarification does not change:

- mandatory explicit Retrait/Livraison selection;
- mandatory planned fulfilment date;
- approved future/advance-order date semantics;
- order reference generation;
- payment/lifecycle semantics;
- pricing, discount, delivery-minimum or VAT rules;
- the approved Commandes column set other than width allocation;
- M06+ scope;
- the requirement that PR #10 remain open/unmerged until explicit project-owner approval.
