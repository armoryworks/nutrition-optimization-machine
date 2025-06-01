CREATE TABLE Recipe.ingredient_nutrient (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ingredient_id BIGINT NOT NULL REFERENCES Nutrient.nutrient(id),
    measurement_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    measurement DECIMAL NOT NULL
);