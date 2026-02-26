using UnityEngine;

[CreateAssetMenu(fileName = "BaseCardSO", menuName = "Scriptable Objects/BaseCardSO")]
public class BaseCardSO : ScriptableObject
{
    public string cardName;
    [TextArea] public string description;
    public Sprite cardCharacterSprite;
    public int gem;
    public CardSymbol cardSymbol = CardSymbol.None;
}
