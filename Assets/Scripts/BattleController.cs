using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// State machine for the "Select → Click-to-Pay → Drag" avatar summoning system.
///
/// States:
///   Idle        – No summon in progress. Normal card interaction.
///   CostStep    – An avatar is selected (pending). Player clicks hand cards to select as tribute (they raise up).
///   ReadyToPlace – Enough gems paid. The pending avatar is now draggable to a board slot.
///
/// Tributes stay in hand (raised up) until the avatar is placed, then go to Hell.
/// No longer uses its own HandController cache — uses the current player's hand
/// via GameManager.instance.CurrentPlayerObj.hand.
/// </summary>
public enum SummonState { Idle, CostStep, ReadyToPlace }

public class BattleController : MonoBehaviour
{
    public static BattleController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Summoning State")]
    public SummonState currentState = SummonState.Idle;
    public Card pendingAvatar;
    public List<Card> currentTributes = new List<Card>();
    public int totalGemsPaid;

    [Header("Graveyard Tracking")]
    public List<Card> graveyard = new List<Card>();

    // ── REMOVED: hellZone Transform — now uses CurrentPlayer.hellZone ──
    // ── REMOVED: theHC — now uses CurrentHand property ──

    private void Awake()
    {
        instance = this;
    }

    // ── HELPER PROPERTIES ─────────────────────────────────────────
    /// <summary>Current player's hand controller.</summary>
    private HandController CurrentHand
        => GameManager.instance?.CurrentPlayerObj?.hand;

    /// <summary>Current player's hell zone transform (for tribute stacking).</summary>
    private Transform CurrentHellTransform
        => GameManager.instance?.CurrentPlayerObj?.hellZone?.transform;

    // ════════════════════════════════════════════════════════════════
    //  PHASE 1 — INITIATION  (called from Card.OnPointerDown)
    // ════════════════════════════════════════════════════════════════

