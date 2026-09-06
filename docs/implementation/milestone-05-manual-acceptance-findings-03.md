# M05 manual Windows/WPF acceptance findings — batch 03

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-02  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `e4796207c0ffa7c8e3c3655b8a14d950e0d421a6`  
**Overall result:** Passed — all three focused FIX-06 manual regression checks passed. M05 remains Partial only because the remaining lifecycle/payment/dashboard manual checklist has not yet been completed.

## FIX-06 directed regression results

After independent code/CI review of `M05-MANUAL-ACCEPTANCE-LOCALIZATION-TIME-LAYOUT-FIX-06`, the project owner republished and manually exercised the three affected Windows/WPF paths.

Passed:

1. **Optional planned fulfilment time** — a new order can be confirmed with a required fulfilment mode and planned date while both planned-time selectors remain unset. The order saves normally without inventing a time.
2. **Live localization of an already-visible validation message** — after a red validation message is visible, switching FR -> zh-CN -> FR immediately re-renders that message in the newly selected language rather than retaining stale text.
3. **Commandes long-text width behavior** — the nine approved business columns remain unchanged; the previously observed filler region is removed by allowing `Commentaire` and `Adresse` to consume available width, and horizontal scrolling remains available when the window is narrow.

No new finding was reported during this focused regression.

## Acceptance state

The findings recorded in batch 02 for live validation localization, Commandes filler width, and optional planned fulfilment time are now manually closed.

The previously passed batch-01 and FIX-05 checks remain accepted and do not require wholesale repetition.

Do not mark M05 Passed yet. Continue with the remaining existing-order snapshot, payment/lifecycle, reuse/date, and dashboard manual acceptance checks.

PR #10 must remain open/unmerged until explicit project-owner merge approval. M06 remains not authorized.
