# Full System Architecture — Card Battle Game (สยามยุทธ์)
### Draft for Report Diagram Reference

---

## LEGEND (Symbol Key)

```
Symbol    Meaning
──────    ──────────────────────────────────
─────►    Data flow / dependency (one-way)
◄────►    Bidirectional communication
─ ─ ─►    Async / event-driven flow
══════    System boundary
┌────┐    Component / Module
│    │
└────┘
[    ]    External service (outside system)
{    }    Data store / database
(    )    Protocol / technology label
```

---

## 1. SYSTEM BOUNDARY OVERVIEW

```
╔══════════════════════════════════════════════════════════════════════════╗
║                         SYSTEM BOUNDARY                                 ║
║                                                                         ║
║  ┌────────────────────────────────────────────────────────────────────┐  ║
║  │                    UNITY CLIENT APPLICATION                        │  ║
║  │                    (C#, Unity 2022 LTS, URP)                       │  ║
║  │                                                                    │  ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐ │  ║
║  │  │ Presentation │  │  Game Logic  │  │  Local Data (SO Cache)   │ │  ║
║  │  │    Layer     │  │    Layer     │  │  Card definitions in     │ │  ║
║  │  │              │  │              │  │  build (read-only)       │ │  ║
║  │  └──────────────┘  └──────────────┘  └──────────────────────────┘ │  ║
║  │                                                                    │  ║
║  │  ┌──────────────┐  ┌──────────────────────────────────────────┐   │  ║
║  │  │ Network SDK  │  │  Persistence SDK (Firebase Unity SDK)    │   │  ║
║  │  │ (Photon PUN2)│  │                                          │   │  ║
║  │  └──────┬───────┘  └──────────────┬───────────────────────────┘   │  ║
║  └─────────┼─────────────────────────┼───────────────────────────────┘  ║
║            │                         │                                   ║
╠════════════╪═════════════════════════╪═══════════════════════════════════╣
║            │   EXTERNAL SERVICES     │                                   ║
║            ▼                         ▼                                   ║
║  ┌──────────────────┐    ┌───────────────────────────────────────────┐  ║
║  │  [ Photon Cloud ] │    │  [ Firebase Cloud ]                      │  ║
║  │                   │    │                                           │  ║
║  │  • Relay Server   │    │  • Firebase Auth (email/anonymous)        │  ║
║  │  • Room State     │    │  • Cloud Firestore (player data, history) │  ║
║  │  • Matchmaking    │    │  • Security Rules (per-user access)       │  ║
║  │  • Region Routing │    │  • Free Tier (Spark Plan)                 │  ║
║  │                   │    │                                           │  ║
║  │  (UDP/TCP Relay)  │    │  (HTTPS + gRPC / Firebase SDK)            │  ║
║  └──────────────────┘    └───────────────────────────────────────────┘  ║
║                                                                         ║
╚══════════════════════════════════════════════════════════════════════════╝
```

---

## 2. FULL 5-LAYER ARCHITECTURE

