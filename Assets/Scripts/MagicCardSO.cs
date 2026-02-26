using UnityEngine;

public enum MagicType { Normal, Modification, React, Land }

/// <summary>
/// What a magic card does when played.
/// Used by both Magic cards (on play) and LIFE cards (on flip).
/// </summary>
public enum MagicEffect
{
    None,                   // No effect (gem-only tribute fodder)
    PowerBoost,             // +N power to target friendly avatar
    PowerReduce,            // -N power to target enemy avatar (or all enemy avatars)
    DestroyAvatar,          // Destroy target enemy avatar
    DrawCards,              // Draw N cards
    HealLife,               // Flip one of your LIFE cards back face-down (heal)
    DirectLifeHit,          // Flip one enemy LIFE card face-up (bypass avatars)

    // ── Symbol-based effects (from Toylaxy TCG) ──
    PowerBoostAllSymbol,    // +N power to ALL friendly avatars matching targetSymbol
    PowerBoostSymbol,       // +N power to one friendly avatar matching targetSymbol
    DiscardSymbolDraw,      // Discard 1 card of targetSymbol from hand → draw N cards
    ThonSoop,               // ถล่มสูป — send top N cards from deck to Hell, then draw N
    SearchDeck,             // Search deck for avatar with cost ≤ effectValue, add to hand
    PowerBoostSymbolTemp,   // +N power to one matching avatar, expires at end of turn

    // ── Devil-themed effects (ธรณีสูบ series) ──
    DiscardSymbolThonSoop,  // Discard 1 [Symbol] from hand → ธรณีสูบ (mill N + draw N)
    SearchDeckByName,       // Search deck for avatar matching name prefix + cost ≤ N
    ReactDiscardThonSoop    // React: discard [Symbol] (cost ≥ trigger) → ธรณีสูบ N + destroy
}

[CreateAssetMenu(menuName = "Cards/Magic Card", fileName = "Magic_")]
public class MagicCardSO : BaseCardSO
{
    [Header("Color Identity")]
    public CardColor gemColor = CardColor.Neutral;

    public MagicType magicType;

    [Header("Effect")]
    public MagicEffect effect = MagicEffect.None;
    public int effectValue = 0;             // e.g. +2 power, draw 2 cards
    public CardSymbol targetSymbol = CardSymbol.None;  // Symbol this effect targets (for symbol-based effects)

    [Header("Advanced")]
    public string searchName = "";                      // Name prefix for SearchDeckByName (e.g. "นายนิรยบาล")
    public bool activateFromHellOnThonSoop;             // If milled by ธรณีสูบ, can activate from Hell
}
