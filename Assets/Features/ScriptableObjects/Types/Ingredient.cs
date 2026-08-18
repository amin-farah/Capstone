using UnityEngine;
using Sirenix.OdinInspector;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "Ingredient", menuName = "Game1/Ingredient")]
public class Ingredient : ScriptableObject
{
    public Image ingredientIcon;
    public string ingredientName;
}
