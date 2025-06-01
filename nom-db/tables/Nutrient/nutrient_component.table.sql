CREATE TABLE Nutrient.nutrient_component (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    macro_nutrient_id BIGINT NOT NULL REFERENCES Nutrient.nutrient(id),
    micro_nutrient_id BIGINT NOT NULL REFERENCES Nutrient.nutrient(id),
    measurement_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    measurement DECIMAL NOT NULL
);