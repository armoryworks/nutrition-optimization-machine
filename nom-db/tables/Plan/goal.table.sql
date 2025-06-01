CREATE TABLE Plan.goal (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES Plan.plan(id),
    name VARCHAR(255) NOT NULL,
    description VARCHAR(2047) NOT NULL,
    goal_type_id BIGINT REFERENCES Reference.reference(id),
    begin_date DATE NULL,
    end_date DATE NULL
);