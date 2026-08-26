# Current system

**Status:** Approved — Phase 1 baseline  
**Last updated:** 2026-08-26  
**Purpose:** Describe how Sushi 81 currently receives, records, prints, reconciles and archives orders before the standalone Sushi81 POS replaces the Excel/VBA workflow. This document records current behavior and workarounds; it does not define the final target architecture or freeze future business rules.

## 1. Operational landscape

Sushi 81 currently uses several tools with distinct roles rather than one unified POS system.

### 1.1 `POS_Caisse.xlsm`

`POS_Caisse.xlsm` is the day-to-day local order-entry tool. It is used primarily for telephone orders and is also used for walk-in/on-site orders because its workflow is faster and simpler than entering those orders directly in Hiboutik.

The normal operator workflow is performed through VBA UserForms rather than by editing Excel cells directly. VBA then writes the resulting data into Excel worksheets.

The workbook currently contains three main data worksheets:

- `Catalogue` — product master data;
- `Commandes` — one row per order, containing order-level information;
- `Ventes` — one row per ordered product line, containing sales detail.

The workbook also contains VBA forms/modules supporting the home screen, cash register, order validation, order search/detail, printing and automatic saving.

### 1.2 Hiboutik

Hiboutik is the official online/web-order system. Website orders arrive directly in Hiboutik and can be either pickup (`Retrait`) or delivery (`Livraison`).

Hiboutik also serves an important financial-recording role: all card revenue must ultimately be represented in Hiboutik. For orders originally created in `POS_Caisse.xlsm`, the product composition does not need to be reproduced in Hiboutik; the operational requirement is that the relevant card amount is added so Hiboutik's card revenue is correct.

Cash-only orders created in `POS_Caisse.xlsm` do not need to be added to Hiboutik.

### 1.3 `Gestion SUSHI 81.xlsm`

`Gestion SUSHI 81.xlsm` is not the order-entry POS. It is a separate management workbook used mainly for:

- keeping the current year's sales history;
- LCL reconciliation;
- product-sales analysis.

Data originating from `POS_Caisse.xlsm` is transferred into corresponding historical structures in `Gestion SUSHI 81.xlsm`. The `Hors Hiboutik` order data corresponds to the `Commandes` data from `POS_Caisse.xlsm`, with a corresponding sales-detail structure for the transferred `Ventes` data.

`Gestion SUSHI 81.xlsm` does not currently store Hiboutik's individual orders. An older workflow that stored Hiboutik daily Ticket Z information has been abandoned.

### 1.4 `Outils Sushi 81`

A separate Windows management tool now performs the POS sales transfer that was originally available through the `Export` button in `POS_Caisse.xlsm`.

The current transfer rule moves orders and sales detail dated J-2 or earlier from `POS_Caisse.xlsm` to `Gestion SUSHI 81.xlsm`, then removes those transferred rows from the POS workbook after a protected transfer workflow. The J-2 rule was originally chosen to leave a short operational buffer in case a customer returned about a previous-day order; it is not an accounting requirement and has not been important in practice.

### 1.5 Deployment and synchronization

Both `POS_Caisse.xlsm` and `Gestion SUSHI 81.xlsm` are stored in a OneDrive-synchronized environment and are used from two Windows computers:

- the shop computer for normal business operation;
- the home computer for end-of-day reconciliation/management.

The same workbook is opened from both locations at different times. The two computers are not intended to open the same workbook simultaneously.

## 2. Home screen and operator entry points

Opening the POS presents a VBA home screen rather than the raw Excel workbook.

The main home screen provides:

- `Caisse` — opens the order-entry form;
- `Commandes` — opens the order search/list workflow;
- `Admin` — exits the front-facing UserForm layer and exposes the Excel workbook for manual administrative work;
- `Export` — legacy transfer function, now largely replaced by `Outils Sushi 81`;
- daily summary values such as `CA du jour`, `CB` and `Espèce`;
- a `Mise à jour CA` action to force recalculation when the displayed values are not current.

The workbook is configured so that the Excel sheets remain hidden behind the front UserForms during normal use. `Admin` is therefore the normal way to reach the underlying worksheets when manual maintenance or reconciliation is required.

## 3. Catalogue

The `Catalogue` worksheet is maintained manually in Excel.

Known catalogue fields include:

