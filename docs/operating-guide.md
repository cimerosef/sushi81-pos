# Sushi81 POS operating guide

**Audience:** Sushi 81 operators
**Language:** The [Simplified Chinese guide](operating-guide.zh-CN.md) is the primary practical manual for Chinese-speaking staff. This English guide is the equivalent reference.

## New PC quick start: join an existing Sushi81 system

Follow these steps in order. Pairing a computer does not give it write authority.

1. **Prepare the prerequisites.**
   - Use the current owner-approved per-user Sushi81 POS installer, obtained through the owner-approved distribution location/process. After installation, verify that `release-provenance.json` version and source identity match the identity supplied with that approved installer. Do not treat an older acceptance artifact or source head as the permanent installer source. Ordinary updates and repairs preserve `%LOCALAPPDATA%\Sushi81 POS`.
   - Install and sign in to OneDrive. Make sure the existing Sushi81 shared root is available and synchronized on this computer.
   - Have the computer/printer administrator install the required Windows printer drivers and create the Windows printer queue or queues.
   - Ask the system owner for the existing GitHub transfer settings and a protected Windows Credential Manager credential that was prepared through the approved process. Do not invent an owner, repository or credential target.
   - Keep the old authoritative PC available for a normal transfer. Only a genuine Disaster Recovery case follows the separate exceptional path below.

2. **Install without removing durable data.**
   - Run the verified per-user installer. Program files go under %LOCALAPPDATA%\Programs\Sushi81 POS; application data goes under %LOCALAPPDATA%\Sushi81 POS.
   - Ordinary installation, update, repair and uninstall must preserve durable application data. Do not treat the data directory as a cache.
   - Never copy live.db, authority JSON or handoff files manually between PCs. Do not rename, replace or repair those files by hand.

3. **Configure the existing system.**
   - Open “Configure Sushi81 system” (配置 Sushi81 系统). This screen stores the OneDrive path and non-secret GitHub transfer settings; saving it does not create pairing or grant authority.
   - Copy each value exactly from the existing PC or obtain it from the system owner:

     | Field | Meaning and source | Secret? |
     |---|---|---|
     | Sushi81 OneDrive shared folder | Absolute path to the existing shared root as synchronized on this PC. Use Browse to select it; do not choose a personal Desktop folder or create a different root. | Not a credential; use the owner-confirmed path. |
     | GitHub owner | GitHub account or organization that owns the transfer repository. | No. Copy from existing configuration. |
     | GitHub transfer repository | Name of the dedicated private transfer repository. | No. Copy exactly; do not substitute the application source repository. |
     | GitHub release tag | Tag used by the configured transfer release. | No. Copy exactly. |
     | GitHub release name | Name of the configured transfer release. | No. Copy exactly. |
     | Protected GitHub credential target name | Lookup name of the Generic credential in Windows Credential Manager. | The name is not secret. The PAT/token is secret and must never be entered in the app. |

   - **Save the token in Windows Credential Manager.** On Windows 10/11, search for `Credential Manager` from the taskbar and open `Credential Manager Control Panel`; it can also be opened through Control Panel > User Accounts > Credential Manager. Select `Windows Credentials`, then choose `Add a generic credential` under `Generic Credentials`.
     1. `Internet or network address`: enter the exact “Protected GitHub credential target name” configured in Sushi81 POS. This is a lookup key, not a secret.
     2. `User name`: if the dialog requires it, enter a non-secret descriptive/account label. Sushi81 POS does not read this field; it does not represent or grant authority.
     3. `Password`: enter the owner/IT-approved GitHub fine-grained personal access token (PAT) for the dedicated private transfer repository, then save.
   - **Minimum token permissions:** restrict fine-grained PAT repository access to only the dedicated private transfer repository; never authorize the Sushi81 POS source-code repository. Grant only `Contents: Read and write` on that repository. Production transfer calls read release/tag/assets and list/download assets; creating a missing release and uploading/deleting assets require Contents write. Repository lookup requires read-only Metadata permission, which GitHub includes by default for fine-grained tokens. `write` includes `read`. No account- or organization-level permissions are needed.
   - After saving the credential, enter only the same target name in the Sushi81 POS “Protected GitHub credential target name” field, never the token; then continue with step 4.
   - **Security boundary:** never put the PAT in a Sushi81 POS field, repository documentation, screenshots, logs, chat, a terminal/command line or shell history.

