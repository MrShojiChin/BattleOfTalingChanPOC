# Project Report: Thai-Themed Turn-Based Card Battler

**Course:** [Course Name]
**Student:** [Student Name / Student ID]
**Date:** March 10, 2026
**Engine:** Unity 6 (URP, New Input System, TextMeshPro)
**Language:** C#
**Repository Branch:** `multiplayer-refactor`

---

## 1. Introduction

This project is a **turn-based card battle game** built in Unity, inspired by Thai mythology and fantasy themes. Players take turns summoning avatar creatures, casting magic spells, and engaging in combat to destroy the opponent's LIFE cards and achieve victory. The game features a rich card system with three distinct card types, over 20 magic effects, 11+ avatar ability keywords, and a structured four-phase turn system.

The project began as a single-player prototype and has since been refactored toward a two-player local multiplayer architecture, replacing global singletons with per-player object ownership.

---

## 2. Game Design Overview

### 2.1 Win Condition

Each player starts with **5 face-down LIFE cards**. When an avatar attacks a LIFE card, it flips face-up and may trigger an effect. Once all 5 LIFE cards are flipped and the opponent lands one additional direct hit (called **Sahat**), the game is won. A player also loses instantly if their **deck runs out of cards**.

### 2.2 Card Types

| Card Type | Description |
|-----------|-------------|
| **Avatar** | Creatures with Power, Cost, Color, and Symbol. Summoned to the board to attack and defend. |
| **Magic**  | Spell cards with four subtypes: Normal (instant), Modification (attach to avatar), React (auto-trigger), and Land (global buff). |
| **LIFE**   | 5 cards dealt face-down at game start. Flipping them triggers special effects. |

### 2.3 Turn Structure

Each turn consists of four sequential phases:

1. **Draw Phase** -- Untap avatars, draw cards (1 card or refill to 3).
2. **Main Phase** -- Summon avatars by paying tribute costs; play magic cards.
3. **Battle Phase** -- Select attackers and resolve Power-vs-Power combat.
4. **End Phase** -- Discard down to a 7-card hand limit, then pass the turn.

### 2.4 Summoning System

Summoning follows a three-step state machine: **Select -> Pay -> Place**.

- The player selects an avatar from hand.
- They click other hand cards as tribute (matching color or Neutral).
- Once the gem cost is met, the avatar glows green and can be dragged onto a board slot.
- Tribute cards are sent to the graveyard (Hell zone).

### 2.5 Magic Effects

Over 20 magic effect types are implemented, including:

- **PowerBoost / PowerDebuff** -- Modify avatar Power values
- **DrawCards** -- Draw additional cards from deck
- **SearchDeck** -- Find a specific card from the deck
- **Discard-for-Effect** -- Sacrifice a hand card to gain an advantage
- **Modification** -- Attach as a continuous buff to an avatar
- **React** -- Place face-down; triggers automatically when conditions are met
- **Land** -- Global buff affecting all avatars matching a specific symbol

### 2.6 Avatar Abilities (11+ Keywords)

Avatars can carry ability keywords as flags, including:

| Ability | Effect |
|---------|--------|
| Juti | On-summon from cost, revive a matching avatar from Hell |
| CombatThonSoop | On attack/defend, mill cards and gain temporary power |
| HellSummonOnThonSoop | If milled, automatically summon from Hell |
| ThonSoopAmplifier | All mill effects gain +2 additional cards |
| HellPowerScaling | +1 Power per matching name in graveyard |
| SymbolAura | Continuous Power buff to allies sharing the same symbol |
| JutiDraw | Draw N cards when summoned via cost |
| AttackPowerBoost | Temporary Power boost only when attacking |

---

## 3. Technical Architecture

### 3.1 High-Level Architecture

The project uses an **enum-driven finite state machine** architecture. Core game flow is managed through interconnected controllers, each responsible for a specific domain.

```
GameManager (Turn/Phase FSM)
├── BattleController (Summoning FSM)
├── CombatController (Combat Resolution FSM)
├── MagicController (Magic Play & Effect Router)
├── AvatarAbilityController (Keyword Ability Handler)
├── DeckController (Deck & Draw Management)
├── HandController (Hand Layout & Display)
└── UIController (HUD & Player Interface)
```

