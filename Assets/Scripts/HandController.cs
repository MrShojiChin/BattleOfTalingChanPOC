using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages one player's hand of cards.
/// No longer a singleton — each Player owns their own HandController.
/// </summary>
public class HandController : MonoBehaviour
{
    // ── REMOVED: public static HandController instance; ──────────

    public List<Card> heldCards = new List<Card>();

    public Transform minPos, maxPos;
    public List<Vector3> cardPositions = new List<Vector3>();

    // ── HAND SLIDE ──────────────────────────────────────────────────
    [Header("Hand Slide")]
    [Tooltip("How far to slide the hand off-screen (Z = toward camera, Y = height)")]
    public Vector3 slideDownOffset = new Vector3(0f, -0.5f, -5f);

    private bool _isSlid = false;

    /// <summary>True when the hand is slid down (hidden).</summary>
    public bool IsSlid => _isSlid;

    void Start()
    {
        SetCardPosistionsInHand();
    }

    // ════════════════════════════════════════════════════════════════
    //  SLIDE DOWN / UP
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Slide the hand down (cards move off-screen / partially hidden).
    /// Called when it's NOT this player's turn, or during ReadyToPlace.
    /// </summary>
    public void SlideDown()
    {
        if (_isSlid) return;
        _isSlid = true;
        SetCardPosistionsInHand();
    }

    /// <summary>
    /// Slide the hand back up to normal visible position.
    /// Called when it becomes this player's turn.
    /// </summary>
    public void SlideUp()
    {
        if (!_isSlid) return;
        _isSlid = false;
        SetCardPosistionsInHand();
    }

    /// <summary>
    /// Returns the card position at index WITHOUT the slide offset.
    /// Used for the pending avatar which should stay raised during ReadyToPlace.
    /// </summary>
    public Vector3 GetBasePosition(int index)
    {
        if (heldCards.Count <= 1)
            return minPos.position;

        Vector3 step = (maxPos.position - minPos.position) / (heldCards.Count - 1);
        return minPos.position + (step * index);
    }

    // ════════════════════════════════════════════════════════════════
    //  CARD POSITIONING
    // ════════════════════════════════════════════════════════════════

    public void SetCardPosistionsInHand()
    {
        cardPositions.Clear();

        if (minPos == null || maxPos == null)
        {
            Debug.LogWarning("HandController: minPos or maxPos not assigned in Inspector!");
            return;
        }

        Vector3 distanceBetweenPoints = Vector3.zero;
        if (heldCards.Count > 1)
        {
            distanceBetweenPoints = (maxPos.position - minPos.position) / (heldCards.Count - 1);
        }

        // Apply slide offset when the hand is hidden
        Vector3 offset = _isSlid ? slideDownOffset : Vector3.zero;

        for (int i = 0; i < heldCards.Count; i++)
        {
            if (heldCards[i] == null)
            {
                Debug.LogWarning($"HandController: heldCards[{i}] is null — skipping.");
                continue;
            }

            cardPositions.Add(minPos.position + (distanceBetweenPoints * i) + offset);

            heldCards[i].handPosition = i;
            heldCards[i].inHand = true;

            // Don't reposition the pending avatar — it must stay raised
            if (BattleController.instance != null &&
                BattleController.instance.pendingAvatar == heldCards[i])
            {
                continue;
            }

            heldCards[i].MoveToPoint(cardPositions[i], minPos.rotation);
        }
    }

    public void RemoveCardFromHand(Card cardToRemove)
    {
        if (heldCards.Contains(cardToRemove))
        {
            heldCards.Remove(cardToRemove);
            SetCardPosistionsInHand();
        }
        else
        {
            Debug.LogError($"Card {cardToRemove.cardName} is not in hand!");
        }
    }

    /// <summary>
    /// Remove multiple cards at once (used by pitch/discard system).
    /// Repositions remaining cards after removal.
    /// </summary>
    public void RemoveCardsFromHand(List<Card> cardsToRemove)
    {
        foreach (var card in cardsToRemove)
        {
            heldCards.Remove(card);
        }
        SetCardPosistionsInHand();
    }

    public void AddCardToHand(Card cardToAdd)
    {
        heldCards.Add(cardToAdd);
        SetCardPosistionsInHand();
    }

    /// <summary>
    /// Returns all cards in hand that are marked for end-of-turn discard.
    /// </summary>
    public List<Card> GetMarkedForDiscard()
    {
        return heldCards.FindAll(c => c.markedForDiscard);
    }
}