```
╔══════════════════════════════════════════════════════════════════════════╗
║                                                                         ║
║  LAYER 1: PRESENTATION (UI / Visuals)                                   ║
║  ┌──────────────┬───────────────┬────────────┬───────────────────────┐  ║
║  │ UIController │PhaseAnnouncer │ Card Visual│ HellViewer            │  ║
║  │              │               │ (3D board) │ (graveyard browser)   │  ║
║  │ • HUD panels │ • Phase stomp│ • Sprites  │ • Double-click view   │  ║
║  │ • Turn text  │   animation  │ • Highlight│                       │  ║
║  │ • Buttons    │              │ • Drag/Drop│ LobbyUI (NEW)         │  ║
║  │ • Mulligan   │              │ • Flip anim│ • Room list           │  ║
║  │ • Game Over  │              │            │ • Create/Join room    │  ║
║  │ • Discard    │              │            │ • Player ready status  │  ║
║  │ • Card zoom  │              │            │                       │  ║
║  │ • Life reveal│              │            │ ProfileUI (NEW)       │  ║
║  │ • Combat msg │              │            │ • Win/Loss display    │  ║
║  │ • Payment gem│              │            │ • Match history list  │  ║
║  │              │              │            │ • Deck viewer         │  ║
║  └──────┬───────┴───────┬──────┴─────┬──────┴───────────┬───────────┘  ║
║         │ State Updates │ Animations │ Card Events      │ UI Events    ║
║         ▼               ▼            ▼                  ▼              ║
║  LAYER 2: GAME LOGIC (Controllers / FSMs)                               ║
║  ┌──────────────────────────────────────────────────────────────────┐   ║
║  │                                                                  │   ║
║  │  ┌──────────────────────────────────────────────────────┐       │   ║
║  │  │              GameManager (Master FSM)                 │       │   ║
║  │  │  TurnPhase: Draw → Main → Battle → End               │       │   ║
║  │  │  TurnPlayer: Player1 ↔ Player2                        │       │   ║
║  │  │  Win Condition: Deck-out / Sahat + Direct Hit         │       │   ║
║  │  └──────────┬───────────┬──────────────┬────────────────┘       │   ║
║  │             │           │              │                         │   ║
║  │    ┌────────▼──┐  ┌─────▼──────┐  ┌───▼──────────────┐         │   ║
║  │    │ Battle    │  │  Combat    │  │  Magic            │         │   ║
║  │    │ Controller│  │ Controller │  │  Controller       │         │   ║
║  │    │           │  │            │  │                    │         │   ║
║  │    │ Summon FSM│  │ Combat FSM │  │  Magic Play FSM   │         │   ║
║  │    │ Idle      │  │ Idle       │  │  Idle             │         │   ║
║  │    │ →CostStep │  │ →Selecting │  │  →SelectModTarget │         │   ║
║  │    │ →ReadyTo  │  │   Attacker │  │  →AwaitReact      │         │   ║
║  │    │   Place   │  │ →Selecting │  │  →SelectDiscard   │         │   ║
║  │    │           │  │   Target   │  │  →DeckSearch      │         │   ║
║  │    │           │  │ →Resolving │  │                    │         │   ║
║  │    └───────────┘  └────────────┘  └────────────────────┘         │   ║
║  │                                                                  │   ║
║  │    ┌───────────────────────┐   ┌──────────────────────────┐     │   ║
║  │    │ AvatarAbility        │   │  GameplayLogger           │     │   ║
║  │    │ Controller           │   │                            │     │   ║
║  │    │                      │   │  • Turn/Combat/Summon logs │     │   ║
║  │    │ 11+ keyword abilities│   │  • Magic/Draw/Life logs    │     │   ║
║  │    │ Juti, ThonSoop,      │   │  • Event system (OnLog)    │     │   ║
║  │    │ HellScaling, Aura... │   │  • Max 150 entries         │     │   ║
║  │    └───────────────────────┘   └──────────────────────────┘     │   ║
║  │                                                                  │   ║
║  └──────────────────────────┬───────────────────────────────────────┘   ║
║                             │ Card Actions / Player Commands            ║
║                             ▼                                           ║
║  LAYER 3: PLAYER DATA (Per-Player Ownership)                            ║
║  ┌──────────────────────────────────────────────────────────────────┐   ║
║  │                                                                  │   ║
║  │   Player 1 Object                 Player 2 Object               │   ║
║  │   ┌─────────────────────┐        ┌─────────────────────┐       │   ║
║  │   │ playerId: Player1   │        │ playerId: Player2   │       │   ║
║  │   │                     │        │                     │       │   ║
║  │   │ HandController      │        │ HandController      │       │   ║
║  │   │  └ heldCards[]      │        │  └ heldCards[]      │       │   ║
║  │   │                     │        │                     │       │   ║
║  │   │ DeckController      │        │ DeckController      │       │   ║
║  │   │  ├ deckToUse[]      │        │  ├ deckToUse[]      │       │   ║
║  │   │  ├ lifeDeckToUse[]  │        │  ├ lifeDeckToUse[]  │       │   ║
║  │   │  └ activeCards[]    │        │  └ activeCards[]    │       │   ║
║  │   │                     │        │                     │       │   ║
║  │   │ Zones:              │        │ Zones:              │       │   ║
║  │   │  ├ avatarZones[4]   │        │  ├ avatarZones[4]   │       │   ║
║  │   │  ├ lifeZones[5]     │        │  ├ lifeZones[5]     │       │   ║
║  │   │  ├ magicZone        │        │  ├ magicZone        │       │   ║
║  │   │  ├ hellZone         │        │  ├ hellZone         │       │   ║
║  │   │  └ deckZone         │        │  └ deckZone         │       │   ║
║  │   │                     │        │                     │       │   ║
║  │   │ pendingLifeDraws    │        │ pendingLifeDraws    │       │   ║
║  │   └─────────────────────┘        └─────────────────────┘       │   ║
║  │                                                                  │   ║
║  │   Card Instance (MonoBehaviour on each card GameObject)          │   ║
║  │   ┌──────────────────────────────────────────────────────────┐  │   ║
║  │   │ cardOwner, cardType, power, cost, gem, symbol, color     │  │   ║
║  │   │ isTapped, isFaceDown, isSelected, inHand, isLifeCard     │  │   ║
║  │   │ attachedMods[], equippedTo, abilities (flags)             │  │   ║
║  │   │ basePower, hellScalingBonus, auraBonus, appliedEffect     │  │   ║
║  │   └──────────────────────────────────────────────────────────┘  │   ║
║  │                                                                  │   ║
║  └──────────────────────────┬───────────────────────────────────────┘   ║
║                             │ References SO data                        ║
║                             ▼                                           ║
║  LAYER 4: LOCAL DATA (ScriptableObjects — Read-Only Card Database)      ║
║  ┌──────────────────────────────────────────────────────────────────┐   ║
║  │                                                                  │   ║
║  │   BaseCardSO (abstract base)                                     │   ║
║  │   ├── cardName, description, cardCharacterSprite, gem, symbol    │   ║
║  │   │                                                              │   ║
║  │   ├── AvatarCardSO                                               │   ║
║  │   │   ├── avatarColor, gemColor (CardColor)                      │   ║
║  │   │   ├── cost, power                                            │   ║
║  │   │   ├── abilities (AvatarAbility flags)                        │   ║
║  │   │   └── Ability params: jutiSearchPrefix, combatThonSoopMill,  │   ║
║  │   │       hellScalingNamePrefix, auraSymbol, auraPowerBoost...    │   ║
║  │   │                                                              │   ║
║  │   ├── MagicCardSO                                                │   ║
║  │   │   ├── gemColor, magicType (Normal/Mod/React/Land)            │   ║
║  │   │   ├── effect (MagicEffect enum — 15 types)                   │   ║
║  │   │   └── effectValue, targetSymbol, searchName                  │   ║
║  │   │                                                              │   ║
║  │   └── LifeCardSO                                                 │   ║
║  │       ├── flavorText                                             │   ║
║  │       └── onFlipEffect, onFlipValue                              │   ║
║  │                                                                  │   ║
║  │   ✓ Baked into game build at compile time                        │   ║
║  │   ✓ Identical copy on every client                               │   ║
║  │   ✓ No network sync needed — both players read same data         │   ║
║  │   ✓ Acts as READ-ONLY card definition database                   │   ║
║  │                                                                  │   ║
║  └──────────────────────────────────────────────────────────────────┘   ║
║                                                                         ║
║  LAYER 5: EXTERNAL SERVICES (Network + Persistence)                     ║
║  ┌──────────────────────────────────────────────────────────────────┐   ║
║  │                                                                  │   ║
║  │  ┌─ PHOTON PUN2 (Multiplayer) ──────────────────────────────┐   │   ║
║  │  │                                                           │   │   ║
║  │  │  NetworkGameManager ──► NetworkRPCRouter                  │   │   ║
║  │  │  NetworkCardSpawner ──► NetworkDeckController              │   │   ║
║  │  │  LobbyManager      ──► RoomManager                       │   │   ║
║  │  │  ReconnectionHandler                                      │   │   ║
║  │  │                                                           │   │   ║
║  │  │  Protocol: UDP (gameplay) + TCP (room management)         │   │   ║
║  │  │  Communication: Photon RPCs + PhotonView sync             │   │   ║
║  │  └───────────────────────────────────────────────────────────┘   │   ║
║  │                          │                                       │   ║
║  │                          ▼ (Internet)                            │   ║
║  │               [ Photon Cloud Server ]                            │   ║
║  │               • Relay + Matchmaking                              │   ║
║  │               • Region: Asia (Singapore)                         │   ║
║  │                                                                  │   ║
║  │  ┌─ FIREBASE (Persistence) ─────────────────────────────────┐   │   ║
║  │  │                                                           │   │   ║
║  │  │  FirebaseAuthManager (NEW)                                │   │   ║
║  │  │  ├─ Login / Register / Anonymous Auth                     │   │   ║
║  │  │  └─ Returns userId (UID) for Firestore queries            │   │   ║
║  │  │                                                           │   │   ║
║  │  │  FirestoreManager (NEW)                                   │   │   ║
║  │  │  ├─ SavePlayerProfile(userId, profileData)                │   │   ║
║  │  │  ├─ LoadPlayerProfile(userId) → ProfileData               │   │   ║
║  │  │  ├─ SaveMatchResult(matchData)                            │   │   ║
║  │  │  ├─ LoadMatchHistory(userId) → List<MatchRecord>          │   │   ║
║  │  │  ├─ SaveDeckList(userId, deckData)                        │   │   ║
║  │  │  ├─ LoadDeckList(userId) → DeckData                       │   │   ║
║  │  │  └─ UpdateWinLoss(userId, isWin)                          │   │   ║
║  │  │                                                           │   │   ║
║  │  │  Protocol: HTTPS + gRPC (via Firebase Unity SDK)          │   │   ║
║  │  │  Auth: Firebase Auth Token (per-user security rules)      │   │   ║
║  │  └───────────────────────────────────────────────────────────┘   │   ║
║  │                          │                                       │   ║
║  │                          ▼ (Internet)                            │   ║
║  │               [ Firebase Cloud ]                                 │   ║
║  │               • Auth Server                                      │   ║
║  │               • Firestore Database                               │   ║
║  │               • Security Rules Engine                            │   ║
║  │                                                                  │   ║
║  └──────────────────────────────────────────────────────────────────┘   ║
║                                                                         ║
╚══════════════════════════════════════════════════════════════════════════╝
```

---

## 3. DATABASE SCHEMA (Cloud Firestore)