4. **Save, restart and test the connection.**
   - Save the technical configuration and wait for the restart-required message. Exit and restart Sushi81 POS completely so it can compose the M07 services.
   - Choose “Test GitHub transfer connection” (测试 GitHub 转移连接). A successful check reports that the transfer connection is available. A missing/unreadable credential, HTTP 401, 403 or 404 indicates a configuration or credential, access, or repository/release problem. Ask the owner to verify the existing values; never solve it by entering a token in the app.

5. **Set the language and join read-only.**
   - At the top of the app, use Language (语言) and choose Simplified Chinese (简体中文 / zh-CN) or French (Français / FR).
   - Confirm the shared root contains the existing lineage. Choose “Join existing system” (加入现有系统 / Rejoindre le système existant), enter a clear device name that distinguishes this computer from the others, and confirm.
   - A successful join proves that device pairing/registration completed; the new PC remains read-only and non-authoritative. If usable local business data is present, read-only consultation may be available. A fresh PC may be `PairedUninitializedReadOnly` and may have no usable current business dataset until an approved target acquisition/reinitialization path completes. Join success proves neither data acquisition nor write authority, and does not move authority from the old PC. If lineage metadata is missing or invalid, do not initialize an empty system or select a different root; ask the owner to investigate.
   - Read the visible authority state before business work. Only a device explicitly shown as authoritative may create or change business records. Read-only, transitioning or recovery-required states do not allow business writes.

6. **Transfer authority from the old PC to the new PC.**
   - Normal authority transfer uses the configured GitHub transfer repository to send one immutable transfer to one paired target. The OneDrive shared root holds system metadata and Disaster Recovery material; it is not the normal handoff transport.
   - On the current authoritative old PC, finish or save pending work and choose “Transfer authority and close” (转移权威并关闭 / Transférer l’autorité et fermer).
   - Check the target by its device name. If the app presents multiple candidates, choose the exact paired device in the current generation. Cancel and confirm with the owner if the identity is uncertain.
   - Wait for the old PC to report completion and close. After it durably relinquishes authority, it must remain read-only; do not take orders on it or retarget the pending transfer.
   - On the designated new PC, choose “Check and acquire transferred authority” (检查并获取已转移的权威 / Vérifier et acquérir l’autorité transférée). Wait for validation and for the validated business dataset to be installed/refreshed locally. Resume business only after acquisition succeeds, the new PC is shown as authoritative/writable and the old PC remains read-only.
   - If you are only closing the app and want authority to remain on this computer, choose “Close and retain authority” (关闭并保留权威 / Fermer et conserver l’autorité). A normal close does not pass authority to another PC.
   - If the transfer is interrupted, the old PC may remain authoritative only if durable relinquishment has not happened. After relinquishment it must stay read-only. It may use “Resume pending transfer” (继续待处理的转移 / Reprendre le transfert en attente) only to retry that same target-bound transfer. Do not edit files, cancel or retarget it, and do not let a third PC write. Non-target devices remain read-only.

7. **Use Disaster Recovery only for a genuine abnormal loss.**
   - Consider Disaster Recovery only when the normal authority/designated-target path genuinely cannot be recovered. The owner must confirm that the prior authoritative/designated-target computer is stopped or quarantined and will not be used for writes. The operator must understand possible loss after the selected recovery point and that the system generation advances.
   - Disaster Recovery is not a routine handoff or a shortcut because a PC is offline, the app is closed or a transfer is slow. If quarantine or the recovery point is uncertain, stop and contact the owner.

## Printer setup (repeat on every PC)

Printer selection is local to the PC and is not carried in the business lineage. Configure each computer separately.

1. Have the computer/printer administrator install the appropriate Windows printer driver and create the queue in Windows. This guide does not prescribe model-specific driver steps.
2. Open Sushi81 POS → Settings (Paramètres / 设置).
3. Choose “Refresh printers” (Actualiser les imprimantes / 刷新打印机) and wait for the installed queues to appear.
4. Select the kitchen queue under “Kitchen printer” (Imprimante cuisine / 厨房打印机) and the customer queue under “Customer printer” (Imprimante client / 客户打印机). Both settings may point to the same Windows queue if that matches the local hardware.
5. Save and confirm that the selection was saved locally.
6. At a safe business time, print a test order from the authoritative computer or use the appropriate reprint action on an existing order. A read-only computer may print orders available in its local data, but create a new test order only on the authoritative computer.
7. If a saved queue is unavailable, first check that its Windows queue is installed and available, then return to Settings, refresh, select an available queue and save again. Do not create duplicate orders to test a broken printer; order saving and printing are separate.