    public bool InitiateSummon(Card avatar)
    {
        if (currentState != SummonState.Idle) return false;
        if (avatar.cardType != CardType.Avatar) return false;

        // Phase check: summoning only allowed during Main Phase
        if (GameManager.instance != null && !GameManager.instance.IsMainPhase())
        {
            Debug.Log("[BattleController] Cannot summon outside Main Phase!");
            return false;
        }

        // Free summon — let Card handle normal drag
        if (avatar.cost <= 0) return false;

        // Enter CostStep
        currentState = SummonState.CostStep;
        pendingAvatar = avatar;
        currentTributes.Clear();
        totalGemsPaid = 0;

        HandController hc = CurrentHand;
        if (hc != null && avatar.handPosition < hc.cardPositions.Count)
        {
            avatar.MoveToPoint(
                hc.cardPositions[avatar.handPosition] + new Vector3(0f, 1.5f, 0.5f),
                Quaternion.identity);
        }
        avatar.SetPitchHighlight(true);

        UIController.instance.ShowPaymentUI(avatar.cardName, 0, avatar.cost);
        Debug.Log($"[Phase 1] Summoning initiated: {avatar.cardName} (cost {avatar.cost}, color {avatar.avatarColor})");
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 2 — COST STEP  (click-to-select tributes)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggle a hand card as tribute. Left-click selects (raises card up),
    /// clicking again or right-click deselects (lowers card back down).
    /// Cards stay in hand until avatar is placed, then go to Hell.
    /// </summary>
    public bool ToggleTribute(Card card)
    {
        if (currentState != SummonState.CostStep && currentState != SummonState.ReadyToPlace)
            return false;
        if (card == pendingAvatar) return false;

        // Already selected → deselect
        if (currentTributes.Contains(card))
        {
            ReturnTribute(card);
            return true;
        }

        // Can't add more tributes if already enough gems
        if (currentState == SummonState.ReadyToPlace) return false;

        // ── Validate tribute ──
        if (card.cannotBeTribute)
        {
            Debug.Log($"[Summon] {card.cardName} cannot be used as tribute (searched from deck).");
            return false;
        }
        if (card.gem <= 0)
        {
            Debug.Log($"[Phase 2] {card.cardName} has 0 gems — cannot tribute.");
            return false;
        }
        if (card.gemColor != CardColor.Neutral &&
            card.gemColor != pendingAvatar.avatarColor)
        {
            Debug.Log($"[Phase 2] {card.cardName} gemColor={card.gemColor} doesn't match {pendingAvatar.avatarColor}.");
            return false;
        }

        // ── Accept tribute — raise card up in hand ──
        currentTributes.Add(card);
        totalGemsPaid += card.gem;

        card.SetPitchHighlight(true);
        HandController hc = CurrentHand;
        if (hc != null && card.handPosition < hc.cardPositions.Count)
        {
            card.MoveToPoint(
                hc.cardPositions[card.handPosition] + new Vector3(0f, 1.5f, 0.5f),
                Quaternion.identity);
        }

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
        Debug.Log($"[Phase 2] Selected {card.cardName} as tribute (gem {card.gem}). Total: {totalGemsPaid}/{pendingAvatar.cost}");

        if (totalGemsPaid >= pendingAvatar.cost)
        {
            EnterReadyToPlace();
        }

        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 3 — READY TO PLACE
    // ════════════════════════════════════════════════════════════════

    private void EnterReadyToPlace()
    {
        currentState = SummonState.ReadyToPlace;
        pendingAvatar.SetReadyHighlight(true);

        // ── HAND SLIDE: done selecting tributes, slide hand down while dragging to board ──
        HandController hc = CurrentHand;
        if (hc != null) hc.SlideDown();

        UIController.instance.ShowReadyToPlace(pendingAvatar.cardName);
        Debug.Log($"[Phase 3] {pendingAvatar.cardName} is now READY TO PLACE. Drag to a board slot.");
    }

    public void FinalizeSummon(CardPlacePoint placePoint)
    {
        if (currentState != SummonState.ReadyToPlace || pendingAvatar == null) return;

        // Place avatar on board
        placePoint.activeCard = pendingAvatar;
        pendingAvatar.assignedPlace = placePoint;
        pendingAvatar.isSelected = false;
        pendingAvatar.inHand = false;
        pendingAvatar.SetPitchHighlight(false);
        pendingAvatar.SetReadyHighlight(false);
        pendingAvatar.EnableInteraction();
        pendingAvatar.MoveToPoint(placePoint.transform.position, Quaternion.identity);

        // Record turn number for summon sickness
        if (GameManager.instance != null)
            pendingAvatar.turnPlaced = GameManager.instance.turnNumber;

        HandController hc = CurrentHand;
        if (hc != null) hc.RemoveCardFromHand(pendingAvatar);

        // Send selected tributes to Hell (they were still in hand, raised up)
        foreach (var tribute in currentTributes)
        {
            tribute.SetPitchHighlight(false);
            tribute.inHand = false;
            if (hc != null) hc.RemoveCardFromHand(tribute);
            CombatController.instance.SendToHell(tribute);
            graveyard.Add(tribute);
            Debug.Log($"[Hell Zone] {tribute.cardName} → Hell.");
        }

        if (totalGemsPaid > pendingAvatar.cost)
        {
            Debug.Log($"[Summon] Overpaid {totalGemsPaid - pendingAvatar.cost} gems — excess lost.");
        }

        Debug.Log($"[Summon] {pendingAvatar.cardName} summoned to {placePoint.name}!");
        if (GameplayLogger.instance != null)
        {
            string tributeNames = "";
            foreach (Card t in currentTributes)
                tributeNames += (tributeNames.Length > 0 ? ", " : "") + t.cardName;
            string tributeInfo = tributeNames.Length > 0 ? $" (tributed: {tributeNames})" : "";
            GameplayLogger.instance.LogSummon($"{pendingAvatar.cardName} summoned!{tributeInfo}");
        }

        // Save reference before ResetSummonState() clears pendingAvatar
        Card summonedAvatar = pendingAvatar;

        ResetSummonState();

        // Trigger React magic and Land buffs for newly summoned avatar
        if (MagicController.instance != null)
            MagicController.instance.OnAvatarSummoned(summonedAvatar);

        // Trigger Juti abilities (on-summon-from-cost path only)
        if (AvatarAbilityController.instance != null)
        {
            AvatarAbilityController.instance.OnAvatarSummonedFromCost(summonedAvatar);
            AvatarAbilityController.instance.RecalculateAllAuraBuffs();
        }

        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 4 — CANCELLATION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deselect a tribute card — lower it back down in hand.
    /// </summary>
    public void ReturnTribute(Card tribute)
    {
        if (!currentTributes.Contains(tribute)) return;

        currentTributes.Remove(tribute);
        totalGemsPaid -= tribute.gem;

        // Lower card back to normal hand position
        tribute.SetPitchHighlight(false);
        HandController hc = CurrentHand;
        if (hc != null && tribute.handPosition < hc.cardPositions.Count)
        {
            tribute.MoveToPoint(hc.cardPositions[tribute.handPosition], hc.minPos.rotation);
        }

        Debug.Log($"[Cancel] Deselected tribute {tribute.cardName}. Total now: {totalGemsPaid}/{pendingAvatar.cost}");

        if (currentState == SummonState.ReadyToPlace && totalGemsPaid < pendingAvatar.cost)
        {
            currentState = SummonState.CostStep;
            pendingAvatar.SetReadyHighlight(false);

            // ── HAND SLIDE: back to CostStep — player needs to select more tributes ──
            HandController hcSlide = CurrentHand;
            if (hcSlide != null) hcSlide.SlideUp();

            Debug.Log("[Cancel] No longer enough gems — back to CostStep.");
        }

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
    }

    public void CancelFullSummon()
    {
        if (currentState == SummonState.Idle) return;

        Debug.Log($"[Cancel] Full summon cancelled for {pendingAvatar?.cardName}.");

        // Tributes never left hand — just clear highlights and lower back down
        foreach (var tribute in currentTributes)
        {
            tribute.SetPitchHighlight(false);
        }

        HandController hc = CurrentHand;
        if (hc != null) hc.SetCardPosistionsInHand();

        if (pendingAvatar != null)
        {
            pendingAvatar.SetPitchHighlight(false);
            pendingAvatar.SetReadyHighlight(false);
            pendingAvatar.ReturnToHand();
        }

        ResetSummonState();
    }

    // ── RESET ────────────────────────────────────────────────────
    private void ResetSummonState()
    {
        currentTributes.Clear();
        totalGemsPaid = 0;
        pendingAvatar = null;
        currentState = SummonState.Idle;

        // ── HAND SLIDE: slide back up if still in Main Phase (player may play more cards) ──
        HandController hc = CurrentHand;
        if (hc != null && GameManager.instance != null && GameManager.instance.IsMainPhase())
            hc.SlideUp();

        UIController.instance.HidePaymentUI();
    }
}
