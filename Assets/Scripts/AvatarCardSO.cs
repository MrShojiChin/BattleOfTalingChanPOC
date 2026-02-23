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
}
