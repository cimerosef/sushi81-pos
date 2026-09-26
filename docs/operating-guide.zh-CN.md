# Sushi81 POS 操作指南（简体中文）

## 新电脑快速上手：加入现有 Sushi81 系统

按顺序完成这些步骤。加入配对不等于取得写入权威。

1. **先备齐条件。**
   - 使用当前由业主批准的 Sushi81 POS 每用户安装程序，并从业主批准的分发位置/流程取得。安装后，将 `release-provenance.json` 中的版本和源代码身份与该批准安装程序随附的身份信息核对；不要把旧验收记录中的 artifact 或 source head 当作长期安装来源。普通更新和修复应保留 `%LOCALAPPDATA%\Sushi81 POS`。
   - 在这台电脑安装并登录 OneDrive；确认已有 Sushi81 OneDrive 共享根目录已完整同步且本机可访问。
   - 先由电脑/打印机管理员安装实际使用的 Windows 打印机驱动，并建立 Windows 打印队列。
   - 请系统负责人提供现有系统的 GitHub 转移配置，以及已按批准方式准备好的 Windows Credential Manager 受保护凭据。不要自行编造 owner、仓库或凭据目标名。
   - 正常转移时，旧权威电脑必须仍可使用。只有真正符合本指南“灾难恢复”条件时，才不依赖旧电脑走该特殊流程。

2. **安装并保留本机数据。**
   - 运行已核对的安装程序并按每用户方式安装。程序文件位于 %LOCALAPPDATA%\Programs\Sushi81 POS；应用数据位于 %LOCALAPPDATA%\Sushi81 POS。
   - 普通安装、更新、修复和卸载不应删除耐久应用数据。不要把应用数据目录当作清理缓存。
   - 绝不要在两台电脑间手工复制 live.db、权威 JSON 或转移文件。不要改名、替换或手动修复这些文件。

3. **首次启动并切换语言。**
   - 启动 Sushi81 POS，在顶部“语言”选择“简体中文”。界面可在法语与简体中文之间切换；已保存的商品名、订单和客户内容不会随语言切换而翻译。
   - 先看应用显示的设备/权威状态。只有明确显示为当前权威设备时，才允许创建或修改业务数据。只读、非权威、正在转移或需要恢复的状态都不能进行业务写入。

4. **填写现有系统的技术配置。**
   - 打开“配置 Sushi81 系统”。此配置只提供 OneDrive 路径及非敏感 GitHub 转移参数；保存它不会创建配对关系或赋予权威。
   - 每个字段都应从旧电脑现有配置抄录，或由系统负责人提供并确认：

     | 界面字段 | 填写内容与来源 | 是否机密 |
     |---|---|---|
     | Sushi81 OneDrive 共享文件夹 | 本机上现有共享根目录的绝对路径。使用“浏览…”定位已同步的目录；不要选个人桌面或新建另一个根目录。 | 路径本身不是凭据；只使用负责人确认的路径。 |
     | GitHub 所有者 | 转移仓库所属的 GitHub 用户或组织名称。 | 非机密；从现有电脑/负责人抄录。 |
     | GitHub 转移仓库 | 已配置的专用私有转移仓库名称。 | 非机密；从现有配置抄录，不要换成业务代码仓库。 |
     | GitHub release 标签 | 转移 release 使用的标签。 | 非机密标识；逐字抄录。 |
     | GitHub release 名称 | 转移 release 使用的名称。 | 非机密标识；逐字抄录。 |
     | 受保护 GitHub 凭据目标名称 | Windows Credential Manager 中 Generic credential 的目标名称；它只是查找键。 | 名称非机密。真正的 PAT/token 是机密，绝不可填入 Sushi81 POS。 |

   - **在 Windows 凭据管理器中保存令牌。**在 Windows 10/11 任务栏搜索 `Credential Manager`，打开 `Credential Manager Control Panel`；也可从“控制面板 > 用户帐户 > 凭据管理器”进入。选择 `Windows Credentials`，在 `Generic Credentials` 下选择 `Add a generic credential`。
     1. `Internet or network address`：逐字填写 Sushi81 POS 配置中的“受保护 GitHub 凭据目标名称”；它是查找键，不是秘密。
     2. `User name`：如界面要求，填写非机密的说明性/账户标签。Sushi81 POS 不读取此字段；它不代表权限，也不授予访问权。
     3. `Password`：填写由负责人/IT 批准、用于专用私有转移仓库的 GitHub fine-grained personal access token (PAT)，然后保存。
   - **令牌的最小权限：**将 fine-grained PAT 的 repository access 限定为仅所需的专用私有转移仓库，绝不要授权 Sushi81 POS 源代码仓库；仅授予该仓库的 `Contents: Read and write`。生产传输调用用仓库元数据和 release/tag/assets 读取版本、列出/下载资源；release 缺失时创建 release，并上传或删除资源需要 Contents 写权限；`write` 已包含 `read`。GitHub 的细粒度权限矩阵将仓库查询列为 Metadata 读取（fine-grained token 自动包含 Metadata read）。当前操作无需账户或组织级权限。
   - 把令牌保存在 Credential Manager 后，在 Sushi81 POS 的“受保护 GitHub 凭据目标名称”字段中只填写同一个目标名称，绝不填写令牌；然后继续执行步骤 4。
   - **安全边界：**绝不要把 PAT 放入 Sushi81 POS 字段、仓库文档、截图、日志、聊天、终端/命令行或 shell 历史。
   - 保存配置，等候“请重启 Sushi81 POS”提示，然后完全退出并重启应用。重启后选择“测试 GitHub 转移连接”。成功时应显示 GitHub 转移连接可用。未配置/无法读取凭据、401、403 或 404 分别说明配置或凭据、访问权、仓库/release 有问题；先让负责人核对现有值，不要尝试把 secret 输入应用。

