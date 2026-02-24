using UnityEngine;

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
/// Manages turn order and phase transitions.
/// Tells UIController to update the HUD whenever state changes.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    // ── GAME STATE ─────────────────────────────────────────────────
    [Header("Current State (Read Only)")]
    public TurnPlayer currentPlayer = TurnPlayer.Player1;
    public TurnPhase  currentPhase  = TurnPhase.Draw;
    public bool       isFirstTurn   = true;

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
    //  GAME START
    // ════════════════════════════════════════════════════════════════

    public void StartGame()
    {
        currentPlayer = TurnPlayer.Player1;
        isFirstTurn   = true;
        EnterDrawPhase();
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE TRANSITIONS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw Phase:
    /// - If hand has fewer than 3 cards → draw until 3
    /// - If hand has 3+ cards → draw 1
    /// - First turn for Player 1 → draw 2 instead
    /// - Avatars wake up (untap) — TODO when avatar tapped state is added
    /// </summary>
    private void EnterDrawPhase()
    {
        currentPhase = TurnPhase.Draw;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);

        // Draw logic (rulebook)
        var hand = HandController.instance;
        var deck = DeckController.instance;

        if (isFirstTurn && currentPlayer == TurnPlayer.Player1)
        {
            // First turn: Player 1 draws 2
            deck.DrawCardToHand();
            deck.DrawCardToHand();
        }
        else
        {
            int currentHandSize = hand.heldCards.Count;
            if (currentHandSize < 3)
            {
                // Draw up to 3
                int cardsToDraw = 3 - currentHandSize;
                for (int i = 0; i < cardsToDraw; i++)
                    deck.DrawCardToHand();
            }
            else
            {
                // Draw exactly 1
                deck.DrawCardToHand();
            }
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
    /// - Avatars cannot attack on their first turn on the field (summon sickness).
    /// </summary>
    public void EnterBattlePhase()
    {
        currentPhase = TurnPhase.Battle;
        UIController.instance.UpdateHUD(currentPlayer, currentPhase);
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
        currentPlayer = (currentPlayer == TurnPlayer.Player1)
                        ? TurnPlayer.Player2
                        : TurnPlayer.Player1;

        Debug.Log($"[GameManager] Turn switched → {currentPlayer}");
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
        switch (currentPhase)
        {
            case TurnPhase.Main:   EnterBattlePhase(); break;
            case TurnPhase.Battle: EnterEndPhase();    break;
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
}
