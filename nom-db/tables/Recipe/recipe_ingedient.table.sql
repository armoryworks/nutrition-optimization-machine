CREATE TABLE Recipe.recipe_ingredient (
     id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
     recipe_id NOT NULL BIGINT REFERENCES Recipe.recipe(id),
     ingredient_id NOT NULL BIGINT REFERENCES Recipe.ingredient(id),
     measurement_type_id NOT NULL BIGINT REFERENCES Reference.reference(id),
     measurement DECIMAL NOT NULL
);