5. **加入现有系统（只读加入）。**
   - 确认共享根目录中已有系统的 lineage 信息可用。点击“加入现有系统”，输入能让同事辨认且不与现有设备混淆的设备名称，并确认。
   - 成功加入只证明设备配对/登记完成，且仍是只读、非权威状态；若本机已有可用的业务数据集，可进行只读查看。全新电脑可能处于 `PairedUninitializedReadOnly`，在获批的目标获取/重新初始化流程完成之前可能没有可用的当前业务数据。加入成功不代表数据已获取，也不赋予写入权威；配对不会把权威从旧电脑移走。若应用提示系统信息缺失或无效，不要初始化空系统或更换共享根目录；请负责人检查。

6. **旧电脑转移权威到新电脑（正常流程）。**
   - 普通权威转移使用已配置的 GitHub 转移仓库，把一次不可变转移定向给一个配对目标；OneDrive 共享根目录用于系统元数据和灾难恢复，不是正常转移通道。
   - 在仍显示为权威的旧电脑，正常保存/完成当前操作，再选择关闭操作“转移权威并关闭”。
   - 按设备名称核对目标；若出现多个候选，选新电脑对应的确切当前代际设备。名字或设备身份不清楚时取消并先确认。
   - 等待旧电脑报告转移完成并关闭。权威一旦在旧电脑持久地释放，旧电脑必须保持只读；不能继续接单，也不能把待处理转移改给另一台机器。
   - 在指定新电脑选择“检查并获取已转移的权威”。等待系统校验转移，并在本机安装/刷新经过验证的业务数据集；只有成功完成获取且确认新电脑显示为权威/可写、旧电脑仍为只读后才继续营业。
   - 若只是关机且权威要留在原电脑，使用“关闭并保留权威”，不要转移。普通关机不会自动把权威交给其他电脑。
   - 若转移在中途失败：释放权威前，旧电脑可能仍可保有权威；释放后它必须只读。旧电脑只可使用“继续待处理的转移”重试同一个目标转移；不要手改文件、取消、换目标或让第三台电脑写入。不是指定目标的其他设备始终只读。

7. **灾难恢复只用于真正异常。**
   - 只有普通权威/指定目标路径确实无法恢复时才考虑 Disaster Recovery。负责人必须确认旧权威/指定目标设备已停止或隔离，不会再用于写入；操作者还须了解选定恢复点之后可能丢失的数据，并确认系统代际将前进。
   - 灾难恢复不是普通交接，也不是因为电脑暂时离线、应用已关闭或转移稍慢就使用的快捷办法。若无法确认隔离或恢复点，停止操作并联系负责人。

## 打印机设置（每台电脑分别配置）

打印机选择保存在本机，不随业务系统转移到另一台电脑。新电脑应独立设置。

1. 由电脑/打印机管理员先在 Windows 安装适用驱动并建立打印队列。本指南不指定任何打印机型号或驱动安装方式。
2. 打开 Sushi81 POS 的“设置”（Paramètres）。
3. 点击“刷新打印机”（Actualiser les imprimantes），等候系统列出已安装队列。
4. 在“厨房打印机”（Imprimante cuisine）选择厨房队列；在“客户打印机”（Imprimante client）选择客户队列。若本地实际布置共用同一 Windows 队列，两项可以选同一队列。
5. 点击保存，确认提示说选择已在本地保存。
6. 在安全的业务时点用权威设备执行一张测试订单的厨房/客户打印，或对现有订单使用相应重印功能。只读设备可打印其本机已有的订单，但测试新订单只能在权威设备上创建。
7. 若已保存队列不可用，先确认 Windows 队列已安装且可用，再返回设置、刷新并重新选择可用队列后保存。队列未恢复前不要反复生成订单来测试打印；订单保存与打印是分开的。

