CREATE TABLE Nutrient.nutrient_guideline (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    guideline_basis_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    measurement_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    minimum_measurement DECIMAL NOT NULL,
    maximum_measurement DECIMAL NOT NULL
);