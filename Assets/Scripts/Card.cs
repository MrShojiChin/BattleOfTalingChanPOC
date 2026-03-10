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
    public CardSymbol cardSymbol;
    public string description;
    public string cardName;
    public string flavorText;      // LIFE card flavor text (Thai narrative)
    public CardColor avatarColor;
    public CardColor gemColor;

    // ── BATTLE STATE ──────────────────────────────────────────────
    [Header("Battle State")]
    public bool isTapped;          // นอน (sleeping/tapped) — already attacked this turn
    public int  turnPlaced = -1;   // Turn number when placed on board (for summon sickness)

    // ── MODIFICATION STATE ────────────────────────────────────────
    [Header("Modification")]
    public List<Card> attachedMods = new List<Card>();  // Mods equipped TO this avatar
    public Card equippedTo;                              // Avatar this mod is attached to
    [HideInInspector] public int appliedEffectValue;     // For undoing buffs when mod destroyed
    [HideInInspector] public bool cannotBeTribute;       // Searched cards can't be used as summon cost

    // ── AVATAR ABILITY STATE ─────────────────────────────────────────
    [Header("Avatar Abilities")]
    public AvatarAbility abilities = AvatarAbility.None;
    [HideInInspector] public string jutiSearchPrefix;
    [HideInInspector] public int combatThonSoopMill;
    [HideInInspector] public int combatThonSoopPowerBoost;
    [HideInInspector] public string hellScalingNamePrefix;
    [HideInInspector] public int basePower;              // Original SO power (before dynamic modifiers)
    [HideInInspector] public int hellScalingBonus = 0;   // Current HellPowerScaling bonus (tracked for recalculation)
    [HideInInspector] public int jutiTargetPowerValue;   // For JutiTargetDebuff/Buff: power change (negative for debuff)
    [HideInInspector] public int jutiDrawCount;          // For JutiDraw: number of cards to draw
    [HideInInspector] public int attackPowerBoost;       // For AttackPowerBoost: temp boost on attack
    [HideInInspector] public CardSymbol auraSymbol;      // For SymbolAura: symbol to buff
    [HideInInspector] public int auraPowerBoost;         // For SymbolAura: power per matching ally
    [HideInInspector] public int auraBonus = 0;          // Current aura bonus (tracked for recalculation)
    [HideInInspector] public bool isAnimating;            // True during scripted animations (blocks normal lerp)

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
    public TMP_Text powerText;    // Shows power on board (visible for avatars)

    [Header("Highlights")]
    public Image pitchHighlightImage;           // Border/glow for "pending" state (yellow)
    public Image readyHighlightImage;           // Border/glow for "ready to place" state (green)
    public Image attackHighlightImage;          // Border/glow for "selected attacker" (red)
    public Image deathGlowImage;               // Glow for death animation (before going to hell)
    public Image modHighlightImage;             // Gradient glow for "has modification attached" (color matches mod)
    private Color normalColor = new Color(1f, 1f, 1f, 0f);  // Transparent (invisible when off)
    private Color pitchColor = new Color(1f, 1f, 0f, 0.6f);  // Yellow glow
    private Color readyColor = new Color(0f, 1f, 0f, 0.6f);  // Green glow
    private Color attackColor = new Color(1f, 0f, 0f, 0.6f);  // Red glow

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
    private static float _lastHellClickTime;  // Static: shared across all cards in hell

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
            cardSymbol = avatarSO.cardSymbol;
            avatarColor = avatarSO.avatarColor;
            gemColor = avatarSO.gemColor;
            abilities = avatarSO.abilities;
            jutiSearchPrefix = avatarSO.jutiSearchPrefix;
            combatThonSoopMill = avatarSO.combatThonSoopMill;
            combatThonSoopPowerBoost = avatarSO.combatThonSoopPowerBoost;
            hellScalingNamePrefix = avatarSO.hellScalingNamePrefix;
            basePower = avatarSO.power;
            jutiTargetPowerValue = avatarSO.jutiTargetPowerValue;
            jutiDrawCount = avatarSO.jutiDrawCount;
            attackPowerBoost = avatarSO.attackPowerBoost;
            auraSymbol = avatarSO.auraSymbol;
            auraPowerBoost = avatarSO.auraPowerBoost;
            if (characterArt) characterArt.sprite = avatarSO.cardCharacterSprite;
        }
        // ── MAGIC ──
        else if (cardType == CardType.Magic)
        {
            cardName = magicSO.cardName;
            description = magicSO.description;
            cost = 0;
            gem = magicSO.gem;
            cardSymbol = magicSO.cardSymbol;
            avatarColor = CardColor.Neutral;
            gemColor = magicSO.gemColor;
            if (characterArt) characterArt.sprite = magicSO.cardCharacterSprite;
        }
        // ── LIFE ──
        else if (cardType == CardType.Life)
        {
            cardName = lifeCardSO.cardName;
            description = lifeCardSO.description;
            flavorText = lifeCardSO.flavorText;
            cost = 0;
            power = 0;
            gem = lifeCardSO.gem;
            cardSymbol = lifeCardSO.cardSymbol;
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

        // Set power display from SO default value (visible in hand + on board for avatars)
        RefreshPowerDisplay();
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
            if (theHC != null && handPosition < theHC.heldCards.Count)
            {
                // Use base position (without slide offset) so pending avatar stays raised
                targetPoint = theHC.GetBasePosition(handPosition) + new Vector3(0f, 1.5f, 0.5f);
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

        if (!isAnimating)
        {
            transform.position = Vector3.Lerp(transform.position, targetPoint, moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targerRot, rotateSpeed * Time.deltaTime);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DROP HANDLER — routes based on context
    // ════════════════════════════════════════════════════════════════

    private void HandleDrop(CardPlacePoint point)
    {
        // Block all drops during React confirmation or Hell activation
        if (MagicController.instance != null
            && (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm
             || MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation))
            return;

        BattleController bc = BattleController.instance;

        // ── CASE A: Placing the unlocked avatar on a board slot (ReadyToPlace) ──
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

        // Magic cards → route through MagicController (handles Normal/Modification/React/Land)
        if (cardType == CardType.Magic && point.isPlayerMagicPoint && point.IsCurrentPlayerZone())
        {
            isSelected = false;
            inHand = false;
            EnableInteraction();
            HandController theHC = OwnerHand;
            if (theHC != null)
            {
                theHC.RemoveCardFromHand(this);
                theHC.SlideUp();  // Card placed — slide hand back up
            }
            MagicController.instance.PlayMagicCard(this, point);
            UIController.instance?.UpdateGameInfo();
            return;
        }

        // Free avatar (cost 0) → single-card zone
        if (point.activeCard == null)
        {
            if (cardType == CardType.Avatar && point.isPlayerAvatarPoint && point.IsCurrentPlayerZone())
            {
                // Ability 3: Block free placement of avatars that cannot be summoned from hand
                if (HasAbility(AvatarAbility.CannotSummonFromHand))
                {
                    Debug.Log($"{cardName} cannot be summoned from hand (ability)!");
                    ReturnToHand();
                    return;
                }
                PlaceOnBoard(point);
                HandController theHC = OwnerHand;
                if (theHC != null)
                {
                    theHC.RemoveCardFromHand(this);
                    theHC.SlideUp();  // Card placed — slide hand back up
                }
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

        // During discard-for-effect selection: block all hover except highlighted cards
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingDiscardForEffect)
        {
            // Only highlighted (matching symbol) hand cards get extra hover
            if (inHand && MagicController.instance.IsHighlightedForDiscard(this))
            {
                HandController hc = OwnerHand;
                if (hc != null && handPosition < hc.cardPositions.Count)
                {
                    Vector3 pos = hc.cardPositions[handPosition] + new Vector3(0f, 2.2f, 0.7f);
                    MoveToPoint(pos, Quaternion.identity);
                }
            }
            return; // Block hover on magic card, non-matching hand cards, board cards
        }

        // During temp boost target selection: extra hover on highlighted avatars only
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingTempBoostTarget)
        {
            if (!inHand && cardType == CardType.Avatar
                && MagicController.instance.IsHighlightedForTempBoost(this))
            {
                if (assignedPlace != null)
                {
                    Vector3 pos = assignedPlace.transform.position + new Vector3(0f, 0.6f, 0f);
                    Quaternion rot = isTapped
                        ? Quaternion.Euler(0f, -90f, 0f)
                        : Quaternion.identity;
                    MoveToPoint(pos, rot);
                }
            }
            return; // Block hover on everything else
        }

        // During react discard selection: extra hover on highlighted cards only
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingReactDiscard)
        {
            if (inHand && MagicController.instance.IsHighlightedForReactDiscard(this))
            {
                HandController hc = OwnerHand;
                if (hc != null && handPosition < hc.cardPositions.Count)
                {
                    Vector3 pos = hc.cardPositions[handPosition] + new Vector3(0f, 2.2f, 0.7f);
                    MoveToPoint(pos, Quaternion.identity);
                }
            }
            return; // Block hover on everything else
        }

        BattleController bc = BattleController.instance;
        HandController theHC = OwnerHand;

        if (this == bc.pendingAvatar) return;

        if ((bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
            && inHand && !isSelected)
        {
            // Don't hover-raise cards already selected as tributes (they're already up)
            if (bc.currentTributes.Contains(this)) return;

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

        // During discard-for-effect selection: return highlighted cards to raised position
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingDiscardForEffect)
        {
            if (inHand && MagicController.instance.IsHighlightedForDiscard(this))
            {
                HandController hc = OwnerHand;
                if (hc != null && handPosition < hc.cardPositions.Count)
                {
                    Vector3 pos = hc.cardPositions[handPosition] + new Vector3(0f, 1.5f, 0.5f);
                    MoveToPoint(pos, hc.minPos.rotation);
                }
            }
            return; // Block exit hover on everything else too
        }

        // During temp boost selection: return highlighted avatars to raised position
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingTempBoostTarget)
        {
            if (!inHand && cardType == CardType.Avatar
                && MagicController.instance.IsHighlightedForTempBoost(this))
            {
                if (assignedPlace != null)
                {
                    Vector3 pos = assignedPlace.transform.position + new Vector3(0f, 0.3f, 0f);
                    Quaternion rot = isTapped
                        ? Quaternion.Euler(0f, -90f, 0f)
                        : Quaternion.identity;
                    MoveToPoint(pos, rot);
                }
            }
            return; // Block exit hover on everything else
        }

        // During react discard selection: return highlighted cards to raised position
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingReactDiscard)
        {
            if (inHand && MagicController.instance.IsHighlightedForReactDiscard(this))
            {
                HandController hc = OwnerHand;
                if (hc != null && handPosition < hc.cardPositions.Count)
                {
                    Vector3 pos = hc.cardPositions[handPosition] + new Vector3(0f, 1.5f, 0.5f);
                    MoveToPoint(pos, hc.minPos.rotation);
                }
            }
            return; // Block exit hover on everything else
        }

        BattleController bc = BattleController.instance;
        HandController theHC = OwnerHand;

        if (this == bc.pendingAvatar) return;

        if ((bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
            && inHand && !isSelected)
        {
            // Don't move selected tributes back down on hover exit
            if (bc.currentTributes.Contains(this)) return;

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

        // ── HELL ZONE: double-click card in hell → open hell viewer ──
        if (assignedPlace != null && assignedPlace.isPlayerHellPoint)
        {
            if (isLeftClick)
            {
                float timeSinceLastClick = Time.unscaledTime - _lastHellClickTime;
                _lastHellClickTime = Time.unscaledTime;

                if (timeSinceLastClick <= 0.5f)
                {
                    Player zoneOwner = GameManager.instance.GetPlayer(cardOwner);
                    UIController.instance?.ShowHellZoneViewer(zoneOwner);
                }
            }
            return; // Cards in hell have no other interactions
        }

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

        // ── JUTI TARGET SELECTION: route clicks to AvatarAbilityController ──
        if (AvatarAbilityController.instance != null
            && AvatarAbilityController.instance.IsSelectingJutiTarget)
        {
            if (isLeftClick && !inHand && cardType == CardType.Avatar)
                AvatarAbilityController.instance.HandleAvatarClickForJuti(this);
            else if (isRightClick)
                AvatarAbilityController.instance.CancelJutiSelection();
            return; // Block all other interaction during Juti target selection
        }

        // ── REACT / HELL ACTIVATION: block all card interaction while waiting ──
        if (MagicController.instance != null
            && (MagicController.instance.magicState == MagicPlayState.AwaitingReactConfirm
             || MagicController.instance.magicState == MagicPlayState.AwaitingHellActivation))
        {
            return; // Block all interaction — owner must click Activate or Keep
        }

        // ── REACT DISCARD SELECTION: player picks which hand card to discard for React ──
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingReactDiscard)
        {
            if (isLeftClick && inHand)
            {
                MagicController.instance.HandleHandCardClickForReactDiscard(this);
            }
            else if (isRightClick)
            {
                MagicController.instance.CancelReactDiscardSelection();
            }
            return; // Block all other interaction during react discard selection
        }

        // ── MODIFICATION TARGET SELECTION: route clicks to MagicController ──
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingModTarget)
        {
            if (isLeftClick && !inHand && cardType == CardType.Avatar
                && cardOwner == GameManager.instance.currentPlayer)
            {
                MagicController.instance.HandleAvatarClickForMod(this);
            }
            else if (isRightClick)
            {
                MagicController.instance.CancelModSelection();
            }
            return; // Block all other interaction during mod selection
        }

        // ── TEMP BOOST TARGET SELECTION: player picks which avatar to boost ──
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingTempBoostTarget)
        {
            if (isLeftClick && !inHand && cardType == CardType.Avatar
                && cardOwner == GameManager.instance.currentPlayer)
            {
                MagicController.instance.HandleAvatarClickForTempBoost(this);
            }
            else if (isRightClick)
            {
                MagicController.instance.CancelTempBoostSelection();
            }
            return; // Block all other interaction during temp boost selection
        }

        // ── DISCARD-FOR-EFFECT SELECTION: player picks which hand card to discard ──
        if (MagicController.instance != null
            && MagicController.instance.magicState == MagicPlayState.SelectingDiscardForEffect)
        {
            if (isLeftClick && inHand && cardOwner == GameManager.instance.currentPlayer)
            {
                MagicController.instance.HandleHandCardClickForDiscard(this);
            }
            else if (isRightClick)
            {
                MagicController.instance.CancelDiscardForEffect();
            }
            return; // Block all other interaction during discard selection
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

        // ── READY TO PLACE: drag avatar to board, or right-click to cancel ──
        if (bc.currentState == SummonState.ReadyToPlace && this == bc.pendingAvatar && inHand)
        {
            if (isLeftClick)
            {
                isSelected = true;
                DisableInteraction();
                justPressed = true;
                Debug.Log($"[Phase 3] Dragging unlocked avatar {cardName} to board.");
            }
            else if (isRightClick)
            {
                bc.CancelFullSummon();
            }
            return;
        }

        // ── COST STEP / READY TO PLACE: click to select/deselect tribute cards ──
        if (bc.currentState == SummonState.CostStep || bc.currentState == SummonState.ReadyToPlace)
        {
            if (this == bc.pendingAvatar)
            {
                if (isRightClick) bc.CancelFullSummon();
                return;
            }

            if (inHand)
            {
                if (isLeftClick)
                {
                    bc.ToggleTribute(this);
                }
                else if (isRightClick)
                {
                    if (bc.currentTributes.Contains(this))
                        bc.ReturnTribute(this);
                }
            }
            return;
        }

        if (bc.currentState == SummonState.Idle && inHand)
        {
            if (cardType == CardType.Avatar)
            {
                // Ability 3: Prevent summoning avatars that cannot be summoned from hand
                if (HasAbility(AvatarAbility.CannotSummonFromHand))
                {
                    Debug.Log($"[Card] {cardName}: Cannot be summoned from hand (ability)!");
                    return;
                }
                bool summonInitiated = bc.InitiateSummon(this);
                if (!summonInitiated)
                {
                    // Free avatar (cost 0) — drag directly, slide hand down
                    isSelected = true;
                    DisableInteraction();
                    justPressed = true;
                    HandController theHC = OwnerHand;
                    if (theHC != null) theHC.SlideDown();
                }
                return;
            }

            // Magic card — drag directly, slide hand down
            isSelected = true;
            DisableInteraction();
            justPressed = true;
            {
                HandController theHC = OwnerHand;
                if (theHC != null) theHC.SlideDown();
            }
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

        // Show power on board for avatars
        RefreshPowerDisplay();

        Debug.Log($"{cardName} ({cardType}) placed at: {point.name}");

        // Trigger React magic and Land buffs for newly summoned avatars
        if (cardType == CardType.Avatar && MagicController.instance != null)
            MagicController.instance.OnAvatarSummoned(this);

        // Recalculate SymbolAura buffs (new avatar on field may gain or provide aura)
        if (cardType == CardType.Avatar && AvatarAbilityController.instance != null)
            AvatarAbilityController.instance.RecalculateAllAuraBuffs();
    }

    /// <summary>
    /// Update the power text display (visible on board for avatars).
    /// Call after any power modification (magic effects, buffs, etc.)
    /// </summary>

    public void RefreshPowerDisplay()
    {
        if (powerText != null)
        {
            if (cardType == CardType.Avatar)
            {
                powerText.gameObject.SetActive(true);

                if (power > basePower)
                    powerText.text = $"<color=#79B445>{power}</color>";
                else if (power < basePower)
                    powerText.text = $"<color=#D44400>{power}</color>";
                else
                    powerText.text = power.ToString();
            }
            else
            {
                powerText.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Check if this avatar has a specific ability flag.</summary>
    public bool HasAbility(AvatarAbility ability)
    {
        return (abilities & ability) != 0;
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

    /// <summary>
    /// Fade the death glow on or off. Pass a color (e.g. red) and alpha 0→1.
    /// Call with alpha=0 to turn off.
    /// </summary>
    public void SetDeathGlow(Color glowColor, float alpha)
    {
        if (deathGlowImage != null)
        {
            glowColor.a = alpha;
            deathGlowImage.color = glowColor;
        }
    }

    /// <summary>
    /// Show or hide the modification highlight glow on this avatar.
    /// Color is derived from the mod card's gemColor (Red/Blue/Yellow).
    /// Call with on=false to clear the highlight.
    /// </summary>
    private static readonly Color modHighlightColor = new Color(0f, 0.898f, 0.145f, 1f); // #00E525

    public void SetModHighlight(bool on, CardColor modColor = CardColor.Neutral)
    {
        if (modHighlightImage == null) return;
        modHighlightImage.color = on ? modHighlightColor : normalColor;
    }

    /// <summary>
    /// Refresh the mod highlight based on currently attached mods.
    /// Uses the first attached mod's gemColor. Clears if no mods.
    /// </summary>
    public void RefreshModHighlight()
    {
        if (modHighlightImage == null) return;
        if (attachedMods.Count == 0)
        {
            modHighlightImage.color = normalColor;
            return;
        }
        // Fixed green highlight (#00E525) when any mod is attached
        SetModHighlight(true);
    }

    /// <summary>Map CardColor enum to a Unity Color.</summary>
    public static Color GetColorFromCardColor(CardColor cc)
    {
        switch (cc)
        {
            case CardColor.Red:     return new Color(1f, 0.2f, 0.2f);
            case CardColor.Blue:    return new Color(0.2f, 0.5f, 1f);
            case CardColor.Yellow:  return new Color(1f, 0.9f, 0.2f);
            default:                return new Color(1f, 1f, 1f);  // Neutral = white
        }
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
        if (theHC != null)
        {
            theHC.SlideUp();  // Drag cancelled — slide hand back up
            if (handPosition < theHC.cardPositions.Count)
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
        // LIFE cards are horizontal (-90° Y). Face-down adds 180° Z flip.
        // Using Z-axis (not X) avoids gimbal lock and gives a clean 180° visual flip.
        if (isLifeCard && assignedPlace != null)
        {
            Quaternion rot = faceDown
                ? Quaternion.Euler(0f, -90f, 180f)   // Horizontal + Z-flipped (card back up)
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