### 3.2 Key Scripts and Responsibilities

| Script | Lines | Responsibility |
|--------|-------|----------------|
| `GameManager.cs` | ~673 | Master turn/phase state machine, turn flow control |
| `Card.cs` | ~1114 | Core card MonoBehaviour: drag/drop, click routing, visual state |
| `MagicController.cs` | ~1701 | Magic card type routing, 20+ effect implementations |
| `UIController.cs` | ~1383 | All 2D HUD: phase bar, gem display, combat info, panels |
| `CombatController.cs` | ~860 | Battle Phase combat selection and Power resolution |
| `AvatarAbilityController.cs` | ~875 | Avatar keyword ability resolution |
| `DeckController.cs` | ~504 | Deck shuffling, card drawing, LIFE card dealing |
| `BattleController.cs` | ~309 | Summoning state machine (Select -> Pay -> Place) |
| `Player.cs` | ~297 | Per-player data: zones, hand, deck references |
| `HandController.cs` | ~157 | Hand card layout, positioning, slide animations |

### 3.3 Data Layer (ScriptableObjects)

Card data is defined using Unity ScriptableObjects, separating data from behavior:

- **`AvatarCardSO`** -- Name, Cost, Power, Color, Symbol, GemValue, Ability flags
- **`MagicCardSO`** -- Name, MagicType, MagicEffect, EffectValue, TargetSymbol
- **`LifeCardSO`** -- LIFE-specific card data referencing avatar instances

### 3.4 Enum-Driven Design

The project relies heavily on enums for type safety and state clarity:

- `TurnPhase` -- {Draw, Main, Battle, End}
- `SummonState` -- {Idle, CostStep, ReadyToPlace}
- `CardType` -- {Avatar, Magic, Life}
- `CardColor` -- {Neutral, Red, Blue, Yellow}
- `CardSymbol` -- {Giant, God, Human, Devil, Ghost, Sorcerer}
- `MagicType` -- {Normal, Modification, React, Land}
- `MagicEffect` -- 20+ effect variants
- `AvatarAbility` -- 11+ ability flag variants
- `ZoneType` -- {Avatar, Magic, Hell, Deck, Life, Construct, LandMagic}

### 3.5 Design Patterns Used

| Pattern | Application |
|---------|-------------|
| **Finite State Machine** | GameManager (phases), BattleController (summoning), CombatController (combat) |
| **Per-Player Ownership** | Each `Player` object owns its own `HandController`, `DeckController`, and zone references (replacing global singletons) |
| **ScriptableObject Data** | Card definitions are pure data assets, decoupled from runtime behavior |
| **Polymorphic Routing** | `Card.OnPointerDown()` and `Card.HandleDrop()` branch behavior based on card type and current game state |
| **Coroutine Sequencing** | Animated card draws, sequential effect resolution, and UI transitions use Unity coroutines |

---

## 4. Project Structure

```
Assets/
├── Scripts/                    # All C# source files (~20 scripts)
│   ├── GameManager.cs          # Turn/phase controller
│   ├── Card.cs                 # Core card behavior
│   ├── BattleController.cs     # Summoning logic
│   ├── CombatController.cs     # Combat resolution
│   ├── MagicController.cs      # Magic effects engine
│   ├── AvatarAbilityController.cs  # Avatar abilities
│   ├── DeckController.cs       # Deck management
│   ├── HandController.cs       # Hand layout
│   ├── Player.cs               # Per-player state
│   ├── UIController.cs         # HUD system
│   ├── CardPlacePoint.cs       # Board zone slots
│   ├── AvatarCardSO.cs         # Avatar data definition
│   ├── MagicCardSO.cs          # Magic data definition
│   ├── LifeCardSO.cs           # LIFE data definition
│   └── [Enums & helpers]       # Type enums, color, symbol
├── Card/                       # ScriptableObject instances
│   ├── Avatar Card/            # Avatar card assets
│   ├── Magic Card/             # Magic card assets
│   └── Life Card/              # LIFE card assets
├── Scenes/
│   ├── Battle.unity            # Main gameplay scene
│   └── SampleScene.unity       # Test scene
├── Prefab/                     # Card prefabs
├── Settings/                   # URP pipeline configuration
└── TextMesh Pro/               # Thai font & TMP assets
```

