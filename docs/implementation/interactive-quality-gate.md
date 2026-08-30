# Interactive implementation quality gate

**Status:** Approved  
**Approved by:** project owner  
**Effective:** 2026-08-30  
**Scope:** all Sushi81 POS implementation handoffs from M03 remediation onward, with particular force for WPF/user-visible work.

This document is process/quality governance. It does not change frozen V1 business behavior, data semantics, architecture, milestone scope or acceptance criteria.

## 1. Why this gate exists

Recent M03 operator acceptance exposed several defects that survived green build/test/CI:

- selection/filter labels became blank after live language switching because WPF selection lifecycle and object identity were not exercised realistically;
- Category manager layout stretched/clipped controls because structural layout behavior at default/maximized sizes was not validated as an operator would see it;
- Category Create technically changed internal state but gave insufficient immediate interaction feedback until focus/action-state behavior was fixed;
- Catalogue headers were blank because `DataGridColumn` was treated as if it participated in the normal Window visual/logical ancestor tree;
- the first dynamic OptionGroup-add path crashes the running application and was not caught before operator testing.

The common problem is not lack of Domain/Application tests. It is that user-visible WPF behavior was too often considered complete when implementation seams, source structure and non-interactive tests were green, without exercising the actual event/lifecycle path a user triggers.

## 2. Root-cause classes to actively prevent

### 2.1 Framework-lifecycle assumptions

Do not assume WPF bindings/events behave like ordinary object calls. Before finalizing a control path, reason explicitly about:

- construction order;
- initialization versus loaded state;
- visual/logical tree membership;
- selection change timing;
- focus timing;
- inherited versus non-inherited DataContext;
- object identity of bound collections/items across refresh/localization;
- dynamically inserted controls after the Window is already shown.

Objects such as `DataGridColumn` that are not normal visual/logical descendants must not rely on ancestor bindings that cannot resolve.

### 2.2 Event handlers observing partially initialized objects

A constructor must not wire an event handler that can access dependent controls/state before those dependencies are initialized.

For dynamic WPF editors, prefer one of these safe patterns:

1. construct all dependent controls/state first, set initial values, then attach event handlers; or
2. use an explicit initialization guard whose behavior is tested; or
3. route initialization through a pure/testable presentation state object and apply the completed state to controls afterward.

Do not rely on the assumption that a WPF event "will not fire yet" merely because the Window is not fully displayed.

### 2.3 Hidden fallback behavior

Localization, validation and command state must not silently fall back in a way that makes tests pass while the operator sees the wrong UI.

Child Windows/dialogs must receive localization/state explicitly. Do not assume setting `Owner` causes `DataContext` inheritance. Business data must never be translated when software labels change language.

### 2.4 Layout tested as structure instead of usability

For user-visible windows/dialogs, validate the actual layout contract at:

- normal/default size;
- minimum supported size where relevant;
- resized/maximized size;
- both French and zh-CN when label length differs.

Only the intended content region should stretch. Action buttons should remain content-sized/usable; controls must not clip or become giant vertical blocks.

### 2.5 Internal-state success without operator-visible success

A command is not accepted merely because an internal boolean/state machine changes. For interactive actions verify the complete operator outcome: visible state change, focus when appropriate, enabled/disabled actions, validation visibility and safe Cancel/close behavior.

## 3. Mandatory operator-journey review before CODEX_DONE

For every handoff that adds or changes user-visible WPF behavior, the Codex main agent must map the affected operator journey before implementation and re-walk it before completion.

At minimum document:

- starting UI state;
- exact operator action(s);
- expected immediate visible response;
- expected enabled/disabled/focus state;
- persistence boundary (what must and must not be saved yet);
- Cancel/close behavior;
- language-switch implications;
- resize/layout implications when relevant;
- restart/reopen implications when persistence is involved.

If the runtime cannot perform a true interactive desktop run, say so explicitly and compensate with the strongest practical STA/WPF lifecycle test plus a clearly scoped outstanding operator check. Do not imply that compile/CI substitutes for manual WPF acceptance.

## 4. Automated test hierarchy for interactive code

Use the strongest applicable level. Lower levels do not replace higher levels when the defect class requires them.

1. **Domain/Application tests** for business rules and persistence semantics.
2. **Pure presentation-state tests** for deterministic state transitions where useful.
3. **STA/WPF construction and event-lifecycle tests** for critical interactive/dynamic paths. Instantiate the relevant control/dialog far enough to execute the same construction/event sequence used by the operator.
4. **Integration tests** for persistence/reopen where the UI action crosses the application/store boundary.
5. **Source/XAML string assertions** only as supplemental structural checks. They are not sufficient proof for event timing, binding resolution, focus, dynamic insertion or crash regressions.
6. **Operator/manual acceptance** remains the final evidence for visual/usability behavior required by the milestone.

