using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages one player's deck.
/// No longer a singleton — each Player owns their own DeckController.
/// Drawing is now triggered by GameManager (not Space key).
/// </summary>
public class DeckController : MonoBehaviour
{
    // ── REMOVED: public static DeckController instance; ──────────

    /// <summary>Set by Player.Start() — knows which player owns this deck.</summary>
    [HideInInspector] public Player owner;

    [Header("Deck List (assign in Inspector)")]
    public List<BaseCardSO> deckToUse = new List<BaseCardSO>();

    [Header("Card Prefabs")]
    public Card avatarCardPrefab;
    public Card magicCardPrefab;

    private List<BaseCardSO> activeCards = new List<BaseCardSO>();

    void Start()
    {
        SetupDeck();
    }

    // ── REMOVED: Update() with Space key — GameManager handles draw timing ──

    public void SetupDeck()
    {
        activeCards.Clear();

        List<BaseCardSO> tempDeck = new List<BaseCardSO>();
        tempDeck.AddRange(deckToUse);

        int iterations = 0;
        while (tempDeck.Count > 0 && iterations < 500)
        {
            int selected = Random.Range(0, tempDeck.Count);
            activeCards.Add(tempDeck[selected]);
            tempDeck.RemoveAt(selected);
            iterations++;
        }
    }

    /// <summary>
    /// Draw the top card from this deck and add it to the OWNER's hand.
    /// Tags the card with the owner's playerId so it knows who it belongs to.
    /// </summary>
    public void DrawCardToHand()
    {
        if (activeCards.Count == 0)
        {
            // TODO: Rulebook says deck-out = instant loss. For now, reshuffle.
            Debug.LogWarning($"[DeckController] {owner?.playerId} deck is empty — reshuffling.");
            SetupDeck();
        }

        if (owner == null || owner.hand == null)
        {
            Debug.LogError("[DeckController] Owner or owner.hand is null! Cannot draw.");
            return;
        }

        // Spawn position: use the owner's deck zone on the board (not this transform)
        Vector3 spawnPos = (owner.deckZone != null)
            ? owner.deckZone.transform.position
            : transform.position;

        // Draw the top card
        BaseCardSO drawnCard = activeCards[0];
        Card newCard = null;

        if (drawnCard is AvatarCardSO avatar)
        {
            newCard = Instantiate(avatarCardPrefab, spawnPos, Quaternion.identity);
            newCard.cardType = CardType.Avatar;
            newCard.avatarSO = avatar;
        }
        else if (drawnCard is MagicCardSO magic)
        {
            newCard = Instantiate(magicCardPrefab, spawnPos, Quaternion.identity);
            newCard.cardType = CardType.Magic;
            newCard.magicSO = magic;
        }

        // Tag the card with its owner BEFORE setup
        newCard.cardOwner = owner.playerId;

        newCard.SetupCard();

        activeCards.RemoveAt(0);

        // Add to the OWNER's hand (not a global singleton)
        owner.hand.AddCardToHand(newCard);

        Debug.Log($"[Deck] {owner.playerId} drew {newCard.cardName}. Cards left: {activeCards.Count}");
    }

    /// <summary>How many cards remain in this deck.</summary>
    public int CardsRemaining => activeCards.Count;

    // ════════════════════════════════════════════════════════════════
    //  MULLIGAN HELPERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return a card's ScriptableObject to the BOTTOM of the deck (for mulligan).
    /// The card is placed without shuffling — shuffle separately after all returns.
    /// </summary>
    public void ReturnCardToBottom(BaseCardSO cardSO)
    {
        activeCards.Add(cardSO);  // Add to end = bottom of deck
    }

    /// <summary>
    /// Shuffle the deck using Fisher-Yates.
    /// Called after mulligan swaps are complete.
    /// </summary>
    public void ShuffleDeck()
    {
        for (int i = activeCards.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (activeCards[i], activeCards[j]) = (activeCards[j], activeCards[i]);
        }
    }
}