- `Code`;
- `Nom`;
- `Categorie`;
- `Prix_TTC`;
- `TVA`;
- `Actif`;
- `Remise_OK`;
- `RaccourciCat`.

### 3.1 Product identity and activation

`Code` is the permanent unique product code. A code is not intended to be reused for a different product.

`Actif = NON` keeps the product in the catalogue/history but removes it from the normal Caisse product-selection interface.

`RaccourciCat` supplies the category shortcut displayed in the Caisse interface, such as `[BR]`, `[ML]` or `[RP]`, and also supports alphabetical display ordering of categories.

### 3.2 Historical stability

Catalogue changes do not rewrite historical orders. Historical sales retain the values that applied when the order was created.

In particular, changing a current catalogue price does not change historical `Ventes` rows.

### 3.3 Current option/variant limitation

Some products or menus require a customer choice, such as selecting one flavour among several available flavours. The current catalogue/order-line model has no structured product-option mechanism for this. These choices are therefore typed manually into the free-text `Commentaire`, which means the choice is not structurally attached to the relevant order item.

## 4. Order-entry workflow

### 4.1 Main use cases

`POS_Caisse.xlsm` is used for:

- telephone orders;
- walk-in/on-site orders.

Both telephone and website orders may be either `Retrait` or `Livraison`; order source and fulfilment mode are separate concepts.

### 4.2 Product selection

The Caisse interface provides:

- category filtering;
- a product list showing code, name and unit price;
- search by either product code or product name;
- a quantity input with `+` and `-` controls before addition;
- `Ajouter` to add the selected product;
- double-click on a product name as an alternative fast-add gesture;
- an in-progress cart (`Commande en cours`).

If the quantity of an already-added cart item is wrong, the current workflow allows the operator to double-click the cart item and modify its quantity.

This works but is inconvenient in a common telephone-order scenario where the customer states the product first and only afterwards states the quantity. The operator then has to reopen the cart line instead of adjusting the selected line directly.

### 4.3 Customer/order information

The validation form currently records:

- telephone number;
- address;
- free-text comment;
- payment category;
- fulfilment mode (`Retrait` or `Livraison`).

The system does not record a customer name.

The telephone number is the primary practical customer identifier and the main way to retrieve a previous order, but it is not technically mandatory for every order. For a walk-in order it may be omitted. It may also be omitted for a known telephone customer whose contact details are already stored on the operator's phone.

When a telephone number is entered, the current interface automatically formats the digits in the familiar French grouped-pair form (for example `07 67 91 03 44`) without requiring the operator to type spaces manually.

For delivery, the address is mandatory. In practice the address is often searched in Google Maps during the call and the complete Maps address, including postcode, is copied into the POS.

`Commentaire` is a deliberately flexible field. It is currently used for several different kinds of information, including:

- expected pickup/delivery time communicated to the customer;
- special preparation requests;
- customer preferences;
- product/menu choices that do not have a structured field;
- mixed-payment details during later reconciliation when needed.

For a known customer whose telephone number is not entered in the POS, the operator may use a smartphone contact whose contact name is itself an operational description (for example delivery address, frequently ordered dish or a distinctive customer characteristic). A handwritten note may then be added to the printed kitchen ticket so the order can be recognized when the customer arrives.

There is currently no structured customer-history feature that automatically recalls a previous address, frequent order or preference from the telephone number.

## 5. Fulfilment and commercial rules

### 5.1 Pickup (`Retrait`)

Pickup has no delivery fee.

The current 10% discount may be applied to eligible products if the resulting discounted order total is at least €15.

### 5.2 Delivery (`Livraison`)

Delivery has no delivery fee.

Delivery orders are accepted from an original-order total of €30 or more and are charged at normal price; the pickup 10% discount workflow is not applied to delivery orders.

Address is mandatory for delivery.

### 5.3 Discount mechanics

The 10% discount is manually triggered by the operator using the `Remise 10%` action. The operator first judges whether the order should receive the discount; the VBA then performs the calculation.

Only products whose catalogue field `Remise_OK = OUI` are discounted. Products with `Remise_OK = NON` remain at full price even when they are in the same order.

For an order containing both eligible and ineligible products:

1. the eligible subtotal is reduced by 10%;
2. the ineligible subtotal is added unchanged;
3. the resulting overall discounted total is evaluated against the €15 threshold.

If the discounted total is below €15, the current system displays the amount in red as a warning. It does not technically prevent the operator from confirming the discounted order, so the threshold can be overridden manually.