```
Firestore Database Structure
════════════════════════════

Collection: players
└── Document: {userId}  (Firebase Auth UID)
    │
    ├── Fields (PlayerProfile):
    │   ├── displayName     : string     "SiamPlayer01"
    │   ├── email           : string     "player@example.com"
    │   ├── createdAt       : timestamp  2026-03-11T10:00:00Z
    │   ├── lastLoginAt     : timestamp  2026-03-11T18:30:00Z
    │   ├── totalWins       : number     15
    │   ├── totalLosses     : number     8
    │   ├── totalMatches    : number     23
    │   └── winRate         : number     0.652  (computed: wins/matches)
    │
    ├── Sub-collection: matchHistory
    │   └── Document: {matchId}  (auto-generated)
    │       ├── matchId         : string     "match_abc123"
    │       ├── opponentName    : string     "Player2Name"
    │       ├── opponentUserId  : string     "uid_xyz789"
    │       ├── result          : string     "win" | "loss"
    │       ├── winCondition    : string     "direct_hit" | "deck_out"
    │       ├── totalTurns      : number     12
    │       ├── playerLifeLost  : number     3   (LIFE cards flipped)
    │       ├── opponentLifeLost: number     5
    │       ├── deckUsed        : string     "deck_default_01"
    │       ├── playedAt        : timestamp  2026-03-11T19:15:00Z
    │       └── duration        : number     480  (seconds)
    │
    └── Sub-collection: decks
        └── Document: {deckId}  (e.g., "deck_default_01")
            ├── deckName        : string     "Default Deck"
            ├── createdAt       : timestamp  2026-03-11T10:00:00Z
            ├── lastUsedAt      : timestamp  2026-03-11T19:15:00Z
            ├── mainDeck        : array      ["avatar_hanuman", "avatar_rama",
            │                                 "magic_fireball", ...]
            │                                (list of cardId strings, maps to SO)
            ├── lifeDeck        : array      ["life_card_01", "life_card_02",
            │                                 "life_card_03", "life_card_04",
            │                                 "life_card_05"]
            ├── mainDeckCount   : number     40
            └── lifeDeckCount   : number     5


Firestore Index Requirements:
─────────────────────────────
• players/{userId}/matchHistory — ordered by playedAt DESC (for recent history)
• players/{userId}/decks        — ordered by lastUsedAt DESC

Firestore Security Rules:
─────────────────────────
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    // Players can only read/write their own profile
    match /players/{userId} {
      allow read, write: if request.auth != null
                         && request.auth.uid == userId;
    }

    // Players can only access their own match history
    match /players/{userId}/matchHistory/{matchId} {
      allow read: if request.auth != null
                  && request.auth.uid == userId;
      allow create: if request.auth != null
                    && request.auth.uid == userId;
      // No update/delete — match records are immutable
    }

    // Players can only access their own decks
    match /players/{userId}/decks/{deckId} {
      allow read, write: if request.auth != null
                         && request.auth.uid == userId;
    }
  }
}
```

---

## 4. COMPONENT INTERACTION MAP (All Layers)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        COMPONENT INTERACTION MAP                         │
│                                                                          │
│  ┌─────────┐                                                            │
│  │  USER   │                                                            │
│  │ (Human) │                                                            │
│  └────┬────┘                                                            │
│       │ Click / Drag / Touch                                            │
│       ▼                                                                  │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ SCENE: Login                                                    │    │
│  │                                                                 │    │
│  │  LoginUI ──────► FirebaseAuthManager ──────► [Firebase Auth]    │    │
│  │                    │                            │               │    │
│  │                    │ userId (UID)                │ Auth Token    │    │
│  │                    ▼                            ▼               │    │
│  │               FirestoreManager ◄──── [Cloud Firestore]          │    │
│  │                    │                                            │    │
│  │                    │ Load PlayerProfile + DeckList               │    │
│  │                    ▼                                            │    │
│  │               ProfileUI (show stats, match history)             │    │
│  └────────────────────┬────────────────────────────────────────────┘    │
│                       │ "Play" button                                    │
│                       ▼                                                  │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ SCENE: Lobby                                                    │    │
│  │                                                                 │    │
│  │  LobbyUI ──────► LobbyManager ──────► PhotonNetwork.Connect()  │    │
│  │                       │                      │                  │    │
│  │                       │ CreateRoom / JoinRoom │                  │    │
│  │                       ▼                      ▼                  │    │
│  │               RoomManager ◄──────── [Photon Cloud]              │    │
│  │                    │                                            │    │
│  │                    │ Both players ready                          │    │
│  │                    │ PhotonNetwork.LoadLevel("Battle")           │    │
│  │                    ▼                                            │    │
│  └────────────────────┬────────────────────────────────────────────┘    │
│                       │ Scene transition (synced)                        │
│                       ▼                                                  │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ SCENE: Battle                                                   │    │
│  │                                                                 │    │
│  │  Player Input (Click/Drag on Card)                              │    │
│  │       │                                                         │    │
│  │       ▼                                                         │    │
│  │  Card.OnPointerDown() ── routes by phase + card type ──┐       │    │
│  │       │                    │                   │        │       │    │
│  │       ▼                    ▼                   ▼        ▼       │    │
│  │  BattleController   CombatController   MagicController  │       │    │
│  │  (Summon avatar)    (Select attacker/  (Play magic      │       │    │
│  │                      target, resolve)   card, effects)  │       │    │
│  │       │                    │                   │        │       │    │
│  │       └────────────────────┴───────────────────┘        │       │    │
│  │                            │                            │       │    │
│  │                            ▼                            │       │    │
│  │                   AvatarAbilityController               │       │    │
│  │                   (trigger keyword abilities)           │       │    │
│  │                            │                            │       │    │
│  │                            ▼                            │       │    │
│  │  ┌─────────────── GameManager ──────────────────┐      │       │    │
│  │  │ • Advance phase  (Draw→Main→Battle→End)      │      │       │    │
│  │  │ • Switch turn    (P1 ↔ P2)                   │      │       │    │
│  │  │ • Check win condition                        │      │       │    │
│  │  └──────────┬──────────────┬────────────────────┘      │       │    │
│  │             │              │                            │       │    │
│  │             ▼              ▼                            │       │    │
│  │       UIController    GameplayLogger                    │       │    │
│  │       (update HUD)    (log events)                      │       │    │
│  │                                                         │       │    │
│  │  ═══ NETWORK SYNC (running in parallel) ═══════════    │       │    │
│  │                                                         │       │    │
│  │  Local Action ──► NetworkRPCRouter ──► [Photon Cloud]  │       │    │
│  │                        │                    │           │       │    │
│  │                        │ RPC broadcast      │           │       │    │
│  │                        ▼                    ▼           │       │    │
│  │               Remote Client receives & applies          │       │    │
│  │                                                         │       │    │
│  │  ═══ ON GAME OVER ════════════════════════════════     │       │    │
│  │                                                         │       │    │
│  │  GameManager.EndGame()                                  │       │    │
│  │       │                                                 │       │    │
│  │       ├──► UIController.ShowGameOver()                   │       │    │
│  │       │                                                 │       │    │
│  │       └──► FirestoreManager.SaveMatchResult()           │       │    │
│  │            FirestoreManager.UpdateWinLoss()             │       │    │
│  │                    │                                    │       │    │
│  │                    ▼                                    │       │    │
│  │             [Cloud Firestore]                           │       │    │
│  │             (persist match record + update stats)       │       │    │
│  │                                                         │       │    │
│  └─────────────────────────────────────────────────────────┘       │    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 5. DATA FLOW DIAGRAM (All Data Paths)