A regression test for an operator-discovered defect must fail on the pre-fix behavior for the actual defect reason whenever technically practical, not merely assert that the new implementation text exists.

## 5. Dynamic WPF editor rules

For dynamically created Product/OptionGroup/Option controls:

- exercise creation after the parent Window is already loaded/shown, not only constructor-time population;
- exercise the first add, second add, remove and reorder paths when those actions exist;
- exercise mode/state changes after insertion;
- ensure every event handler can run safely at every lifecycle point at which WPF may raise it;
- verify the actual visual container inserted is the intended container, including borders/margins/scrolling;
- verify ScrollViewer behavior with enough repeated dynamic content to require scrolling;
- ensure unsaved dynamic additions disappear on Cancel and do not leak into persistence;
- ensure existing saved dynamic content reopens with IDs/order/values intact.

## 6. Binding/localization rules

Before completion of any localized WPF screen/dialog:

- verify each binding source actually exists in the relevant tree/context;
- verify explicit localization flow for child Windows and dynamically created controls;
- verify FR → zh-CN → FR on the affected screen without changing business data;
- for selection-bound localized options, preserve stable identity where WPF selection semantics depend on identity, updating labels in place rather than replacing objects unless replacement is intentionally handled and tested;
- do not consider resource-key parity alone sufficient proof that the rendered control receives the resource.

## 7. Defect-escape retrospective requirement

Whenever operator acceptance discovers a defect that automated verification missed, the fixing handoff must include a short retrospective before coding:

1. what assumption in the previous implementation allowed the defect;
2. why existing tests did not catch it;
3. which adjacent code uses the same risky pattern;
4. what regression test closes the exact gap;
5. what general rule should prevent recurrence.

The main agent must inspect adjacent occurrences of the same pattern in the affected hot area rather than fixing only the single observed line.

For repeated escapes in the same screen/area, perform a focused audit of that entire interaction cluster before asking the operator to resume testing.

## 8. Independent Luna audit for escaped UI defects

The normal rule remains: narrow production hotfixes should usually have a single writer to avoid overlapping edits.

However, for any of the following, the main agent should normally delegate **one independent read-only audit/test-design package** when the runtime supports useful subagents:

- a user-visible application crash;
- the second or later operator-discovered defect in the same UI area;
- a defect caused by event/binding/lifecycle behavior that escaped existing automated tests.

That audit subagent must not edit the same production hot files. Its task is to independently inspect the affected lifecycle, identify adjacent risks, and propose/verify regression coverage. When explicit selection is available use GPT-5.6 Luna at the highest available reasoning effort (`max` preferred), consistent with `agent-execution-contract.md`.

The main agent remains sole integrator and may reject the audit findings. If no subagent is used despite this trigger, `CODEX_DONE` must state the concrete reason (for example runtime unavailable or no safe independent audit seam), not merely say the task was narrow.

For larger milestones, the existing execution contract still governs true parallel write workstreams after shared seams are stable.

## 9. Pre-completion UI defect-prevention checklist

Before posting `CODEX_DONE` for affected WPF work, the main agent must explicitly check:

- no event handler can observe partially initialized controls/state;
- no binding depends on an invalid visual/logical-tree assumption;
- child-dialog localization/DataContext is explicit;
- selection-bound objects survive refresh/language changes as intended;
- default/min/resized/maximized layout is sane where applicable;
- every newly added button has its actual click path exercised;
- dynamic add/remove/reorder paths are exercised when present;
- focus/action-state feedback is operator-visible;
- Cancel/close does not persist unsaved changes;
- source-string tests are not being used as a substitute for runtime/lifecycle tests;
- the affected operator journey is ready for manual continuation.

## 10. Completion evidence

`CODEX_DONE` for a handoff governed by this file must include, in addition to the normal execution topology:

- the operator-journey path(s) exercised automatically;
- the defect-escape retrospective when fixing an operator-found defect;
- the exact regression test level used (pure presentation, STA/WPF lifecycle, integration, structural-only supplemental);
- adjacent risky-pattern audit result;
- any remaining manual-only evidence;
- independent Luna audit result when section 8 applies, or the explicit reason it could not be used.

This quality gate is inherited by future milestone contracts alongside `agent-execution-contract.md`.
