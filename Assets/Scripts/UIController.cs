using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages ALL 2D HUD elements on the Screen Space canvas.
///
/// Responsibilities:
///   1. Payment UI  — gem cost display during avatar summoning (existing)
///   2. Turn HUD    — whose turn it is (Player 1 / Player 2)
///   3. Phase Bar   — highlights the active phase (Draw/Main/Battle/End)
///   4. Next Phase button — visible during Main and Battle phases
/// </summary>
public class UIController : MonoBehaviour
{
    public static UIController instance;

    // ════════════════════════════════════════════════════════════════
    //  1. PAYMENT UI  (existing — summon cost panel)
    // ════════════════════════════════════════════════════════════════

    [Header("── Payment UI ──────────────────────")]
    public GameObject paymentPanel;
    public TMP_Text   playerGemPaidText;
    public TMP_Text   summonStatusText;

    // ════════════════════════════════════════════════════════════════
    //  2. TURN INDICATOR
    // ════════════════════════════════════════════════════════════════

    [Header("── Turn Indicator ───────────────────")]
    public TMP_Text turnPlayerText;     // e.g. "PLAYER 1'S TURN"

    // Player name colors
    private readonly Color player1Color = new Color(0.20f, 0.60f, 1.00f);   // Blue
    private readonly Color player2Color = new Color(1.00f, 0.35f, 0.35f);   // Red

    // ════════════════════════════════════════════════════════════════
    //  3. PHASE BAR
    // ════════════════════════════════════════════════════════════════

    [Header("── Phase Bar ───────────────────────")]
    public Image    drawPhasePanel;
    public Image    mainPhasePanel;
    public Image    battlePhasePanel;
    public Image    endPhasePanel;

    public TMP_Text drawPhaseLabel;
    public TMP_Text mainPhaseLabel;
    public TMP_Text battlePhaseLabel;
    public TMP_Text endPhaseLabel;

    // Phase bar colors
    private readonly Color activePhaseColor   = new Color(1.00f, 0.80f, 0.00f);  // Gold
    private readonly Color inactivePhaseColor = new Color(0.20f, 0.20f, 0.20f);  // Dark grey
    private readonly Color activeTextColor    = Color.black;
    private readonly Color inactiveTextColor  = new Color(0.60f, 0.60f, 0.60f);

    // ════════════════════════════════════════════════════════════════
    //  4. NEXT PHASE BUTTON
    // ════════════════════════════════════════════════════════════════

    [Header("── Next Phase Button ───────────────")]
    public Button   nextPhaseButton;
    public TMP_Text nextPhaseButtonText;

    // ════════════════════════════════════════════════════════════════
    //  5. COMBAT UI
    // ════════════════════════════════════════════════════════════════

    [Header("── Combat UI ──────────────────────")]
    public GameObject combatPanel;        // Panel shown during Battle Phase
    public TMP_Text   combatInfoText;     // "Select attacker" / combat results

    // ════════════════════════════════════════════════════════════════
    //  6. MULLIGAN UI
    // ════════════════════════════════════════════════════════════════

    [Header("── Mulligan UI ──────────────────────")]
    public GameObject mulliganPanel;        // Panel shown during setup mulligan
    public TMP_Text   mulliganInfoText;     // "Player 1 — Select cards to swap..."
    public Button     mulliganConfirmButton; // "Swap Selected"
    public Button     mulliganKeepButton;    // "Keep All"

    // ════════════════════════════════════════════════════════════════
    //  7. GAME OVER UI
    // ════════════════════════════════════════════════════════════════

    [Header("── Game Over UI ──────────────────────")]
    public GameObject gameOverPanel;        // Full-screen overlay when game ends
    public TMP_Text   gameOverText;         // "PLAYER X WINS!" message

    // ════════════════════════════════════════════════════════════════
    //  10. CARD PREVIEW (right-click zoom)
    // ════════════════════════════════════════════════════════════════

    [Header("── Card Preview ──────────────────────")]
    public GameObject cardPreviewPanel;     // Full panel (enable/disable)
    public Image      cardPreviewArt;       // Card art image
    public TMP_Text   cardPreviewName;      // Card name
    public TMP_Text   cardPreviewDesc;      // Description / effect text
    public TMP_Text   cardPreviewStats;     // "Power: X  |  Cost: Y  |  Gem: Z"
    public TMP_Text   cardPreviewType;      // "AVATAR — Red" or "MAGIC — Normal"

    // ════════════════════════════════════════════════════════════════
    //  9. GAME INFO HUD (deck count, hand count, turn number)
    // ════════════════════════════════════════════════════════════════

