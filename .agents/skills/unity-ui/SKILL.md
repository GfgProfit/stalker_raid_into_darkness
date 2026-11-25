---
name: unity-ui
description: "Apply MVVM, R3, TextMeshPro and lifecycle rules for new or changed Unity UI features, bindings, reactive subscriptions, and legacy UI fixes."
---

# Unity UI

This skill is REQUIRED before implementing new UI, adding functionality or bindings to existing UI, or changing UI subscriptions and their lifetimes. [AGENTS.md](../../../AGENTS.md) controls mandatory routing. For domain changes, you MUST use its game architecture route to identify the authoritative owner and composition boundary. Unrelated legacy screens MUST stay outside the change.

## Before implementation

- You MUST verify that the existing R3 package and relevant assembly references are available. If R3 is missing or inaccessible, you MUST report the blocker and request authorization where needed. Explicit authorization is REQUIRED to install a dependency. Substituting a reactive framework, mixing R3/UniRx, and using polling as a substitute are FORBIDDEN.
- MVVM + R3 is REQUIRED for new UI features, screens, windows, menus, interactive widgets with state or commands, new functionality in legacy UI, and new bindings in existing MVVM features. Pure stateless visuals MAY stay View-only under the feature. You MUST avoid a ViewModel per label or icon.
- You MUST start from the existing View, ViewModel, model/owner, composition, and binding lifetime. Where a legacy owner lacks a change signal, you MUST add a focused adapter or notification. You MUST NOT extend ownership, state mutation, or polling in legacy controls.

## Responsibilities and command flow

- **View.** It MUST own Unity references, TextMeshPro, layout, visuals, animation, pooling, lifecycle, and R3 bindings, and forward input to ViewModel commands.
- **ViewModel.** It MUST use plain C# where practical. It MUST own presentation logic, UI-local state, formatting, commands and read-only reactive projections, receive explicit dependencies through DI or feature composition, and delegate gameplay operations and validation to services. It MUST own subscriptions and cancellable work for its lifetime. New ViewModels MUST NOT depend on scene objects or manipulate visual objects.
- **Model/services.** They MUST keep gameplay, inventory, equipment, persistence, item configuration, rules, validation and authoritative state independent of UI/visibility, and expose commands, queries and notifications. Views and ViewModels MUST NOT contain gameplay rules or write directly to authoritative state.
- The flow MUST be input → ViewModel command → service → committed state/notification → affected controls. The UI MUST show the committed result or error. Each gameplay fact MUST have one mutable authority. A competing UI copy is FORBIDDEN.

## Reactive bindings and lifetime

- You MUST bind initial and changed inventory, equipment, health, ammo, settings and other gameplay values through R3 projections from their owners and keep writable reactive state private. You MUST use `ReactiveProperty`, read-only properties, streams, and `CompositeDisposable` or equivalents as appropriate. You MUST filter or batch redundant emissions without losing required events and avoid feedback loops, full rebuilds, unchanged assignments, repeated formatting, and transient allocations. Domain systems MAY keep explicit event/query contracts adapted at the presentation boundary. Managed R3 is FORBIDDEN in native hot loops.
- You MUST assign each subscription to the actual View, ViewModel, service or operation lifetime. You MUST pair setup with disposal on unbind/close/disable/return to pool when those end the binding, and dispose longer-lived work at its owner boundary. You MUST reinitialize current state on reopen/re-enable/rebind and avoid duplicate or stale subscriptions. Orphan subscriptions, unowned static reactive state, and destroyed Unity object captures in long-lived observers are FORBIDDEN.
- Unity object callbacks MUST run on the main thread. When lifetime matters, you MUST propagate cancellation through the entire async chain. When the corresponding route applies, you MUST check [Unity lifecycle](../unity-lifecycle/SKILL.md) for changed enable/disable, pooling, rebinding, and destruction behavior and [async patterns](../unity-async-patterns/SKILL.md) for changed cancellation or coroutine behavior.
- Polling gameplay state from UI in `Update`, `LateUpdate`, `FixedUpdate`, coroutines, repeating timers, or per-frame observables is FORBIDDEN. You MUST remove polling that causes the bug or is replaced by the binding. Frame-dependent animation, interpolation, drag, input, and visuals are allowed. If an external or engine API only exposes sampled data, you MUST sample it at a justified cadence in the owner/adapter and publish changes reactively.
- You MUST use `TMP_Text`, `TextMeshProUGUI`, and `TMP_InputField` explicitly as appropriate. New UI MUST use legacy `UnityEngine.UI.Text` only where surrounding code requires it. Frequent UI updates MUST avoid repeated string allocations.

## Verify

You MUST check initial display, committed updates and errors, owner independence from UI visibility, and updates limited to affected controls. You MUST exercise applicable open/close, disable/enable, pool reuse, rebind, and cancellation/destruction paths and confirm one subscription per binding, current state after reuse, main-thread Unity access, and absence of stale result application or constant gameplay polling. You MUST use the root Unity validation route.
