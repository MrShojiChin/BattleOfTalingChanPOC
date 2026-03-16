# Photon Multiplayer Architecture Draft (For Report Diagram)

## Layer Diagram (Top to Bottom)

```
┌─────────────────────────────────────────────────────────────┐
│                      PRESENTATION LAYER                      │
│  ┌──────────┐  ┌────────────┐  ┌──────────┐  ┌───────────┐ │
│  │UIController│ │PhaseAnnouncer│ │Card Visual│ │HellViewer │ │
│  └──────────┘  └────────────┘  └──────────┘  └───────────┘ │
└─────────────────────────┬───────────────────────────────────┘
                          │ UI Events / State Updates
┌─────────────────────────▼───────────────────────────────────┐
│                     GAME LOGIC LAYER                         │
│  ┌────────────┐  ┌──────────────┐  ┌─────────────────────┐  │
│  │GameManager  │  │BattleController│ │CombatController    │  │
│  │(Phase FSM)  │  │(Summon FSM)   │  │(Combat FSM)        │  │
│  └────────────┘  └──────────────┘  └─────────────────────┘  │
│  ┌──────────────┐  ┌──────────────────────────────────────┐  │
│  │MagicController│ │AvatarAbilityController               │  │
│  │(Magic FSM)    │ │(Keyword Abilities)                   │  │
│  └──────────────┘  └──────────────────────────────────────┘  │
└─────────────────────────┬───────────────────────────────────┘
                          │ Game Actions (Summon, Attack, Play Magic)
┌─────────────────────────▼───────────────────────────────────┐
│                 NETWORK SYNC LAYER (Photon)                   │
│                                                               │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │              NetworkGameManager (PhotonView)             │  │
│  │  - Owns master game state (phase, turn, win condition)   │  │
│  │  - Only MasterClient can advance phases                  │  │
│  │  - Broadcasts state changes to all clients               │  │
│  └──────────────────────┬──────────────────────────────────┘  │
│                         │                                     │
│  ┌──────────────────────▼──────────────────────────────────┐  │
│  │                   RPC Router                             │  │
│  │                                                          │  │
│  │  Client → Host (Request RPCs):                           │  │
│  │    RequestSummonAvatar(cardId, slotIndex, tributeIds[])   │  │
│  │    RequestAttack(attackerId, targetId)                    │  │
│  │    RequestPlayMagic(cardId, targetId?)                    │  │
│  │    RequestAdvancePhase()                                  │  │
│  │    RequestMulligan(swapCardIds[])                         │  │
│  │    RequestDiscard(cardIds[])                              │  │
│  │                                                          │  │
│  │  Host → All (Broadcast RPCs):                            │  │
│  │    RPC_SyncPhaseChange(phase, turnPlayer)                │  │
│  │    RPC_SyncCardSummoned(cardId, slotIndex, ownerId)      │  │
│  │    RPC_SyncCombatResult(attackerId, defenderId, result)  │  │
│  │    RPC_SyncMagicPlayed(cardId, effectType, targetId)     │  │
│  │    RPC_SyncLifeFlipped(lifeIndex, ownerId, effectType)   │  │
│  │    RPC_SyncCardDrawn(cardId, ownerId)  [owner only]      │  │
│  │    RPC_SyncGameOver(winnerId, reason)                    │  │
│  │    RPC_SyncHandCount(ownerId, count)   [opponent sees]   │  │
│  └──────────────────────┬──────────────────────────────────┘  │
│                         │                                     │
│  ┌──────────────────────▼──────────────────────────────────┐  │
│  │              Per-Player Network Objects                   │  │
│  │                                                          │  │
│  │  Player1 (PhotonView, Owner = Client A)                  │  │
│  │    ├─ NetworkDeckController (synced deck count)           │  │
│  │    ├─ NetworkHandController (hidden from opponent)        │  │
│  │    └─ Zone references (Avatar, LIFE, Hell, Magic)        │  │
│  │                                                          │  │
│  │  Player2 (PhotonView, Owner = Client B)                  │  │
│  │    ├─ NetworkDeckController (synced deck count)           │  │
│  │    ├─ NetworkHandController (hidden from opponent)        │  │
│  │    └─ Zone references (Avatar, LIFE, Hell, Magic)        │  │
│  └──────────────────────┬──────────────────────────────────┘  │
│                         │                                     │
│  ┌──────────────────────▼──────────────────────────────────┐  │
│  │              Card Sync (per card instance)                │  │
│  │                                                          │  │
│  │  Card GameObject (PhotonView)                            │  │
│  │    ├─ NetworkTransform (position, rotation sync)         │  │
│  │    ├─ Synced Properties:                                 │  │
│  │    │    cardId, cardOwner, isTapped, isFaceDown,         │  │
│  │    │    power (with buffs), assignedZone                  │  │
│  │    └─ Visual-only on each client (highlights, preview)   │  │
│  └─────────────────────────────────────────────────────────┘  │
└─────────────────────────┬───────────────────────────────────┘
                          │ Photon Messages
┌─────────────────────────▼───────────────────────────────────┐
│                   PHOTON CLOUD LAYER                         │
│                                                               │
│  ┌───────────┐    ┌──────────────┐    ┌──────────────────┐   │
│  │  Lobby     │    │  Room         │    │  Photon Cloud    │   │
│  │  Manager   │    │  Manager      │    │  Server          │   │
│  │            │    │               │    │  (Relay)         │   │
│  │ -FindRoom  │    │ -CreateRoom   │    │                  │   │
│  │ -QuickMatch│    │ -JoinRoom     │    │ -Message Relay   │   │
│  │ -RoomList  │    │ -PlayerReady  │    │ -Room State      │   │
│  │ -Filters   │    │ -LoadScene    │    │ -Matchmaking     │   │
│  └───────────┘    └──────────────┘    └──────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

## Authority Model

```
┌──────────────────┐                    ┌──────────────────┐
│    Client A       │                    │    Client B       │
│  (MasterClient)   │                    │   (Guest)         │
│                   │                    │                   │
│  Controls:        │    Photon Cloud    │  Controls:        │
│  - Player 1 Hand  │◄──────────────────►│  - Player 2 Hand  │
│  - Player 1 Deck  │    (Relay Server)  │  - Player 2 Deck  │
│  - Phase Advance  │                    │  - Own Card Drag  │
│  - Combat Resolve │                    │                   │
│  - Win Condition  │                    │  Requests:        │
│  - Effect Resolve │                    │  - Summon → Host  │
│                   │                    │  - Attack → Host  │
│  Validates ALL    │                    │  - Magic  → Host  │
│  game actions     │                    │  - Phase  → Host  │
└──────────────────┘                    └──────────────────┘
```

## Information Hiding (What Each Client Sees)

```
┌─────────────────────────────────────────────────────┐
│                  Client A (Player 1)                 │
│                                                      │
│  FULL INFO:                 HIDDEN INFO:             │
│  ✓ Own hand cards           ✗ Opponent hand cards    │
│  ✓ Own deck count           ✗ Opponent deck contents │
│  ✓ Own LIFE cards (face-up) ✗ Opponent LIFE (down)   │
│  ✓ Board state (all)        ✗ Opponent deck order    │
│  ✓ Hell zone (all cards)                             │
│  ✓ Magic zone (all cards)                            │
│                                                      │
│  SHARED INFO (both clients see):                     │
│  ✓ Phase / Turn indicator                            │
│  ✓ Avatars on board (power, tapped, mods)            │
│  ✓ LIFE cards flipped face-up                        │
│  ✓ Hell zone contents                                │
│  ✓ Magic zone (React, Land cards)                    │
│  ✓ Opponent hand count (not contents)                │
│  ✓ Opponent deck count                               │
└─────────────────────────────────────────────────────┘
```

## Connection Flow (Sequence)

```
Client A                  Photon Cloud               Client B
   │                          │                          │
   │── ConnectToPhoton() ────►│                          │
   │◄─ OnConnectedToMaster() │                          │
   │                          │                          │
   │── CreateRoom("room1") ──►│                          │
   │◄─ OnJoinedRoom()        │                          │
   │   (becomes MasterClient) │                          │
   │                          │                          │
   │                          │◄── ConnectToPhoton() ────│
   │                          │──► OnConnectedToMaster() │
   │                          │                          │
   │                          │◄── JoinRoom("room1") ───│
   │◄─ OnPlayerEnteredRoom() │──► OnJoinedRoom()        │
   │                          │                          │
   │── PhotonNetwork         │                          │
   │   .LoadLevel("Battle")──►│──► LoadLevel("Battle") ─│
   │                          │                          │
   │   [Both clients load Battle scene]                  │
   │                          │                          │
   │── SpawnPlayerObjects() ──►│──► Instantiate on B ────│
   │◄─ Instantiate on A ─────│◄── SpawnPlayerObjects() ─│
   │                          │                          │
   │── RPC: StartGame() ─────►│──► Execute StartGame() ─│
   │   [MasterClient deals    │                          │
   │    cards, sets up board]  │                          │
   │                          │                          │
   │   ══════ GAME LOOP ══════════════════════════════   │
   │                          │                          │
   │◄─────────────────────────│◄── RequestSummon(card) ──│
   │   [Validate & resolve]   │                          │
   │── RPC: SyncSummon() ────►│──► Update board visuals ─│
   │                          │                          │