```
┌──────────────────────────────────────────────────────────────────────┐
│                       DATA FLOW DIAGRAM                               │
│                                                                       │
│  ╔═══════════════════╗                                                │
│  ║ ScriptableObjects ║                                                │
│  ║ (Card Definitions)║                                                │
│  ╚════════╤══════════╝                                                │
│           │                                                           │
│           │ (1) Compile-time bake                                     │
│           │     Read-only card stats                                  │
│           ▼                                                           │
│  ┌────────────────┐    (2) Copy SO data     ┌──────────────────────┐ │
│  │ DeckController │ ──────────────────────► │ Card (runtime inst.) │ │
│  │ (per player)   │    at card spawn time    │ power, cost, gem,    │ │
│  │                │                          │ abilities, effects   │ │
│  │ activeCards[]  │                          └──────────┬───────────┘ │
│  │ (shuffled SO   │                                     │             │
│  │  references)   │                                     │             │
│  └────────────────┘                          (3) Runtime mutations   │
│                                              (buffs, mods, tap,      │
│                                               flip, damage)          │
│                                                         │             │
│                                                         ▼             │
│                                              ┌──────────────────────┐ │
│                                              │ Board State (RAM)    │ │
│                                              │ • Zone occupancy     │ │
│                                              │ • Attached mods      │ │
│                                              │ • Temp/Land buffs    │ │
│                                              │ • LIFE flip state    │ │
│                                              │ • Sahat status       │ │
│                                              └──────────┬───────────┘ │
│                                                         │             │
│                              ┌───────────────────────────┤             │
│                              │                           │             │
│                   (4) Sync via Photon         (5) On game end         │
│                       RPCs (real-time)             save to DB          │
│                              │                           │             │
│                              ▼                           ▼             │
│                   ┌──────────────────┐     ┌────────────────────────┐ │
│                   │ [Photon Cloud]   │     │ [Cloud Firestore]      │ │
│                   │                  │     │                        │ │
│                   │ Relays to other  │     │ players/{uid}          │ │
│                   │ client in room   │     │   ├ profile (W/L)     │ │
│                   │                  │     │   ├ matchHistory/      │ │
│                   │ Synced data:     │     │   └ decks/             │ │
│                   │ • Card positions │     │                        │ │
│                   │ • Phase/turn     │     │ Written:               │ │
│                   │ • Combat results │     │ • Match end only       │ │
│                   │ • Summon events  │     │ • Win/loss update      │ │
│                   │ • Hand count     │     │ • Match record create  │ │
│                   │ (NOT hand cards) │     │                        │ │
│                   └──────────────────┘     └────────────────────────┘ │
│                                                                       │
│  Data Flow Summary:                                                   │
│  ─────────────────                                                    │
│  (1) SO → Build         : compile-time, read-only, local             │
│  (2) SO → Card Instance : runtime spawn, one-time copy               │
│  (3) Card Mutations     : in-memory only, per-match                  │
│  (4) Board → Photon     : real-time RPCs, UDP/TCP relay              │
│  (5) Result → Firestore : HTTPS, at match end only, persistent       │
│                                                                       │
│  Read Flow:                                                           │
│  ──────────                                                           │
│  (6) Firestore → Client : on login, load profile + decks + history   │
│  (7) Firestore → Client : on lobby, load deck for match setup        │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 6. INFRASTRUCTURE / DEPLOYMENT DIAGRAM

```
┌──────────────────────────────────────────────────────────────────────┐
│                   DEPLOYMENT ARCHITECTURE                             │
│                                                                       │
│                                                                       │
│   ┌──────────────────┐              ┌──────────────────┐             │
│   │  CLIENT A (PC)   │              │  CLIENT B (PC)   │             │
│   │  Windows 10/11   │              │  Windows 10/11   │             │
│   │                  │              │                  │             │
│   │  Unity Runtime   │              │  Unity Runtime   │             │
│   │  (IL2CPP / Mono) │              │  (IL2CPP / Mono) │             │
│   │                  │              │                  │             │
│   │  Photon PUN2 SDK │              │  Photon PUN2 SDK │             │
│   │  Firebase SDK    │              │  Firebase SDK    │             │
│   └────────┬─────────┘              └────────┬─────────┘             │
│            │                                  │                       │
│            │  UDP/TCP                         │  UDP/TCP              │
│            │  (gameplay)                      │  (gameplay)           │
│            │                                  │                       │
│            ▼                                  ▼                       │
│   ┌────────────────────────────────────────────────────┐             │
│   │              PHOTON CLOUD                          │             │
│   │              (Managed Service)                     │             │
│   │                                                    │             │
│   │  Region: Asia (Singapore) ◄── nearest to Thailand  │             │
│   │                                                    │             │
│   │  ┌─────────────┐  ┌─────────────┐  ┌───────────┐ │             │
│   │  │ Name Server │  │ Master      │  │ Game      │ │             │
│   │  │ (region     │  │ Server      │  │ Server    │ │             │
│   │  │  selection) │  │ (lobby,     │  │ (room     │ │             │
│   │  │             │  │  room list) │  │  relay)   │ │             │
│   │  └─────────────┘  └─────────────┘  └───────────┘ │             │
│   │                                                    │             │
│   │  Free Plan: 20 CCU (concurrent users)              │             │
│   │  Paid Plan: 100-500+ CCU                           │             │
│   └────────────────────────────────────────────────────┘             │
│                                                                       │
│            │                                  │                       │
│            │  HTTPS / gRPC                    │  HTTPS / gRPC        │
│            │  (persistence)                   │  (persistence)       │
│            ▼                                  ▼                       │
│   ┌────────────────────────────────────────────────────┐             │
│   │              FIREBASE CLOUD                        │             │
│   │              (Google Cloud Platform)                │             │
│   │                                                    │             │
│   │  ┌──────────────┐  ┌──────────────────────────┐   │             │
│   │  │ Firebase     │  │ Cloud Firestore          │   │             │
│   │  │ Auth         │  │                          │   │             │
│   │  │              │  │  players/                │   │             │
│   │  │ • Email/Pass │  │  ├── {userId}            │   │             │
│   │  │ • Anonymous  │  │  │   ├── profile fields  │   │             │
│   │  │              │  │  │   ├── matchHistory/    │   │             │
│   │  │ Returns:     │  │  │   └── decks/           │   │             │
│   │  │ userId (UID) │  │  └── ...                  │   │             │
│   │  │ Auth Token   │  │                          │   │             │
│   │  │              │  │  Security Rules enforce   │   │             │
│   │  │              │  │  per-user data isolation  │   │             │
│   │  └──────────────┘  └──────────────────────────┘   │             │
│   │                                                    │             │
│   │  Spark Plan (Free): 1GB storage, 50K reads/day     │             │
│   │  Blaze Plan (Pay): Unlimited scale                 │             │
│   └────────────────────────────────────────────────────┘             │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 7. AUTHENTICATION & SECURITY FLOW

