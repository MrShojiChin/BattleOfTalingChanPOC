using UnityEngine;

[CreateAssetMenu(fileName = "New Card", menuName = "Card", order = 1)]
public class CardScriptableObject : ScriptableObject
{
    public int cost, gem;

    public string desribtion;

    public Sprite cardCharacterSprite;
    
}
