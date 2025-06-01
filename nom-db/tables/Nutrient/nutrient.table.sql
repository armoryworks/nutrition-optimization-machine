CREATE TABLE Nutrient.nutrient (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    nutrient_type_id BIGINT NOT NULL REFERENCES Reference.reference(id)
);