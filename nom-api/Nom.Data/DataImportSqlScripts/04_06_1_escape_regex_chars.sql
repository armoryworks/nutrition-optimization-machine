-- 04_06_1_escape_regex_chars.sql
-- Helper function to escape special regex characters for use in POSIX regular expressions
-- This is a manual implementation to replace the non-existent regexp_quote_literal.
CREATE OR REPLACE FUNCTION escape_regex_chars(input_text TEXT) RETURNS TEXT AS $$
BEGIN
    -- Escape common regex special characters by prepending them with a backslash.
    -- The order of replacements is crucial: '\' must be escaped first.
    RETURN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
           input_text,
           '\\', '\\\\'),  -- Escape backslash first
           '.', '\.'),   -- Dot
           '+', '\+'),   -- Plus
           '*', '\*'),   -- Asterisk
           '?', '\?'),   -- Question mark
           '[', '\['),   -- Opening bracket
           ']', '\]'),   -- Closing bracket
           '(', '\('),   -- Opening parenthesis
           ')', '\)'),   -- Closing parenthesis
           '{', '\{'),   -- Opening brace
           '}', '\}'),   -- Closing brace
           '|', '\|'),   -- Pipe
           '^', '\^'),   -- Caret
           '$', '\$');   -- Dollar
END;
$$ LANGUAGE plpgsql IMMUTABLE;
