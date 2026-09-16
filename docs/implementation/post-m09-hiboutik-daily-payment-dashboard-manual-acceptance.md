# Post-M09 Hiboutik daily payment dashboard — Windows/WPF manual acceptance

**Status:** Prepared — owner execution pending  
**Owner:** project owner / manual acceptance executor  
**Accepted candidate:** TBD after implementation  
**Result:** NOT YET EXECUTED

This checklist verifies only the approved post-M09 dashboard enhancement. It does not reopen M09 and does not authorize merge or M10+.

Use synthetic/test orders only for recorded evidence.

## A. Top Caisse presentation

- [ ] The existing top Caisse daily dashboard still shows the ordinary POS metrics and operational counters as before.
- [ ] Exactly two additional passive values are visible: `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`.
- [ ] The two new values are text/read-only values, not buttons or editable controls.
- [ ] No Hiboutik turnover, total-received, order-count, discrepancy or dedicated workflow appears.

## B. Source separation and same-day payments

Using a synthetic non-Cancelled Hiboutik paste-created order:

- [ ] adding/changing same-day cumulative CB updates `Hiboutik CB aujourd'hui` by the corresponding signed effective-date delta;
- [ ] adding/changing same-day cumulative Espèce updates `Hiboutik Espèce aujourd'hui` by the corresponding signed effective-date delta;
- [ ] the existing ordinary POS `Encaissé`, `CB`, `Espèce` and operational turnover values do not increase because of the Hiboutik payment;
- [ ] ordinary POS orders do not contribute to either new Hiboutik value.

## C. Cancellation boundary

- [ ] After cancelling a Hiboutik order that has retained payment facts, that order no longer contributes to either Hiboutik daily value.
- [ ] The order remains viewable as Cancelled with its retained payment facts; the dashboard exclusion does not delete history.

## D. Effective business date and corrections

- [ ] A payment/correction assigned to another effective date does not appear in today's Hiboutik values.
- [ ] A later-entered payment back-dated to today appears in today's Hiboutik value even though its technical recorded timestamp is later.
- [ ] A negative correction reduces the corresponding Hiboutik CB/Espèce daily value rather than being ignored or converted to a positive amount.

## E. Localization

- [ ] Switching FR -> zh-CN changes the two new labels to the approved localized equivalents while amounts/business data remain unchanged.
- [ ] Switching back to FR restores the French labels.

## F. Read-only device boundary

- [ ] On a non-authoritative/read-only paired device, the two values remain visible from locally available committed data.
- [ ] The enhancement adds no write action and does not weaken the existing non-authoritative/stale warning or authority rules.

## Final disposition

- [ ] All applicable sections A–F passed on the exact candidate.
- [ ] No regression requiring remediation was found.
- [ ] Project owner declares this enhancement manual acceptance PASSED.

A PASSED disposition is not merge approval. Merge remains a separate explicit project-owner decision.