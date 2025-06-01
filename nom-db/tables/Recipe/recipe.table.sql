CREATE TABLE Recipe.recipe (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description VARCHAR(2047) NULL,
    number_of_servings TINYINT NOT NULL,
    serving_size VARCHAR(255) NOT NULL
);