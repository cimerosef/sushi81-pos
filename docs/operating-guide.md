# Sushi81 POS — day-to-day operating guide

**For:** Sushi 81 operators

**Status:** M13 final-candidate guide. Use the exact installer artifact identified by the matching WP6 `CODEX_DONE: M13-WP6-FINAL-CANDIDATE-OPERATING-GUIDE-06` comment on PR #26. Owner acceptance remains pending until the owner completes the checklist in [`implementation/milestone-13-final-manual-acceptance.md`](implementation/milestone-13-final-manual-acceptance.md).

This guide covers the workflows present in the V1 candidate. It does not grant a device authority, replace the separate Gestion SUSHI 81 workbook, or make an unverified screen or feature available.

## 1. Install and preserve your data

Use the exact per-user installer and version recorded in the WP6 completion comment. The installed program files live under:

```text
%LOCALAPPDATA%\Programs\Sushi81 POS
```

Durable application data is stored separately under:

```text
%LOCALAPPDATA%\Sushi81 POS
```

That data area contains the live database and may contain `Archive`, `Recovery`, `Config`, printer/settings state and logs. Application updates, repair/reinstall and ordinary uninstall are designed to leave durable data in place. Do not delete this data folder as an uninstall step. Before testing an existing operational profile, make and verify a safe recovery point.

To identify the installed build, inspect `release-provenance.json` in the installed program folder. Compare its source head and version with the exact candidate recorded in the matching WP6 completion comment. Logs are under:

```text
%LOCALAPPDATA%\Sushi81 POS\Logs
```

Share logs only through the owner's approved support route; do not edit them to troubleshoot business records.

## 2. First launch and device authority

The application shows whether this device is authoritative, read-only/non-authoritative, or waiting for an authority/recovery action. **Only the current authoritative device may create or change business data.** A paired device, a computer that happens to be online, or a device with a newer-looking file does not gain write authority automatically.

When setting up another computer, install the application, join the existing Sushi81 lineage using the configured shared root and pairing flow, and let the application validate the available snapshot. The new device is read-only unless a completed normal handoff specifically targets it and it successfully acquires that handoff. Pairing alone does not transfer authority.

At close, use the explicit close intent that matches what you mean:

- **Close and retain authority** keeps this same device authoritative for its next valid launch.
- **Transfer authority and close** is a deliberate transfer to one eligible paired target. Select the intended device when asked and wait for the application to report the transfer complete. The source becomes read-only after it durably relinquishes authority.

Do not copy or edit handoff, pairing, authority or database files by hand. If a transfer is pending or cannot be confirmed, keep to the displayed read-only/retry state and contact the owner before trying another device as writable.

**Disaster Recovery is for a genuine abnormal loss of the normal authoritative/target path.** Do not use it just because the authoritative application is closed, a computer is temporarily offline, or a handoff is inconvenient. Recovery can lose changes made after the selected checkpoint and advances the device generation. Use it only through the application after confirming the prior authoritative/designated-target device is unavailable and will remain stopped/quarantined from Sushi81 business writes. If unsure, stop and ask the owner.

## 3. Caisse: orders, payment and retrieval

### Create and confirm

In **Caisse**, find products by category, code or name, add them to the cart, set quantities and choose any required product options. Complete the order's **Retrait** or **Livraison** mode, planned fulfilment date/time where needed, telephone, delivery address and comment. Review the total before confirming.

The configured Retrait discount applies only to eligible products and follows the configured post-discount minimum. Delivery charges and delivery minimums follow the current settings. If an exceptional total is needed, use the order's authoritative total field and recheck it after changing products, quantities, options or fulfilment fees because a price-affecting change recalculates it.

Confirmation saves the order before sending its kitchen and customer tickets to the configured printers. If printing fails, the saved order remains; use the retry/reprint action rather than creating a duplicate order.

### Change, pay, close or cancel

Orders can be edited while Open or Closed. Saving an edit updates the same order; it does not create an operator-facing revision history. If an edit makes the recorded payment total differ from the current order total, the order is Open again until balanced.

Enter cumulative money actually received in **CB** and **Espèce**. If payment was received on a different date, use its effective payment date. The **Clôturer** action is allowed only when `CB + Espèce` exactly equals the current order total to the cent. Underpayment or overpayment leaves the order Open. Cancel only when the order itself should be cancelled; cancellation retains its record and payment facts and does not execute a card refund.

The Caisse dashboard shows operational turnover, received CB/cash and Hiboutik CB/cash separately, plus future, due-today and overdue order views. Hiboutik values are reference totals and do not change ordinary POS turnover or payment totals.

Use **Commandes** to retrieve orders by date, reference, telephone or comment and review their current status. A future order keeps its planned fulfilment date/time; due-today and overdue reminders remain visible according to their status and date.

### Print and reprint

Normal confirmation attempts kitchen and customer tickets after the order is saved. These outputs can be reprinted independently. Payment changes may require a new customer ticket. Archived orders can also be reprinted from their historical snapshot. A printer failure never means that the order was rolled back; check the order first, then retry the relevant output.

## 4. Catalogue and settings

In **Catalogue**, maintain products, categories, prices, VAT, active status, Retrait-discount eligibility and product options. Deactivate a temporarily unavailable product; inactive items are hidden from normal new-order selection but can be reactivated. Permanent deletion is a separately confirmed action and does not remove historical order snapshots.

