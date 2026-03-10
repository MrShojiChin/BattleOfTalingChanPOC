using UnityEngine;
using System.Collections;
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
    public Card lifeCardPrefab;

    [Header("LIFE Cards (separate from main deck — 5 per player)")]
    public List<LifeCardSO> lifeDeckToUse = new List<LifeCardSO>();

    private List<BaseCardSO> activeCards = new List<BaseCardSO>();
    private int lifeCardIndex = 0;  // Tracks how many LIFE cards have been dealt
    private bool deckInitialized = false;

    void Start()
    {
        // Safety net — SetupDeck() is also called by GameManager.SetupPhase()
        // to guarantee order. Skip if already done.
        if (!deckInitialized)
            SetupDeck();
    }

    // ── REMOVED: Update() with Space key — GameManager handles draw timing ──

    public void SetupDeck()
    {
        activeCards.Clear();
        deckInitialized = true;

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
            // Rulebook: deck-out = instant loss
            Debug.LogError($"[DeckController] {owner?.playerId} deck is EMPTY — DECK-OUT LOSS!");
            if (GameManager.instance != null)
            {
                GameManager.instance.DeclareLoser(owner.playerId, "deck is empty (deck-out)");
            }
            return;
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

        // Deck hits 0 after drawing → immediate loss
        if (activeCards.Count == 0 && GameManager.instance != null && !GameManager.instance.isGameOver)
        {
            Debug.LogError($"[DeckController] {owner.playerId} deck is now EMPTY — DECK-OUT LOSS!");
            GameManager.instance.DeclareLoser(owner.playerId, "deck is empty (deck-out)");
        }
    }

    /// <summary>How many cards remain in this deck.</summary>
    public int CardsRemaining => activeCards.Count;

    // ════════════════════════════════════════════════════════════════
    //  SEQUENTIAL DRAW (animated delay between each card)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw multiple cards one at a time with a short delay between each.
    /// Returns a Coroutine that callers can yield on to wait for completion.
    /// If count ≤ 1, draws immediately with no delay.
    /// </summary>
    public Coroutine DrawMultipleCards(int count, float delayBetween = 0.15f)
    {
        if (count <= 0) return null;
        if (count == 1)
        {
            DrawCardToHand();
            return null;
        }
        return StartCoroutine(DrawCardsSequential(count, delayBetween));
    }

    private IEnumerator DrawCardsSequential(int count, float delay)
    {
        for (int i = 0; i < count; i++)
        {
            DrawCardToHand();
            if (GameManager.instance != null && GameManager.instance.isGameOver)
                yield break;
            if (i < count - 1)
                yield return new WaitForSeconds(delay);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MILL / SEARCH (used by Magic effects)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// ถล่มสูป (Thon Soop): Send the top N cards from the deck to the Hell Zone.
    /// Cards are instantiated briefly so they appear in the Hell Zone card list.
    /// Returns how many cards were actually milled.
    /// </summary>
    public List<Card> MillCards(int count)
    {
        List<Card> milledCards = new List<Card>();
        for (int i = 0; i < count; i++)
        {
            if (activeCards.Count == 0) break;

            BaseCardSO topCard = activeCards[0];
            activeCards.RemoveAt(0);

            // Spawn the card, tag it, set up, then send to Hell
            Vector3 spawnPos = (owner.deckZone != null)
                ? owner.deckZone.transform.position
                : transform.position;

            Card newCard = null;
            if (topCard is AvatarCardSO avatar)
            {
                newCard = Instantiate(avatarCardPrefab, spawnPos, Quaternion.identity);
                newCard.cardType = CardType.Avatar;
                newCard.avatarSO = avatar;
            }
            else if (topCard is MagicCardSO magic)
            {
                newCard = Instantiate(magicCardPrefab, spawnPos, Quaternion.identity);
                newCard.cardType = CardType.Magic;
                newCard.magicSO = magic;
            }

            if (newCard != null)
            {
                newCard.cardOwner = owner.playerId;
                newCard.SetupCard();
                CombatController.instance.SendToHell(newCard);
                milledCards.Add(newCard);
                Debug.Log($"[Mill] {owner.playerId}: {newCard.cardName} sent from deck to Hell.");
            }
        }
        return milledCards;
    }

    // ════════════════════════════════════════════════════════════════
    //  SEQUENTIAL MILL (animated delay between each card)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mill multiple cards one at a time with a short delay between each.
    /// Populates the supplied resultList as cards are milled.
    /// Returns a Coroutine callers can yield on to wait for completion.
    /// If count ≤ 1, mills immediately with no delay.
    /// </summary>
    public Coroutine MillMultipleCards(int count, List<Card> resultList, float delayBetween = 0.2f)
    {
        if (count <= 0) return null;
        if (count == 1)
        {
            resultList.AddRange(MillCards(1));
            return null;
        }
        return StartCoroutine(MillCardsSequential(count, resultList, delayBetween));
    }

    private IEnumerator MillCardsSequential(int count, List<Card> resultList, float delay)
    {
        for (int i = 0; i < count; i++)
        {
            var milled = MillCards(1);
            resultList.AddRange(milled);
            if (GameManager.instance != null && GameManager.instance.isGameOver)
                yield break;
            if (i < count - 1)
                yield return new WaitForSeconds(delay);
        }
    }

    /// <summary>
    /// Search the deck for the first Avatar card with cost ≤ maxCost.
    /// If found, instantiate it, add to hand, and shuffle the deck.
    /// Returns the drawn Card, or null if not found.
    /// </summary>
    public Card SearchAndDrawAvatar(int maxCost)
    {
        // Find first matching avatar SO in the deck
        int foundIndex = -1;
        for (int i = 0; i < activeCards.Count; i++)
        {
            if (activeCards[i] is AvatarCardSO avatarSO && avatarSO.cost <= maxCost)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0) return null;

        BaseCardSO foundSO = activeCards[foundIndex];
        activeCards.RemoveAt(foundIndex);

        // Shuffle remaining deck
        for (int i = activeCards.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (activeCards[i], activeCards[r]) = (activeCards[r], activeCards[i]);
        }

        // Instantiate and add to hand
        Vector3 spawnPos = (owner.deckZone != null)
            ? owner.deckZone.transform.position
            : transform.position;

        Card newCard = Instantiate(avatarCardPrefab, spawnPos, Quaternion.identity);
        newCard.cardType = CardType.Avatar;
        newCard.avatarSO = (AvatarCardSO)foundSO;
        newCard.cardOwner = owner.playerId;
        newCard.SetupCard();

        owner.hand.AddCardToHand(newCard);
        Debug.Log($"[Search] {owner.playerId}: Found {newCard.cardName} (cost {newCard.cost}) in deck → added to hand. Deck shuffled.");

        return newCard;
    }

    /// <summary>
    /// Search the deck for the first Avatar card whose name starts with namePrefix
    /// and cost ≤ maxCost. If found, instantiate, add to hand, and shuffle deck.
    /// The returned card is marked cannotBeTribute = true.
    /// </summary>
    public Card SearchAndDrawAvatarByName(string namePrefix, int maxCost)
    {
        int foundIndex = -1;
        for (int i = 0; i < activeCards.Count; i++)
        {
            if (activeCards[i] is AvatarCardSO avatarSO
                && avatarSO.cost <= maxCost
                && avatarSO.cardName.StartsWith(namePrefix, System.StringComparison.Ordinal))
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0) return null;

        BaseCardSO foundSO = activeCards[foundIndex];
        activeCards.RemoveAt(foundIndex);

        // Shuffle remaining deck (Fisher-Yates)
        for (int i = activeCards.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (activeCards[i], activeCards[r]) = (activeCards[r], activeCards[i]);
        }

        // Instantiate and add to hand
        Vector3 spawnPos = (owner.deckZone != null)
            ? owner.deckZone.transform.position
            : transform.position;

        Card newCard = Instantiate(avatarCardPrefab, spawnPos, Quaternion.identity);
        newCard.cardType = CardType.Avatar;
        newCard.avatarSO = (AvatarCardSO)foundSO;
        newCard.cardOwner = owner.playerId;
        newCard.SetupCard();

        // Mark as cannot be used for tribute/summon cost
        newCard.cannotBeTribute = true;

        owner.hand.AddCardToHand(newCard);
        Debug.Log($"[Search] {owner.playerId}: Found '{newCard.cardName}' by name (cost {newCard.cost}) → hand. Cannot be tribute. Deck shuffled.");

        return newCard;
    }

    /// <summary>
    /// Query the deck for ALL Avatar cards matching namePrefix and cost ≤ maxCost.
    /// Read-only — does NOT remove cards from the deck.
    /// Used by the interactive deck search panel.
    /// </summary>
    public List<AvatarCardSO> FindMatchingAvatarsByName(string namePrefix, int maxCost)
    {
        List<AvatarCardSO> matches = new List<AvatarCardSO>();
        for (int i = 0; i < activeCards.Count; i++)
        {
            if (activeCards[i] is AvatarCardSO avatarSO
                && avatarSO.cost <= maxCost
                && avatarSO.cardName.StartsWith(namePrefix, System.StringComparison.Ordinal))
            {
                matches.Add(avatarSO);
            }
        }
        return matches;
    }

    /// <summary>
    /// Remove a SPECIFIC AvatarCardSO from the deck, instantiate it, add to hand,
    /// mark cannotBeTribute, and shuffle remaining deck.
    /// Used when the player picks a card from the deck search panel.
    /// Returns the new Card, or null if the SO was not found in deck.
    /// </summary>
    public Card SearchAndDrawSpecificAvatar(AvatarCardSO targetSO)
    {
        // Find the exact SO reference in the deck
        int foundIndex = -1;
        for (int i = 0; i < activeCards.Count; i++)
        {
            if (activeCards[i] == targetSO)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0)
        {
            Debug.LogWarning($"[Search] {owner.playerId}: Target avatar '{targetSO.cardName}' not found in deck!");
            return null;
        }

        activeCards.RemoveAt(foundIndex);

        // Shuffle remaining deck (Fisher-Yates)
        for (int i = activeCards.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            (activeCards[i], activeCards[r]) = (activeCards[r], activeCards[i]);
        }

        // Instantiate and add to hand
        Vector3 spawnPos = (owner.deckZone != null)
            ? owner.deckZone.transform.position
            : transform.position;

        Card newCard = Instantiate(avatarCardPrefab, spawnPos, Quaternion.identity);
        newCard.cardType = CardType.Avatar;
        newCard.avatarSO = targetSO;
        newCard.cardOwner = owner.playerId;
        newCard.SetupCard();

        // Mark as cannot be used for tribute/summon cost
        newCard.cannotBeTribute = true;

        owner.hand.AddCardToHand(newCard);
        Debug.Log($"[Search] {owner.playerId}: Player selected '{newCard.cardName}' (cost {newCard.cost}) from deck → hand. Cannot be tribute. Deck shuffled.");

        return newCard;
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFE CARD DEALING
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deal a LIFE card from the separate lifeDeckToUse list (NOT the main deck).
    /// Uses lifeCardPrefab and LifeCardSO. Called during game setup.
    /// LIFE cards are their own card type — not Avatar or Magic.
    /// </summary>
    public Card DrawCardToLifeZone(CardPlacePoint lifeZone)
    {
        if (lifeCardIndex >= lifeDeckToUse.Count)
        {
            Debug.LogWarning($"[DeckController] {owner?.playerId} no more LIFE cards to deal (have {lifeDeckToUse.Count}, need {lifeCardIndex + 1}).");
            return null;
        }

        if (owner == null)
        {
            Debug.LogError("[DeckController] Owner is null! Cannot deal LIFE card.");
            return null;
        }

        if (lifeCardPrefab == null)
        {
            Debug.LogError("[DeckController] lifeCardPrefab is not assigned!");
            return null;
        }

        // Get the next LIFE card SO from the separate pool
        LifeCardSO lifeSO = lifeDeckToUse[lifeCardIndex];
        lifeCardIndex++;

        // Spawn at the life zone — LIFE cards are horizontal + face-down (card back up)
        Vector3 spawnPos = lifeZone.transform.position;
        Quaternion lifeRot = Quaternion.Euler(0f, -90f, 180f);  // Horizontal + Z-flipped (face-down)
        Card newCard = Instantiate(lifeCardPrefab, spawnPos, lifeRot);
        newCard.MoveToPoint(spawnPos, lifeRot);   // Pin card to zone (prevents drift to 0,0,0)
        newCard.cardType = CardType.Life;
        newCard.lifeCardSO = lifeSO;

        // Tag ownership
        newCard.cardOwner = owner.playerId;
        newCard.SetupCard();

        // Place in LIFE zone (single-card slot)
        lifeZone.activeCard = newCard;
        newCard.assignedPlace = lifeZone;
        newCard.inHand = false;
        newCard.isLifeCard = true;

        // Face-down — card back shows (rotation handles visuals, no art hiding)
        newCard.SetFaceDown(true);

        // Disable zone collider so the card's own BoxCollider receives Battle Phase clicks
        // (Life zones are never used for drag-drop, so the collider serves no purpose)
        BoxCollider zoneCol = lifeZone.GetComponent<BoxCollider>();
        if (zoneCol != null) zoneCol.enabled = false;

        Debug.Log($"[Deck] {owner.playerId} LIFE card '{lifeSO.cardName}' dealt to {lifeZone.name}.");
        return newCard;
    }

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