## 业务设置：默认值与既有系统核对

首次初始化时，以下是批准的默认值。打开“设置”（Paramètres）核对字段；字段名以当前界面的法语/简体中文显示为准。

| 界面字段（FR / 简体中文） | 首次初始化默认值 | 含义 |
|---|---:|---|
| Remise Retrait (%) / 自取折扣 (%) | 10% | 自取折扣比例。 |
| Minimum après remise (€) / 折扣后最低金额 (€) | €15.00 | 应用折扣后的订单总额不得低于此值。 |
| Minimum Livraison (€) / 配送最低金额 (€) | €30.00 | 配送商品/商业金额最低要求。 |
| Frais de livraison activés / 启用配送费 | 关闭 | 默认不收配送费。 |
| Frais de livraison (€) / 配送费 (€) | €0.00 | 配送费金额；启用后按设置值追加。 |
| 启用配送费的 VAT / TVA des frais de livraison activés | 固定 10%，不是可配置字段 | 当前 UI/spec 不提供选择配送费 VAT 的设置。 |

自取折扣只有在操作员申请时才应用，且只优惠符合条件的目录商品基础金额；正向选项加价不参与折扣，负向选项调整会先减少可折扣金额。如果应用折扣会使订单低于配置的折扣后最低金额，应用不会给出该折扣。

配送不应用普通自取折扣。配送最低金额在加配送费之前检查；配送费不能让原本未达到最低金额的订单变为合格订单。

在已有系统上完成权威获取后，应核对已转移的商品目录和业务设置。不要为了让现有系统看起来符合这些默认值而重置或覆盖设置。默认值仅供首次初始化和解释规则使用。

## 首次使用检查

恢复营业前逐项确认：

- 设备名称正确；当前电脑的只读/权威状态清楚。
- 看得到正确的现有商品目录。
- 已核对转移后的业务设置；不要为了匹配默认值而覆盖它们。
- 厨房与客户打印队列均已选择，GitHub 转移连接测试成功。
- 如需测试订单，只在明确显示为权威的电脑创建。
- 安全打印或重印成功；完成权威转移后旧电脑保持只读。

## 日常营业

### 收银台（Caisse）：创建、修改、收款、关闭或取消订单

在“收银台”按分类、代码或名称找商品，添加到订单，确认数量和选项，再选“自取”（Retrait）或“配送”（Livraison），填写所需日期/时间、电话、地址和备注并复核金额。电话与配送地址按已批准业务规则不是创建订单时的必填项。

保存修改仍是同一订单。录入实际已收到的银行卡（CB）与现金（Espèce）金额；如款项实际收到日期不同，填写实际收款日期。只有 CB 与现金合计精确等于订单总额时才能“关闭订单”（Clôturer）；欠款或多收都不能关闭。取消保留订单及支付事实，不会执行银行卡退款。价格影响项更改可能重新计算总额；改后再次核对。

### 业务数据完整重置——仅限业主维护

完整重置是会删除数据的维护操作，不是取消订单或故障排查。仅当业主指定此配置文件可重置时，才在当前权威设备的“设置”（Paramètres）中打开“数据维护”（Données → Maintenance des données）。先检查只显示数量的预览及保留状态清单；关闭或取消不会更改数据。继续时准确输入大写 `RESET`，再检查并接受单独的最终确认。应用会先创建并保留经过验证的私有备份，然后清空整个运营数据集并刷新业务页面。设备配对、权威状态、配置、凭据、打印机选择、界面语言、业务设置和恢复材料都会保留。绝不要在营业中的真实业务配置文件上执行，也不要用它排查安装、启动、迁移或权威转移。自动测试与培训只能使用合成数据。参见 [`decisions/m13-full-business-data-reset.md`](decisions/m13-full-business-data-reset.md)。

### 打印/重印

确认订单时先保存订单，再发送厨房单与客户单。打印失败不代表订单回滚。先查订单，再单独重试对应打印或重印；不要为同一订单重复新建订单。付款变动后可能需要新的客户单。只读设备也可打印其本地可见的订单。

### 商品目录与 XLSX

