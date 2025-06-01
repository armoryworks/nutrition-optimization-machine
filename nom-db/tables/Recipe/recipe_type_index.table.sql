CREATE TABLE Recipe.recipe_type_index (
    recipe_id BIGINT NOT NULL REFERENCES Recipe.recipe(id),
    recipe_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    PRIMARY KEY (recipe_id, redipe_type_id)
);