```
┌──────────────────────────────────────────────────────────────────────┐
│                     SECURITY ARCHITECTURE                             │
│                                                                       │
│  1. AUTHENTICATION FLOW                                               │
│  ───────────────────                                                  │
│                                                                       │
│  User ──► LoginUI ──► FirebaseAuthManager                             │
│                            │                                          │
│                            ├─ CreateAccount(email, password)          │
│                            │   └─► Firebase Auth ──► returns UID      │
│                            │                                          │
│                            ├─ SignIn(email, password)                  │
│                            │   └─► Firebase Auth ──► returns UID      │
│                            │                  + Auth Token (JWT)       │
│                            │                                          │
│                            └─ SignInAnonymously()                     │
│                                └─► Firebase Auth ──► returns temp UID │
│                                                                       │
│  Auth Token (JWT) ──► attached to all Firestore requests              │
│                  ──► Firestore Security Rules validate UID match      │
│                                                                       │
│                                                                       │
│  2. DATA ACCESS SECURITY                                              │
│  ────────────────────                                                 │
│                                                                       │
│  ┌──────────────────────────────────────────────────────────┐        │
│  │ Firestore Security Rules                                  │        │
│  │                                                           │        │
│  │  READ own profile   ✓  (auth.uid == document userId)      │        │
│  │  WRITE own profile  ✓  (auth.uid == document userId)      │        │
│  │  READ other profile ✗  (blocked by rule)                  │        │
│  │  DELETE match record✗  (no delete rule — immutable)       │        │
│  │  Unauthenticated    ✗  (all rules require auth != null)   │        │
│  └──────────────────────────────────────────────────────────┘        │
│                                                                       │
│                                                                       │
│  3. GAME STATE SECURITY (Anti-Cheat)                                  │
│  ───────────────────────────────────                                  │
│                                                                       │
│  ┌──────────────────────────────────────────────────────────┐        │
│  │ MasterClient Authority Model                              │        │
│  │                                                           │        │
│  │  Client sends REQUEST ──► Host VALIDATES ──► Host APPLIES │        │
│  │                                                           │        │
│  │  Validated:                                               │        │
│  │  ✓ Is it your turn?                                       │        │
│  │  ✓ Is it the correct phase?                               │        │
│  │  ✓ Do you own this card?                                  │        │
│  │  ✓ Is tribute color valid?                                │        │
│  │  ✓ Are gems sufficient?                                   │        │
│  │  ✓ Is the target slot empty?                              │        │
│  │                                                           │        │
│  │  If invalid → RPC_ActionRejected (back to sender only)   │        │
│  │  If valid   → RPC_SyncAction     (broadcast to all)      │        │
│  └──────────────────────────────────────────────────────────┘        │
│                                                                       │
│                                                                       │
│  4. INFORMATION HIDING (Hand/Deck Privacy)                            │
│  ─────────────────────────────────────────                            │
│                                                                       │
│  ┌──────────────────────────────────────────────────────────┐        │
│  │                                                           │        │
│  │  YOUR client sees:        OPPONENT client sees:           │        │
│  │  ✓ Your hand cards        ✗ Opponent hand (count only)    │        │
│  │  ✓ Your deck contents     ✗ Opponent deck (count only)    │        │
│  │  ✓ Your LIFE face-down    ✗ Opponent LIFE (back only)     │        │
│  │                                                           │        │
│  │  BOTH clients see:                                        │        │
│  │  ✓ All board avatars (power, tapped, mods)                │        │
│  │  ✓ All flipped LIFE cards                                 │        │
│  │  ✓ Hell zone (all destroyed cards)                        │        │
│  │  ✓ Magic zone (React, Land, Modification cards)           │        │
│  │  ✓ Phase, turn, Sahat status                              │        │
│  │                                                           │        │
│  └──────────────────────────────────────────────────────────┘        │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 8. ERROR HANDLING & RECOVERY

```
┌──────────────────────────────────────────────────────────────────────┐
│                   ERROR HANDLING & RECOVERY                           │
│                                                                       │
│  SCENARIO                    │ DETECTION           │ RECOVERY         │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  1. Player disconnects       │ Photon callback:    │ • Pause game     │
│     mid-match                │ OnPlayerLeft-       │ • 60s reconnect  │
│                              │ Room()              │   timer          │
│                              │                     │ • If timeout:    │
│                              │                     │   award win to   │
│                              │                     │   remaining      │
│                              │                     │   player         │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  2. Player reconnects        │ Photon callback:    │ • Send full      │
│                              │ OnPlayerEnteredRoom │   state snapshot │
│                              │ + rejoin token      │ • Resume game    │
│                              │                     │ • Cancel timer   │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  3. MasterClient             │ Photon auto-assigns │ • New host       │
│     disconnects              │ new MasterClient    │   receives state │
│     (host migration)         │ OnMasterClient-     │   from Photon    │
│                              │ Switched()          │   room props     │
│                              │                     │ • Game continues │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  4. Firebase write fails     │ Task.IsFaulted      │ • Retry 3 times  │
│     (save match result)      │ (async callback)    │   with backoff   │
│                              │                     │ • Cache locally  │
│                              │                     │   (PlayerPrefs)  │
│                              │                     │ • Sync on next   │
│                              │                     │   successful     │
│                              │                     │   connection     │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  5. Firebase auth expired    │ Firebase SDK auto-  │ • SDK refreshes  │
│     (token timeout)          │ refresh on most     │   token silently │
│                              │ calls               │ • If fail: show  │
│                              │                     │   re-login UI    │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  6. Invalid game action      │ MasterClient        │ • RPC_Action-    │
│     (cheat attempt or        │ validation fails    │   Rejected()     │
│      desync)                 │                     │ • Log warning    │
│                              │                     │ • No state       │
│                              │                     │   change         │
│  ────────────────────────────│─────────────────────│─────────────────│
│                              │                     │                  │
│  7. Network latency          │ Photon ping >       │ • Show latency   │
│     (high ping)              │ 300ms               │   indicator      │
│                              │                     │ • Buffer inputs  │
│                              │                     │ • Turn-based =   │
│                              │                     │   latency is     │
│                              │                     │   tolerable      │
│                              │                     │                  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 9. SCALABILITY CONSIDERATIONS

```
┌──────────────────────────────────────────────────────────────────────┐
│                   SCALABILITY ARCHITECTURE                            │
│                                                                       │
│  COMPONENT        │ FREE TIER LIMIT      │ SCALE PATH                │
│  ─────────────────│──────────────────────│────────────────────────── │
│                   │                      │                            │
│  Photon Cloud     │ 20 CCU               │ Upgrade plan: 100/500 CCU │
│                   │ (10 rooms × 2        │ Multi-region (Asia, US,   │
│                   │  players)            │ EU) for global players     │
│                   │                      │                            │
│  Firebase Auth    │ Unlimited free       │ No scaling needed          │
│                   │ (email/anonymous)    │                            │
│                   │                      │                            │
│  Firestore        │ 1 GB storage         │ Blaze plan: pay-as-you-go │
│                   │ 50K reads/day        │ Auto-scales reads/writes   │
│                   │ 20K writes/day       │ Add indexes for queries    │
│                   │ 20K deletes/day      │                            │
│                   │                      │                            │
│  Unity Client     │ No limit             │ Build for multiple         │
│                   │ (local app)          │ platforms (PC, Mac,        │
│                   │                      │ Mobile future)             │
│                   │                      │                            │
│  ─────────────────│──────────────────────│────────────────────────── │
│                   │                      │                            │
│  DATA VOLUME ESTIMATE (per match):                                    │
│  • 1 match record  ≈ 500 bytes                                       │
│  • 1 player profile ≈ 200 bytes                                      │
│  • 1 deck list      ≈ 1 KB                                           │
│  • 100 players × 50 matches each = 2.5 MB (well within 1 GB)        │
│                                                                       │
│  QUERY COST ESTIMATE (per session):                                   │
│  • Login: 1 read (profile) + 1 read (deck) + 10 reads (history)     │
│  • Per match end: 1 write (match) + 1 write (profile update)        │
│  • 100 daily active users × 5 matches = ~1,600 reads + 1,000 writes │
│  • Well within 50K reads / 20K writes free tier                      │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 10. TECHNOLOGIES & PROTOCOLS SUMMARY

```
┌──────────────────────────────────────────────────────────────────────┐
│                TECHNOLOGIES & PROTOCOLS                                │
│                                                                       │
│  LAYER              │ TECHNOLOGY          │ PROTOCOL / FORMAT          │
│  ───────────────────│─────────────────────│──────────────────────────│
│  Game Engine        │ Unity 2022 LTS      │ C# (Mono / IL2CPP)       │
│  Rendering          │ URP (Universal RP)  │ GPU shaders               │
│  UI Framework       │ Unity UI (Canvas)   │ EventSystem raycasts      │
│  Card Data          │ ScriptableObjects   │ Unity serialized assets   │
│  Text Rendering     │ TextMeshPro         │ Thai font (TH Sarabun)   │
│  ───────────────────│─────────────────────│──────────────────────────│
│  Multiplayer SDK    │ Photon PUN2         │ UDP (gameplay relay)      │
│                     │                     │ TCP (room management)     │
│  Real-time Sync     │ PhotonView + RPC    │ Serialized RPC params    │
│  Card Position Sync │ PhotonTransformView │ Position + Rotation      │
│  Matchmaking        │ Photon Lobby API    │ Room properties filter   │
│  ───────────────────│─────────────────────│──────────────────────────│
│  Authentication     │ Firebase Auth       │ HTTPS + JWT tokens       │
│  Database           │ Cloud Firestore     │ HTTPS + gRPC             │
│  DB SDK             │ Firebase Unity SDK  │ Async Task<T> callbacks  │
│  Security           │ Firestore Rules     │ CEL (Common Expression)  │
│  ───────────────────│─────────────────────│──────────────────────────│
│  Local Fallback     │ Unity PlayerPrefs   │ Key-value (string/int)   │
│  (offline cache)    │                     │ For failed DB writes     │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 11. NEW SCRIPTS NEEDED (Firebase + Photon)

