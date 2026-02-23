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

    [Header("Live variables (runtime)")]
    public int cost;
    public int gem;
    public string symbol;
    public string description;
    public string cardName;
    public CardColor avatarColor;
    public CardColor gemColor;

    [Header("UI")]
    public Image characterArt;
    public TMP_Text nameText;
    public TMP_Text descText;
    public TMP_Text costText;

    [Header("Highlights")]
    public Image pitchHighlightImage;           // Border/glow for "pending" state (yellow)
    public Image readyHighlightImage;           // Border/glow for "ready to place" state (green)
    private Color normalColor = Color.white;
    private Color pitchColor = Color.yellow;
    private Color readyColor = Color.green;

    // ── MOVEMENT ───────────────────────────────────────────────────
    private Vector3 targetPoint;
    private Quaternion targerRot;
    public float moveSpeed = 5f, rotateSpeed = 540f;

    // ── HAND STATE ─────────────────────────────────────────────────
    public bool inHand;
    public int handPosition;

    /// <summary>
    /// True when this card is being dragged by the player (following the mouse).
    /// Only set for: free-cost avatars, magic cards, tribute cards being dragged to hell,
    /// or the pending avatar in ReadyToPlace state.
    /// </summary>
    public bool isSelected;

    private HandController theHC;
    private Image cardImage;

    public LayerMask whatIsDesktop, whatIsPlacement;
    private bool justPressed;

    public CardPlacePoint assignedPlace;

    // ════════════════════════════════════════════════════════════════
    //  SETUP
    // ════════════════════════════════════════════════════════════════

    private void Start()
    {
        SetupCard();

        theHC = FindFirstObjectByType<HandController>();

        cardImage = GetComponent<Image>();
        if (cardImage == null)
            cardImage = GetComponentInChildren<Image>();

        if (theHC == null)
            Debug.LogError($"{cardName}: HandController not found!");

        if (cardImage == null)
            Debug.LogWarning($"{cardName}: Image component not found!");
    }

    public void SetupCard()
    {
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

        if (cardType == CardType.Avatar)
        {
            cardName = avatarSO.cardName;
            description = avatarSO.description;
            cost = avatarSO.cost;
            gem = avatarSO.gem;
            symbol = avatarSO.symbol;
            avatarColor = avatarSO.avatarColor;
            gemColor = avatarSO.gemColor;
            if (characterArt) characterArt.sprite = avatarSO.cardCharacterSprite;
        }
        else
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

        if (nameText) nameText.text = cardName;
        if (descText) descText.text = description;
        if (costText)
        {
            costText.text = cost.ToString();
            costText.gameObject.SetActive(cardType == CardType.Avatar);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  UPDATE — DRAG LOGIC
    // ════════════════════════════════════════════════════════════════

    void Update()
    {
        // ── HIGH-PRIORITY: Lock pending avatar in its raised position ──
        BattleController bc = BattleController.instance;
        if (bc != null && bc.pendingAvatar == this && !isSelected)
        {
            // Force the raised position every frame so nothing (hand repositioning, etc.) can pull it down
            if (theHC != null && handPosition < theHC.cardPositions.Count)
            {
                targetPoint = theHC.cardPositions[handPosition] + new Vector3(0f, 1.5f, 0.5f);
                targerRot = Quaternion.identity;
            }
            // Skip all other Update logic — this card is untouchable until summon ends
            transform.position = Vector3.Lerp(transform.position, targetPoint, moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targerRot, rotateSpeed * Time.deltaTime);
            return;
        }

        if (isSelected)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);

            // Follow cursor on desktop layer
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 100f, whatIsDesktop))
            {
                MoveToPoint(hit.point, Quaternion.identity);
            }

            // LEFT CLICK — attempt to drop
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

            // RIGHT CLICK — cancel drag, return to hand
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                ReturnToHand();
            }
        }

        justPressed = false;

        // Movement interpolation
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
                // Accepted — card is now a tribute sitting in hell
                isSelected = false;
                EnableInteraction();                       // Must stay clickable for right-click recall
                targerRot = Quaternion.identity;           // Flat/face-up in hell
                Debug.Log($"{cardName} tributed successfully.");
            }
            else
            {
                // Rejected — return to hand
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
        if (point.activeCard == null)
        {
            bool valid = false;
            if (cardType == CardType.Avatar && point.isPlayerAvatarPoint) valid = true;
            if (cardType == CardType.Magic && point.isPlayerMagicPoint) valid = true;

            if (valid)
            {
                PlaceOnBoard(point);
                if (theHC != null) theHC.RemoveCardFromHand(this);
                return;
            }
        }

        // ── DEFAULT: invalid drop → return to hand ──
        Debug.Log($"{cardName} dropped on invalid point, returning to hand.");
        ReturnToHand();
    }

    // ════════════════════════════════════════════════════════════════
    //  POINTER EVENTS
    // ════════════════════════════════════════════════════════════════

    public void OnPointerEnter(PointerEventData eventData)
    {
        BattleController bc = BattleController.instance;

        // Pending avatar stays in its popped-up position — don't touch it
        if (this == bc.pendingAvatar) return;

        // During CostStep/ReadyToPlace, hover non-pending hand cards to show they're draggable
        if ((bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
            && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition] + new Vector3(0f, 1f, 0.5f), Quaternion.identity);
            return;
        }

        // Normal idle hover
        if (bc.currentState == SummonState.Idle && inHand && !isSelected)
        {
            if (theHC != null && handPosition < theHC.cardPositions.Count)
                MoveToPoint(theHC.cardPositions[handPosition] + new Vector3(0f, 1f, 0.5f), Quaternion.identity);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        BattleController bc = BattleController.instance;

        // Pending avatar stays in its popped-up position — never drop it back on mouse exit
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

        // ── RIGHT-CLICK: Cancellation ──────────────────────────────
        if (isRightClick)
        {
            // Right-click on a tribute card at the hell point → return just that tribute
            if (bc.currentTributes.Contains(this))
            {
                bc.ReturnTribute(this);
                return;
            }

            // Right-click on the pending avatar → full cancel
            if (this == bc.pendingAvatar)
            {
                bc.CancelFullSummon();
                return;
            }

            // Right-click while dragging → return to hand (handled in Update)
            return;
        }

        // ── LEFT-CLICK ─────────────────────────────────────────────
        if (!isLeftClick) return;

        // --- STATE: ReadyToPlace ---
        // Click the unlocked avatar to start dragging it to a board slot
        if (bc.currentState == SummonState.ReadyToPlace && this == bc.pendingAvatar && inHand)
        {
            isSelected = true;
            DisableInteraction();
            justPressed = true;
            Debug.Log($"[Phase 3] Dragging unlocked avatar {cardName} to board.");
            return;
        }

        // --- STATE: CostStep ---
        if (bc.currentState == SummonState.CostStep)
        {
            // Can't click the pending avatar to drag during cost step
            if (this == bc.pendingAvatar) return;

            // Click a hand card to start dragging it to the hell point as tribute
            if (inHand)
            {
                isSelected = true;
                DisableInteraction();
                justPressed = true;
                Debug.Log($"[Phase 2] Dragging {cardName} to Hell Point as tribute.");
            }
            return;
        }

        // --- STATE: Idle ---
        if (bc.currentState == SummonState.Idle && inHand)
        {
            // Phase 1: Left-click an Avatar → initiate summon or drag if free
            if (cardType == CardType.Avatar)
            {
                bool summonInitiated = bc.InitiateSummon(this);
                if (!summonInitiated)
                {
                    // Cost == 0: go straight to normal drag
                    isSelected = true;
                    DisableInteraction();
                    justPressed = true;
                }
                // If summonInitiated == true, card is now the pendingAvatar (not draggable yet)
                return;
            }

            // Magic card in Idle → normal drag to board
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

        if (theHC != null && handPosition < theHC.cardPositions.Count)
        {
            MoveToPoint(theHC.cardPositions[handPosition], theHC.minPos.rotation);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            SetupCard();
    }
#endif
}