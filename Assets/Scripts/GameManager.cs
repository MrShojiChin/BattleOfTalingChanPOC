using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Whose turn it is.
/// </summary>
public enum TurnPlayer { Player1, Player2 }

/// <summary>
/// The four phases of each turn (from the rulebook).
/// Draw → Main → Battle → End
/// </summary>
public enum TurnPhase { Draw, Main, Battle, End }

/// <summary>
/// Central game state controller.
/// Manages turn order, phase transitions, and player references.
/// Tells UIController to update the HUD whenever state changes.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    // ── PLAYERS ────────────────────────────────────────────────────
    [Header("Players (assign in Inspector)")]
    public Player player1;
    public Player player2;

    [Header("Shared Zones")]
    public CardPlacePoint landMagicZone;   // Shared LandMagic zone (centre of board)

    // ── GAME STATE ─────────────────────────────────────────────────
    [Header("Current State (Read Only)")]
    public TurnPlayer currentPlayer = TurnPlayer.Player1;
    public TurnPhase  currentPhase  = TurnPhase.Draw;
    public bool       isFirstTurn   = true;
    public int        turnNumber    = 1;

    // ── SETUP / MULLIGAN ─────────────────────────────────────────
    [Header("Setup Phase")]
    public bool isSetupPhase = false;
    private int mulliganStep = 0;   // 0 = P1 mulligan, 1 = P2 mulligan

    // ── DISCARD (End Phase) ────────────────────────────────────
    [Header("Discard Phase")]
    public bool isDiscardPhase = false;

    // ── GAME OVER ────────────────────────────────────────────────
    [Header("Game Over")]
    public bool isGameOver = false;

    // ── SETUP ──────────────────────────────────────────────────────
    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        StartGame();
    }

    // ════════════════════════════════════════════════════════════════
    //  PLAYER HELPERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>Returns the Player object for the current turn.</summary>
    public Player CurrentPlayerObj
        => currentPlayer == TurnPlayer.Player1 ? player1 : player2;

    /// <summary>Returns the opponent Player object.</summary>
    public Player OpponentPlayerObj
        => currentPlayer == TurnPlayer.Player1 ? player2 : player1;

    /// <summary>Returns the Player object for a given TurnPlayer enum.</summary>
    public Player GetPlayer(TurnPlayer p)
        => p == TurnPlayer.Player1 ? player1 : player2;

    // ── Phase announcer colors per player ──
    private static readonly Color P1_PHASE_COLOR = new Color(0x38 / 255f, 0x67 / 255f, 0xDD / 255f); // #3867DD
    private static readonly Color P2_PHASE_COLOR = new Color(0xE7 / 255f, 0x45 / 255f, 0x45 / 255f); // #E74545

    /// <summary>Returns the phase announcer color for the current player.</summary>
    private Color CurrentPhaseColor
        => currentPlayer == TurnPlayer.Player1 ? P1_PHASE_COLOR : P2_PHASE_COLOR;

    // ════════════════════════════════════════════════════════════════
    //  GAME START
    // ════════════════════════════════════════════════════════════════

    public void StartGame()
    {
        currentPlayer = TurnPlayer.Player1;
        isFirstTurn   = true;
        SetupPhase();
    }

    // ════════════════════════════════════════════════════════════════
    //  SETUP PHASE — Draw 5 each + Mulligan
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-game setup per rulebook:
    ///   1. Deal 5 LIFE cards face-down for each player (from deck top)
    ///   2. Both players draw 5 cards to hand
    ///   3. P1 mulligans (swap any cards → bottom of deck → redraw → shuffle)
    ///   4. P2 mulligans
    ///   5. Game begins — P1 skips Draw Phase on Turn 1
    /// </summary>
    private void SetupPhase()
    {
        isSetupPhase = true;
        isGameOver   = false;

        // ── Ensure decks are initialized (Start() order is not guaranteed) ──
        player1.deck.SetupDeck();
        player2.deck.SetupDeck();

        // ── Auto-discover any missing zone references (fixes broken Inspector refs) ──
        player1.AutoDiscoverZones();
        player2.AutoDiscoverZones();

        // ── Auto-discover shared Land Magic zone if not assigned ──
        if (landMagicZone == null)
        {
            foreach (var zone in FindObjectsOfType<CardPlacePoint>())
            {
                if (zone.zoneType == ZoneType.LandMagic)
                {
                    landMagicZone = zone;
                    Debug.Log("[GameManager] Auto-discovered LandMagic zone.");
                    break;
                }
            }
        }

        // ── Adjust AVATAR zone spacing ──
        player1.AdjustAvatarZoneSpacing(2.5f);
        player2.AdjustAvatarZoneSpacing(2.5f);

        // ── Adjust LIFE zone spacing for horizontal cards ──
        player1.AdjustLifeZoneSpacing(1.35f);
        player2.AdjustLifeZoneSpacing(1.35f);

        // Deal 5 LIFE cards face-down for each player (from deck top, before hand draw)
        DealLifeCards(player1);
        DealLifeCards(player2);

        // Draw 5 for each player
        for (int i = 0; i < 5; i++)
            player1.deck.DrawCardToHand();
        for (int i = 0; i < 5; i++)
            player2.deck.DrawCardToHand();

        // Start P1 mulligan
        mulliganStep = 0;
        currentPlayer = TurnPlayer.Player1;
        UIController.instance.ShowMulliganUI("Player 1 — Select cards to swap, then press Swap. Or press Keep All.");
        Debug.Log("[GameManager] Setup: LIFE cards dealt + both players drew 5 cards. P1 mulligan begins.");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogTurn("Game started! LIFE cards dealt, hands drawn. P1 mulligan.");

    }

    /// <summary>
    /// Called by UIController's "Swap Selected" button.
    /// Returns marked cards to deck bottom, redraws, shuffles.
    /// </summary>
    public void OnMulliganConfirmed()
    {
        Player cp = CurrentPlayerObj;

        // Collect cards marked for mulligan
        List<Card> markedCards = new List<Card>();
        foreach (Card card in cp.hand.heldCards)
        {
            if (card != null && card.markedForMulligan)
                markedCards.Add(card);
        }

        int swapCount = markedCards.Count;

        // Return each marked card's SO to bottom of deck, then destroy the GO
        foreach (Card card in markedCards)
        {
            BaseCardSO so = null;
            if (card.cardType == CardType.Avatar)
                so = card.avatarSO;
            else if (card.cardType == CardType.Magic)
                so = card.magicSO;
            else if (card.cardType == CardType.Life)
                so = card.lifeCardSO;  // Shouldn't happen, but defensive

            if (so != null)
                cp.deck.ReturnCardToBottom(so);
        }

        // Remove from hand and destroy GameObjects
        cp.hand.RemoveCardsFromHand(markedCards);
        foreach (Card card in markedCards)
            Destroy(card.gameObject);

        // Draw back the same number
        for (int i = 0; i < swapCount; i++)
            cp.deck.DrawCardToHand();

        // Shuffle the deck after mulligan
        cp.deck.ShuffleDeck();

        Debug.Log($"[GameManager] {currentPlayer} swapped {swapCount} cards.");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogDraw($"{CurrentPlayerName()} swapped {swapCount} card(s) (mulligan).");

        AdvanceMulligan();
    }

    /// <summary>
    /// Called by UIController's "Keep All" button.
    /// Skips the mulligan for this player.
    /// </summary>
    public void OnMulliganSkipped()
    {
        // Clear any accidental highlights
        Player cp = CurrentPlayerObj;
        foreach (Card card in cp.hand.heldCards)
        {
            if (card != null)
            {
                card.markedForMulligan = false;
                card.SetPitchHighlight(false);
            }
        }

        Debug.Log($"[GameManager] {currentPlayer} kept all cards.");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogDraw($"{CurrentPlayerName()} kept all cards.");
        AdvanceMulligan();
    }

    /// <summary>Move from P1 mulligan → P2 mulligan → Finish Setup.</summary>
    private void AdvanceMulligan()
    {
        if (mulliganStep == 0)
        {
            // P1 done → P2 mulligan
            mulliganStep = 1;
            currentPlayer = TurnPlayer.Player2;
            UIController.instance.ShowMulliganUI("Player 2 — Select cards to swap, then press Swap. Or press Keep All.");
            Debug.Log("[GameManager] P2 mulligan begins.");
        }
        else
        {
            // P2 done → finish setup
            FinishSetup();
        }
    }

    /// <summary>
    /// End the setup phase and begin Turn 1.
    /// P1 skips Draw Phase → goes directly to Main Phase.
    /// </summary>
    private void FinishSetup()
    {
        isSetupPhase = false;
        UIController.instance.HideMulliganUI();

        currentPlayer = TurnPlayer.Player1;
        isFirstTurn   = true;
        turnNumber    = 1;

        // ── HAND SLIDE: P1 visible, P2 hidden at game start ──
        player1.hand.SlideUp();
        player2.hand.SlideDown();

        // P1 draws +2 bonus cards on Turn 1 sequentially, then goes to Main Phase
        Player cp = CurrentPlayerObj;
        if (cp != null && cp.deck != null)
        {
            Debug.Log("[GameManager] Setup complete! Turn 1 — P1 draws +2 bonus cards.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogTurn("Setup complete! Turn 1 begins. P1 draws +2 bonus cards.");
            StartCoroutine(DrawThenMainPhase(cp, 2));
        }
        else
        {
            EnterMainPhase();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE TRANSITIONS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw Phase:
    /// - P1 Turn 1: skip draw entirely (already has 5 from setup)
    /// - If hand has fewer than 3 cards → draw until 3
    /// - If hand has 3+ cards → draw 1
    /// - Avatars wake up (untap / ลุก) at the start of their owner's turn
    /// Shows a "Draw Phase" stomp announcement before drawing.
    /// </summary>
    private void EnterDrawPhase()
    {
        currentPhase = TurnPhase.Draw;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);

        // Show stomp animation, then proceed with draw logic
        if (PhaseAnnouncer.instance != null)
        {
            PhaseAnnouncer.instance.AnnouncePhase("Draw Phase", CurrentPhaseColor, () => ExecuteDrawPhase());
        }
        else
        {
            ExecuteDrawPhase();
        }
    }

    /// <summary>Executes the actual Draw Phase logic after the announcement.</summary>
    private void ExecuteDrawPhase()
    {
        // ── HAND SLIDE: current player's hand up, opponent's hand down ──
        CurrentPlayerObj.hand.SlideUp();
        OpponentPlayerObj.hand.SlideDown();

        // Use CURRENT player's hand and deck (no more singletons)
        Player cp = CurrentPlayerObj;

        if (cp == null || cp.hand == null || cp.deck == null)
        {
            Debug.LogError("[GameManager] Current player, hand, or deck is null!");
            EnterMainPhase();
            return;
        }

        // ── Untap all current player's avatars (wake up / ลุก) ──
        UntapAllAvatars(cp);

        // P1 skips draw on Turn 1 (they already have 5 from setup)
        if (isFirstTurn && currentPlayer == TurnPlayer.Player1)
        {
            Debug.Log("[GameManager] P1 Turn 1 — skip draw (already has 5 from setup).");
            EnterMainPhase();
            return;
        }

        // Normal draw rules
        int currentHandSize = cp.hand.heldCards.Count;
        int cardsToDraw = (currentHandSize < 3) ? (3 - currentHandSize) : 1;

        // Draw sequentially, then advance to Main Phase
        StartCoroutine(DrawThenMainPhase(cp, cardsToDraw));
    }

    /// <summary>
    /// Main Phase:
    /// - Player can summon avatars, play magic cards.
    /// - BattleController handles summon logic.
    /// Shows a "Main Phase" stomp announcement before player gets control.
    /// </summary>
    public void EnterMainPhase()
    {
        currentPhase = TurnPhase.Main;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);

        // Show stomp animation, then proceed with main phase logic
        if (PhaseAnnouncer.instance != null)
        {
            PhaseAnnouncer.instance.AnnouncePhase("Main Phase", CurrentPhaseColor, () => ExecuteMainPhase());
        }
        else
        {
            ExecuteMainPhase();
        }
    }

    /// <summary>Executes the actual Main Phase logic after the announcement.</summary>
    private void ExecuteMainPhase()
    {
        // ── LIFE CARD EFFECT: Draw queued cards from LIFE flips (sequentially) ──
        Player cp = CurrentPlayerObj;
        if (cp != null && cp.pendingLifeDraws > 0)
        {
            int draws = cp.pendingLifeDraws;
            cp.pendingLifeDraws = 0;
            if (cp.deck != null)
            {
                Debug.Log($"[GameManager] {currentPlayer} drawing {draws} card(s) from LIFE effect...");
                StartCoroutine(SequentialLifeDraws(cp, draws));
            }
        }

        Debug.Log($"[GameManager] {currentPlayer} — MAIN PHASE");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogTurn($"{CurrentPlayerName()} — Main Phase");
    }

    /// <summary>Draw N cards sequentially, wait a beat, then enter Main Phase.</summary>
    private IEnumerator DrawThenMainPhase(Player cp, int count)
    {
        yield return cp.deck.DrawMultipleCards(count);
        if (isGameOver) yield break;

        // Small pause so the player can see the drawn cards before Main Phase stomp
        yield return new WaitForSeconds(0.8f);

        EnterMainPhase();
    }

    /// <summary>Draw pending LIFE effect cards sequentially during Main Phase.</summary>
    private IEnumerator SequentialLifeDraws(Player cp, int count)
    {
        yield return cp.deck.DrawMultipleCards(count);
        Debug.Log($"[GameManager] {currentPlayer} drew {count} card(s) from LIFE effect.");
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>
    /// Battle Phase:
    /// - Player selects avatars to attack.
    /// - Avatars CAN attack on their first turn on the field (no summon sickness).
    /// - P1 cannot enter Battle Phase on Turn 1 (handled in OnNextPhasePressed).
    /// </summary>
    public void EnterBattlePhase()
    {
        currentPhase = TurnPhase.Battle;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);

        // Show stomp animation, then proceed with battle logic
        if (PhaseAnnouncer.instance != null)
        {
            PhaseAnnouncer.instance.AnnouncePhase("Battle Phase", CurrentPhaseColor, () => ExecuteBattlePhase());
        }
        else
        {
            ExecuteBattlePhase();
        }
    }

    /// <summary>Executes the actual Battle Phase logic after the announcement.</summary>
    private void ExecuteBattlePhase()
    {
        // ── HAND SLIDE: no hand interaction during Battle Phase ──
        CurrentPlayerObj.hand.SlideDown();

        // Start combat selection
        if (CombatController.instance != null)
            CombatController.instance.BeginBattlePhase();

        Debug.Log($"[GameManager] {currentPlayer} — BATTLE PHASE");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogTurn($"{CurrentPlayerName()} — Battle Phase");
    }

    /// <summary>
    /// End Phase:
    /// - Discard down to 7 cards if hand exceeds 7.
    /// - Then pass the turn.
    /// </summary>
    public void EnterEndPhase()
    {
        currentPhase = TurnPhase.End;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);
        Debug.Log($"[GameManager] {currentPlayer} — END PHASE");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogTurn($"{CurrentPlayerName()} — End Phase");

        // ── DISCARD-TO-7: check hand limit ──
        Player cp = CurrentPlayerObj;
        int handSize = cp.hand.heldCards.Count;

        if (handSize > 7)
        {
            // ── HAND SLIDE: slide up so player can select cards to discard ──
            cp.hand.SlideUp();

            int discardCount = handSize - 7;
            isDiscardPhase = true;
            Debug.Log($"[GameManager] {currentPlayer} has {handSize} cards — must discard {discardCount}.");
            UIController.instance.ShowDiscardUI(
                $"{CurrentPlayerName()} — Discard {discardCount} card(s)\n(hand limit = 7)");
            return; // Wait for player to confirm discard
        }

        // Hand is within limit — pass the turn
        SwitchTurn();
    }

    /// <summary>
    /// Called by the Discard UI "Confirm" button.
    /// Moves marked cards from hand to the Hell Zone, then passes the turn.
    /// </summary>
    public void OnDiscardConfirmed()
    {
        Player cp = CurrentPlayerObj;
        int required = cp.hand.heldCards.Count - 7;

        // Collect marked cards
        List<Card> toDiscard = cp.hand.GetMarkedForDiscard();

        if (toDiscard.Count != required)
        {
            Debug.LogWarning($"[GameManager] Need to discard {required} but {toDiscard.Count} selected!");
            UIController.instance.ShowDiscardUI(
                $"Select exactly {required} card(s) to discard!\n(You selected {toDiscard.Count})");
            return;
        }

        // Remove from hand
        cp.hand.RemoveCardsFromHand(toDiscard);

        // Send each card to the Hell Zone
        foreach (Card card in toDiscard)
        {
            card.markedForDiscard = false;
            card.SetPitchHighlight(false);
            card.inHand = false;

            if (cp.hellZone != null)
                cp.hellZone.AddCard(card);

            Debug.Log($"[Discard] {card.cardName} discarded to Hell Zone.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogDraw($"{card.cardName} discarded to Hell.");
        }

        // Clear any remaining highlights (safety)
        foreach (Card card in cp.hand.heldCards)
        {
            card.markedForDiscard = false;
            card.SetPitchHighlight(false);
        }

        isDiscardPhase = false;
        UIController.instance.HideDiscardUI();

        Debug.Log($"[GameManager] {currentPlayer} discarded {toDiscard.Count} card(s). Hand now: {cp.hand.heldCards.Count}");

        // Now pass the turn
        SwitchTurn();
    }

    // ════════════════════════════════════════════════════════════════
    //  TURN SWITCH
    // ════════════════════════════════════════════════════════════════

    private void SwitchTurn()
    {
        // Clean up temporary buffs from this turn before switching
        if (MagicController.instance != null)
            MagicController.instance.CleanupTempBuffs();

        isFirstTurn   = false;
        turnNumber++;
        currentPlayer = (currentPlayer == TurnPlayer.Player1)
                        ? TurnPlayer.Player2
                        : TurnPlayer.Player1;

        Debug.Log($"[GameManager] Turn #{turnNumber} switched → {currentPlayer}");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogTurn($"── Turn {turnNumber} → {CurrentPlayerName()} ──");
        EnterDrawPhase();
    }

    // ════════════════════════════════════════════════════════════════
    //  BUTTON CALLBACK — called by the "Next Phase" button in HUD
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Advances to the next phase when the player presses the Next Phase button.
    /// Draw is auto-skipped, so button only appears during Main / Battle / End.
    /// </summary>
    public void OnNextPhasePressed()
    {
        if (isGameOver) return;

        switch (currentPhase)
        {
            case TurnPhase.Main:
                // P1 cannot enter Battle Phase on Turn 1 (rulebook)
                if (isFirstTurn && currentPlayer == TurnPlayer.Player1)
                {
                    Debug.Log("[GameManager] P1 Turn 1 — skip Battle Phase (rulebook).");
                    EnterEndPhase();
                }
                else
                {
                    EnterBattlePhase();
                }
                break;
            case TurnPhase.Battle:
                if (CombatController.instance != null)
                    CombatController.instance.EndBattlePhase();
                EnterEndPhase();
                break;
            default: break;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPER QUERIES  (used by Card.cs and BattleController)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Returns true if it is currently the Main Phase.</summary>
    public bool IsMainPhase()   => currentPhase == TurnPhase.Main;

    /// <summary>Returns true if it is currently the Battle Phase.</summary>
    public bool IsBattlePhase() => currentPhase == TurnPhase.Battle;

    /// <summary>Returns a display-friendly name for the current player.</summary>
    public string CurrentPlayerName()
        => currentPlayer == TurnPlayer.Player1 ? "Player 1" : "Player 2";

    // ════════════════════════════════════════════════════════════════
    //  AVATAR MANAGEMENT
    // ════════════════════════════════════════════════════════════════

    /// <summary>Untap (wake up / ลุก) all of a player's avatars on the board.</summary>
    private void UntapAllAvatars(Player player)
    {
        foreach (var zone in player.avatarZones)
        {
            if (zone != null && zone.activeCard != null)
            {
                zone.activeCard.SetTapped(false);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFE CARD SETUP
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deal 5 face-down LIFE cards from the player's deck to their LIFE zones.
    /// Called during setup, BEFORE drawing the opening hand.
    /// Cards are dealt from the shuffled deck top — players don't know what they are.
    /// </summary>
    private void DealLifeCards(Player player)
    {
        if (player.deck.lifeCardPrefab == null)
            Debug.LogError($"[GameManager] {player.playerId}: lifeCardPrefab not assigned on DeckController!");
        if (player.deck.lifeDeckToUse.Count == 0)
            Debug.LogError($"[GameManager] {player.playerId}: lifeDeckToUse is empty on DeckController! Assign 5 LifeCardSO assets.");

        int dealt = 0;
        for (int i = 0; i < player.lifeZones.Length; i++)
        {
            if (player.lifeZones[i] == null)
            {
                Debug.LogWarning($"[GameManager] {player.playerId}: lifeZones[{i}] is null — assign a CardPlacePoint in Inspector!");
                continue;
            }

            Card card = player.deck.DrawCardToLifeZone(player.lifeZones[i]);
            if (card != null) dealt++;
        }

        Debug.Log($"[GameManager] {player.playerId} — {dealt} LIFE cards dealt face-down (of {player.lifeZones.Length} zones).");
    }

    // ════════════════════════════════════════════════════════════════
    //  WIN CONDITION — สหัส (Sahat)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Declare a player as the loser (e.g., deck-out).
    /// The other player wins. Ends the game.
    /// </summary>
    public void DeclareLoser(TurnPlayer loser, string reason)
    {
        if (isGameOver) return;

        isGameOver = true;
        string winner = loser == TurnPlayer.Player1 ? "PLAYER 2" : "PLAYER 1";
        string loserName = loser == TurnPlayer.Player1 ? "Player 1" : "Player 2";

        Debug.Log($"[GameManager] {loserName} loses — {reason}. {winner} WINS!");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogGameOver($"{winner} WINS! {loserName} — {reason}");
        UIController.instance.ShowGameOver($"{winner} WINS!\n{loserName}'s {reason}");
    }

    /// <summary>
    /// Check if either player has reached สาหัส (all 5 LIFE cards flipped face-up).
    /// สาหัส does NOT end the game — the opponent must land one more
    /// direct hit on the life zone to win.
    /// Returns true if any player just entered สาหัส (for UI notification).
    /// </summary>
    public bool CheckWinCondition()
    {
        if (isGameOver) return true;

        // ── DECK-OUT: deck = 0 → immediate loss ──
        if (player1.deck != null && player1.deck.CardsRemaining == 0)
        {
            Debug.Log("[GameManager] Player 1 deck is empty — DECK-OUT LOSS!");
            DeclareLoser(TurnPlayer.Player1, "deck is empty (deck-out)");
            return true;
        }
        if (player2.deck != null && player2.deck.CardsRemaining == 0)
        {
            Debug.Log("[GameManager] Player 2 deck is empty — DECK-OUT LOSS!");
            DeclareLoser(TurnPlayer.Player2, "deck is empty (deck-out)");
            return true;
        }

        // สาหัส = all 5 LIFE flipped. Game does NOT end immediately.
        // The opponent must land one more direct hit on the life zone.
        if (player1.IsSahat)
        {
            Debug.Log("[GameManager] Player 1 is สาหัส (all LIFE flipped)! Opponent must direct hit to win.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogLife("Player 1 is สาหัส! All LIFE exposed!");
        }
        if (player2.IsSahat)
        {
            Debug.Log("[GameManager] Player 2 is สาหัส (all LIFE flipped)! Opponent must direct hit to win.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogLife("Player 2 is สาหัส! All LIFE exposed!");
        }

        return false; // Game never ends from สาหัส alone
    }

    /// <summary>
    /// End the game via direct hit on a สาหัส player's life zone.
    /// Called by CombatController when an attacker hits the life zone
    /// of a player who is already in สาหัส status.
    /// </summary>
    public void EndGameDirectHit(TurnPlayer loser)
    {
        isGameOver = true;
        string loserName = loser == TurnPlayer.Player1 ? "Player 1" : "Player 2";
        string winnerName = loser == TurnPlayer.Player1 ? "PLAYER 2" : "PLAYER 1";
        Debug.Log($"[GameManager] {loserName} receives DIRECT HIT while สาหัส! {winnerName} WINS!");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogGameOver($"DIRECT HIT! {winnerName} WINS! {loserName} is defeated!");
        UIController.instance.ShowGameOver($"{winnerName} WINS!\n{loserName} received a direct hit! (\u0E2A\u0E32\u0E2B\u0E31\u0E2A)");
    }
}