## Verify business settings after joining

For a first initialization/reference, the approved defaults are:

| Setting field in the UI | French / Simplified Chinese label | First-initialization default |
|---|---|---:|
| Retrait discount rate | Remise Retrait (%) / 自取折扣 (%) | 10% |
| Minimum after Retrait discount | Minimum après remise (€) / 折扣后最低金额 (€) | €15.00 |
| Livraison minimum | Minimum Livraison (€) / 配送最低金额 (€) | €30.00 |
| Delivery fee enabled | Frais de livraison activés / 启用配送费 | Off |
| Fixed delivery fee amount | Frais de livraison (€) / 配送费 (€) | €0.00 |
| Delivery-fee VAT when enabled | TVA des frais de livraison activés / 启用配送费的增值税 | Fixed at 10%; not configurable in V1 |

The values follow docs/business-rules.md. Their meaning is:

- A Retrait discount applies only when requested and only to discount-eligible catalogue product amounts. Positive option surcharges are not discounted; negative option adjustments reduce the discountable amount.
- If applying the discount would put the final total below the configured post-discount minimum, the discount is rejected.
- Livraison does not receive the normal Retrait discount. Its merchandise/commercial minimum is checked before a delivery fee is added; a fee cannot make an under-minimum order eligible.
- Defaults are for first initialization and reference. On an existing lineage, verify the transferred settings and catalogue after authority acquisition. Do not reset working business settings to the defaults.

## First-use checklist

Before resuming business, confirm:

- The device name and read-only/authoritative status are clear.
- The existing catalogue is visible and correct.
- Transferred business settings have been checked without resetting them.
- Kitchen and customer queues are selected; the GitHub transfer connection test succeeds.
- Any test order is created only on the computer explicitly shown as authoritative.
- A safe print or reprint works; after a completed transfer the old PC remains read-only.

## Day-to-day operation

### Caisse: create, edit, pay, close or cancel an order

In Caisse, find products by category, code or name, add them to the cart, set quantities and options, choose Retrait or Livraison, enter the needed fulfilment date/time, telephone, delivery address and comment, then review the total. Telephone and delivery address are not required for initial confirmation under the approved rules.

Saving an edit updates the same order. Enter cumulative money actually received in CB and Espèce. If payment was received on another date, record its effective payment date. “Close” (Clôturer) is allowed only when CB plus cash exactly equals the order total. Underpayment or overpayment leaves the order open. Cancellation preserves the order and payment facts; it does not issue a card refund. A price-affecting edit may recalculate the total, so review it again.

### Full business-data reset — owner-only maintenance

This is a destructive maintenance operation, not order cancellation or troubleshooting. Use it only on the authoritative device when the owner has designated the profile for a full reset. In Paramètres, open “Maintenance des données”; review the count-only preview and the list of preserved system/device state. Close or cancel to leave all data unchanged. To proceed, type exactly `RESET`, then review and accept the separate final confirmation. The application first retains a private, validated backup, then clears the entire operational dataset and refreshes the business views. Pairing, authority, configuration, credentials, printer selections, language, business settings and recovery material remain. Never use this on a live service profile or to troubleshoot installation, startup, migrations or authority transfer. Automated tests and training must use synthetic data. See [`decisions/m13-full-business-data-reset.md`](decisions/m13-full-business-data-reset.md).

### Print and reprint

Order confirmation saves the order before attempting the kitchen and customer tickets. A print failure does not roll back the saved order. Check the order, then retry or reprint the relevant output instead of creating a duplicate. A payment change may require a new customer ticket. A read-only device may print or reprint orders visible in its local data.

### Catalogue and XLSX

In Catalogue, maintain products, categories, prices, VAT, active status, Retrait-discount eligibility and product options. Deactivate temporarily unavailable products; historical order snapshots remain. For XLSX batch import, review the preview and resolve blocking validation errors before committing. Do not change technical internal IDs. Export a fresh workbook when you need a current template. The workbook is for catalogue maintenance, not order editing or the separate Gestion export.

### Hiboutik paste fallback

If Hiboutik server-side printing is unavailable, copy the complete product-detail block from the Hiboutik order email. In Caisse expand “Commande Hiboutik”, paste it and choose “Analyser la commande Hiboutik”. Resolve unknown lines using current catalogue product codes, enter the normal fulfilment details and review the POS price/options before confirmation.