```

## Game Action Flow (Example: Summon Avatar)

```
Player B clicks avatar card in hand
         │
         ▼
    Client B: Local UI feedback (lift card, yellow highlight)
         │
         ▼
    Client B: Player selects tributes locally
         │
         ▼
    Client B: Confirms summon (drag to slot)
         │
         ▼
    Client B ──► RPC RequestSummon(avatarCardId, tributeIds[], slotIndex)
                         │
                         ▼ (Photon Cloud relays to MasterClient)
                         │
              Client A (MasterClient): Validate
              ├─ Is it Player B's turn?
              ├─ Is it Main Phase?
              ├─ Does Player B own these cards?
              ├─ Are tributes valid (color, gem value)?
              ├─ Is the slot empty?
              └─ Total gems >= cost?
                         │
                    ┌────┴────┐
                    │Valid?   │
                 YES│         │NO
                    ▼         ▼
         RPC_SyncCardSummoned()   RPC_SyncActionRejected()
         (broadcast to ALL)       (send to Client B only)
                    │
                    ▼
         Both Clients:
         ├─ Move avatar to board slot
         ├─ Send tributes to Hell
         ├─ Trigger Juti abilities
         ├─ Trigger React magic checks
         ├─ Recalculate Land buffs
         └─ Update UI (avatar count, hand count)