## 6. Order persistence

### 6.1 Order identifier

The order ID is generated automatically when the operator confirms the order with `Confirmer`.

The current format is timestamp-based:

`YYYYMMDD_HHMMSS`

There is no special collision mechanism for two orders confirmed in the same second. With the current single-store workflow this has not been a practical issue.

### 6.2 `Commandes`

`Commandes` stores one row per order and includes fields such as:

- `ID_Commande`;
- date/time;
- final order total (`Total_TTC`);
- payment category;
- fulfilment type;
- address;
- telephone;
- comment;
- status.

A normal active order is stored with status `OK`.

### 6.3 `Ventes`

`Ventes` stores one row per order line and includes order ID, date/time, product code, product name, quantity, unit price, line total and status.

The sales detail stores the product's historical selling price/line total before the order-level discount adjustment. The current data model does not store a per-line discount amount or a direct per-line indication that the 10% discount was applied.

The final discounted amount is represented at the `Commandes.Total_TTC` level. Consequently, historical `Ventes` lines alone do not fully express the effective discounted selling value of each item.

### 6.4 Autosave

The workbook is configured to save automatically after modifications. Committed orders therefore do not rely on the operator remembering to manually save the workbook.

## 7. Order modification and cancellation

### 7.1 Finding an order

The home-screen `Commandes` button and the Caisse-screen `Afficher / modifier commande` action open the same order-search workflow.

The current search/list form shows orders from the period still retained in the POS workbook, with information such as date, amount, telephone and comment. The operator can select an order and view or modify it.

The principal practical way to identify a specific order is currently the telephone number. This can be inefficient because the operator is visually comparing numbers and the list does not strongly distinguish today's orders from older retained orders.

### 7.2 Modification semantics

When a confirmed order is modified:

1. the original order is not overwritten as the active record;
2. the original `Commandes` row is marked `ANNULE`;
3. the corresponding original `Ventes` rows are also marked `ANNULE`;
4. the modified order is confirmed as a new order with a new ID;
5. existing customer/order information and comments are preserved into the new order unless intentionally changed;
6. the kitchen and customer tickets are printed again.

The `Annuler modification` action abandons the in-progress modification and leaves the original persisted order unchanged.

### 7.3 Cancellation

Cancelling an already confirmed order changes its status to `ANNULE` rather than physically deleting its history. The corresponding order-detail rows are retained with the cancelled status.

This gives the current workbook a simple but important history-preservation behavior for cancellations and revisions.

## 8. Printing

### 8.1 Current sequence

When an order is confirmed, the order is persisted first. The VBA then automatically prints:

1. one kitchen ticket;
2. one customer ticket.

Both tickets are printed on the same physical printer. Because persistence occurs before printing, a printer failure does not remove the already-created order.

Existing orders can be retrieved and either the kitchen ticket or the customer ticket can be reprinted independently.

### 8.2 Kitchen ticket

The kitchen ticket includes at least:

- order ID;
- order time;
- customer/order information;
- telephone when present;
- address when present;
- full comment;
- order total;
- ordered products.

Products are printed in the operational format:

`quantity - code - name`

The kitchen ticket does not print the payment method.

Because `Commentaire` currently contains pickup time, preparation requests, preferences and unstructured product choices, the kitchen ticket is also the main operational carrier for those instructions.

### 8.3 Customer ticket

The customer ticket includes the order and item information needed by the customer, including item quantities/prices and order total, together with the shop's statutory receipt information.

It also includes VAT information, including totals by the different VAT rates represented in the order, and the business VAT identification information required for the receipt format used by Sushi 81.

The exact formatting and calculations are implemented in the current VBA printing code.

### 8.4 Current printing mismatch

The customer ticket is currently printed automatically at order creation, before the final payment method is usually known.

Operationally, this is not the ideal sequence: the more logical workflow would be to print the kitchen ticket when the order is created, then retrieve the order at payment time, record the final payment method/composition and print the customer receipt at that point.

### 8.5 Printing latency

Printing is occasionally delayed by approximately 5–10 seconds. This delay is noticeable and frustrating during live order handling.

## 9. Payments and end-of-day reconciliation

### 9.1 Current payment categories

The POS currently distinguishes:

- `CB`;
- `Espèce`;
- `DIV`.

Sushi 81 does not accept cheques.

