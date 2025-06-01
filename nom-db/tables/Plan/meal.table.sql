CREATE TABLE Plan.meal (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES Plan.plan(id),
    meal_type_id BIGINT NOT NULL REFERENCES Reference.reference(id),
    [date] DATE NOT NULL
);