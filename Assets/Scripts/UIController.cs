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
    //  SETUP
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        HidePaymentUI();

        // Wire the Next Phase button to GameManager
        if (nextPhaseButton != null)
            nextPhaseButton.onClick.AddListener(OnNextPhaseClicked);
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

        nextPhaseButtonText.text = phase switch
        {
            TurnPhase.Main   => "Battle Phase →",
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
}
