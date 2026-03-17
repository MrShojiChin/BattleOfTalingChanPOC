using UnityEngine;

[CreateAssetMenu(menuName = "Cards/Avatar Card", fileName = "Avatar_")]
public class AvatarCardSO : BaseCardSO
{
    
    [Header("Color Identity")]
    public CardColor avatarColor = CardColor.Neutral;  // This avatar's color
    public CardColor gemColor = CardColor.Neutral;      // Color of mana this card provides when pitched

    [Header("Avatar Economy")]
    public int cost;   // Cost to summon this avatar
    public int power;

    [Header("Abilities / Keywords")]
    public AvatarAbility abilities = AvatarAbility.None;

    [Header("Ability Parameters")]
    [Tooltip("For Juti: name prefix to search in Hell (e.g. 'นายนิรยบาล')")]
    public string jutiSearchPrefix = "";

    [Tooltip("For CombatThonSoop: how many cards to mill")]
    public int combatThonSoopMill = 0;

    [Tooltip("For CombatThonSoop: temporary power boost amount")]
    public int combatThonSoopPowerBoost = 0;

    [Tooltip("For HellPowerScaling: name prefix to count in Hell (e.g. 'นายนิรยบาล')")]
    public string hellScalingNamePrefix = "";

    [Header("Juti Target / Draw Parameters")]
    [Tooltip("For JutiTargetDebuff/Buff: power change amount (use negative for debuff, e.g. -4)")]
    public int jutiTargetPowerValue = 0;

    [Tooltip("For JutiDraw: number of cards to draw")]
    public int jutiDrawCount = 0;

    [Header("Attack Power Boost")]
    [Tooltip("For AttackPowerBoost: temporary power boost when attacking")]
    public int attackPowerBoost = 0;

    [Header("Symbol Aura")]
    [Tooltip("For SymbolAura: which symbol gets the continuous buff")]
    public CardSymbol auraSymbol = CardSymbol.None;

    [Tooltip("For SymbolAura: power boost to each matching avatar on your side")]
    public int auraPowerBoost = 0;
}