---

## 5. Development History

The project was developed iteratively, tracked through Git version control:

| Commit | Milestone |
|--------|-----------|
| `0dc421e` | Initial commit -- working single-player prototype |
| `0bdf662` | Added `Player` class, removed all singletons (multiplayer refactor begins) |
| `7c6e859` | Added `GameManager`, HUD phase bar, board zone system |
| `1485495` | Phase-aware summoning, deck spawn positioning |
| `2170e7b` | Battle Phase combat, mulligan system |
| `9edce6d` | LIFE card system with face-down rotation and win condition |
| `92dd335` | LIFE card click fix, discard-to-7, Thai font support |
| `9d553ec` | HUD info display, deck-out loss, combat highlights, card preview |
| `455641b` | Fixed deck initialization race condition |
| `124ff74` | Magic effects, hell viewer, LIFE on-flip effects |
| `c29cd3c` | MagicController, CardSymbol system, themed magic effects |
| `688651c` | Phase stomp animation, avatar abilities, board layout polish |

---

## 6. Features Implemented

- [x] Four-phase turn system (Draw, Main, Battle, End)
- [x] Three-step summoning with tribute cost payment
- [x] Power-vs-Power avatar combat
- [x] LIFE card system (face-down placement, flip-on-hit, triggered effects)
- [x] 20+ magic effects across 4 magic subtypes
- [x] 11+ avatar ability keywords
- [x] Modification system (continuous buffs attached to avatars)
- [x] React magic (auto-trigger on game events)
- [x] Land magic (global symbol-based buffs)
- [x] Deck-out instant loss condition
- [x] Sahat win condition (all LIFE flipped + direct hit)
- [x] Mulligan system during game setup
- [x] Discard-to-7 hand limit enforcement
- [x] Hell zone viewer (double-click to browse graveyard)
- [x] Card preview (right-click zoom)
- [x] Hand slide animation (hide opponent hand off-turn)
- [x] Animated sequential card draws
- [x] Thai language text support with custom font
- [x] Per-player architecture (multiplayer-ready)
- [x] Phase transition animations

---

## 7. Challenges and Solutions

### Race Condition in Deck Initialization
**Problem:** Unity's `Start()` execution order caused `DeckController` to initialize before `GameManager` assigned player references, leading to null reference errors.
**Solution:** Introduced explicit initialization order by having `GameManager` call deck setup methods directly instead of relying on `Start()` auto-execution.

### LIFE Card Click Detection
**Problem:** LIFE cards use rotated canvases (face-down), which caused Unity's EventSystem raycasting to fail on click detection.
**Solution:** Bypassed EventSystem for LIFE cards and implemented direct Physics/Physics2D raycasting from `CombatController.Update()`.

### Singleton Removal for Multiplayer
**Problem:** Original single-player design used global singletons for Hand, Deck, and other controllers, making two-player support impossible.
**Solution:** Refactored all player-specific controllers to be owned by a `Player` object. `GameManager` routes to `CurrentPlayerObj` or `OpponentPlayerObj` based on turn state.

### Complex Magic Effect Routing
**Problem:** 20+ magic effects with different targeting, timing, and resolution behaviors created routing complexity.
**Solution:** Centralized all magic handling in `MagicController` with enum-based routing. Each `MagicEffect` variant maps to a dedicated handler method, keeping the dispatch logic clean.

---

## 8. Future Work

- Online multiplayer networking (Netcode / Photon integration)
- AI opponent for single-player mode
- Deck building screen and card collection system
- Additional card sets and ability keywords
- Visual effects and sound design polish
- Tutorial and onboarding flow

---

## 9. Conclusion

This project demonstrates a fully playable turn-based card battle game with deep strategic mechanics rooted in Thai fantasy themes. The architecture supports extensibility through ScriptableObject-based card definitions, enum-driven state machines, and a per-player ownership model ready for multiplayer expansion. Over 12 iterative development commits transformed a single-player prototype into a feature-rich two-player card game with 20+ magic effects, 11+ avatar abilities, and a complete turn cycle.

---

*Report generated for academic purposes.*
