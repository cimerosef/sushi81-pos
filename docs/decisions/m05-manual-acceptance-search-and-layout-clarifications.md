# M05 manual-acceptance search and layout clarifications

**Status:** Approved  
**Approved by:** project owner  
**Approval date:** 2026-09-02  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Scope:** operator-facing M05 search and presentation only; no order/payment/data-model semantic change

## Context

During the first project-owner Windows/WPF M05 acceptance batch, ordinary order creation, stable human references, same-day reference increment, Abandon, same-ID Save and restart persistence behaved as expected. The operator found one search usability gap and approved two layout simplifications before continuing the remaining M05 manual checklist.

This record supersedes any narrower M05 presentation assumption that would preserve exact-only operator-reference search or keep the M04 diagnostic order browser/reload UI duplicated inside Caisse.

## D1 — Live search is partial for all three operator search fields

The Commandes live search remains one free-text search across the current live database.

A non-empty query must support substring/partial matching for:

- human order reference;
- telephone;
- comment.

Requirements:

- full exact reference/telephone/comment queries continue to match;
- a meaningful substring of an order reference must match that reference;
- telephone partial matching must work against stored normalized French telephone values even when the operator types an ordinary local-format fragment and even when punctuation/spacing differs;
- comment matching remains substring-based;
- Cancelled orders remain searchable;
- current-live-database and M12 archive boundaries are unchanged;
- do not introduce a separate advanced-search form for M05.

Implementation may maintain a normalized digits-only/canonical telephone comparison form internally, but must not change the persisted customer telephone meaning merely to make search work.

## D2 — Commandes uses top-list / bottom-detail composition

The dedicated Commandes workspace must place:

1. search/date/refresh controls at the top;
2. the orders DataGrid below those controls, spanning the available width;
3. the selected-order structured detail below the DataGrid.

The list must support both vertical and horizontal scrolling. Column widths should remain practical rather than compressing every field to fit the viewport.

The operator-facing list should expose enough distinguishing information to make the wider horizontal list useful, including at least:

- human reference;
- planned date;
- planned time;
- fulfilment mode (Retrait/Livraison);
- status;
- Total TTC;
- CB;
- Espèce;
- difference/remaining;
- telephone;
- comment;
- delivery address.

Presentation width/order is a technical choice as long as the fields remain readable in French and Simplified Chinese and horizontal scrolling works at supported window sizes.

The selected-order detail keeps the existing M05 structured edit/payment/lifecycle behavior and must remain vertically scrollable when the available lower area is smaller than its content.

## D3 — Remove duplicated M04 saved-order browser/reload UI from Caisse

Caisse remains the fast new-order-entry workspace plus the compact M05 operational dashboard strip.

Remove the M04-era operator UI that duplicates Commandes functionality, specifically:

- the saved/reloaded-order summary group;
- the old technical/GUID-style order ID entry/display;
- the `Recharger ID` / reload-order button row;
- the Caisse `Commandes enregistrées` / date-browser group and its order list.

The dedicated Commandes destination is now the operator-facing location for finding and viewing committed orders.

Underlying Application/store seams may remain when still required by regression compatibility or another approved workflow; removal of the duplicated Caisse controls does not authorize deleting persistence capabilities merely because their old UI is gone.

Use the released vertical space to enlarge the Caisse Panier area substantially. The cart should consume available vertical space naturally and retain its own scrolling for long carts. Do not add a replacement diagnostic summary beneath the cart.

## Non-changes

This clarification does not change:

- order reference generation or immutability;
- order lifecycle/status semantics;
- payment arithmetic/effective dates;
- snapshot-preserving modification rules;
- dashboard accounting semantics;
- M12 archive ownership;
- M06+ scope;
- the requirement that PR #10 remain unmerged until explicit project-owner approval.
