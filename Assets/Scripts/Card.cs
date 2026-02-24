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
        if (point.activeCard == null)
        {
            bool valid = false;
            if (cardType == CardType.Avatar && point.isPlayerAvatarPoint) valid = true;
            if (cardType == CardType.Magic && point.isPlayerMagicPoint) valid = true;

            if (valid)
            {
                PlaceOnBoard(point);
                HandController theHC = OwnerHand;
                if (theHC != null) theHC.RemoveCardFromHand(this);
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

        // ── RIGHT-CLICK: Cancellation ──────────────────────────────
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

            return;
        }

        // ── LEFT-CLICK ─────────────────────────────────────────────
        if (!isLeftClick) return;

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

        HandController theHC = OwnerHand;
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