Catalogue `.xlsx` import/export is a batch-maintenance option. Review the import preview and resolve blocking validation issues before committing. Do not rename, invent or edit technical internal IDs in the workbook; use operator-facing product codes, category names and fields. Export a fresh workbook when you need a current template. The workbook is for catalogue maintenance, not for editing orders or the separate Gestion export file.

Use **Paramètres** for the settings shown by the application, including fulfilment/discount settings, device/system configuration and kitchen/customer printer selection. Do not manually edit configuration, identity or authority files to change these values.

## 5. Hiboutik paste fallback

If Hiboutik server-side printing is unavailable, copy the complete product-detail block from the Hiboutik order email. In Caisse expand **Commande Hiboutik**, paste the block and choose **Analyser la commande Hiboutik**. The parser uses exact current catalogue product codes; explicitly resolve unknown lines or leave the import. Enter Retrait/Livraison, date/time, telephone, address and comment through the ordinary order fields, review the ordinary POS price/options, then confirm through the normal order flow.

Pasting or analysing does not save an order. The application does not read the clipboard, email or Hiboutik automatically. The pasted source total is reference-only; it does not set the POS total. After confirmation, the order uses the ordinary Caisse lifecycle and print flow. Its Hiboutik origin is hidden and keeps it out of ordinary POS turnover and Gestion export to avoid counting the same sale twice.

## 6. Gestion export to Gestion SUSHI 81

**Gestion export** creates a controlled intermediate Excel file; it does not write directly into `Gestion SUSHI 81.xlsm`. An operator imports the generated file into the separate workbook using that workbook's process.

Choose the inclusive fulfilment-date range or leave it unfiltered, then use **Aperçu / actualiser** to review applicable actions. Eligible ordinary closed orders may produce `CREATE`, later corrections may produce `UPDATE`, and cancellations may produce `CANCEL`; the application maintains the stable order identifiers and duplicate-protection state. Hiboutik paste-created orders are excluded.

Use **Générer le fichier Excel** after reviewing the preview. Successful batches appear under **Lots réussis** and can be regenerated from their retained immutable payload. A pending **PREPARED** batch appears under **Lots à finaliser**; use **Réessayer le lot en attente** to retry it. Do not manually delete or rewrite batch history.

Successful payloads are retained for at least 30 days from batch completion. After that, a batch may be compacted only when its orders are no longer live, have positive annual-archive proof, and have no unresolved dependency. Therefore older, safely archived history may disappear; an order's absence from the live list alone is not enough for cleanup. The compact archive-proof record may remain longer. The operator does not need to wait 30 real days during owner acceptance; automated tests cover that boundary.

## 7. Annual archives and historical access

Completed annual databases are stored locally under the application-managed `Archive` area and are retained permanently. Open **Archives historiques**, choose an archive year and search/filter the available historical orders. Archive viewing is read-only; historical values come from saved snapshots, not today's catalogue. Select an order for an explicit reprint if needed.

**Copier l’archive** makes a copy of a validated completed archive to a destination you choose. It does not move or delete the canonical archive. Annual archiving is an application-managed authoritative-device operation; ordinary POS use does not synchronize canonical annual archives through OneDrive. Normal authority handoff transfers live data only, so archive files on another computer may need their own approved copy/availability plan.

The first real populated 2026-to-2027 archive operational verification remains deferred under the existing M12 owner waiver. It is not claimed Passed by this guide or by M13. The deferred check is due at the first safe authoritative startup on or after 2027-02-01 with real 2026 rows.

## 8. Language and support

Use the language selector at the top of the application to switch between **FR** and **zh-CN**. Only interface text changes. Stored product/category names, customer details, comments, orders and historical snapshots are not translated.

Common screen and action labels:

| Français | 简体中文 |
|---|---|
| Caisse | 收银台 |
| Commandes | 订单 |
| Catalogue | 商品目录 |
| Gestion export | 销售数据导出 |
| Paramètres | 设置 |
| Archives historiques | 历史归档 |
| Confirmer | 确认 |
| Enregistrer la modification | 保存修改 |
| Clôturer | 关闭订单 |
| Aperçu / actualiser | 预览 / 刷新 |
| Générer le fichier Excel | 生成 Excel 文件 |
| Réessayer le lot en attente | 重试待完成批次 |
| Réimprimer client / cuisine | 重印客户单 / 重印厨房单 |
| Copier l’archive | 复制归档 |

For an unexpected failure, note the screen/action and approximate time and ask the owner to review the application message and recent files in `%LOCALAPPDATA%\Sushi81 POS\Logs`. Retry only the action the application identifies as safe. Do not delete, replace or manually edit `live.db`, an archive, a recovery checkpoint, settings, configuration, pairing or authority files. Never solve an uncertain startup or migration issue by resetting the profile.

## 9. V1 boundaries

V1 does not provide inventory or purchasing, table management, staff accounts/roles, loyalty/CRM, automatic Hiboutik API/email synchronization, direct card-terminal control or POS-run refunds, full accounting/ERP, a replacement for Gestion SUSHI 81, formal B2B invoices, live SQLite synchronization through OneDrive, simultaneous multi-writer operation, or an automatic updater. Ask the owner before relying on a workflow outside the controls described in this guide.
