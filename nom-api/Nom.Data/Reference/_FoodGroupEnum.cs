namespace Nom.Data.Reference
{
    /// <summary>
    /// Nutritional food groups (reference group <see cref="ReferenceDiscriminatorEnum.FoodGroupType"/> = 3002).
    /// An ingredient may be classified into one of these via <c>IngredientEntity.FoodGroupId</c>, and
    /// households can require a minimum number of servings of a group per day or per meal
    /// (<c>FoodGroupRuleEntity</c>). The vocabulary is the USDA MyPlate five plus common extensions;
    /// it is admin-editable at runtime, so treat these ids as seed values, not a closed set.
    /// </summary>
    public enum FoodGroupEnum : long
    {
        Vegetables = 3200L,
        Fruits = 3201L,
        Grains = 3202L,
        ProteinFoods = 3203L,
        Dairy = 3204L,
        FatsOils = 3205L,
        Legumes = 3206L,
        NutsSeeds = 3207L,
        SweetsSnacks = 3208L,
        Beverages = 3209L
    }
}
