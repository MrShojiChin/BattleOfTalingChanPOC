using UnityEngine;
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

        // P1 skips Draw Phase on Turn 1 → go straight to Main
        Debug.Log("[GameManager] Setup complete! Turn 1 — P1 starts at Main Phase.");
        EnterMainPhase();
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
    /// </summary>
    private void EnterDrawPhase()
    {
        currentPhase = TurnPhase.Draw;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);

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
        if (currentHandSize < 3)
        {
            // Draw up to 3
            int cardsToDraw = 3 - currentHandSize;
            for (int i = 0; i < cardsToDraw; i++)
                cp.deck.DrawCardToHand();
        }
        else
        {
            // Draw exactly 1
            cp.deck.DrawCardToHand();
        }

        // Auto-advance to Main Phase after drawing
        EnterMainPhase();
    }

    /// <summary>
    /// Main Phase:
    /// - Player can summon avatars, play magic cards.
    /// - BattleController handles summon logic.
    /// </summary>
    public void EnterMainPhase()
    {
        currentPhase = TurnPhase.Main;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);
        Debug.Log($"[GameManager] {currentPlayer} — MAIN PHASE");
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

        // Start combat selection
        if (CombatController.instance != null)
            CombatController.instance.BeginBattlePhase();

        Debug.Log($"[GameManager] {currentPlayer} — BATTLE PHASE");
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

        // TODO: Enforce discard-to-7 when hand limit UI is added

        // Pass the turn
        SwitchTurn();
    }

    // ════════════════════════════════════════════════════════════════
    //  TURN SWITCH
    // ════════════════════════════════════════════════════════════════

    private void SwitchTurn()
    {
        isFirstTurn   = false;
        turnNumber++;
        currentPlayer = (currentPlayer == TurnPlayer.Player1)
                        ? TurnPlayer.Player2
                        : TurnPlayer.Player1;

        Debug.Log($"[GameManager] Turn #{turnNumber} switched → {currentPlayer}");
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
    /// Check if either player has reached สหัส (all 5 LIFE cards flipped face-up).
    /// If so, the game is over — the other player wins.
    /// Returns true if game over.
    /// </summary>
    public bool CheckWinCondition()
    {
        if (player1.IsSahat)
        {
            Debug.Log("[GameManager] Player 1 is สหัส (all LIFE flipped)! PLAYER 2 WINS!");
            isGameOver = true;
            UIController.instance.ShowGameOver("PLAYER 2 WINS!\nPlayer 1's LIFE is destroyed! (สหัส)");
            return true;
        }

        if (player2.IsSahat)
        {
            Debug.Log("[GameManager] Player 2 is สหัส (all LIFE flipped)! PLAYER 1 WINS!");
            isGameOver = true;
            UIController.instance.ShowGameOver("PLAYER 1 WINS!\nPlayer 2's LIFE is destroyed! (สหัส)");
            return true;
        }

        return false;
    }
}