```

## Mapping: Existing Controllers → Network Role

```
┌────────────────────────┬─────────────────────────────────────┐
│  Existing Controller   │  Network Role                       │
├────────────────────────┼─────────────────────────────────────┤
│  GameManager           │  MasterClient-authoritative         │
│                        │  Syncs phase/turn via RPC            │
│                        │  Only host advances phases           │
├────────────────────────┼─────────────────────────────────────┤
│  BattleController      │  Split: local UI + host validation  │
│                        │  Client: visual feedback (lift/glow) │
│                        │  Host: validate & finalize summon    │
├────────────────────────┼─────────────────────────────────────┤
│  CombatController      │  MasterClient-authoritative         │
│                        │  Client: select attacker/target      │
│                        │  Host: resolve power, send result    │
├────────────────────────┼─────────────────────────────────────┤
│  MagicController       │  MasterClient-authoritative         │
│                        │  Client: choose card & target        │
│                        │  Host: resolve effect, broadcast     │
├────────────────────────┼─────────────────────────────────────┤
│  AvatarAbilityController│ MasterClient-authoritative         │
│                        │  Triggered by host after summon/     │
│                        │  combat resolution                   │
├────────────────────────┼─────────────────────────────────────┤
│  DeckController        │  Owner-authoritative (per player)   │
│                        │  Deck contents hidden from opponent  │
│                        │  Only deck count synced publicly     │
├────────────────────────┼─────────────────────────────────────┤
│  HandController        │  Owner-authoritative (per player)   │
│                        │  Hand contents hidden from opponent  │
│                        │  Only hand count synced publicly     │
├────────────────────────┼─────────────────────────────────────┤
│  UIController          │  Fully local (no sync needed)       │
│                        │  Each client renders own HUD         │
├────────────────────────┼─────────────────────────────────────┤
│  GameplayLogger        │  Fully local OR sync log entries    │
│                        │  Each client can log independently   │
├────────────────────────┼─────────────────────────────────────┤
│  Card (MonoBehaviour)  │  PhotonView + NetworkTransform      │
│                        │  Synced: position, rotation, power,  │
│                        │  tapped, faceDown, assignedZone      │
└────────────────────────┴─────────────────────────────────────┘
```

## New Components Needed for Photon

```
NEW SCRIPTS (Network Layer):
├── NetworkGameManager.cs      - PhotonView, master state sync, phase RPCs
├── NetworkCardSpawner.cs      - PhotonNetwork.Instantiate() for cards
├── NetworkRPCRouter.cs        - Central RPC send/receive hub
├── LobbyManager.cs           - Room create/join/matchmaking UI
├── RoomManager.cs             - Player ready, scene loading, disconnect
├── NetworkDeckController.cs   - Secure deck shuffle (seed sync), hidden draw
└── ReconnectionHandler.cs     - State snapshot, rejoin, catch-up sync

