# M04 manual Windows/WPF acceptance findings — attempt 01

**Status:** Acceptance stopped / failed early; remediation required  
**Date:** 2026-08-31  
**Reviewed PR:** #6 — `M04: first complete order-entry vertical slice`  
**Operator-tested head:** `4cdbe59fc980b40f38770f7b2b565f0c3d9e936c`  
**Follow-up authority:** `docs/decisions/m04-order-entry-operator-ergonomics-amendment.md`

The project owner began the required Windows/WPF operator acceptance and found multiple first-use usability defects before the full checklist could be completed. M04 therefore remains **In progress** and PR #6 must remain OPEN/UNMERGED.

The purpose of this record is to preserve the observed operator problems and the required remediation boundary. It is not a new M05 authorization.

## 1. Category navigation is too slow

Observed behavior:

- Caisse uses a category drop-down as the main category selector.
- The operator must repeatedly open the drop-down to move between categories.

Required remediation:

- replace the drop-down-first interaction with the Approved category-first two-pane navigation in `m04-order-entry-operator-ergonomics-amendment.md`;
- categories must remain directly visible; selecting one immediately updates the adjacent Product list;
- keep code/name search as a secondary tool.

## 2. Cart quantity/delete controls drift with line content

Observed behavior:

- `-`, `+`, and delete controls sit too close to Product text;
- their horizontal positions vary visually with line content/length;
- the result is hard to scan and visually untidy.

Required remediation:

- use stable cart-row columns;
- Product/configuration information uses the flexible left area;
- line total and `-` / `+` / delete controls use fixed/right-aligned columns;
- the controls must occupy the same horizontal positions for every cart line regardless of Product name/configuration length;
- preserve localized quantity text and current pricing behavior.

## 3. Planned-time entry is ergonomically poor

Observed behavior:

- the operator must type an `HH:mm` string manually;
- finding/typing the colon is unnecessarily awkward for rapid counter operation;
- malformed text blocks confirmation.

Required remediation:

- retain structured planned-time semantics but replace the free-form normal path with the Approved click-friendly hour/minute selector;
- no colon typing required.

## 4. Delivery-address control is too short

Observed behavior:

- address entry is visually too narrow for ordinary delivery-address text.

Required remediation:

- give Address materially more horizontal space, preferably stretching across the practical order-information width;
- preserve Address as optional for M04 initial Livraison confirmation;
- ensure Comment also remains practical and readable.

## 5. Confirmed-order viewing is not discoverable

Observed behavior:

- after creating an Order, the operator cannot immediately understand where to view the saved Order;
- the existing exact-ID/reloaded snapshot area is not sufficiently obvious in the normal flow.

Required remediation:

- immediately after successful confirmation, show the just-confirmed committed snapshot and stable Order ID in a clearly discoverable area without requiring the operator to re-enter the ID;
- keep exact-ID reload for verification/reopening a read-only historical snapshot;
- make the exact-ID field/action and committed snapshot area visually understandable/reachable;
- do **not** implement M05 general live order search, phone/comment search or committed-order editing.

## 6. Past planned date can be confirmed

Observed behavior:

- a new Order was successfully confirmed with a planned fulfilment date earlier than the current business date.

Required remediation:

- implement Approved C1: new-order planned date must be `>= IBusinessClock.BusinessDate`;
- enforce in Application/Domain and prevent/limit past selection in WPF;
- add localized feedback and automated boundary tests.

## 7. Simple Product addition opens an unnecessary dialog

Observed behavior:

- adding a Product opens `OptionSelectionDialog` even for a Product without an option workflow;
- this makes the common direct-add path unnecessarily complex;
- the current Add button position is also awkward for rapid selection.

Required remediation:

- no-options Product: Product-row double-click and explicit Add directly append quantity-1 line to cart without dialog;
- options-enabled Product: retain automatic option dialog and validation;
- double-click must be a first-class fast path;
- place explicit Add coherently with the Product selection area instead of as a visually detached bottom action.

## 8. Caisse Product-list column headers are not readable/visible

Observed behavior:

- the first/header row does not present usable Code / Name / Price column headings.

Required remediation:

- ensure real visible localized column headers for Code, Name and TTC price in FR and zh-CN;
- do not rely on a WPF binding pattern that fails for `DataGridTextColumn.Header`;
- add shown-window regression proving the header text is actually rendered.

## 9. Layout/readability acceptance must be repeated

The first operator pass stopped before the complete M04 checklist because the issues above make further acceptance inefficient/unreliable.

After remediation, repeat the M04 manual acceptance from the beginning. Automated tests must also cover the corrected primary interactions where practical, especially:

- category selection -> adjacent product refresh;
- simple Product explicit Add and double-click without dialog;
- options-enabled Product opens dialog;
- fixed/right-aligned cart controls across short/long configured lines;
- structured time picker empty/hour/minute behavior;
- past/today/future date boundaries;
- visible localized Product table headers;
- immediate post-confirm committed-order visibility;
- FR/zh-CN state preservation and default/min/resized/maximized layout.

## Governance

- No merge authorization is granted.
- M04 remains In progress.
- M05 is not started/authorized.
- The next implementation remediation stays on `codex/m04-order-entry` / PR #6.
- Manual acceptance is not claimed until the project owner reruns and explicitly passes it.
