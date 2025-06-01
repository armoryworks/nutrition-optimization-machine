CREATE TABLE Plan.plan_person_administrator_index (
     plan_id BIGINT REFERENCES Plan.plan(id),
    person_id BIGINT REFERENCES Person.person(id),
    PRIMARY KEY (plan_id, person_id)
);