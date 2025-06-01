CREATE TABLE Person.person_attribute (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    person_id NOT NULL REFERENCES Person.person(id),
    attribute_type_id NOT NULL REFERENCES Reference.reference(id),
    value NOT NULL VARCHAR(255),
    on_date DATE NULL
);