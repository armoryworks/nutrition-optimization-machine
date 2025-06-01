CREATE TABLE Plan.meal_recipe_index (
    meal_id BIGINT REFERENCES Plan.meal(id),
    recipe_id BIGINT REFERENCES Recipe.recipe(id),
    PRIMARY KEY (meal_id, recipe_id)
);