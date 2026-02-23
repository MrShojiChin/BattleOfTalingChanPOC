using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class DeckController : MonoBehaviour
{
    public static DeckController instance;

    private void Awake()
    {
        instance = this;
    }

    // Change this to BaseCardSO so it accepts both Avatar and Magic
    public List<BaseCardSO> deckToUse = new List<BaseCardSO>();

    private List<BaseCardSO> activeCards = new List<BaseCardSO>();

    public Card avatarCardPrefab;
    public Card magicCardPrefab;

    void Start()
    {
        SetupDeck();
    }

    void Update()
    {
        if(Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            DrawCardToHand();
        }
    }

    public void SetupDeck()
    {
        activeCards.Clear();

        List<BaseCardSO> tempDeck = new List<BaseCardSO>();
        tempDeck.AddRange(deckToUse); // Create a temporary copy of the deck to draw from

        int interations = 0;
        while(tempDeck.Count > 0 && interations < 500)
        {
            int selected = Random.Range(0, tempDeck.Count);
            activeCards.Add(tempDeck[selected]);
            tempDeck.RemoveAt(selected); // Remove the card from the temp deck to avoid duplicates
            interations++;
        }

    }

    public void DrawCardToHand()
    {
        if (activeCards.Count == 0)
        {
            SetupDeck(); // Reshuffle the deck if we've run out of cards
        }

        // Choose the correct prefab based on the drawn card's type
        BaseCardSO drawnCard = activeCards[0];
        Card newCard = null;

        if (drawnCard is AvatarCardSO avatar)
        {
            newCard = Instantiate(avatarCardPrefab, transform.position, Quaternion.identity);
            newCard.cardType = CardType.Avatar;
            newCard.avatarSO = avatar;
        }
        else if (drawnCard is MagicCardSO magic)
        {
            newCard = Instantiate(magicCardPrefab, transform.position, Quaternion.identity);
            newCard.cardType = CardType.Magic;
            newCard.magicSO = magic;
        }

        newCard.SetupCard();

        activeCards.RemoveAt(0); // Remove the drawn card from the active deck

        HandController.instance.AddCardToHand(newCard);
    }
}