MODIFIED SCRIPTS (Add [PunRPC] methods):
├── GameManager.cs             + RPC phase/turn sync
├── BattleController.cs        + RPC summon request/response
├── CombatController.cs        + RPC attack request/response
├── MagicController.cs         + RPC magic play request/response
├── Card.cs                    + PhotonView, IPunObservable
└── Player.cs                  + PhotonPlayer mapping

SCENES:
├── Lobby.unity                - New: matchmaking/room browser
├── Battle.unity               - Modified: networked spawning
└── (Loading.unity)            - Optional: transition screen
```

## Full System Architecture (Complete Diagram)

```
╔══════════════════════════════════════════════════════════════════════╗
║                        COMPLETE SYSTEM ARCHITECTURE                  ║
╠══════════════════════════════════════════════════════════════════════╣
║                                                                      ║
║  ┌─── SCENE: Lobby ──────────────────────────────────────────────┐   ║
║  │  LobbyManager ──► RoomManager ──► PhotonNetwork.LoadLevel()   │   ║
║  └───────────────────────────┬───────────────────────────────────┘   ║
║                              │ Scene Transition                      ║
║  ┌─── SCENE: Battle ────────▼───────────────────────────────────┐   ║
║  │                                                               │   ║
║  │  ┌─ PRESENTATION ──────────────────────────────────────────┐  │   ║
║  │  │  UIController │ PhaseAnnouncer │ Card Visuals │ HellView │  │   ║
║  │  └──────────────────────┬──────────────────────────────────┘  │   ║
║  │                         │                                     │   ║
║  │  ┌─ GAME LOGIC ────────▼──────────────────────────────────┐  │   ║
║  │  │  GameManager ─┬─ BattleController (Summon)              │  │   ║
║  │  │               ├─ CombatController (Battle)              │  │   ║
║  │  │               ├─ MagicController (Magic Effects)        │  │   ║
║  │  │               └─ AvatarAbilityController (Keywords)     │  │   ║
║  │  └──────────────────────┬──────────────────────────────────┘  │   ║
║  │                         │                                     │   ║
║  │  ┌─ PLAYER DATA ───────▼──────────────────────────────────┐  │   ║
║  │  │  Player 1              │  Player 2                      │  │   ║
║  │  │  ├─ HandController     │  ├─ HandController             │  │   ║
║  │  │  ├─ DeckController     │  ├─ DeckController             │  │   ║
║  │  │  ├─ AvatarZones[4]     │  ├─ AvatarZones[4]             │  │   ║
║  │  │  ├─ LifeZones[5]       │  ├─ LifeZones[5]               │  │   ║
║  │  │  ├─ MagicZone          │  ├─ MagicZone                  │  │   ║
║  │  │  └─ HellZone           │  └─ HellZone                   │  │   ║
║  │  └──────────────────────┬──────────────────────────────────┘  │   ║
║  │                         │                                     │   ║
║  │  ┌─ DATA (ScriptableObjects) ─────────────────────────────┐  │   ║
║  │  │  AvatarCardSO │ MagicCardSO │ LifeCardSO │ BaseCardSO  │  │   ║
║  │  └─────────────────────────────────────────────────────────┘  │   ║
║  │                         │                                     │   ║
║  │  ┌─ NETWORK LAYER (Photon PUN2) ──────────────────────────┐  │   ║
║  │  │  NetworkGameManager ──► NetworkRPCRouter                │  │   ║
║  │  │  NetworkCardSpawner ──► NetworkDeckController            │  │   ║
║  │  │  ReconnectionHandler                                    │  │   ║
║  │  │                                                          │  │   ║
║  │  │  PhotonView (on GameManager, Player, Card objects)      │  │   ║
║  │  │  PhotonTransformView (on Card objects)                  │  │   ║
║  │  └──────────────────────┬──────────────────────────────────┘  │   ║
║  └─────────────────────────┼─────────────────────────────────────┘   ║
║                            │                                         ║
║  ┌─ PHOTON CLOUD ──────────▼─────────────────────────────────────┐   ║
║  │  Matchmaking Server │ Relay Server │ Room State │ Player Auth  │   ║
║  └───────────────────────────────────────────────────────────────┘   ║
║                                                                      ║
╚══════════════════════════════════════════════════════════════════════╝
```
