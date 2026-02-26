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

    void Start()
    {
        SetCardPosistionsInHand();
    }

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

        for (int i = 0; i < heldCards.Count; i++)
        {
            if (heldCards[i] == null)
            {
                Debug.LogWarning($"HandController: heldCards[{i}] is null — skipping.");
                continue;
            }

            cardPositions.Add(minPos.position + (distanceBetweenPoints * i));

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