For operational classification:

- paper meal vouchers/tickets are treated as cash (`Espèce`);
- meal-voucher cards processed through the card terminal are treated as card (`CB`);
- mixed cash/card payment is represented through the broad `DIV` concept during the current workflow.

### 9.2 Payment is usually unknown when the order is created

Even for an on-site order, the normal sequence is to create the order and print the ticket before the customer actually pays. Therefore the final payment method is usually not known when `Confirmer` creates the order.

The current workaround is to leave the order with the default `DIV` value until later reconciliation. For a delivery order, if the customer states the intended payment method during ordering, the operator may enter `CB` or `Espèce` immediately.

`DIV` therefore has an ambiguous current meaning:

- payment method not yet confirmed;
- genuinely mixed payment.

The workbook does not have a structured unpaid/payment-pending state separate from mixed payment.

### 9.3 Mixed payment limitation

The current data model does not store a structured split such as:

- €10 cash;
- €20 card;
- €30 total.

During end-of-day reconciliation, the operator manually resolves this. In the current practical workflow, the cash part may be left/represented in the POS order while the card amount is entered separately into Hiboutik so the card revenue is correctly reflected there. The exact split may also be written in free text for reference.

It is not necessary in the current business process to preserve a formal relationship between the POS-side cash fragment and the Hiboutik-side card amount as one original order. The essential requirement is accurate card revenue in Hiboutik and accurate overall daily cash/card reconciliation.

### 9.4 End-of-day workflow

All current-day orders are reconciled on the same day.

At the end of the day, the operator normally:

1. returns home and opens the same OneDrive-synchronized `POS_Caisse.xlsm` on the home computer;
2. uses `Admin` to expose the Excel workbook;
3. works directly in the `Commandes` worksheet rather than through a dedicated reconciliation UserForm;
4. uses the card-terminal transaction information to review the day's orders;
5. corrects the provisional payment classifications to the actual cash/card outcome;
6. handles mixed payments manually;
7. ensures the relevant card amounts from POS-originated orders are entered in Hiboutik;
8. checks that the total card-terminal receipts match the card amounts represented across the two order sources/workflows;
9. records/combines the cash amounts from the two order sources;
10. obtains the day's overall turnover.

When the card total reconciles and the combined cash figures are recorded, the daily reconciliation is considered complete.

## 10. Daily summary display

The home screen displays an at-a-glance summary including:

- `CA du jour`;
- `CB`;
- `Espèce`.

The values are calculated from orders dated today and exclude cancelled (`ANNULE`) orders.

The display is refreshed automatically after order creation, but is not always perfectly current. The operator can use `Mise à jour CA` to force a recalculation.

Before final payment reconciliation, an order still recorded as `DIV` is treated as card for the purpose of the current summary logic. Orders already marked as cash are counted as cash.

This display is useful operationally because it provides immediate visibility of the day's turnover and payment split, despite the limitations of the underlying provisional-payment model.

## 11. Hiboutik relationship

### 11.1 Online orders

Hiboutik receives website orders directly. Website orders may be either pickup or delivery.

Operationally, Hiboutik displays a requested pickup date (`date de retrait`). A browser userscript currently highlights future pickup dates in red so that a future order is not mistaken for a same-day order after the page is refreshed.

### 11.2 POS-originated card revenue

For a telephone or walk-in order created in `POS_Caisse.xlsm`, the business does not require the full product detail to be recreated in Hiboutik. The core requirement is that the relevant card amount is present in Hiboutik so that Hiboutik contains the correct card revenue.

A cash-only POS order therefore does not need to be entered in Hiboutik.

This manual card-revenue entry is currently regarded as a necessary business workflow rather than, by itself, a defect in the POS. Future design may optimize how the amount is transferred, but should not remove the requirement that Hiboutik's card revenue remain correct unless the broader business process is explicitly changed.

## 12. Archival flow and retention

`POS_Caisse.xlsm` is designed as a lightweight operational workbook rather than the long-term historical archive.

The current transfer process moves J-2-and-older `Commandes` and `Ventes` data into `Gestion SUSHI 81.xlsm` and then deletes the successfully transferred rows from the POS workbook to keep it small and responsive.

The J-2 buffer was originally intended to keep very recent orders available in case a customer returned with an order issue. In practice this has not been needed often and is not a fundamental data-management requirement.

