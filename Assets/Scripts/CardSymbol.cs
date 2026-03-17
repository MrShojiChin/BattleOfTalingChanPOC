/// <summary>
/// Card symbol / type identity — determines which effects can target this card.
/// Avatar cards: Giant, God, Human, Devil, Ghost
/// Magic cards: Sorcerer (จอมเวทย์) or any Avatar symbol
/// </summary>
public enum CardSymbol
{
    None,       // No symbol
    Giant,      // ยักษ์
    God,        // เทพ
    Human,      // คน
    Devil,      // นรก
    Ghost,      // ผี
    Sorcerer    // จอมเวทย์ (Magic-only)
}
