using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// State machine for the "Select → Pay → Drag" avatar summoning system.
/// 
/// States:
///   Idle        – No summon in progress. Normal card interaction.
///   CostStep    – An avatar is selected (pending). Player drags tribute cards to the Hell Point.
///   ReadyToPlace – Enough gems paid. The pending avatar is now draggable to a board slot.
/// </summary>
public enum SummonState { Idle, CostStep, ReadyToPlace }

public class BattleController : MonoBehaviour
{
    public static BattleController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Summoning State")]
    public SummonState currentState = SummonState.Idle;
    public Card pendingAvatar;                                     // The avatar waiting to be summoned
    public List<Card> currentTributes = new List<Card>();           // Cards dropped on hell point as payment
    public int totalGemsPaid;

    [Header("Hell Zone (Graveyard)")]
    public Transform hellZone;                                     // Assign in Inspector — world position for graveyard stack
    public List<Card> graveyard = new List<Card>();

    // ── CACHED REFS ────────────────────────────────────────────────
    private HandController theHC;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        theHC = FindFirstObjectByType<HandController>();
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 1 — INITIATION  (called from Card.OnPointerDown)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Player left-clicked an Avatar in hand.
    /// Cost == 0 → returns false so Card can go straight to drag mode.
    /// Cost >  0 → enters CostStep, returns true.
    /// </summary>
    public bool InitiateSummon(Card avatar)
    {
        if (currentState != SummonState.Idle) return false;
        if (avatar.cardType != CardType.Avatar) return false;

        // Free summon — let Card handle normal drag
        if (avatar.cost <= 0) return false;

        // Enter CostStep
        currentState = SummonState.CostStep;
        pendingAvatar = avatar;
        currentTributes.Clear();
        totalGemsPaid = 0;

        // Visual: lift the avatar slightly to show it's "selected/pending"
        if (theHC != null && avatar.handPosition < theHC.cardPositions.Count)
        {
            avatar.MoveToPoint(
                theHC.cardPositions[avatar.handPosition] + new Vector3(0f, 1.5f, 0.5f),
                Quaternion.identity);
        }
        avatar.SetPitchHighlight(true);

        UIController.instance.ShowPaymentUI(avatar.cardName, 0, avatar.cost);
        Debug.Log($"[Phase 1] Summoning initiated: {avatar.cardName} (cost {avatar.cost}, color {avatar.avatarColor})");
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 2 — COST STEP  (called when a card is dropped on hell point)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates and accepts a tribute card dropped onto the Hell Point.
    /// Uses the hell CardPlacePoint's transform.position as the stacking anchor.
    /// Returns true if accepted, false if rejected.
    /// </summary>
    public bool TryPayTribute(Card tribute, CardPlacePoint hellPoint)
    {
        if (currentState != SummonState.CostStep) return false;
        if (tribute == pendingAvatar) return false;

        // Rule B: Gem must be > 0
        if (tribute.gem <= 0)
        {
            Debug.Log($"[Phase 2] {tribute.cardName} has 0 gems — cannot tribute.");
            return false;
        }

        // Rule A: Color match — colored gem only pays for matching avatar color
        if (tribute.gemColor != CardColor.Neutral &&
            tribute.gemColor != pendingAvatar.avatarColor)
        {
            Debug.Log($"[Phase 2] {tribute.cardName} gemColor={tribute.gemColor} doesn't match {pendingAvatar.avatarColor}.");
            return false;
        }

        // Accept tribute
        currentTributes.Add(tribute);
        totalGemsPaid += tribute.gem;

        // Remove from hand
        tribute.inHand = false;
        tribute.isSelected = false;
        tribute.EnableInteraction();
        if (theHC != null) theHC.RemoveCardFromHand(tribute);

        // Snap to the hell point's transform with a stack offset (NOT hit.point)
        Vector3 anchor = hellPoint.transform.position;
        Vector3 offset = new Vector3(0f, 0.05f * currentTributes.Count, 0f);
        tribute.MoveToPoint(anchor + offset, Quaternion.identity);

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
        Debug.Log($"[Phase 2] Tributed {tribute.cardName} (gem {tribute.gem}). Total: {totalGemsPaid}/{pendingAvatar.cost}");

        // Check if payment is complete → transition to ReadyToPlace
        if (totalGemsPaid >= pendingAvatar.cost)
        {
            EnterReadyToPlace();
        }

        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 3 — READY TO PLACE  (avatar unlocked for dragging)
    // ════════════════════════════════════════════════════════════════

    private void EnterReadyToPlace()
    {
        currentState = SummonState.ReadyToPlace;

        // Visual cue: highlight green
        pendingAvatar.SetReadyHighlight(true);

        UIController.instance.ShowReadyToPlace(pendingAvatar.cardName);
        Debug.Log($"[Phase 3] {pendingAvatar.cardName} is now READY TO PLACE. Drag to a board slot.");
    }

    /// <summary>
    /// Called when the unlocked avatar is successfully placed on a board slot.
    /// Finalizes tributes into graveyard, resets state.
    /// </summary>
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

        if (theHC != null) theHC.RemoveCardFromHand(pendingAvatar);

        // Move tributes officially to graveyard
        foreach (var tribute in currentTributes)
        {
            graveyard.Add(tribute);
            Debug.Log($"[Hell Zone] {tribute.cardName} → graveyard.");
        }

        // One-to-One rule: excess gems are lost — no carry-over
        if (totalGemsPaid > pendingAvatar.cost)
        {
            Debug.Log($"[Summon] Overpaid {totalGemsPaid - pendingAvatar.cost} gems — excess lost.");
        }

        Debug.Log($"[Summon] {pendingAvatar.cardName} summoned to {placePoint.name}!");

        ResetSummonState();
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE 4 — CANCELLATION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Right-click on a specific tribute card at the hell point.
    /// Returns it to hand and subtracts its gem value.
    /// </summary>
    public void ReturnTribute(Card tribute)
    {
        if (!currentTributes.Contains(tribute)) return;

        currentTributes.Remove(tribute);
        totalGemsPaid -= tribute.gem;

        // Return card to hand
        if (theHC != null)
        {
            theHC.heldCards.Add(tribute);
            theHC.SetCardPosistionsInHand();
        }
        tribute.ReturnToHand();

        // Restack remaining tributes so positions stay clean
        RestackTributes();

        Debug.Log($"[Cancel] Returned tribute {tribute.cardName}. Total now: {totalGemsPaid}/{pendingAvatar.cost}");

        // If we were ReadyToPlace but now underpaid, revert to CostStep
        if (currentState == SummonState.ReadyToPlace && totalGemsPaid < pendingAvatar.cost)
        {
            currentState = SummonState.CostStep;
            pendingAvatar.SetReadyHighlight(false);
            Debug.Log("[Cancel] No longer enough gems — back to CostStep.");
        }

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
    }

    /// <summary>
    /// Right-click on the pending avatar — full cancel.
    /// All tributes return to hand, avatar deselects.
    /// </summary>
    public void CancelFullSummon()
    {
        if (currentState == SummonState.Idle) return;

        Debug.Log($"[Cancel] Full summon cancelled for {pendingAvatar?.cardName}.");

        // Return all tributes to hand
        foreach (var tribute in currentTributes)
        {
            if (theHC != null)
            {
                theHC.heldCards.Add(tribute);
            }
            tribute.ReturnToHand();
        }
        if (theHC != null) theHC.SetCardPosistionsInHand();

        // Deselect avatar
        if (pendingAvatar != null)
        {
            pendingAvatar.SetPitchHighlight(false);
            pendingAvatar.SetReadyHighlight(false);
            pendingAvatar.ReturnToHand();
        }

        ResetSummonState();
    }

    // ── RESTACK TRIBUTES ───────────────────────────────────────
    /// <summary>
    /// Repositions all tribute cards at the hell zone with correct stacking offsets.
    /// Called after a tribute is returned to keep the stack tidy.
    /// </summary>
    private void RestackTributes()
    {
        if (hellZone == null) return;

        for (int i = 0; i < currentTributes.Count; i++)
        {
            Vector3 offset = new Vector3(0f, 0.05f * (i + 1), 0f);
            currentTributes[i].MoveToPoint(hellZone.position + offset, Quaternion.identity);
        }
    }

    // ── RESET ──────────────────────────────────────────────────
    private void ResetSummonState()
    {
        currentTributes.Clear();
        totalGemsPaid = 0;
        pendingAvatar = null;
        currentState = SummonState.Idle;

        UIController.instance.HidePaymentUI();
    }
}
