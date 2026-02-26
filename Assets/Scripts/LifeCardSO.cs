using UnityEngine;

[CreateAssetMenu(menuName = "Cards/Life Card", fileName = "Life_")]
public class LifeCardSO : BaseCardSO
{
    // LIFE cards inherit from BaseCardSO:
    //   cardName, description, cardCharacterSprite, gem, symbol
    //
    // LIFE cards have no cost and no power — they sit face-down
    // in LIFE zones and flip face-up when attacked.

    [Header("On-Flip Effect (triggers when this LIFE card is revealed)")]
    public MagicEffect onFlipEffect = MagicEffect.None;
    public int onFlipValue = 0;   // e.g. draw 1, -1 power to all enemy avatars
}
