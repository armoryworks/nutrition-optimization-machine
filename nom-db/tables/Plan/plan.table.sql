CREATE TABLE Plan.plan (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description VARCHAR(2047) NULL,
    purpose VARCHAR(2047) NULL,
    begin_date DATE NULL,
    end_date DATE NULL
);