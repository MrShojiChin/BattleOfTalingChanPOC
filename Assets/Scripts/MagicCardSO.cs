using UnityEngine;

public enum MagicType { Normal, Modification, React, Land }

/// <summary>
/// What a magic card does when played.
/// Used by both Magic cards (on play) and LIFE cards (on flip).
/// </summary>
public enum MagicEffect
{
    None,               // No effect (gem-only tribute fodder)
    PowerBoost,         // +N power to target friendly avatar
    PowerReduce,        // -N power to target enemy avatar (or all enemy avatars)
    DestroyAvatar,      // Destroy target enemy avatar
    DrawCards,          // Draw N cards
    HealLife,           // Flip one of your LIFE cards back face-down (heal)
    DirectLifeHit       // Flip one enemy LIFE card face-up (bypass avatars)
}

[CreateAssetMenu(menuName = "Cards/Magic Card", fileName = "Magic_")]
public class MagicCardSO : BaseCardSO
{
    [Header("Color Identity")]
    public CardColor gemColor = CardColor.Neutral;

    public MagicType magicType;

    [Header("Effect")]
    public MagicEffect effect = MagicEffect.None;
    public int effectValue = 0;   // e.g. +2 power, draw 2 cards
}