```
┌──────────────────────────────────────────────────────────────────────┐
│              NEW SCRIPTS TO BE CREATED                                │
│                                                                       │
│  FIREBASE LAYER (Persistence):                                        │
│  ──────────────────────────────                                       │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ FirebaseAuthManager.cs                                          │ │
│  │ • Initialize Firebase on app start                              │ │
│  │ • CreateAccount(email, password) → Task<FirebaseUser>           │ │
│  │ • SignIn(email, password) → Task<FirebaseUser>                  │ │
│  │ • SignInAnonymously() → Task<FirebaseUser>                      │ │
│  │ • SignOut()                                                      │ │
│  │ • CurrentUser property (cached UID)                             │ │
│  │ • OnAuthStateChanged event                                      │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ FirestoreManager.cs                                             │ │
│  │ • SavePlayerProfile(userId, PlayerProfile)                      │ │
│  │ • LoadPlayerProfile(userId) → Task<PlayerProfile>               │ │
│  │ • UpdateWinLoss(userId, isWin) → Task                           │ │
│  │ • SaveMatchResult(userId, MatchRecord) → Task                   │ │
│  │ • LoadMatchHistory(userId, limit) → Task<List<MatchRecord>>     │ │
│  │ • SaveDeckList(userId, DeckData) → Task                         │ │
│  │ • LoadDeckList(userId, deckId) → Task<DeckData>                 │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ Data Models (C# classes):                                       │ │
│  │ • PlayerProfile { displayName, totalWins, totalLosses, ... }    │ │
│  │ • MatchRecord { matchId, opponentName, result, turns, ... }     │ │
│  │ • DeckData { deckName, mainDeckCardIds[], lifeDeckCardIds[] }   │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  PHOTON LAYER (Multiplayer):                                          │
│  ────────────────────────────                                         │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ NetworkGameManager.cs     — PhotonView, master state sync       │ │
│  │ NetworkRPCRouter.cs       — Central RPC send/receive            │ │
│  │ NetworkCardSpawner.cs     — PhotonNetwork.Instantiate() cards   │ │
│  │ NetworkDeckController.cs  — Secure shuffle (seed sync)          │ │
│  │ LobbyManager.cs          — Room create/join/matchmaking         │ │
│  │ RoomManager.cs            — Player ready, scene load, disconnect│ │
│  │ ReconnectionHandler.cs    — State snapshot, rejoin, catch-up    │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  NEW SCENES:                                                          │
│  ───────────                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ Login.unity   — Firebase auth UI (login/register/anonymous)     │ │
│  │ Lobby.unity   — Photon room browser, create/join, deck select   │ │
│  │ Profile.unity — Player stats, match history, deck viewer        │ │
│  │                 (OR integrated into Lobby as a panel)            │ │
│  │ Battle.unity  — Existing (modified for networked spawning)      │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  SCENE FLOW:                                                          │
│  ───────────                                                          │
│  Login ──► Lobby/Profile ──► Battle ──► (save result) ──► Lobby      │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 12. COMPLETE SYSTEM ARCHITECTURE (Single Diagram for Report)

```
╔══════════════════════════════════════════════════════════════════════════════════════╗
║                                                                                     ║
║                    CARD BATTLE GAME — COMPLETE SYSTEM ARCHITECTURE                   ║
║                                                                                     ║
║  ┌─── USER ────────────────────────────────────────────────────────────────────────┐║
║  │  Player A (PC)                                    Player B (PC)                 │║
║  └──────┬──────────────────────────────────────────────────┬───────────────────────┘║
║         │                                                  │                        ║
║  ═══════╪══════════════════════════════════════════════════╪════════════════════════ ║
║         │            UNITY CLIENT APPLICATION              │                        ║
║  ═══════╪══════════════════════════════════════════════════╪════════════════════════ ║
║         │                                                  │                        ║
║         ▼                                                  ▼                        ║
║  ┌─ L1: PRESENTATION ──────────────────────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  LoginUI ──► LobbyUI ──► ProfileUI ──► BattleUI (UIController)              │   ║
║  │                                        PhaseAnnouncer │ CardVisuals          │   ║
║  │                                        HellViewer │ Card Preview             │   ║
║  └──────────────────────────────────┬───────────────────────────────────────────┘   ║
║                                     │                                               ║
║  ┌─ L2: GAME LOGIC ────────────────▼───────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  GameManager ─── BattleController ─── CombatController ─── MagicController  │   ║
║  │  (Phase FSM)     (Summon FSM)         (Combat FSM)         (Magic FSM)      │   ║
║  │       │                                                                      │   ║
║  │       ├── AvatarAbilityController (11+ keyword abilities)                    │   ║
║  │       └── GameplayLogger (event tracking)                                    │   ║
║  └──────────────────────────────────┬───────────────────────────────────────────┘   ║
║                                     │                                               ║
║  ┌─ L3: PLAYER DATA ──────────────▼───────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  Player 1                          Player 2                                  │   ║
║  │  ├─ HandController                 ├─ HandController                         │   ║
║  │  ├─ DeckController                 ├─ DeckController                         │   ║
║  │  ├─ AvatarZones[4]                 ├─ AvatarZones[4]                         │   ║
║  │  ├─ LifeZones[5]                   ├─ LifeZones[5]                           │   ║
║  │  ├─ MagicZone                      ├─ MagicZone                              │   ║
║  │  └─ HellZone                       └─ HellZone                               │   ║
║  │                                                                              │   ║
║  │  Card (MonoBehaviour) — runtime instance per card on board/hand              │   ║
║  └──────────────────────────────────┬───────────────────────────────────────────┘   ║
║                                     │                                               ║
║  ┌─ L4: LOCAL DATA ───────────────▼───────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  ScriptableObjects (Read-Only Card Database — baked into build)              │   ║
║  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                       │   ║
║  │  │ AvatarCardSO │  │ MagicCardSO  │  │ LifeCardSO   │                       │   ║
║  │  │ cost, power, │  │ magicType,   │  │ onFlipEffect,│                       │   ║
║  │  │ gem, color,  │  │ effect,      │  │ onFlipValue, │                       │   ║
║  │  │ symbol,      │  │ effectValue, │  │ flavorText   │                       │   ║
║  │  │ abilities    │  │ targetSymbol │  │              │                       │   ║
║  │  └──────────────┘  └──────────────┘  └──────────────┘                       │   ║
║  └──────────────────────────────────────────────────────────────────────────────┘   ║
║                                                                                     ║
║  ═══════════════════════════════════════════════════════════════════════════════════ ║
║                           EXTERNAL SERVICES                                         ║
║  ═══════════════════════════════════════════════════════════════════════════════════ ║
║                                                                                     ║
║  ┌─ L5a: NETWORK (Photon PUN2) ───────────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  Client Scripts:                                                             │   ║
║  │  NetworkGameManager │ NetworkRPCRouter │ LobbyManager │ RoomManager          │   ║
║  │  NetworkCardSpawner │ NetworkDeckController │ ReconnectionHandler             │   ║
║  │                                                                              │   ║
║  │  ┌────────────────────────────────────────────────────────────────────────┐  │   ║
║  │  │  Client A ◄────── UDP/TCP ──────► [PHOTON CLOUD] ◄── UDP/TCP ──► Client B│ │   ║
║  │  │                                  │ Relay Server  │                       │  │   ║
║  │  │  RPCs: RequestSummon,            │ Matchmaking   │                       │  │   ║
║  │  │        RequestAttack,            │ Room State    │                       │  │   ║
║  │  │        RequestMagic,             │ Region: Asia  │                       │  │   ║
║  │  │        SyncPhase, SyncResult     │ 20 CCU free   │                       │  │   ║
║  │  └────────────────────────────────────────────────────────────────────────┘  │   ║
║  └──────────────────────────────────────────────────────────────────────────────┘   ║
║                                                                                     ║
║  ┌─ L5b: PERSISTENCE (Firebase) ──────────────────────────────────────────────┐   ║
║  │                                                                              │   ║
║  │  Client Scripts:                                                             │   ║
║  │  FirebaseAuthManager │ FirestoreManager │ Data Models                        │   ║
║  │                                                                              │   ║
║  │  ┌────────────────────────────────────────────────────────────────────────┐  │   ║
║  │  │  Client ◄──── HTTPS/gRPC ────► [FIREBASE CLOUD]                      │  │   ║
║  │  │                                │                                      │  │   ║
║  │  │  Auth: email/password ─────────┤  Firebase Auth (JWT tokens)          │  │   ║
║  │  │                                │                                      │  │   ║
║  │  │  Read/Write: ──────────────────┤  Cloud Firestore                     │  │   ║
║  │  │    PlayerProfile               │  ┌─ players/{uid} ─────────────┐    │  │   ║
║  │  │    MatchRecord                 │  │  displayName, wins, losses  │    │  │   ║
║  │  │    DeckData                    │  │  ├─ matchHistory/{matchId}  │    │  │   ║
║  │  │                                │  │  │   result, turns, date   │    │  │   ║
║  │  │                                │  │  └─ decks/{deckId}         │    │  │   ║
║  │  │                                │  │      cardIds[], lifeDeck[] │    │  │   ║
║  │  │                                │  └────────────────────────────┘    │  │   ║
║  │  │                                │                                      │  │   ║
║  │  │                                │  Security Rules: per-user isolation  │  │   ║
║  │  │                                │  Free tier: 1GB, 50K reads/day      │  │   ║
║  │  └────────────────────────────────────────────────────────────────────────┘  │   ║
║  └──────────────────────────────────────────────────────────────────────────────┘   ║
║                                                                                     ║
╚══════════════════════════════════════════════════════════════════════════════════════╝
```

---

## 13. CONTROLLER-TO-LAYER MAPPING TABLE

```
┌────────────────────────────┬──────────┬──────────────────────────────────────┐
│  Component                 │  Layer   │  Role                                │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  UIController              │  L1      │  All HUD panels, buttons, text       │
│  PhaseAnnouncer            │  L1      │  Phase transition animation          │
│  Card Visuals (sprites)    │  L1      │  3D card rendering, drag/drop        │
│  LoginUI / LobbyUI (NEW)  │  L1      │  Auth forms, room browser            │
│  ProfileUI (NEW)           │  L1      │  Stats display, history list         │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  GameManager               │  L2      │  Master FSM (phase/turn control)     │
│  BattleController          │  L2      │  Summon FSM (cost→place)             │
│  CombatController          │  L2      │  Combat FSM (select→resolve)         │
│  MagicController           │  L2      │  Magic routing (20+ effects)         │
│  AvatarAbilityController   │  L2      │  Keyword ability resolver            │
│  GameplayLogger            │  L2      │  Event logging (150 entries)         │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  Player                    │  L3      │  Per-player zones, hand, deck refs   │
│  HandController            │  L3      │  Hand card list, layout              │
│  DeckController            │  L3      │  Deck management, draw, shuffle      │
│  Card (MonoBehaviour)      │  L3      │  Runtime card instance data          │
│  CardPlacePoint            │  L3      │  Board zone slot reference           │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  AvatarCardSO              │  L4      │  Avatar card definitions             │
│  MagicCardSO               │  L4      │  Magic card definitions              │
│  LifeCardSO                │  L4      │  LIFE card definitions               │
│  BaseCardSO                │  L4      │  Shared card base class              │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  NetworkGameManager (NEW)  │  L5a     │  Photon master state sync            │
│  NetworkRPCRouter (NEW)    │  L5a     │  Central RPC hub                     │
│  LobbyManager (NEW)        │  L5a     │  Room create/join/matchmake          │
│  RoomManager (NEW)         │  L5a     │  Player ready, scene load            │
│  ReconnectionHandler (NEW) │  L5a     │  Disconnect recovery                 │
├────────────────────────────┼──────────┼──────────────────────────────────────┤
│  FirebaseAuthManager (NEW) │  L5b     │  Login, register, auth state         │
│  FirestoreManager (NEW)    │  L5b     │  CRUD: profile, history, decks       │
│  PlayerProfile (NEW)       │  L5b     │  Data model: player stats            │
│  MatchRecord (NEW)         │  L5b     │  Data model: match result            │
│  DeckData (NEW)            │  L5b     │  Data model: saved deck list         │
└────────────────────────────┴──────────┴──────────────────────────────────────┘
```

---

## 14. DECK SYSTEM: SO ↔ DATABASE BRIDGE

```
┌──────────────────────────────────────────────────────────────────────┐
│                DECK: HOW SO AND DATABASE CONNECT                      │
│                                                                       │
│  Each SO has a unique cardId (e.g., SO asset filename):               │
│  • "avatar_hanuman"     → AvatarCardSO asset                         │
│  • "avatar_rama"        → AvatarCardSO asset                         │
│  • "magic_fireball"     → MagicCardSO asset                          │
│  • "life_blessing_01"   → LifeCardSO asset                           │
│                                                                       │
│  Database stores ONLY the cardId strings:                             │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ Firestore: decks/{deckId}                                       │ │
│  │                                                                  │ │
│  │ mainDeck: ["avatar_hanuman", "avatar_rama", "magic_fireball",   │ │
│  │            "magic_heal", "avatar_narai", ...]                    │ │
│  │                                                                  │ │
│  │ lifeDeck: ["life_blessing_01", "life_curse_02",                  │ │
│  │            "life_shield_03", "life_draw_04", "life_trap_05"]     │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  At match start, client resolves IDs → SO references:                 │
│                                                                       │
│  Firestore cardId ──► CardDatabase.Lookup(cardId) ──► BaseCardSO     │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │ CardDatabase.cs (NEW — SO lookup registry)                      │ │
│  │                                                                  │ │
│  │ // Pre-loaded dictionary of all card SOs by cardId               │ │
│  │ Dictionary<string, BaseCardSO> allCards;                         │ │
│  │                                                                  │ │
│  │ // Populated at game start from Resources or Addressables        │ │
│  │ void Initialize() {                                              │ │
│  │     var avatars = Resources.LoadAll<AvatarCardSO>("Cards/Avatar");│ │
│  │     var magics  = Resources.LoadAll<MagicCardSO>("Cards/Magic"); │ │
│  │     var lifes   = Resources.LoadAll<LifeCardSO>("Cards/Life");   │ │
│  │     // Map each SO's cardName → SO reference                     │ │
│  │ }                                                                │ │
│  │                                                                  │ │
│  │ BaseCardSO Lookup(string cardId) => allCards[cardId];            │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  FLOW:                                                                │
│  ─────                                                                │
│  1. Player logs in → Firebase loads DeckData (cardId strings)         │
│  2. Player enters match → CardDatabase resolves IDs to SOs            │
│  3. DeckController.deckToUse = resolved List<BaseCardSO>              │
│  4. Game proceeds as normal (SO data drives all gameplay)              │
│                                                                       │
│  WHY THIS WORKS:                                                      │
│  ──────────────                                                       │
│  • SO contains ALL card stats (read-only, baked in build)             │
│  • Database stores ONLY which cards are in the deck (IDs)             │
│  • No card stats in database = no stat tampering possible             │
│  • Adding new cards = add new SO assets + deploy game update          │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 15. FULL SESSION LIFECYCLE (End-to-End Flow)