    [Header("── Game Info HUD ──────────────────────")]
    public TMP_Text turnNumberText;        // "Turn 3"
    public TMP_Text p1DeckCountText;       // "Deck: 15"
    public TMP_Text p1HandCountText;       // "Hand: 5"
    public TMP_Text p2DeckCountText;       // "Deck: 15"
    public TMP_Text p2HandCountText;       // "Hand: 5"
    public TMP_Text p1LifeCountText;       // "LIFE: 5/5"
    public TMP_Text p2LifeCountText;       // "LIFE: 5/5"

    // ════════════════════════════════════════════════════════════════
    //  11. HELL ZONE VIEWER (discard pile browser)
    // ════════════════════════════════════════════════════════════════

    [Header("── Hell Zone Viewer ──────────────────────")]
    public GameObject hellViewerPanel;
    public TMP_Text   hellViewerTitle;
    public TMP_Text   hellViewerCardList;    // Text list of all cards
    public Button     hellViewerCloseButton;

    // ════════════════════════════════════════════════════════════════
    //  8. DISCARD UI (End Phase — hand limit)
    // ════════════════════════════════════════════════════════════════

    [Header("── Discard UI ──────────────────────")]
    public GameObject discardPanel;          // Panel shown when hand > 7
    public TMP_Text   discardInfoText;       // "Player X — Discard N card(s)"
    public Button     discardConfirmButton;  // "Discard Selected"

    // ════════════════════════════════════════════════════════════════
    //  12. REACT CONFIRMATION UI
    // ════════════════════════════════════════════════════════════════

    [Header("── React Confirmation UI ──────────────")]
    public GameObject reactPanel;            // Panel shown when React can trigger
    public TMP_Text   reactInfoText;         // "React! [card] can destroy [avatar]. Activate?"
    public Button     reactActivateButton;   // "Activate" — trigger the React card
    public Button     reactKeepButton;       // "Keep" — save React for later

    // ════════════════════════════════════════════════════════════════
    //  SETUP
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        HidePaymentUI();
        HideCombatUI();
        HideMulliganUI();
        HideGameOverUI();
        HideDiscardUI();
        HideCardPreview();
        HideHellZoneViewer();
        HideReactUI();

        // Wire the Next Phase button to GameManager
        if (nextPhaseButton != null)
            nextPhaseButton.onClick.AddListener(OnNextPhaseClicked);

        // Wire the Mulligan buttons to GameManager
        if (mulliganConfirmButton != null)
            mulliganConfirmButton.onClick.AddListener(() => GameManager.instance.OnMulliganConfirmed());
        if (mulliganKeepButton != null)
            mulliganKeepButton.onClick.AddListener(() => GameManager.instance.OnMulliganSkipped());

        // Wire the Discard button to GameManager
        if (discardConfirmButton != null)
            discardConfirmButton.onClick.AddListener(() => GameManager.instance.OnDiscardConfirmed());

        // Wire Hell Zone viewer close button
        if (hellViewerCloseButton != null)
            hellViewerCloseButton.onClick.AddListener(HideHellZoneViewer);

