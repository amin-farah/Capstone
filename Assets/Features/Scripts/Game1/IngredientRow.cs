namespace IngredientRows
{
    struct IngredientRow
    {
        Ingredient ingredientLeft;
        Ingredient ingredientRight;
        
        public IngredientRow(Ingredient ingredientLeft, Ingredient ingredientRight)
        {
            this.ingredientLeft = ingredientLeft;
            this.ingredientRight = ingredientRight;
        }
    }
}
