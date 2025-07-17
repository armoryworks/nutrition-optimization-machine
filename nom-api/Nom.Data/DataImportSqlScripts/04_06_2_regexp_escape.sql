-- 04_06_2_regexp_escape.sql
-- NEWLY ADDED FUNCTION: regexp_escape for PostgreSQL compatibility
-- This function correctly escapes a string for use as a literal pattern within a regex.
CREATE OR REPLACE FUNCTION regexp_escape(text_to_escape TEXT) RETURNS TEXT AS $$
BEGIN
    -- Escape all characters that have special meaning in POSIX regular expressions.
    -- The order is important: backslash must be escaped first.
    RETURN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        text_to_escape,
        '\\', '\\\\'), -- Escape backslash first
        '.', '\.'),
        '*', '\*'),
        '+', '\+'),
        '?', '\?'),
        '|', '\|'),
        '(', '\('),
        ')', '\)'),
        '[', '\['),
        ']', '\]'),
        '{', '\{'),
        '}', '\}'),
        '^', '\^'),
        '$', '\$'),
        '#', '\#'),
        '-', '\-'),
        '&', '\&'),
        '<', '\<'),
        '>', '\>');
END;
$$ LANGUAGE plpgsql IMMUTABLE;
