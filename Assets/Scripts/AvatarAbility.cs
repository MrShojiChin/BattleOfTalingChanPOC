/// <summary>
/// Flags enum for avatar-specific abilities/keywords.
/// An avatar can have multiple abilities (e.g. CannotSummonFromHand | HellSummonOnThonSoop).
/// Configured on AvatarCardSO, copied to Card.cs at runtime.
/// </summary>
[System.Flags]
public enum AvatarAbility
{
    None                   = 0,
    Juti                   = 1 << 0,  // จุติ: On-summon-from-cost → revive matching avatar from Hell
    CombatThonSoop         = 1 << 1,  // On attack/defend → ธรณีสูบ N + temp power boost
    CannotSummonFromHand   = 1 << 2,  // Cannot be played from hand normally
    HellSummonOnThonSoop   = 1 << 3,  // If milled by ธรณีสูบ → can summon from Hell
    ThonSoopAmplifier      = 1 << 4,  // While on field, all ธรณีสูบ effects mill +2 additional
    HellPowerScaling       = 1 << 5,  // +1 POWER per distinct matching name in Hell
    JutiTargetDebuff       = 1 << 6,  // จุติ: select enemy avatar, apply temp power debuff
    AttackPowerBoost       = 1 << 7,  // On attack only: temp power boost
    JutiTargetBuff         = 1 << 8,  // จุติ: select any avatar (own or opponent), apply temp power buff
    SymbolAura             = 1 << 9,  // Continuous: all matching-symbol friendlies on your side +power
    JutiDraw               = 1 << 10, // จุติ: draw N cards on summon from cost
}