Long-term historical lookup is therefore expected to occur in the management history rather than in the live POS workbook. It is uncommon in the restaurant's normal operation for a customer to request information about an order from several months earlier.

## 13. Current strengths to preserve

The current system has several operational qualities that are important to retain in the replacement application:

- very direct, low-friction order entry;
- product selection through category browsing;
- double-click fast-add as well as an explicit `Ajouter` button;
- one search field that can find both product code and product name;
- a convenient custom quantity input before adding a product;
- a flexible free-text comment field;
- automatic French telephone-number formatting for readability;
- kitchen tickets containing enough customer/order information and the total to help identify the order later;
- ability to retrieve and selectively reprint kitchen/customer tickets;
- preservation of cancelled/replaced orders instead of silently overwriting history;
- a home screen showing daily turnover/payment summaries at a glance;
- no login requirement;
- no employee-role/permission system;
- absence of unrelated generic-POS features that would slow down the operator.

The replacement product should remain a focused Sushi 81 operational tool rather than becoming a feature-heavy generic POS.

## 14. Current limitations and operational friction

The most important limitations identified in the current workflow are:

1. **Excel/VBA stability** — closing the Caisse form does not always return cleanly to the home screen. The workbook can become stuck on the order-entry interface and sometimes cannot be closed normally, requiring termination of the background Excel process.
2. **OneDrive synchronization reliability** — synchronization is not always prompt. The operator sometimes has to disable and re-enable OneDrive synchronization to force the latest workbook state to propagate.
3. **Printing latency** — printing can occasionally block/delay the workflow for roughly 5–10 seconds.
4. **Cart quantity editing** — changing the quantity after an item is already in the cart requires reopening the line by double-click, which is inconvenient during live telephone ordering.
5. **Order-list readability** — today's orders and older retained orders appear together without strong visual separation, making the list harder to scan.
6. **Order search efficiency** — telephone number is the main lookup aid and requires visual comparison of digits; other available information such as comments is not sufficiently leveraged to make finding an order easier.
7. **Unstructured product choices** — flavours/menu variants and similar selections are stored in free text rather than as structured item-specific choices.
8. **Payment-state ambiguity** — `DIV` represents both not-yet-confirmed payment and true mixed payment.
9. **Mixed-payment data loss** — the actual cash/card split is not stored structurally in the POS data model.
10. **Customer receipt timing** — the customer receipt is printed before the payment method is normally known.
11. **Manual reconciliation in raw Excel** — end-of-day payment correction is performed directly in worksheet cells rather than through a dedicated reconciliation interface.
12. **Summary freshness** — the home-screen daily summary is useful but may require manual refresh.
13. **Temporary-workbook data management** — old operational data must be transferred and removed to keep the workbook lightweight.
14. **Maintainability/testability** — UI behavior, persistence, business rules and printing are tightly coupled in VBA, making safe extension and automated regression testing difficult.

The manual requirement to reflect POS-originated card revenue in Hiboutik is **not** currently considered an unnecessary annoyance. It is a business requirement of the present accounting/transaction-recording workflow. The optimization opportunity is in how that requirement is fulfilled, not in simply removing it.

## 15. Boundary of this document

This document deliberately does **not** decide:

- the target technology stack;
- database engine or physical schema;
- final target order-state model;
- whether the current order-replacement semantics will be preserved exactly;
- final discount/delivery rules beyond documenting the current behavior;
- final customer-data model;
- final product-option model;
- future structured payment model;
- final Hiboutik integration method;
- target storage/synchronization/backup architecture;
- final printer protocol or receipt templates;
- final export/archive strategy.

Those decisions belong to the later business and architecture documents in the project roadmap.

## 16. Phase 1 baseline

The current system is best understood as a set of cooperating tools:

- **`POS_Caisse.xlsm`** — fast telephone/walk-in order entry, recent operational order storage and printing;
- **Hiboutik** — website orders and the authoritative operational destination for card-revenue representation;
- **`Gestion SUSHI 81.xlsm`** — current-year sales history, LCL reconciliation and product-sales analysis;
- **`Outils Sushi 81`** — supporting management automation, including safe transfer of older POS data into the management workbook.

The replacement Sushi81 POS should preserve the speed and simplicity of the current order-entry experience while removing the structural limitations of Excel/VBA, improving payment/order-state handling, making order retrieval and reconciliation clearer, and providing a more reliable local operational foundation.