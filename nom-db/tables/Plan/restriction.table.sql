CREATE TABLE Plan.restriction (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES Plan.plan(id),
    person_id BIGINT NULL REFERENCES Person.person(id),
    name varchar(255) NOT NULL,
    description varchar(2047) NULL,
    restriction_type_id BIGINT REFERENCES Reference.reference(id),
    ingredient_id BIGINT NULL REFERENCES Recipe.ingredient(id),
    nutrient_id BIGINT NULL REFERENCES Nutrient.nutrient(id),
    begin_date DATE NULL,
    end_date DATE NULL
);