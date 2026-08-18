using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using IngredientRows;

public class IngredienPool : MonoBehaviour
{
   [SerializeField] private List<Ingredient> ingredientPool;
   [SerializeField] private List<Ingredient> usedIngredients;
   
   [Button]
   public void GenerateRow()
   {
       int randomIndex = 0;
       usedIngredients.Add(ingredientPool[randomIndex]);
       int randomIndex2 = 1;
       
       IngredientRow row = new IngredientRow(ingredientPool[randomIndex], ingredientPool[randomIndex2]);
       
   }
   
}
