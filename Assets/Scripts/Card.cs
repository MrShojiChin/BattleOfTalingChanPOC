using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

public class Card : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    [Header("Choose type first")]
    [SerializeField] public CardType cardType;

    [Header("Assign data based on type")]
    [SerializeField] public AvatarCardSO avatarSO;
    [SerializeField] public MagicCardSO magicSO;
    [SerializeField] public LifeCardSO lifeCardSO;

    [Header("Live variables (runtime)")]
    public int cost;
    public int power;
    public int gem;
    public string symbol;
    public string description;
    public string cardName;
    public CardColor avatarColor;
    public CardColor gemColor;

    // ── BATTLE STATE ──────────────────────────────────────────────
    [Header("Battle State")]
    public bool isTapped;          // นอน (sleeping/tapped) — already attacked this turn
    public int  turnPlaced = -1;   // Turn number when placed on board (for summon sickness)

    // ── LIFE CARD STATE ─────────────────────────────────────────
    [Header("Life Card")]
    public bool isLifeCard;       // This card is placed in a LIFE zone
    public bool isFaceDown;       // LIFE card hasn't been flipped yet

    // ── MULLIGAN / DISCARD ──────────────────────────────────────
    [HideInInspector] public bool markedForMulligan;
    [HideInInspector] public bool markedForDiscard;

    [Header("UI")]
    public Image characterArt;
    public TMP_Text nameText;
    public TMP_Text descText;
    public TMP_Text costText;

    [Header("Highlights")]
    public Image pitchHighlightImage;           // Border/glow for "pending" state (yellow)
    public Image readyHighlightImage;           // Border/glow for "ready to place" state (green)
    public Image attackHighlightImage;          // Border/glow for "selected attacker" (red)
    private Color normalColor = Color.white;
    private Color pitchColor = Color.yellow;
    private Color readyColor = Color.green;
    private Color attackColor = Color.red;

    // ── OWNERSHIP ─────────────────────────────────────────────────
    /// <summary>
    /// Which player owns this card. Set by DeckController when drawn.
    /// Used to find the correct HandController (no more singletons).
    /// </summary>
    public TurnPlayer cardOwner;

    // ── MOVEMENT ───────────────────────────────────────────────────
    private Vector3 targetPoint;
    private Quaternion targerRot;
    public float moveSpeed = 5f, rotateSpeed = 540f;

    // ── HAND STATE ─────────────────────────────────────────────────
    public bool inHand;
    public int handPosition;

    /// <summary>
    /// True when this card is being dragged by the player (following the mouse).
    /// </summary>
    public bool isSelected;

    private Image cardImage;

    public LayerMask whatIsDesktop, whatIsPlacement;
    private bool justPressed;

    public CardPlacePoint assignedPlace;

    // ── OWNER'S HAND (lazy lookup — no more singleton) ────────────
    private HandController _cachedHC;

    /// <summary>
    /// Returns the HandController of the player who owns this card.
    /// Lazy-resolved on first access via GameManager.
    /// </summary>
    private HandController OwnerHand
    {
        get
        {
            if (_cachedHC == null && GameManager.instance != null)
                _cachedHC = GameManager.instance.GetPlayer(cardOwner).hand;
            return _cachedHC;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SETUP
    // ════════════════════════════════════════════════════════════════

    private void Start()
    {
        SetupCard();

        cardImage = GetComponent<Image>();
        if (cardImage == null)
            cardImage = GetComponentInChildren<Image>();

        if (OwnerHand == null)
            Debug.LogError($"{cardName}: Owner's HandController not found!");

        if (cardImage == null)
            Debug.LogWarning($"{cardName}: Image component not found!");
    }

    public void SetupCard()
    {
        // ── VALIDATION ──
        if (cardType == CardType.Avatar && avatarSO == null)
        {
            Debug.LogError($"{name}: Avatar selected but avatarSO is not assigned!");
            return;
        }
        if (cardType == CardType.Magic && magicSO == null)
        {
            Debug.LogError($"{name}: Magic selected but magicSO is not assigned!");
            return;
        }
        if (cardType == CardType.Life && lifeCardSO == null)
        {
            Debug.LogError($"{name}: Life selected but lifeCardSO is not assigned!");
            return;
        }

        // ── AVATAR ──
        if (cardType == CardType.Avatar)
        {
            cardName = avatarSO.cardName;
            description = avatarSO.description;
            cost = avatarSO.cost;
            gem = avatarSO.gem;
            power = avatarSO.power;
            symbol = avatarSO.symbol;
            avatarColor = avatarSO.avatarColor;
            gemColor = avatarSO.gemColor;
            if (characterArt) characterArt.sprite = avatarSO.cardCharacterSprite;
        }
        // ── MAGIC ──
        else if (cardType == CardType.Magic)
        {
            cardName = magicSO.cardName;
            description = magicSO.description;
            cost = 0;
            gem = magicSO.gem;
            symbol = magicSO.symbol;
            avatarColor = CardColor.Neutral;
            gemColor = magicSO.gemColor;
            if (characterArt) characterArt.sprite = magicSO.cardCharacterSprite;
        }
        // ── LIFE ──
        else if (cardType == CardType.Life)
        {
            cardName = lifeCardSO.cardName;
            description = lifeCardSO.description;
            cost = 0;
            power = 0;
            gem = lifeCardSO.gem;
            symbol = lifeCardSO.symbol;
            avatarColor = CardColor.Neutral;
            gemColor = CardColor.Neutral;

            if (characterArt != null)
                characterArt.sprite = lifeCardSO.cardCharacterSprite;
            else
                Debug.LogWarning($"[Card] {name}: characterArt is null — wire it on the LifeCard prefab!");

            if (lifeCardSO.cardCharacterSprite == null)
                Debug.LogWarning($"[Card] {cardName}: LifeCardSO has no cardCharacterSprite assigned!");
        }

        if (nameText) nameText.text = cardName;
        if (descText) descText.text = description;
        if (costText)
        {
            costText.text = cost.ToString();
            costText.gameObject.SetActive(cardType == CardType.Avatar);  // Only Avatars show cost
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  UPDATE — DRAG LOGIC
    // ════════════════════════════════════════════════════════════════

    void Update()
    {
        HandController theHC = OwnerHand;

        // ── HIGH-PRIORITY: Lock pending avatar in its raised position ──
        BattleController bc = BattleController.instance;
        if (bc != null && bc.pendingAvatar == this && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
            {
                targetPoint = theHC.cardPositions[handPosition] + new Vector3(0f, 1.5f, 0.5f);
                targerRot = Quaternion.identity;
            }
            transform.position = Vector3.Lerp(transform.position, targetPoint, moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targerRot, rotateSpeed * Time.deltaTime);
            return;
        }

        if (isSelected)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 100f, whatIsDesktop))
            {
                MoveToPoint(hit.point, Quaternion.identity);
            }

            if (Mouse.current.leftButton.wasPressedThisFrame && !justPressed)
            {
                if (Physics.Raycast(ray, out hit, 100f, whatIsPlacement))
                {
                    CardPlacePoint point = hit.collider.GetComponent<CardPlacePoint>();
                    if (point != null)
                    {
                        HandleDrop(point);
                    }
                    else
                    {
                        ReturnToHand();
                    }
                }
                else
                {
                    ReturnToHand();
                }
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                ReturnToHand();
            }
        }

        justPressed = false;

        transform.position = Vector3.Lerp(transform.position, targetPoint, moveSpeed * Time.deltaTime);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targerRot, rotateSpeed * Time.deltaTime);
    }

    // ════════════════════════════════════════════════════════════════
    //  DROP HANDLER — routes based on context
    // ════════════════════════════════════════════════════════════════

    private void HandleDrop(CardPlacePoint point)
    {
        BattleController bc = BattleController.instance;

        // ── CASE A: Dropping a tribute card onto the Hell Point during CostStep ──
        if (bc.currentState == SummonState.CostStep && point.isPlayerHellPoint && this != bc.pendingAvatar)
        {
            if (bc.TryPayTribute(this, point))
            {
                isSelected = false;
                EnableInteraction();
                targerRot = Quaternion.identity;
                Debug.Log($"{cardName} tributed successfully.");
            }
            else
            {
                Debug.Log($"{cardName} rejected as tribute, returning to hand.");
                ReturnToHand();
            }
            return;
        }

        // ── CASE B: Placing the unlocked avatar on a board slot (ReadyToPlace) ──
        if (bc.currentState == SummonState.ReadyToPlace && this == bc.pendingAvatar)
        {
            if (point.activeCard == null && point.isPlayerAvatarPoint)
            {
                bc.FinalizeSummon(point);
                Debug.Log($"{cardName} placed on board via ReadyToPlace.");
            }
            else
            {
                Debug.Log($"{cardName} can't go here — returning to hand position.");
                ReturnToHand();
            }
            return;
        }

        // ── CASE C: Normal placement (free avatar cost==0, or magic card) ──

        // Magic cards → multi-card zone (stacking)
        if (cardType == CardType.Magic && point.isPlayerMagicPoint && point.IsCurrentPlayerZone())
        {
            isSelected = false;
            inHand = false;
            EnableInteraction();
            point.AddCard(this);  // Uses multi-card stacking
            HandController theHC = OwnerHand;
            if (theHC != null) theHC.RemoveCardFromHand(this);
            Debug.Log($"{cardName} (Magic) placed in Magic Zone (stack #{point.activeCards.Count}).");
            UIController.instance?.UpdateGameInfo();
            return;
        }

        // Free avatar (cost 0) → single-card zone
        if (point.activeCard == null)
        {
            if (cardType == CardType.Avatar && point.isPlayerAvatarPoint && point.IsCurrentPlayerZone())
            {
                PlaceOnBoard(point);
                HandController theHC = OwnerHand;
                if (theHC != null) theHC.RemoveCardFromHand(this);
                UIController.instance?.UpdateGameInfo();
                return;
            }
        }

        Debug.Log($"{cardName} dropped on invalid point, returning to hand.");
        ReturnToHand();
    }

    // ════════════════════════════════════════════════════════════════
    //  POINTER EVENTS
    // ════════════════════════════════════════════════════════════════

    public void OnPointerEnter(PointerEventData eventData)
    {
        // During setup, mulligan handles all card positioning
        if (GameManager.instance != null && GameManager.instance.isSetupPhase) return;

        BattleController bc = BattleController.instance;
        HandController theHC = OwnerHand;

        if (this == bc.pendingAvatar) return;

        if ((bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
            && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition] + new Vector3(0f, 1f, 0.5f), Quaternion.identity);
            return;
        }

        if (bc.currentState == SummonState.Idle && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition] + new Vector3(0f, 1f, 0.5f), Quaternion.identity);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // During setup, mulligan handles all card positioning
        if (GameManager.instance != null && GameManager.instance.isSetupPhase) return;

        BattleController bc = BattleController.instance;
        HandController theHC = OwnerHand;

        if (this == bc.pendingAvatar) return;

        if ((bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
            && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition], theHC.minPos.rotation);
            return;
        }

        if (bc.currentState == SummonState.Idle && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition], theHC.minPos.rotation);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        BattleController bc = BattleController.instance;
        bool isLeftClick = eventData.button == PointerEventData.InputButton.Left;
        bool isRightClick = eventData.button == PointerEventData.InputButton.Right;

        // Dismiss card preview on any left-click
        if (isLeftClick)
            UIController.instance?.HideCardPreview();

        // ── GAME OVER: block all interaction ──
        if (GameManager.instance != null && GameManager.instance.isGameOver) return;

        // ── END PHASE DISCARD: toggle card selection for discard ──
        if (GameManager.instance != null && GameManager.instance.isDiscardPhase)
        {
            if (inHand && cardOwner == GameManager.instance.currentPlayer)
            {
                if (isLeftClick)
                    ToggleDiscardSelection();
                else if (isRightClick && markedForDiscard)
                    ToggleDiscardSelection(); // Right-click cancels selection
            }
            return; // No other interactions during discard
        }

        // ── SETUP/MULLIGAN: toggle card selection for swap ────────
        if (GameManager.instance != null && GameManager.instance.isSetupPhase)
        {
            if (inHand && cardOwner == GameManager.instance.currentPlayer)
            {
                if (isLeftClick)
                    ToggleMulliganSelection();
                else if (isRightClick && markedForMulligan)
                    ToggleMulliganSelection(); // Right-click cancels selection
            }
            return; // No other interactions during setup
        }

        // ── LIFE CARDS: never draggable, only clickable in Battle Phase ──
        if (isLifeCard)
        {
            Debug.Log($"[Card] LIFE card '{cardName}' clicked! Owner={cardOwner}, isFaceDown={isFaceDown}, IsBattle={GameManager.instance?.IsBattlePhase()}");

            if (GameManager.instance != null && GameManager.instance.IsBattlePhase())
                CombatController.instance.HandleCardClick(this, isRightClick);
            else
                Debug.Log($"[Card] LIFE card '{cardName}' clicked outside Battle Phase — ignored.");
            return; // LIFE cards only respond during Battle Phase
        }

        // ── BATTLE PHASE: route all clicks to CombatController ────
        if (GameManager.instance != null && GameManager.instance.IsBattlePhase())
        {
            if (!inHand) // Only board cards respond during Battle Phase
                CombatController.instance.HandleCardClick(this, isRightClick);
            return; // No other card logic during Battle Phase
        }

        // ── RIGHT-CLICK: Cancellation or Card Preview ─────────────
        if (isRightClick)
        {
            if (bc.currentTributes.Contains(this))
            {
                bc.ReturnTribute(this);
                return;
            }

            if (this == bc.pendingAvatar)
            {
                bc.CancelFullSummon();
                return;
            }

            // Right-click a card in hand (no summon active) → show preview
            if (inHand && bc.currentState == SummonState.Idle)
            {
                UIController.instance?.ShowCardPreview(this);
                return;
            }

            // Right-click a card on board → show preview
            if (!inHand && assignedPlace != null)
            {
                UIController.instance?.ShowCardPreview(this);
                return;
            }

            return;
        }

        // ── LEFT-CLICK ─────────────────────────────────────────────
        if (!isLeftClick) return;

        // ── PHASE CHECK: only allow card play during Main Phase ──
        GameManager gm = GameManager.instance;
        if (gm != null && !gm.IsMainPhase())
        {
            // Exception: allow continuing a summon already in progress
            if (bc.currentState == SummonState.Idle)
            {
                Debug.Log($"[Card] {cardName}: Can only play cards during Main Phase!");
                return;
            }
        }

        // ── OWNERSHIP CHECK: only the current player can play their cards ──
        if (gm != null && cardOwner != gm.currentPlayer && inHand)
        {
            Debug.Log($"[Card] {cardName}: Not your card! Belongs to {cardOwner}.");
            return;
        }

        if (bc.currentState == SummonState.ReadyToPlace && this == bc.pendingAvatar && inHand)
        {
            isSelected = true;
            DisableInteraction();
            justPressed = true;
            Debug.Log($"[Phase 3] Dragging unlocked avatar {cardName} to board.");
            return;
        }

        if (bc.currentState == SummonState.CostStep)
        {
            if (this == bc.pendingAvatar) return;

            if (inHand)
            {
                isSelected = true;
                DisableInteraction();
                justPressed = true;
                Debug.Log($"[Phase 2] Dragging {cardName} to Hell Point as tribute.");
            }
            return;
        }

        if (bc.currentState == SummonState.Idle && inHand)
        {
            if (cardType == CardType.Avatar)
            {
                bool summonInitiated = bc.InitiateSummon(this);
                if (!summonInitiated)
                {
                    isSelected = true;
                    DisableInteraction();
                    justPressed = true;
                }
                return;
            }

            isSelected = true;
            DisableInteraction();
            justPressed = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════

    public void PlaceOnBoard(CardPlacePoint point)
    {
        point.activeCard = this;
        assignedPlace = point;
        isSelected = false;
        inHand = false;
        EnableInteraction();
        MoveToPoint(point.transform.position, Quaternion.identity);

        // Record turn number for summon sickness
        if (GameManager.instance != null)
            turnPlaced = GameManager.instance.turnNumber;

        Debug.Log($"{cardName} ({cardType}) placed at: {point.name}");
    }

    public void MoveToPoint(Vector3 pointToMoveTo, Quaternion rotToMatch)
    {
        targetPoint = pointToMoveTo;
        targerRot = rotToMatch;
    }

    // ── HIGHLIGHTS ─────────────────────────────────────────────────
    public void SetPitchHighlight(bool on)
    {
        if (pitchHighlightImage != null)
            pitchHighlightImage.color = on ? pitchColor : normalColor;
    }

    public void SetReadyHighlight(bool on)
    {
        if (readyHighlightImage != null)
            readyHighlightImage.color = on ? readyColor : normalColor;
    }

    public void SetAttackHighlight(bool on)
    {
        if (attackHighlightImage != null)
            attackHighlightImage.color = on ? attackColor : normalColor;
    }

    // ── MULLIGAN TOGGLE ─────────────────────────────────────────
    /// <summary>
    /// Toggle this card's selection for mulligan swap.
    /// Yellow highlight + slight lift = marked for swap.
    /// </summary>
    private void ToggleMulliganSelection()
    {
        markedForMulligan = !markedForMulligan;
        SetPitchHighlight(markedForMulligan);  // Yellow glow = marked

        // Visual feedback: fly card above the ground when marked, back down when unmarked
        HandController hc = OwnerHand;
        if (hc != null && handPosition < hc.cardPositions.Count)
        {
            Vector3 pos = hc.cardPositions[handPosition];
            if (markedForMulligan)
                pos += new Vector3(0f, 1.5f, 0.5f); // Fly up + slightly forward
            MoveToPoint(pos, hc.minPos.rotation);
        }

        Debug.Log($"[Mulligan] {cardName} {(markedForMulligan ? "MARKED" : "unmarked")} for swap.");
    }

    // ── DISCARD TOGGLE (End Phase — hand limit) ───────────────────
    /// <summary>
    /// Toggle this card's selection for end-of-turn discard.
    /// Yellow highlight + slight lift = marked for discard.
    /// </summary>
    private void ToggleDiscardSelection()
    {
        markedForDiscard = !markedForDiscard;
        SetPitchHighlight(markedForDiscard);  // Yellow glow = marked

        // Visual feedback: fly card above the ground when marked
        HandController hc = OwnerHand;
        if (hc != null && handPosition < hc.cardPositions.Count)
        {
            Vector3 pos = hc.cardPositions[handPosition];
            if (markedForDiscard)
                pos += new Vector3(0f, 1.5f, 0.5f); // Fly up + slightly forward
            MoveToPoint(pos, hc.minPos.rotation);
        }

        Debug.Log($"[Discard] {cardName} {(markedForDiscard ? "MARKED" : "unmarked")} for discard.");

        // Update the UI counter so the player knows how many they've selected
        UIController.instance?.UpdateDiscardCount();
    }

    // ── TAPPED (นอน) ──────────────────────────────────────────────
    /// <summary>
    /// Tap (lay down) or untap (stand up) an avatar on the board.
    /// Tapped avatars cannot attack again this turn.
    /// </summary>
    public void SetTapped(bool tapped)
    {
        isTapped = tapped;
        if (assignedPlace != null)
        {
            Quaternion rot = tapped
                ? Quaternion.Euler(0f, -90f, 0f)  // Rotate Y axis = lay sideways (horizontal)
                : Quaternion.identity;
            MoveToPoint(assignedPlace.transform.position, rot);
        }
    }

    // ── INTERACTION ────────────────────────────────────────────────
    public void EnableInteraction()
    {
        if (cardImage != null)
            cardImage.raycastTarget = true;
    }

    public void DisableInteraction()
    {
        if (cardImage != null)
            cardImage.raycastTarget = false;
    }

    public void ReturnToHand()
    {
        isSelected = false;
        inHand = true;
        EnableInteraction();

        HandController theHC = OwnerHand;
        if (theHC != null && handPosition < theHC.cardPositions.Count)
        {
            MoveToPoint(theHC.cardPositions[handPosition], theHC.minPos.rotation);
        }
    }

    // ── LIFE CARD ────────────────────────────────────────────────

    /// <summary>
    /// Set the face-down state of a LIFE card.
    /// Face-down: rotates the card 180° to show the card back (no art hiding).
    /// Face-up: rotates back to normal, revealing the front with art.
    /// </summary>
    public void SetFaceDown(bool faceDown)
    {
        isFaceDown = faceDown;

        // Rotate the whole card to show back (face-down) or front (face-up).
        // LIFE cards are horizontal (-90° Y). Face-down adds 180° X flip.
        if (isLifeCard && assignedPlace != null)
        {
            Quaternion rot = faceDown
                ? Quaternion.Euler(180f, -90f, 0f)   // Horizontal + flipped (card back up)
                : Quaternion.Euler(0f, -90f, 0f);     // Horizontal + normal (card front up)
            MoveToPoint(assignedPlace.transform.position, rot);
        }
    }

    /// <summary>
    /// Flip a face-down LIFE card face-up (reveal it).
    /// Called when an attacker hits this LIFE card during Battle Phase.
    /// </summary>
    public void FlipLifeCard()
    {
        if (!isLifeCard || !isFaceDown) return;

        SetFaceDown(false);
        Debug.Log($"[LIFE] {cardOwner}'s LIFE card flipped: {cardName}!");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            SetupCard();
    }
#endif
}
