using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Game1Manager : MonoBehaviour
{
    [Title("Ingredient List")]
    [SerializeField, ReadOnly] public List<Ingredient> ingredient;

    [Button]
    public void GenerateCoffee()
    {
        Debug.Log("Event Triggered");
    }
}
