CREATE TABLE Recipe.recipe_step (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    recipe_id BIGINT NOT NULL REFERENCES Recipe.recips(id),
    step_type_id BIGINT NULL REFERENCES Reference.reference(id),
    summary VARCHAR(255) NOT NULL,
    step_number TINYINT NOT NULL,
    description VARCHAR(2047) NOT NULL
);