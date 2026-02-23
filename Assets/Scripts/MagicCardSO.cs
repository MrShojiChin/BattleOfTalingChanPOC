using UnityEngine;

public enum MagicType { Normal, Modification, React, Land }

[CreateAssetMenu(menuName = "Cards/Magic Card", fileName = "Magic_")]
public class MagicCardSO : BaseCardSO
{

    [Header("Color Identity")]
    public CardColor gemColor = CardColor.Neutral;  // Color of mana this card provides when pitched

    public MagicType magicType;
}
