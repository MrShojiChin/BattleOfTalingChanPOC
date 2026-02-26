using UnityEngine;

[CreateAssetMenu(menuName = "Cards/Life Card", fileName = "Life_")]
public class LifeCardSO : BaseCardSO
{
    // LIFE cards inherit from BaseCardSO:
    //   cardName, description, cardCharacterSprite, gem, symbol
    //
    // No additional fields needed for now.
    // LIFE cards have no cost and no power — they sit face-down
    // in LIFE zones and flip face-up when attacked.
    //
    // Future: add special effect triggers (e.g. on-flip abilities).
}