```
┌──────────────────────────────────────────────────────────────────────┐
│                    FULL SESSION LIFECYCLE                              │
│                                                                       │
│  ┌─ PHASE 1: AUTHENTICATION ─────────────────────────────────────┐  │
│  │                                                                │  │
│  │  App Launch → Login.unity scene                                │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  FirebaseAuthManager.SignIn(email, pass)                        │  │
│  │       │                                                        │  │
│  │       ▼ (HTTPS → Firebase Auth)                                │  │
│  │  Returns: userId (UID) + Auth Token (JWT)                      │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  FirestoreManager.LoadPlayerProfile(userId)                     │  │
│  │  FirestoreManager.LoadDeckList(userId, "deck_default_01")      │  │
│  │       │                                                        │  │
│  │       ▼ (HTTPS → Cloud Firestore)                              │  │
│  │  Returns: PlayerProfile + DeckData                              │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  Navigate to Lobby.unity                                        │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─ PHASE 2: MATCHMAKING ────────────────────────────────────────┐  │
│  │                                                                │  │
│  │  LobbyManager.ConnectToPhoton()                                │  │
│  │       │                                                        │  │
│  │       ▼ (TCP → Photon Name Server → Master Server)             │  │
│  │  OnConnectedToMaster()                                          │  │
│  │       │                                                        │  │
│  │       ├─ CreateRoom(roomName, maxPlayers=2)                    │  │
│  │       │   OR                                                   │  │
│  │       └─ JoinRoom(roomName)                                    │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  Both players in room → RoomManager.OnAllPlayersReady()        │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  PhotonNetwork.LoadLevel("Battle")                              │  │
│  │  (synchronized scene transition for both clients)               │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─ PHASE 3: MATCH SETUP ───────────────────────────────────────┐   │
│  │                                                                │  │
│  │  Battle.unity loaded on both clients                           │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  CardDatabase.Lookup(deckData.mainDeck[]) → List<BaseCardSO>   │  │
│  │  CardDatabase.Lookup(deckData.lifeDeck[]) → List<LifeCardSO>   │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  DeckController.deckToUse = resolved SOs                       │  │
│  │  DeckController.lifeDeckToUse = resolved SOs                   │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  GameManager.StartGame()                                        │  │
│  │  ├─ SetupDeck() (shuffle with synced random seed)              │  │
│  │  ├─ DealLifeCards() (5 face-down per player)                   │  │
│  │  ├─ DrawCardToHand() × 5 (initial hand)                       │  │
│  │  └─ ShowMulliganUI() (card swap opportunity)                   │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─ PHASE 4: GAMEPLAY LOOP ─────────────────────────────────────┐   │
│  │                                                                │  │
│  │  ┌──────────────────────────────────────────────────────────┐  │  │
│  │  │              TURN LOOP (repeats)                          │  │  │
│  │  │                                                          │  │  │
│  │  │  Draw Phase → Main Phase → Battle Phase → End Phase      │  │  │
│  │  │       │            │            │             │          │  │  │
│  │  │       ▼            ▼            ▼             ▼          │  │  │
│  │  │    Draw cards   Summon      Select          Discard     │  │  │
│  │  │    Untap        avatars     attacker/       to 7        │  │  │
│  │  │    avatars      Play magic  target          Switch      │  │  │
│  │  │                             Resolve         turn        │  │  │
│  │  │                             combat                      │  │  │
│  │  │                                                          │  │  │
│  │  │  Every action: Client → RPC → Host validates → Broadcast │  │  │
│  │  │                                                          │  │  │
│  │  │  Win check after each combat:                            │  │  │
│  │  │  • Deck-out? → instant loss                              │  │  │
│  │  │  • All LIFE flipped? → Sahat status                      │  │  │
│  │  │  • Direct hit on Sahat? → game over                      │  │  │
│  │  └──────────────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─ PHASE 5: MATCH END & PERSIST ────────────────────────────────┐  │
│  │                                                                │  │
│  │  GameManager.EndGame(winnerId, reason)                          │  │
│  │       │                                                        │  │
│  │       ├──► UIController.ShowGameOver()                          │  │
│  │       │                                                        │  │
│  │       ├──► FirestoreManager.SaveMatchResult(userId, {           │  │
│  │       │        matchId, opponentName, result,                   │  │
│  │       │        winCondition, totalTurns, lifeStats,             │  │
│  │       │        deckUsed, playedAt, duration                     │  │
│  │       │    })                                                   │  │
│  │       │       │                                                 │  │
│  │       │       ▼ (HTTPS → Cloud Firestore)                      │  │
│  │       │    Write to: players/{uid}/matchHistory/{matchId}       │  │
│  │       │                                                        │  │
│  │       └──► FirestoreManager.UpdateWinLoss(userId, isWin)        │  │
│  │               │                                                 │  │
│  │               ▼ (HTTPS → Cloud Firestore)                      │  │
│  │            Update: players/{uid}/totalWins++                     │  │
│  │                    players/{uid}/totalMatches++                  │  │
│  │                    players/{uid}/winRate = wins/matches          │  │
│  │                                                                │  │
│  │       │                                                        │  │
│  │       ▼                                                        │  │
│  │  PhotonNetwork.LeaveRoom()                                      │  │
│  │  Navigate back to Lobby.unity                                   │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 16. ANNOTATION NOTES FOR DIAGRAM DRAWING

```
KEY ANNOTATIONS TO INCLUDE ON YOUR DIAGRAM:
════════════════════════════════════════════

① ScriptableObjects = Read-only card definition database
   (baked into build, no network sync needed, identical on all clients)

② Runtime Card Instance = mutable copy of SO data
   (buffs, mods, tap state — exists only during a match in RAM)

③ Photon RPCs = real-time game state sync
   (Client sends request → Host validates → Host broadcasts result)

④ Firebase writes happen ONLY at match end
   (not during gameplay — minimal DB load)

⑤ Hand/Deck contents are NEVER synced to opponent
   (only card count is visible — information hiding)

⑥ MasterClient = authoritative game host
   (validates all actions, prevents cheating)

⑦ Firestore Security Rules = per-user data isolation
   (players can only read/write their own data)

⑧ Pre-built deck only (for now)
   (database stores card ID list, client resolves to SO at match start)

⑨ Turn-based gameplay tolerates network latency
   (no need for prediction/interpolation like real-time games)

⑩ Firebase Auth JWT token attached to all Firestore requests
   (automatic via Firebase SDK — no manual token management)
```