在“商品目录”（Catalogue）维护商品、分类、价格、VAT、启用状态、自取折扣资格和商品选项。临时无货可停用；历史订单会保留当时快照。批量 XLSX 导入先检查预览与阻止性错误后再提交。不要改技术内部 ID；新导出工作簿是当前模板。此工作簿用于商品目录，不用于修改订单或 Gestion 导出文件。

### Hiboutik 粘贴备用流程

若 Hiboutik 服务端打印不可用，从订单邮件复制完整商品明细，在“收银台”展开“Commande Hiboutik”，粘贴并选择“Analyser la commande Hiboutik”。用当前商品代码解决未知行，随后通过普通订单字段填写履约方式、时间、电话、地址和备注，并检查 POS 自己的价格/选项再确认。粘贴/解析不会保存订单；应用不会自动读取剪贴板、邮件或 Hiboutik。邮件总价只作参考，不决定 POS 总价。Hiboutik 来源订单通过隐藏标记避免重复计入普通 POS 营业额与 Gestion export。

### Gestion export（销售数据导出）

在顶部“数据”（Données / 数据）打开独立的“销售数据导出”（Gestion export / 销售数据导出）页签。选择包含首尾日期的履约日期范围，或不设日期过滤；点击“预览 / 刷新”（Aperçu / actualiser）检查将要导出的动作。符合条件的普通已关闭订单可产生 CREATE；后续修正可产生 UPDATE；取消可产生 CANCEL。Hiboutik 粘贴订单不导出。

复核后点击“生成 Excel 文件”（Générer le fichier Excel），再按单独的 Gestion SUSHI 81 工作簿流程导入；应用不会直接修改该工作簿。成功批次可从保留的不可变载荷重新生成。待完成的 PREPARED 批次应保留在“待完成批次”，用“重试待完成批次”处理，不要手工删除或改写批次历史。

成功批次从完成时间起至少保留 30 天。超过期限后，只有当相关订单均已不在实时订单中、具备正向年度归档证明且没有未决依赖时，系统才可以压缩批次。PREPARED 永不删除；仅凭实时订单列表中找不到订单不能证明可以清理。操作员不用等待 30 天来做验收测试，也不应手工清理。

### 年度归档和历史订单

在“数据”页签打开“历史归档”（Archives historiques），选择年份并搜索。归档只读，显示下单时保存的历史快照，不使用当前商品目录重算。需要时可从历史订单明确重印。完成的年度数据库保存在应用管理的本地 Archive 目录并永久保留。“复制归档”只复制到所选位置，不移动或删除规范归档。年度归档是权威设备本地管理的操作，普通权威转移不会自动复制年度归档到新电脑。

真实且有订单数据的 2026→2027 归档实测依旧按 M12 业主豁免延期；首次在 2027-02-01 或之后且包含真实 2026 行的安全权威启动时另行验证，不代表本次 M13 已通过。

## 语言、故障处理与 V1 范围

顶部“语言”（Langue）可在“法语”（Français / FR）和“简体中文”（中文（简体）/ zh-CN）间切换。更改的只有界面文字，已存商品名、客户信息、订单、备注和历史快照保持原样。

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

遇到意外错误时，记下页面、操作和大致时间，把应用提示与 %LOCALAPPDATA%\Sushi81 POS\Logs 中近期日志交系统负责人查看。仅重试应用明确允许安全重试的操作。不要删除、替换或手动编辑 live.db、归档、恢复点、设置、配对或权威文件；不要通过重置用户资料排查问题。

V1 不提供库存/采购、桌位管理、员工权限、会员 CRM、Hiboutik API/邮件自动同步、银行卡终端控制或 POS 退款、完整会计/ERP、替代 Gestion SUSHI 81、正式 B2B 发票、通过 OneDrive 实时同步 SQLite、多写入者同时工作或自动更新。超出本指南的流程先询问系统负责人。

## 官方参考

- Microsoft：[Windows 凭据管理器](https://support.microsoft.com/en-us/windows/security/credential-manager-in-windows) 说明 Windows 10/11 的打开方式和 Windows Credentials 入口；[Generic credential 图形界面示例](https://learn.microsoft.com/en-us/sharepointmigration/mm-setup-clients) 展示字段名称（示例中的 Azure 值与 Sushi81 POS 无关）；[CREDENTIALA 结构](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentiala) 说明 Credential Manager 会忽略 Generic credential 的 UserName 字段。
- GitHub：[fine-grained PAT 权限矩阵](https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens)、[Release REST API](https://docs.github.com/en/rest/releases/releases)、[Release asset REST API](https://docs.github.com/en/rest/releases/assets) 和 [PAT 管理指南](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)。