        // Wire React / Hell Activation confirmation buttons (shared panel, routes by state)
        if (reactActivateButton != null)
            reactActivateButton.onClick.AddListener(() => {
                if (MagicController.instance == null) return;
                if (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm)
                    MagicController.instance.OnReactActivate();
                else if (MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation)
                    MagicController.instance.OnHellActivate();
            });
        if (reactKeepButton != null)
            reactKeepButton.onClick.AddListener(() => {
                if (MagicController.instance == null) return;
                if (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm)
                    MagicController.instance.OnReactKeep();
                else if (MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation)
                    MagicController.instance.OnHellKeep();
            });
    }

    // ════════════════════════════════════════════════════════════════
    //  CALLED BY GameManager — update everything at once
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by GameManager every time the phase or player changes.
    /// Updates the Turn Indicator and the Phase Bar.
    /// </summary>
    public void UpdateHUD(TurnPlayer player, TurnPhase phase)
    {
        UpdateTurnIndicator(player);
        UpdatePhaseBar(phase);
        UpdateNextPhaseButton(phase);
        UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  TURN INDICATOR
    // ════════════════════════════════════════════════════════════════

    private void UpdateTurnIndicator(TurnPlayer player)
    {
        if (turnPlayerText == null) return;

        if (player == TurnPlayer.Player1)
        {
            turnPlayerText.text  = "PLAYER 1'S TURN";
            turnPlayerText.color = player1Color;
        }
        else
        {
            turnPlayerText.text  = "PLAYER 2'S TURN";
            turnPlayerText.color = player2Color;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE BAR
    // ════════════════════════════════════════════════════════════════

    private void UpdatePhaseBar(TurnPhase phase)
    {
        SetPhaseActive(drawPhasePanel,   drawPhaseLabel,   phase == TurnPhase.Draw);
        SetPhaseActive(mainPhasePanel,   mainPhaseLabel,   phase == TurnPhase.Main);
        SetPhaseActive(battlePhasePanel, battlePhaseLabel, phase == TurnPhase.Battle);
        SetPhaseActive(endPhasePanel,    endPhaseLabel,    phase == TurnPhase.End);
    }

    private void SetPhaseActive(Image panel, TMP_Text label, bool isActive)
    {
        if (panel != null)
            panel.color = isActive ? activePhaseColor : inactivePhaseColor;

        if (label != null)
            label.color = isActive ? activeTextColor : inactiveTextColor;
    }

    // ════════════════════════════════════════════════════════════════
    //  NEXT PHASE BUTTON
    // ════════════════════════════════════════════════════════════════

    private void UpdateNextPhaseButton(TurnPhase phase)
    {
        if (nextPhaseButton == null) return;

        // Only show during Main and Battle (Draw is auto, End auto-triggers SwitchTurn)
        bool showButton = phase == TurnPhase.Main || phase == TurnPhase.Battle;
        nextPhaseButton.gameObject.SetActive(showButton);

        if (nextPhaseButtonText == null) return;

        // P1 Turn 1: can't enter Battle Phase, so show "End Phase →" during Main
        GameManager gm = GameManager.instance;
        bool p1SkipBattle = gm != null && gm.isFirstTurn && gm.currentPlayer == TurnPlayer.Player1;

        nextPhaseButtonText.text = phase switch
        {
            TurnPhase.Main   => p1SkipBattle ? "End Phase →" : "Battle Phase →",
            TurnPhase.Battle => "End Phase →",
            _                => "Next →"
        };
    }

    private void OnNextPhaseClicked()
    {
        if (GameManager.instance != null)
            GameManager.instance.OnNextPhasePressed();
    }

    // ════════════════════════════════════════════════════════════════
    //  PAYMENT UI  (unchanged from original)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show during CostStep — displays gem payment progress.</summary>
    public void ShowPaymentUI(string avatarName, int currentGem, int requiredGem)
    {
        if (paymentPanel != null) paymentPanel.SetActive(true);

        if (playerGemPaidText != null)
            playerGemPaidText.text = $"Gems Paid: {currentGem} / {requiredGem}";

        if (summonStatusText != null)
            summonStatusText.text = $"Summoning <b>{avatarName}</b>\nDrag cards to the Hell Point to pay.";
    }

    /// <summary>Show when gem payment is complete — avatar is ready to place.</summary>
    public void ShowReadyToPlace(string avatarName)
    {
        if (paymentPanel != null) paymentPanel.SetActive(true);

        if (summonStatusText != null)
            summonStatusText.text = $"<color=green><b>{avatarName}</b> is READY!</color>\nDrag it to a board slot.";
    }

    /// <summary>Hide the payment panel entirely.</summary>
    public void HidePaymentUI()
    {
        if (paymentPanel != null) paymentPanel.SetActive(false);

        if (playerGemPaidText != null) playerGemPaidText.text = "";
        if (summonStatusText  != null) summonStatusText.text  = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  MAGIC UI  (reuses combat panel — they never overlap)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show magic selection/status message (reuses combat panel).</summary>
    public void ShowMagicUI(string message) => ShowCombatUI(message);

    /// <summary>Hide magic UI.</summary>
    public void HideMagicUI() => HideCombatUI();

    // ════════════════════════════════════════════════════════════════
    //  REACT CONFIRMATION UI
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show the React confirmation panel with Activate/Keep buttons.</summary>
    public void ShowReactUI(string message)
    {
        if (reactPanel != null) reactPanel.SetActive(true);
        if (reactInfoText != null) reactInfoText.text = message;
    }

    /// <summary>Hide the React confirmation panel.</summary>
    public void HideReactUI()
    {
        if (reactPanel != null) reactPanel.SetActive(false);
        if (reactInfoText != null) reactInfoText.text = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  COMBAT UI  (Battle Phase info panel)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show the combat panel with a status message.</summary>
    public void ShowCombatUI(string message)
    {
        if (combatPanel != null) combatPanel.SetActive(true);
        if (combatInfoText != null) combatInfoText.text = message;
    }

    /// <summary>Hide the combat panel entirely.</summary>
    public void HideCombatUI()
    {
        if (combatPanel != null) combatPanel.SetActive(false);
        if (combatInfoText != null) combatInfoText.text = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  MULLIGAN UI  (Setup Phase — card swap)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show the mulligan panel with instructions for the current player.</summary>
    public void ShowMulliganUI(string message)
    {
        if (mulliganPanel != null) mulliganPanel.SetActive(true);
        if (mulliganInfoText != null) mulliganInfoText.text = message;

        // Hide the next phase button during setup
        if (nextPhaseButton != null)
            nextPhaseButton.gameObject.SetActive(false);
    }

    /// <summary>Hide the mulligan panel entirely.</summary>
    public void HideMulliganUI()
    {
        if (mulliganPanel != null) mulliganPanel.SetActive(false);
        if (mulliganInfoText != null) mulliganInfoText.text = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  GAME OVER UI  (Win / Loss — สหัส)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show the game over overlay with the winner message.</summary>
    public void ShowGameOver(string message)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText != null) gameOverText.text = message;

        // Hide all other combat/phase UI
        HideCombatUI();
        if (nextPhaseButton != null)
            nextPhaseButton.gameObject.SetActive(false);
    }

    /// <summary>Hide the game over panel entirely.</summary>
    public void HideGameOverUI()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (gameOverText != null) gameOverText.text = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  DISCARD UI  (End Phase — hand limit = 7)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show the discard panel when hand exceeds 7 cards.</summary>
    public void ShowDiscardUI(string message)
    {
        if (discardPanel != null) discardPanel.SetActive(true);
        if (discardInfoText != null) discardInfoText.text = message;

        // Hide the next phase button during discard
        if (nextPhaseButton != null)
            nextPhaseButton.gameObject.SetActive(false);

        // Disable confirm button until correct count is selected
        if (discardConfirmButton != null)
            discardConfirmButton.interactable = false;
    }

    /// <summary>Hide the discard panel entirely.</summary>
    public void HideDiscardUI()
    {
        if (discardPanel != null) discardPanel.SetActive(false);
        if (discardInfoText != null) discardInfoText.text = "";
    }

    /// <summary>
    /// Called by Card.ToggleDiscardSelection() whenever a card is marked/unmarked.
    /// Updates the counter text and enables/disables the confirm button.
    /// </summary>
    public void UpdateDiscardCount()
    {
        GameManager gm = GameManager.instance;
        if (gm == null || !gm.isDiscardPhase) return;

        Player cp = gm.CurrentPlayerObj;
        int handSize = cp.hand.heldCards.Count;
        int required = handSize - 7;
        int selected = cp.hand.GetMarkedForDiscard().Count;

        if (discardInfoText != null)
            discardInfoText.text =
                $"{gm.CurrentPlayerName()} — Discard {required} card(s)\n" +
                $"Selected: {selected} / {required}";

        // Enable confirm only when the exact count is selected
        if (discardConfirmButton != null)
            discardConfirmButton.interactable = (selected == required);
    }

    // ════════════════════════════════════════════════════════════════
    //  HELL ZONE VIEWER — discard pile browser
    // ════════════════════════════════════════════════════════════════

    public void ShowHellZoneViewer(Player player)
    {
        if (hellViewerPanel == null) return;
        hellViewerPanel.SetActive(true);

        string playerName = player.playerId == TurnPlayer.Player1 ? "Player 1" : "Player 2";
        int count = player.hellZone != null ? player.hellZone.activeCards.Count : 0;

        if (hellViewerTitle != null)
            hellViewerTitle.text = $"{playerName}'s Hell Zone ({count} cards)";

        if (hellViewerCardList != null)
        {
            if (count == 0)
            {
                hellViewerCardList.text = "(empty)";
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < player.hellZone.activeCards.Count; i++)
                {
                    Card c = player.hellZone.activeCards[i];
                    string symbolStr = c.cardSymbol != CardSymbol.None
                        ? $" <color=#FFCC00>{GetSymbolThaiName(c.cardSymbol)}</color>"
                        : "";
                    string typeTag = c.cardType switch
                    {
                        CardType.Avatar => $"<color=#FF6666>AVA</color> Pw:{c.power}",
                        CardType.Magic  => "<color=#6699FF>MAG</color>",
                        CardType.Life   => "<color=#999999>LIFE</color>",
                        _               => "???"
                    };
                    sb.AppendLine($"{i + 1}. {c.cardName}  [{typeTag}]{symbolStr}  Gem:{c.gem}");
                }
                hellViewerCardList.text = sb.ToString();
            }
        }
    }

    public void HideHellZoneViewer()
    {
        if (hellViewerPanel != null) hellViewerPanel.SetActive(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  CARD PREVIEW — right-click zoom detail
    // ════════════════════════════════════════════════════════════════

    /// <summary>Show a card's full details in the preview panel.</summary>
    public void ShowCardPreview(Card card)
    {
        if (cardPreviewPanel == null) return;
        cardPreviewPanel.SetActive(true);

        if (cardPreviewName != null)
            cardPreviewName.text = card.cardName;

        if (cardPreviewDesc != null)
            cardPreviewDesc.text = string.IsNullOrEmpty(card.description) ? "(no description)" : card.description;

        if (cardPreviewArt != null && card.characterArt != null)
            cardPreviewArt.sprite = card.characterArt.sprite;

        if (cardPreviewType != null)
        {
            string symbolTag = card.cardSymbol != CardSymbol.None
                ? $"  [{GetSymbolThaiName(card.cardSymbol)}]"
                : "";

            cardPreviewType.text = card.cardType switch
            {
                CardType.Avatar => $"AVATAR — {card.avatarColor}{symbolTag}",
                CardType.Magic  => $"MAGIC — {(card.magicSO != null ? card.magicSO.magicType.ToString() : "Normal")}{symbolTag}",
                CardType.Life   => $"LIFE CARD{symbolTag}",
                _               => card.cardType.ToString()
            };
        }

        if (cardPreviewStats != null)
        {
            string stats = "";
            if (card.cardType == CardType.Avatar)
                stats = $"Power: {card.power}  |  Cost: {card.cost}  |  Gem: {card.gem} ({card.gemColor})";
            else if (card.cardType == CardType.Magic)
                stats = $"Gem: {card.gem} ({card.gemColor})";
            else
                stats = $"Gem: {card.gem}";
            cardPreviewStats.text = stats;
        }
    }

    /// <summary>Get the Thai display name for a card symbol.</summary>
    private string GetSymbolThaiName(CardSymbol symbol)
    {
        return symbol switch
        {
            CardSymbol.Giant    => "ยักษ์",
            CardSymbol.God      => "เทพ",
            CardSymbol.Human    => "คน",
            CardSymbol.Devil    => "นรก",
            CardSymbol.Ghost    => "ผี",
            CardSymbol.Sorcerer => "จอมเวทย์",
            _                   => symbol.ToString()
        };
    }

    /// <summary>Hide the card preview panel.</summary>
    public void HideCardPreview()
    {
        if (cardPreviewPanel != null)
            cardPreviewPanel.SetActive(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  GAME INFO HUD — deck/hand/life/turn counters
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Refresh all info counters (deck size, hand size, life remaining, turn number).
    /// Called automatically by UpdateHUD() on every phase/turn change.
    /// Can also be called manually after any card movement (draw, discard, etc.).
    /// </summary>
    public void UpdateGameInfo()
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        // Turn number
        if (turnNumberText != null)
            turnNumberText.text = $"Turn {gm.turnNumber}";

        // Player 1 info
        if (gm.player1 != null)
        {
            if (p1DeckCountText != null && gm.player1.deck != null)
            {
                int remaining = gm.player1.deck.CardsRemaining;
                p1DeckCountText.text = $"Deck: {remaining}";
                p1DeckCountText.color = remaining <= 3 ? Color.red : Color.white;
            }
            if (p1HandCountText != null && gm.player1.hand != null)
                p1HandCountText.text = $"Hand: {gm.player1.hand.heldCards.Count}";
            if (p1LifeCountText != null)
            {
                int remaining = 5 - gm.player1.LifeCardsFlipped;
                p1LifeCountText.text = $"LIFE: {remaining}/5";
                p1LifeCountText.color = remaining <= 2 ? Color.red : remaining <= 3 ? Color.yellow : Color.white;
            }
        }

        // Player 2 info
        if (gm.player2 != null)
        {
            if (p2DeckCountText != null && gm.player2.deck != null)
            {
                int remaining = gm.player2.deck.CardsRemaining;
                p2DeckCountText.text = $"Deck: {remaining}";
                p2DeckCountText.color = remaining <= 3 ? Color.red : Color.white;
            }
            if (p2HandCountText != null && gm.player2.hand != null)
                p2HandCountText.text = $"Hand: {gm.player2.hand.heldCards.Count}";
            if (p2LifeCountText != null)
            {
                int remaining = 5 - gm.player2.LifeCardsFlipped;
                p2LifeCountText.text = $"LIFE: {remaining}/5";
                p2LifeCountText.color = remaining <= 2 ? Color.red : remaining <= 3 ? Color.yellow : Color.white;
            }
        }
    }
}
