# Copilot Instructions - Card Battler (Unity 6)

## Project Overview

A turn-based card battler built in **Unity 6000.2.x** (Unity 6) with **URP**, **New Input System**, and **TextMeshPro**. Uses a **Pitch/Discard summoning system** (not global mana) with a **Select -> Pay -> Drag** workflow.

## Architecture

### Data Layer - ScriptableObjects

| SO Type | Menu Path | Key Fields |
|---------|-----------|------------|
| `AvatarCardSO` | `Cards/Avatar Card` | `avatarColor`, `gemColor`, `cost`, `gem`, `power` |
| `MagicCardSO` | `Cards/Magic Card` | `gemColor`, `gem`, `magicType` (Normal, Modification, React, Land) |

Both SO types carry `gemColor` (`CardColor` enum) - the color of mana the card provides when pitched.

> **`CardScriptableObject.cs`** is legacy/unused. Always use `AvatarCardSO` or `MagicCardSO`.

### Enums

- `SummonState` - `Idle`, `CostStep`, `ReadyToPlace` - the BattleController state machine
- `CardType` - `Avatar`, `Magic` - drives polymorphic setup in `Card.SetupCard()`
- `CardColor` - `Neutral`, `Red`, `Blue`, `Yellow` - color identity for summoning rules
- `MagicType` - `Normal`, `Modification`, `React`, `Land`

### Runtime Components

| Script | Role |
|--------|------|
| `Card.cs` | Core MonoBehaviour - SO data setup, drag interaction, `OnPointerDown` routes by SummonState, `HandleDrop()` routes by drop target |
| `BattleController.cs` | Singleton state machine (`SummonState`) - `InitiateSummon()`, `TryPayTribute()`, `FinalizeSummon()`, `ReturnTribute()`, `CancelFullSummon()` |
| `HandController.cs` | Manages player hand - positions cards between `minPos`/`maxPos`, `RemoveCardFromHand()`, `RemoveCardsFromHand()` |
| `CardPlacePoint.cs` | Board/hell slot - `isPlayerAvatarPoint`, `isPlayerMagicPoint`, `isPlayerHellPoint` booleans + `activeCard` |
| `UIController.cs` | Singleton UI - `ShowPaymentUI()`, `ShowReadyToPlace()`, `HidePaymentUI()` |

### Summoning Flow (Select -> Pay -> Drag)

Phase 1 - INITIATION (Idle state):
  Left-click Avatar in hand
    cost == 0: normal drag (isSelected = true), place on any avatar point
    cost > 0: BattleController.InitiateSummon() -> CostStep state
              Avatar lifts up + yellow highlight, NOT draggable yet

Phase 2 - COST STEP (CostStep state):
  Drag other hand cards to a CardPlacePoint where isPlayerHellPoint == true
    BattleController.TryPayTribute() validates:
      Rule A (Color): tribute.gemColor must be Neutral OR match pendingAvatar.avatarColor
      Rule B (Value): tribute.gem must be > 0
    Accepted tributes move to hellZone Transform, added to currentTributes list
    UIController updates "Gems Paid: X / Cost: Y"
    When totalGemsPaid >= cost -> transitions to ReadyToPlace

Phase 3 - READY TO PLACE (ReadyToPlace state):
  Avatar gets green highlight (SetReadyHighlight)
  Left-click avatar -> isSelected = true -> drag to board
  Drop on valid isPlayerAvatarPoint -> BattleController.FinalizeSummon()
    Tributes officially go to graveyard, avatar placed, state resets to Idle
    Excess gems are lost (one-to-one rule)

Phase 4 - CANCELLATION (right-click):
  Right-click tribute at hell -> BattleController.ReturnTribute() (returns that card to hand)
    If was ReadyToPlace but now underpaid -> reverts to CostStep
  Right-click pending avatar -> BattleController.CancelFullSummon() (all tributes + avatar return to hand)

### Key Card.OnPointerDown Routing

The `OnPointerDown` method checks `eventData.button` (Left/Right) and `BattleController.instance.currentState` to decide behavior:
- Right-click on tribute -> `ReturnTribute()`
- Right-click on pending avatar -> `CancelFullSummon()`
- Left-click in ReadyToPlace on pending avatar -> start dragging
- Left-click in CostStep on hand card (not avatar) -> start dragging to hell
- Left-click in Idle on Avatar -> `InitiateSummon()` or drag if free
- Left-click in Idle on Magic -> normal drag

### Key Card.HandleDrop Routing

When a dragged card is dropped on a `CardPlacePoint`:
- Case A: CostStep + hell point + not the pending avatar -> `TryPayTribute()`
- Case B: ReadyToPlace + this IS the pending avatar + avatar point -> `FinalizeSummon()`
- Case C: Normal placement (free avatar or magic) -> `PlaceOnBoard()`

## Conventions

- **Input**: `UnityEngine.InputSystem` (`Mouse.current`) - never legacy `Input`
- **UI Text**: `TMPro` (`TMP_Text`) only
- **Singletons**: `BattleController.instance`, `UIController.instance` (set in `Awake`)
- **Finding singletons**: `FindFirstObjectByType<T>()` (Unity 6), not deprecated `FindObjectOfType<T>()`
- **Card setup**: `Card.SetupCard()` runs in `OnValidate()` for Inspector preview - keep side-effect-free
- **Movement**: `Vector3.Lerp` / `Quaternion.RotateTowards` toward `targetPoint`/`targerRot` each frame
- **Layer masks**: `whatIsDesktop` and `whatIsPlacement` on Card - all raycast logic uses these

## Project Structure

Assets/
  Scripts/              # All C# scripts (flat)
    Card/               # SO asset instances (Avatar Card/, Magic Card/)
  Scenes/Battle.unity   # Main battle scene
  Prefab/               # Card prefabs
  Settings/             # URP pipeline assets
  _Udemy Card Battler Assets/  # Art, audio, 3D models - DO NOT modify

## Inspector Setup Required

- **BattleController**: Assign `hellZone` Transform (world position for graveyard stack)
- **UIController**: Assign `paymentPanel` (parent GO), `playerGemPaidText` (TMP_Text), `summonStatusText` (TMP_Text)
- **Card prefab**: Assign `pitchHighlightImage` (yellow border for pending), `readyHighlightImage` (green border for ready)
- **Card prefab**: Set `whatIsDesktop` and `whatIsPlacement` LayerMasks
- **CardPlacePoint**: Set `isPlayerHellPoint = true` on the hell/graveyard drop zone(s)

## When Adding Features

1. New card fields -> add to `AvatarCardSO`/`MagicCardSO` -> extend `Card.SetupCard()` branching on `cardType`
2. New `CardType` enum value -> update `CardPlacePoint` bools + `Card.HandleDrop()` routing
3. New tribute rules -> add checks in `BattleController.TryPayTribute()`
4. New summon-state transitions -> update `SummonState` enum + `Card.OnPointerDown`/`HandleDrop` routing
5. New UI feedback -> add methods to `UIController`, call from `BattleController`
