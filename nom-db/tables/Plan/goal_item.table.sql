CREATE TABLE Plan.goal_item (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    goal_id BIGINT NOT NULL REFERENCES Plan.goal(id),
    name VARCHAR(255) NOT NULL,
    description VARCHAR(2047) NOT NULL,
    is_quantifiable BIT NOT NULL DEFAULT(0),
    ingredient_id BIGINT NULL REFERENCES Recipe.ingredient(id),
    nutrient_id BIGINT NULL REFERENCES Nutrient.nutrient(id),
    timeframe_type_id BIGINT REFERENCES Reference.reference(id),
    measurement_type_id BIGINT NULL REFERENCES Reference.reference(id),
    measurement_minimum DECIMAL NULL,
    measurement_maximum DECIMAL NULL
);