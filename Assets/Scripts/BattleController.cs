using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// State machine for the "Select → Pay → Drag" avatar summoning system.
///
/// States:
///   Idle        – No summon in progress. Normal card interaction.
///   CostStep    – An avatar is selected (pending). Player drags tribute cards to the Hell Point.
///   ReadyToPlace – Enough gems paid. The pending avatar is now draggable to a board slot.
///
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
    //  PHASE 2 — COST STEP
    // ════════════════════════════════════════════════════════════════

    public bool TryPayTribute(Card tribute, CardPlacePoint hellPoint)
    {
        if (currentState != SummonState.CostStep) return false;
        if (tribute == pendingAvatar) return false;

        if (tribute.gem <= 0)
        {
            Debug.Log($"[Phase 2] {tribute.cardName} has 0 gems — cannot tribute.");
            return false;
        }

        if (tribute.gemColor != CardColor.Neutral &&
            tribute.gemColor != pendingAvatar.avatarColor)
        {
            Debug.Log($"[Phase 2] {tribute.cardName} gemColor={tribute.gemColor} doesn't match {pendingAvatar.avatarColor}.");
            return false;
        }

        // Accept tribute
        currentTributes.Add(tribute);
        totalGemsPaid += tribute.gem;

        tribute.inHand = false;
        tribute.isSelected = false;
        tribute.EnableInteraction();

        HandController hc = CurrentHand;
        if (hc != null) hc.RemoveCardFromHand(tribute);

        // Snap to the hell point's transform with a stack offset
        Vector3 anchor = hellPoint.transform.position;
        Vector3 offset = new Vector3(0f, 0.05f * currentTributes.Count, 0f);
        tribute.MoveToPoint(anchor + offset, Quaternion.identity);

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
        Debug.Log($"[Phase 2] Tributed {tribute.cardName} (gem {tribute.gem}). Total: {totalGemsPaid}/{pendingAvatar.cost}");

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

        HandController hc = CurrentHand;
        if (hc != null) hc.RemoveCardFromHand(pendingAvatar);

        // Move tributes to graveyard
        foreach (var tribute in currentTributes)
        {
            graveyard.Add(tribute);
            Debug.Log($"[Hell Zone] {tribute.cardName} → graveyard.");
        }

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

    public void ReturnTribute(Card tribute)
    {
        if (!currentTributes.Contains(tribute)) return;

        currentTributes.Remove(tribute);
        totalGemsPaid -= tribute.gem;

        HandController hc = CurrentHand;
        if (hc != null)
        {
            hc.heldCards.Add(tribute);
            hc.SetCardPosistionsInHand();
        }
        tribute.ReturnToHand();

        RestackTributes();

        Debug.Log($"[Cancel] Returned tribute {tribute.cardName}. Total now: {totalGemsPaid}/{pendingAvatar.cost}");

        if (currentState == SummonState.ReadyToPlace && totalGemsPaid < pendingAvatar.cost)
        {
            currentState = SummonState.CostStep;
            pendingAvatar.SetReadyHighlight(false);
            Debug.Log("[Cancel] No longer enough gems — back to CostStep.");
        }

        UIController.instance.ShowPaymentUI(pendingAvatar.cardName, totalGemsPaid, pendingAvatar.cost);
    }

    public void CancelFullSummon()
    {
        if (currentState == SummonState.Idle) return;

        Debug.Log($"[Cancel] Full summon cancelled for {pendingAvatar?.cardName}.");

        HandController hc = CurrentHand;
        foreach (var tribute in currentTributes)
        {
            if (hc != null)
            {
                hc.heldCards.Add(tribute);
            }
            tribute.ReturnToHand();
        }
        if (hc != null) hc.SetCardPosistionsInHand();

        if (pendingAvatar != null)
        {
            pendingAvatar.SetPitchHighlight(false);
            pendingAvatar.SetReadyHighlight(false);
            pendingAvatar.ReturnToHand();
        }

        ResetSummonState();
    }

    // ── RESTACK TRIBUTES ─────────────────────────────────────────
    private void RestackTributes()
    {
        Transform hell = CurrentHellTransform;
        if (hell == null) return;

        for (int i = 0; i < currentTributes.Count; i++)
        {
            Vector3 offset = new Vector3(0f, 0.05f * (i + 1), 0f);
            currentTributes[i].MoveToPoint(hell.position + offset, Quaternion.identity);
        }
    }

    // ── RESET ────────────────────────────────────────────────────
    private void ResetSummonState()
    {
        currentTributes.Clear();
        totalGemsPaid = 0;
        pendingAvatar = null;
        currentState = SummonState.Idle;

        UIController.instance.HidePaymentUI();
    }
}
