using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
    public TMP_Text   hellViewerCardList;    // Text list of all cards (fallback)
    public Button     hellViewerCloseButton;
    public RectTransform hellViewerContent;  // ScrollRect content parent for card entries
    public Button        deckSearchSummonButton; // Summon button (only visible during deck search)

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
    //  13. LIFE CARD REVEAL PANEL
    // ════════════════════════════════════════════════════════════════

    [Header("── Life Card Reveal ──────────────")]
    public GameObject lifeRevealPanel;              // Panel shown when LIFE card is flipped
    public Image      lifeRevealArt;                // Card art
    public TMP_Text   lifeRevealTitle;              // Card title (e.g. "ไม่นะ โปโป้!")
    public TMP_Text   lifeRevealFlavor;             // Flavor text (Thai narrative)
    public TMP_Text   lifeRevealEffect;             // Common effect text
    public Button     lifeRevealContinueButton;     // "Continue" button

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
        HideDeckSearchState();
        HideReactUI();
        HideLifeRevealPanel();

        // Life Reveal: click-anywhere-to-close is handled by CombatController.Update()
        // Hide the Continue button if it still exists in the scene
        if (lifeRevealContinueButton != null)
            lifeRevealContinueButton.gameObject.SetActive(false);

        // ── Clear all "New Text" placeholders at startup ──
        if (turnNumberText   != null) turnNumberText.text   = "";
        if (p1DeckCountText  != null) p1DeckCountText.text  = "";
        if (p1HandCountText  != null) p1HandCountText.text  = "";
        if (p1LifeCountText  != null) p1LifeCountText.text  = "";
        if (p2DeckCountText  != null) p2DeckCountText.text  = "";
        if (p2HandCountText  != null) p2HandCountText.text  = "";
        if (p2LifeCountText  != null) p2LifeCountText.text  = "";
        if (turnPlayerText   != null) turnPlayerText.text   = "";
        if (lifeRevealTitle  != null) lifeRevealTitle.text  = "";
        if (lifeRevealFlavor != null) lifeRevealFlavor.text = "";
        if (lifeRevealEffect != null) lifeRevealEffect.text = "";

        // Wire the Deck Search Summon button
        if (deckSearchSummonButton != null)
            deckSearchSummonButton.onClick.AddListener(OnDeckSearchSummonClicked);

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
        // Priority: Avatar Hell summon → Magic Hell activation → React confirm
        if (reactActivateButton != null)
            reactActivateButton.onClick.AddListener(() => {
                // Avatar Hell summon takes priority (uses same AwaitingHellActivation state)
                if (AvatarAbilityController.instance != null
                    && AvatarAbilityController.instance.IsAwaitingAvatarHellSummon)
                {
                    AvatarAbilityController.instance.OnAvatarHellSummonActivate();
                    return;
                }
                if (MagicController.instance == null) return;
                if (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm)
                    MagicController.instance.OnReactActivate();
                else if (MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation)
                    MagicController.instance.OnHellActivate();
            });
        if (reactKeepButton != null)
            reactKeepButton.onClick.AddListener(() => {
                // Avatar Hell summon takes priority
                if (AvatarAbilityController.instance != null
                    && AvatarAbilityController.instance.IsAwaitingAvatarHellSummon)
                {
                    AvatarAbilityController.instance.OnAvatarHellSummonKeep();
                    return;
                }
                if (MagicController.instance == null) return;
                if (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm)
                    MagicController.instance.OnReactKeep();
                else if (MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation)
                    MagicController.instance.OnHellKeep();
            });
    }

    // ════════════════════════════════════════════════════════════════
    //  CLICK-TO-DISMISS: hide combat/magic info panel on empty click
    // ════════════════════════════════════════════════════════════════

    private void Update()
    {
        // Only care when combat panel is visible
        if (combatPanel == null || !combatPanel.activeSelf) return;

        // Don't dismiss if React panel buttons are showing (interactive panel)
        if (reactPanel != null && reactPanel.activeSelf) return;

        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        // Don't dismiss if clicking on a UI element (button, card, etc.)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        HideCombatUI();
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
    //  LIFE CARD REVEAL PANEL
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Show the Life Card Reveal panel with card art, title, flavor text, and effect.
    /// Called when a LIFE card is flipped face-up during Battle Phase.
    /// </summary>
    public void ShowLifeRevealPanel(Card lifeCard)
    {
        if (lifeRevealPanel != null) lifeRevealPanel.SetActive(true);
        if (lifeRevealArt != null && lifeCard.characterArt != null)
            lifeRevealArt.sprite = lifeCard.characterArt.sprite;
        if (lifeRevealTitle != null)
            lifeRevealTitle.text = !string.IsNullOrEmpty(lifeCard.cardName) ? lifeCard.cardName : "LIFE Card";
        if (lifeRevealFlavor != null)
            lifeRevealFlavor.text = !string.IsNullOrEmpty(lifeCard.flavorText) ? lifeCard.flavorText : "";
        // Effect text is hidden here — shown only in the combat result panel after dismiss
        if (lifeRevealEffect != null)
            lifeRevealEffect.text = "";
    }

    /// <summary>Hide the Life Card Reveal panel.</summary>
    public void HideLifeRevealPanel()
    {
        if (lifeRevealPanel != null) lifeRevealPanel.SetActive(false);
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

    /// <summary>Track spawned card entries so we can clean them up.</summary>
    private readonly List<GameObject> hellViewerEntries = new List<GameObject>();

    public void ShowHellZoneViewer(Player player)
    {
        if (hellViewerPanel == null) return;
        hellViewerPanel.SetActive(true);

        string playerName = player.playerId == TurnPlayer.Player1 ? "Player 1" : "Player 2";
        int count = player.hellZone != null ? player.hellZone.activeCards.Count : 0;

        if (hellViewerTitle != null)
            hellViewerTitle.text = $"{playerName}'s Hell Zone ({count} cards)";

        // Clear previous entries
        ClearHellViewerEntries();

        // Ensure scroll content has required layout components
        if (hellViewerContent != null)
            EnsureHellViewerLayout();

        // If we have a scroll content area, build clickable card entries
        if (hellViewerContent != null)
        {
            if (hellViewerCardList != null)
                hellViewerCardList.gameObject.SetActive(false);

            if (count == 0)
            {
                CreateHellViewerLabel("(empty)");
            }
            else
            {
                for (int i = 0; i < player.hellZone.activeCards.Count; i++)
                {
                    Card c = player.hellZone.activeCards[i];
                    CreateHellViewerCardEntry(c, i + 1);
                }
            }
        }
        // Text list fallback (hellViewerCardList wired in Inspector)
        else if (hellViewerCardList != null)
        {
            hellViewerCardList.gameObject.SetActive(true);
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

    /// <summary>
    /// Ensure hellViewerContent has VerticalLayoutGroup + ContentSizeFitter.
    /// The Scroll View (Viewport, Mask, ScrollRect) must be set up in the Editor.
    /// </summary>
    private bool _hellLayoutReady;
    private void EnsureHellViewerLayout()
    {
        if (_hellLayoutReady || hellViewerContent == null) return;
        _hellLayoutReady = true;

        // Add VerticalLayoutGroup so card entries stack properly
        if (hellViewerContent.GetComponent<VerticalLayoutGroup>() == null)
        {
            var vlg = hellViewerContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // ContentSizeFitter so content grows to fit entries
        if (hellViewerContent.GetComponent<ContentSizeFitter>() == null)
        {
            var csf = hellViewerContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    /// <summary>Create a clickable card entry row in the hell viewer scroll content.</summary>
    private void CreateHellViewerCardEntry(Card card, int index)
    {
        // Row container (Button for click handling)
        GameObject row = new GameObject($"HellEntry_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
        row.transform.SetParent(hellViewerContent, false);
        hellViewerEntries.Add(row);

        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, 80);

        // Row background — dark semi-transparent
        Image rowBg = row.GetComponent<Image>();
        rowBg.color = new Color(0.15f, 0.15f, 0.20f, 0.9f);

        // Button click → show full card preview
        Button rowBtn = row.GetComponent<Button>();
        Card capturedCard = card; // Capture for closure
        rowBtn.onClick.AddListener(() => ShowCardPreview(capturedCard));

        // Button color states
        ColorBlock cb = rowBtn.colors;
        cb.normalColor = new Color(0.15f, 0.15f, 0.20f, 0.9f);
        cb.highlightedColor = new Color(0.30f, 0.30f, 0.40f, 1f);
        cb.pressedColor = new Color(0.10f, 0.10f, 0.15f, 1f);
        cb.selectedColor = cb.normalColor;
        rowBtn.colors = cb;

        // Use HorizontalLayoutGroup for row layout
        var hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Card art thumbnail
        GameObject artObj = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artObj.transform.SetParent(row.transform, false);
        Image artImage = artObj.GetComponent<Image>();
        if (card.characterArt != null && card.characterArt.sprite != null)
            artImage.sprite = card.characterArt.sprite;
        else
            artImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        RectTransform artRT = artObj.GetComponent<RectTransform>();
        artRT.sizeDelta = new Vector2(56, 72);
        var artLE = artObj.AddComponent<UnityEngine.UI.LayoutElement>();
        artLE.preferredWidth = 56;
        artLE.preferredHeight = 72;

        // Info text (name + type + stats)
        GameObject textObj = new GameObject("Info", typeof(RectTransform));
        textObj.transform.SetParent(row.transform, false);
        TMP_Text infoText = textObj.AddComponent<TextMeshProUGUI>();

        string symbolStr = card.cardSymbol != CardSymbol.None
            ? $"  <color=#FFCC00>{GetSymbolThaiName(card.cardSymbol)}</color>"
            : "";
        string typeTag = card.cardType switch
        {
            CardType.Avatar => $"<color=#FF6666>AVATAR</color>  Pw:{card.power}  Cost:{card.cost}",
            CardType.Magic  => $"<color=#6699FF>MAGIC</color>  {(card.magicSO != null ? card.magicSO.magicType.ToString() : "")}",
            CardType.Life   => "<color=#999999>LIFE</color>",
            _               => "???"
        };

        infoText.text = $"<b>{card.cardName}</b>\n<size=80%>{typeTag}{symbolStr}  Gem:{card.gem}</size>";
        infoText.fontSize = 16;
        infoText.color = Color.white;
        infoText.alignment = TextAlignmentOptions.MidlineLeft;
        infoText.enableWordWrapping = false;
        infoText.overflowMode = TextOverflowModes.Ellipsis;
        infoText.raycastTarget = false;

        var textLE = textObj.AddComponent<UnityEngine.UI.LayoutElement>();
        textLE.preferredWidth = 300;
        textLE.flexibleWidth = 1;
    }

    /// <summary>Create a simple text label in the hell viewer scroll content.</summary>
    private void CreateHellViewerLabel(string text)
    {
        GameObject labelObj = new GameObject("HellLabel", typeof(RectTransform));
        labelObj.transform.SetParent(hellViewerContent, false);
        hellViewerEntries.Add(labelObj);

        TMP_Text label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18;
        label.color = new Color(0.6f, 0.6f, 0.6f);
        label.alignment = TextAlignmentOptions.Center;

        RectTransform rt = labelObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 40);
    }

    /// <summary>Remove all dynamically created hell viewer entries.</summary>
    private void ClearHellViewerEntries()
    {
        foreach (var entry in hellViewerEntries)
        {
            if (entry != null) Destroy(entry);
        }
        hellViewerEntries.Clear();
    }

    public void HideHellZoneViewer()
    {
        // If in deck search mode, cancel the search via MagicController
        if (_deckSearchMode)
        {
            HideDeckSearchPanel();
            if (MagicController.instance != null
                && MagicController.instance.magicState == MagicPlayState.SelectingDeckSearch)
            {
                MagicController.instance.CancelDeckSearch();
            }
            return;
        }

        // If in hell search mode, cancel the Juti hell search
        if (_hellSearchMode)
        {
            HideHellSearchPanel();
            if (AvatarAbilityController.instance != null)
                AvatarAbilityController.instance.CancelJutiHellSearch();
            return;
        }

        if (hellViewerPanel != null) hellViewerPanel.SetActive(false);
        ClearHellViewerEntries();
    }

    // ════════════════════════════════════════════════════════════════
    //  DECK SEARCH PANEL — interactive card selection from deck
    // ════════════════════════════════════════════════════════════════

    private bool _deckSearchMode;                            // Deck search (AvatarCardSO)
    private AvatarCardSO _selectedDeckSearchSO;
    private GameObject _selectedDeckSearchRow;
    private System.Action<AvatarCardSO> _onDeckSearchConfirm;

    private bool _hellSearchMode;                            // Hell search (Card)
    private Card _selectedHellSearchCard;
    private GameObject _selectedHellSearchRow;
    private System.Action<Card> _onHellSearchConfirm;

    /// <summary>
    /// Show the deck search panel populated with matching AvatarCardSOs.
    /// Reuses the hellViewerPanel layout with an added Summon button.
    /// </summary>
    public void ShowDeckSearchPanel(List<AvatarCardSO> matches, string title, System.Action<AvatarCardSO> onConfirm)
    {
        if (hellViewerPanel == null) return;

        _deckSearchMode = true;
        _selectedDeckSearchSO = null;
        _selectedDeckSearchRow = null;
        _onDeckSearchConfirm = onConfirm;

        hellViewerPanel.SetActive(true);

        if (hellViewerTitle != null)
            hellViewerTitle.text = title;

        // Clear previous entries
        ClearHellViewerEntries();

        // Ensure scroll content has required layout components
        if (hellViewerContent != null)
            EnsureHellViewerLayout();

        // Show Summon button (disabled until selection)
        if (deckSearchSummonButton != null)
        {
            deckSearchSummonButton.gameObject.SetActive(true);
            deckSearchSummonButton.interactable = false;
        }

        // Hide text fallback
        if (hellViewerCardList != null)
            hellViewerCardList.gameObject.SetActive(false);

        // Build card entries from SOs
        if (hellViewerContent != null)
        {
            if (matches.Count == 0)
            {
                CreateHellViewerLabel("(no matching cards found)");
            }
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    CreateDeckSearchCardEntry(matches[i], i + 1);
                }
            }
        }
    }

    /// <summary>Create a clickable card entry row from an AvatarCardSO for the deck search panel.</summary>
    private void CreateDeckSearchCardEntry(AvatarCardSO so, int index)
    {
        // Row container (Button for click handling)
        GameObject row = new GameObject($"DeckEntry_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
        row.transform.SetParent(hellViewerContent, false);
        hellViewerEntries.Add(row);

        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, 80);

        // Row background — dark semi-transparent
        Image rowBg = row.GetComponent<Image>();
        rowBg.color = new Color(0.15f, 0.15f, 0.20f, 0.9f);

        // Button click → select this card
        Button rowBtn = row.GetComponent<Button>();
        AvatarCardSO capturedSO = so;
        GameObject capturedRow = row;
        rowBtn.onClick.AddListener(() => SelectDeckSearchCard(capturedSO, capturedRow));

        // Button color states
        ColorBlock cb = rowBtn.colors;
        cb.normalColor = new Color(0.15f, 0.15f, 0.20f, 0.9f);
        cb.highlightedColor = new Color(0.30f, 0.30f, 0.40f, 1f);
        cb.pressedColor = new Color(0.10f, 0.10f, 0.15f, 1f);
        cb.selectedColor = cb.normalColor;
        rowBtn.colors = cb;

        // Use HorizontalLayoutGroup for row layout
        var hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Card art thumbnail
        GameObject artObj = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artObj.transform.SetParent(row.transform, false);
        Image artImage = artObj.GetComponent<Image>();
        if (so.cardCharacterSprite != null)
            artImage.sprite = so.cardCharacterSprite;
        else
            artImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        RectTransform artRT = artObj.GetComponent<RectTransform>();
        artRT.sizeDelta = new Vector2(56, 72);
        var artLE = artObj.AddComponent<UnityEngine.UI.LayoutElement>();
        artLE.preferredWidth = 56;
        artLE.preferredHeight = 72;

        // Info text (name + type + stats)
        GameObject textObj = new GameObject("Info", typeof(RectTransform));
        textObj.transform.SetParent(row.transform, false);
        TMP_Text infoText = textObj.AddComponent<TextMeshProUGUI>();

        string symbolStr = so.cardSymbol != CardSymbol.None
            ? $"  <color=#FFCC00>{GetSymbolThaiName(so.cardSymbol)}</color>"
            : "";
        string typeTag = $"<color=#FF6666>AVATAR</color>  Pw:{so.power}  Cost:{so.cost}";

        infoText.text = $"<b>{so.cardName}</b>\n<size=80%>{typeTag}{symbolStr}  Gem:{so.gem}</size>";
        infoText.fontSize = 16;
        infoText.color = Color.white;
        infoText.alignment = TextAlignmentOptions.MidlineLeft;
        infoText.enableWordWrapping = false;
        infoText.overflowMode = TextOverflowModes.Ellipsis;
        infoText.raycastTarget = false;

        var textLE = textObj.AddComponent<UnityEngine.UI.LayoutElement>();
        textLE.preferredWidth = 300;
        textLE.flexibleWidth = 1;
    }

    /// <summary>Select a card in the deck search panel — highlight row and show preview.</summary>
    private void SelectDeckSearchCard(AvatarCardSO so, GameObject row)
    {
        // Un-highlight previous selection
        if (_selectedDeckSearchRow != null)
        {
            Image prevBg = _selectedDeckSearchRow.GetComponent<Image>();
            if (prevBg != null) prevBg.color = new Color(0.15f, 0.15f, 0.20f, 0.9f);
        }

        _selectedDeckSearchSO = so;
        _selectedDeckSearchRow = row;

        // Highlight selected row
        Image rowBg = row.GetComponent<Image>();
        if (rowBg != null) rowBg.color = new Color(0.25f, 0.40f, 0.25f, 0.95f); // Green tint

        // Enable Summon button
        if (deckSearchSummonButton != null)
            deckSearchSummonButton.interactable = true;

        // Show card preview from SO data
        ShowCardPreviewFromSO(so);
    }

    /// <summary>Show the card preview panel populated from AvatarCardSO fields.</summary>
    public void ShowCardPreviewFromSO(AvatarCardSO so)
    {
        if (cardPreviewPanel == null) return;
        cardPreviewPanel.SetActive(true);

        if (cardPreviewName != null)
            cardPreviewName.text = so.cardName;

        if (cardPreviewDesc != null)
            cardPreviewDesc.text = string.IsNullOrEmpty(so.description) ? "(no description)" : so.description;

        if (cardPreviewArt != null && so.cardCharacterSprite != null)
            cardPreviewArt.sprite = so.cardCharacterSprite;

        if (cardPreviewType != null)
        {
            string symbolTag = so.cardSymbol != CardSymbol.None
                ? $"  [{GetSymbolThaiName(so.cardSymbol)}]"
                : "";
            cardPreviewType.text = $"AVATAR — {so.avatarColor}{symbolTag}";
        }

        if (cardPreviewStats != null)
            cardPreviewStats.text = $"Power: {so.power}  |  Cost: {so.cost}  |  Gem: {so.gem} ({so.gemColor})";
    }

    /// <summary>Handle Summon button click — routes to deck search or hell search callback.</summary>
    private void OnDeckSearchSummonClicked()
    {
        // Deck search mode (AvatarCardSO)
        if (_deckSearchMode && _selectedDeckSearchSO != null && _onDeckSearchConfirm != null)
        {
            System.Action<AvatarCardSO> callback = _onDeckSearchConfirm;
            AvatarCardSO selected = _selectedDeckSearchSO;
            HideSearchPanel();
            callback.Invoke(selected);
            return;
        }

        // Hell search mode (Card)
        if (_hellSearchMode && _selectedHellSearchCard != null && _onHellSearchConfirm != null)
        {
            System.Action<Card> callback = _onHellSearchConfirm;
            Card selected = _selectedHellSearchCard;
            HideSearchPanel();
            callback.Invoke(selected);
            return;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  HELL SEARCH PANEL — interactive card selection from Hell zone
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Show the hell search panel populated with matching Card objects from Hell.
    /// Reuses the hellViewerPanel layout with Summon button (same as deck search).
    /// </summary>
    public void ShowHellSearchPanel(List<Card> matches, string title, System.Action<Card> onConfirm)
    {
        if (hellViewerPanel == null) return;

        _hellSearchMode = true;
        _deckSearchMode = false;
        _selectedHellSearchCard = null;
        _selectedHellSearchRow = null;
        _onHellSearchConfirm = onConfirm;

        hellViewerPanel.SetActive(true);

        if (hellViewerTitle != null)
            hellViewerTitle.text = title;

        // Clear previous entries
        ClearHellViewerEntries();

        // Ensure scroll content has required layout components
        if (hellViewerContent != null)
            EnsureHellViewerLayout();

        // Show Summon button (disabled until selection)
        if (deckSearchSummonButton != null)
        {
            deckSearchSummonButton.gameObject.SetActive(true);
            deckSearchSummonButton.interactable = false;
        }

        // Hide text fallback
        if (hellViewerCardList != null)
            hellViewerCardList.gameObject.SetActive(false);

        // Build card entries from Card objects
        if (hellViewerContent != null)
        {
            if (matches.Count == 0)
            {
                CreateHellViewerLabel("(no matching cards found)");
            }
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    CreateHellSearchCardEntry(matches[i], i + 1);
                }
            }
        }
    }

    /// <summary>Create a clickable card entry row from a Card object for the hell search panel.</summary>
    private void CreateHellSearchCardEntry(Card card, int index)
    {
        // Row container (Button for click handling)
        GameObject row = new GameObject($"HellSearchEntry_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
        row.transform.SetParent(hellViewerContent, false);
        hellViewerEntries.Add(row);

        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, 80);

        // Row background — dark semi-transparent
        Image rowBg = row.GetComponent<Image>();
        rowBg.color = new Color(0.15f, 0.15f, 0.20f, 0.9f);

        // Button click → select this card
        Button rowBtn = row.GetComponent<Button>();
        Card capturedCard = card;
        GameObject capturedRow = row;
        rowBtn.onClick.AddListener(() => SelectHellSearchCard(capturedCard, capturedRow));

        // Button color states
        ColorBlock cb = rowBtn.colors;
        cb.normalColor = new Color(0.15f, 0.15f, 0.20f, 0.9f);
        cb.highlightedColor = new Color(0.30f, 0.30f, 0.40f, 1f);
        cb.pressedColor = new Color(0.10f, 0.10f, 0.15f, 1f);
        cb.selectedColor = cb.normalColor;
        rowBtn.colors = cb;

        // Use HorizontalLayoutGroup for row layout
        var hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Card art thumbnail
        GameObject artObj = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artObj.transform.SetParent(row.transform, false);
        Image artImage = artObj.GetComponent<Image>();
        if (card.characterArt != null && card.characterArt.sprite != null)
            artImage.sprite = card.characterArt.sprite;
        else
            artImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        RectTransform artRT = artObj.GetComponent<RectTransform>();
        artRT.sizeDelta = new Vector2(56, 72);
        var artLE = artObj.AddComponent<UnityEngine.UI.LayoutElement>();
        artLE.preferredWidth = 56;
        artLE.preferredHeight = 72;

        // Info text (name + type + stats)
        GameObject textObj = new GameObject("Info", typeof(RectTransform));
        textObj.transform.SetParent(row.transform, false);
        TMP_Text infoText = textObj.AddComponent<TextMeshProUGUI>();

        string symbolStr = card.cardSymbol != CardSymbol.None
            ? $"  <color=#FFCC00>{GetSymbolThaiName(card.cardSymbol)}</color>"
            : "";
        string typeTag = card.cardType switch
        {
            CardType.Avatar => $"<color=#FF6666>AVATAR</color>  Pw:{card.power}  Cost:{card.cost}",
            CardType.Magic  => $"<color=#6699FF>MAGIC</color>  {(card.magicSO != null ? card.magicSO.magicType.ToString() : "")}",
            CardType.Life   => "<color=#999999>LIFE</color>",
            _               => "???"
        };

        infoText.text = $"<b>{card.cardName}</b>\n<size=80%>{typeTag}{symbolStr}  Gem:{card.gem}</size>";
        infoText.fontSize = 16;
        infoText.color = Color.white;
        infoText.alignment = TextAlignmentOptions.MidlineLeft;
        infoText.enableWordWrapping = false;
        infoText.overflowMode = TextOverflowModes.Ellipsis;
        infoText.raycastTarget = false;

        var textLE = textObj.AddComponent<UnityEngine.UI.LayoutElement>();
        textLE.preferredWidth = 300;
        textLE.flexibleWidth = 1;
    }

    /// <summary>Select a card in the hell search panel — highlight row and show preview.</summary>
    private void SelectHellSearchCard(Card card, GameObject row)
    {
        // Un-highlight previous selection
        if (_selectedHellSearchRow != null)
        {
            Image prevBg = _selectedHellSearchRow.GetComponent<Image>();
            if (prevBg != null) prevBg.color = new Color(0.15f, 0.15f, 0.20f, 0.9f);
        }

        _selectedHellSearchCard = card;
        _selectedHellSearchRow = row;

        // Highlight selected row
        Image rowBg = row.GetComponent<Image>();
        if (rowBg != null) rowBg.color = new Color(0.25f, 0.40f, 0.25f, 0.95f); // Green tint

        // Enable Summon button
        if (deckSearchSummonButton != null)
            deckSearchSummonButton.interactable = true;

        // Show card preview
        ShowCardPreview(card);
    }

    // ════════════════════════════════════════════════════════════════
    //  SHARED SEARCH PANEL — hide/reset for both deck and hell search
    // ════════════════════════════════════════════════════════════════

    /// <summary>Hide the search panel and reset all search state (deck or hell).</summary>
    public void HideSearchPanel()
    {
        _deckSearchMode = false;
        _hellSearchMode = false;
        _selectedDeckSearchSO = null;
        _selectedDeckSearchRow = null;
        _onDeckSearchConfirm = null;
        _selectedHellSearchCard = null;
        _selectedHellSearchRow = null;
        _onHellSearchConfirm = null;

        if (deckSearchSummonButton != null)
            deckSearchSummonButton.gameObject.SetActive(false);

        if (hellViewerPanel != null) hellViewerPanel.SetActive(false);
        ClearHellViewerEntries();
        HideCardPreview();
    }

    /// <summary>Hide the deck search panel (calls shared HideSearchPanel).</summary>
    public void HideDeckSearchPanel()
    {
        HideSearchPanel();
    }

    /// <summary>Hide the hell search panel (calls shared HideSearchPanel).</summary>
    public void HideHellSearchPanel()
    {
        HideSearchPanel();
    }

    /// <summary>Reset search button state at startup.</summary>
    private void HideDeckSearchState()
    {
        _deckSearchMode = false;
        _hellSearchMode = false;
        if (deckSearchSummonButton != null)
            deckSearchSummonButton.gameObject.SetActive(false);
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

    /// <summary>Get the Thai display name for a card symbol (public accessor for other scripts).</summary>
    public string GetSymbolThaiNamePublic(CardSymbol symbol) => GetSymbolThaiName(symbol);

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
                if (gm.player1.IsSahat)
                {
                    p1LifeCountText.text = "LIFE: \u0E2A\u0E32\u0E2B\u0E31\u0E2A!";
                    p1LifeCountText.color = Color.red;
                }
                else
                {
                    int remaining = 5 - gm.player1.LifeCardsFlipped;
                    p1LifeCountText.text = $"LIFE: {remaining}/5";
                    p1LifeCountText.color = remaining <= 2 ? Color.red : remaining <= 3 ? Color.yellow : Color.white;
                }
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
                if (gm.player2.IsSahat)
                {
                    p2LifeCountText.text = "LIFE: \u0E2A\u0E32\u0E2B\u0E31\u0E2A!";
                    p2LifeCountText.color = Color.red;
                }
                else
                {
                    int remaining = 5 - gm.player2.LifeCardsFlipped;
                    p2LifeCountText.text = $"LIFE: {remaining}/5";
                    p2LifeCountText.color = remaining <= 2 ? Color.red : remaining <= 3 ? Color.yellow : Color.white;
                }
            }
        }
    }
}