Pasting or analysing does not save an order. The app does not read the clipboard, email or Hiboutik automatically. The source total is reference-only; it does not set the POS total. A hidden source discriminator keeps Hiboutik paste-created orders out of ordinary POS turnover and Gestion export to avoid double-counting.

### Gestion export to Gestion SUSHI 81

Open Data (Données / 数据) and choose the separate Gestion export (销售数据导出) tab. Choose an inclusive fulfilment-date range or leave it unfiltered. Use “Preview / refresh” (Aperçu / actualiser) to review applicable actions. Eligible ordinary closed orders may create CREATE actions; later corrections may create UPDATE actions; cancellations may create CANCEL actions. Hiboutik paste-created orders are excluded.

After reviewing the preview, choose “Generate Excel file” (Générer le fichier Excel). Import the generated file into the separate Gestion SUSHI 81 workbook using that workbook’s process; the POS does not write directly into it. Successful batches can be regenerated from their retained immutable payload. Keep a pending PREPARED batch and use “Retry pending batch” (Réessayer le lot en attente); do not delete or rewrite batch history manually.

Successful payloads are retained for at least 30 days from completion. After that, a batch may be compacted only if all related orders are no longer live, have positive annual-archive proof and have no unresolved dependency. PREPARED batches are never pruned. An order missing from the live list alone is not proof that its export history is safe to remove. Operators do not need to wait 30 days for acceptance testing and must not manually clean up the records.

### Annual archives and historical access

In Data, open “Historical archives” (Archives historiques), select an archive year and search/filter orders. Archive access is read-only; values come from saved historical snapshots, not the current catalogue. Select an order for an explicit reprint. Completed annual databases are retained permanently under the application-managed local Archive area. “Copy archive” (Copier l’archive) copies a validated archive to a chosen location; it does not move or delete the canonical archive. Annual archiving is managed on the authoritative PC; normal authority handoff does not automatically copy annual archive files to another computer.

The first real populated 2026-to-2027 archive operational verification remains deferred under the M12 owner waiver. It must be performed separately at the first safe authoritative startup on or after 2027-02-01 with real 2026 rows. This guide does not claim that check Passed.

## Language, support and V1 boundaries

Use Language (Langue / 语言) at the top to switch between French (Français / FR) and Simplified Chinese (中文（简体）/ zh-CN). Only the interface changes; stored product names, customer details, orders, comments and historical snapshots are not translated.

Common labels:

| Français | 简体中文 |
|---|---|
| Caisse | 收银台 |
| Commandes | 订单 |
| Catalogue | 商品目录 |
| Données | 数据 |
| Gestion export | 销售数据导出 |
| Paramètres | 设置 |
| Archives historiques | 历史归档 |
| Rejoindre le système existant | 加入现有系统 |
| Transférer l’autorité et fermer | 转移权威并关闭 |
| Vérifier et acquérir l’autorité transférée | 检查并获取已转移的权威 |
| Fermer et conserver l’autorité | 关闭并保留权威 |
| Imprimante cuisine | 厨房打印机 |
| Imprimante client | 客户打印机 |
| Actualiser les imprimantes | 刷新打印机 |

For an unexpected failure, note the screen/action and approximate time. Ask the owner to review the app message and recent files in %LOCALAPPDATA%\Sushi81 POS\Logs. Retry only an action the app identifies as safe. Do not delete, replace or manually edit live.db, an archive, a recovery checkpoint, settings, pairing or authority files. Never reset the profile to troubleshoot a startup or migration issue.

V1 does not provide inventory/purchasing, table management, staff accounts/roles, loyalty/CRM, automatic Hiboutik API/email synchronization, direct card-terminal control or POS-run refunds, full accounting/ERP, a replacement for Gestion SUSHI 81, formal B2B invoices, live SQLite synchronization through OneDrive, simultaneous multi-writer operation or an automatic updater. Ask the system owner before relying on a workflow outside this guide.

## Official references

- Microsoft: [Credential Manager in Windows](https://support.microsoft.com/en-us/windows/security/credential-manager-in-windows) documents the Windows 10/11 entry point; this [Generic credential UI example](https://learn.microsoft.com/en-us/sharepointmigration/mm-setup-clients) shows the field labels (its Azure-specific values do not apply to Sushi81 POS); the [CREDENTIALA structure](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentiala) documents that Credential Manager ignores UserName for a Generic credential.
- GitHub: [fine-grained PAT permission matrix](https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens), [Release REST API](https://docs.github.com/en/rest/releases/releases), [release asset REST API](https://docs.github.com/en/rest/releases/assets), and [PAT management guide](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens).
