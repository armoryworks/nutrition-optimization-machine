-- 04_6_recipe_com_parsing_functions.sql
-- This script defines a comprehensive PL/pgSQL function for parsing and aggressively cleaning
-- raw ingredient lines. This function extracts quantity, measurement type, and a cleaned
-- ingredient name, removing common extraneous phrases and descriptors.
-- It does NOT split multi-ingredient lines (e.g., "salt and pepper"); that is handled in a later stage.

-- Set client_min_messages to WARNING to suppress verbose DEBUG notices during normal operation.
-- Only WARNINGs and higher will be displayed.
SET client_min_messages TO WARNING;
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Explicitly drop the functions if they exist to ensure a clean re-creation
-- Dropping old versions and new versions to be safe
DROP FUNCTION IF EXISTS escape_regex_chars(TEXT) CASCADE;
DROP FUNCTION IF EXISTS parse_ingredient_line_comprehensive(TEXT) CASCADE;
DROP FUNCTION IF EXISTS escape_regex_chars_v2(TEXT) CASCADE; -- New function name
DROP FUNCTION IF EXISTS parse_ingredient_line_comprehensive_v2(TEXT) CASCADE; -- New function name

-- Helper function to escape special regex characters for use in POSIX regular expressions
-- This is a manual implementation to replace the non-existent regexp_quote_literal.
CREATE OR REPLACE FUNCTION escape_regex_chars_v2(input_text TEXT) RETURNS TEXT AS $$
BEGIN
    -- Escape common regex special characters by prepending them with a backslash.
    -- The order of replacements is crucial: '\' must be escaped first.
    RETURN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
           input_text,
           '\', '\\'),  -- Escape backslash first
           '.', '\.'),   -- Dot
           '+', '\+'),   -- Plus
           '*', '\*'),   -- Asterisk
           '?', '\?'),   -- Question mark
           '[', '\['),   -- Opening square bracket
           ']', '\]'),   -- Closing square bracket
           '^', '\^'),   -- Caret
           '$', '\$'),   -- Dollar sign
           '(', '\('),   -- Opening parenthesis
           ')', '\)'),   -- Closing parenthesis
           '{', '\{'),   -- Opening curly brace
           '}', '\}'),   -- Closing curly brace
           '|', '\|'),   -- Pipe
           '-', '\-');    -- Hyphen (when used in character sets or ranges)
END;
$$ LANGUAGE plpgsql IMMUTABLE;


-- Function to parse quantity, unit, and aggressively clean the ingredient name from a raw line
CREATE OR REPLACE FUNCTION parse_ingredient_line_comprehensive_v2(raw_text TEXT) RETURNS TABLE(quantity_out DECIMAL(18,4), unit_name_out TEXT, cleaned_name_out TEXT) AS $$
DECLARE
    trimmed_raw_text TEXT;
    temp_working_text TEXT; -- Use a working copy for manipulations
    parsed_qty DECIMAL(18,4);
    parsed_unit TEXT;
    temp_cleaned_name TEXT;
    match_result TEXT[];
    -- Variables for quantity parsing
    whole_num_str TEXT;
    fraction_str TEXT;
    decimal_str TEXT;
    temp_qty_val DECIMAL(18,4);
    numerator DECIMAL(18,4);
    denominator DECIMAL(18,4);
    found_explicit_unit_in_original BOOLEAN;
    unit_regex TEXT;
    unit_check TEXT; -- Declare unit_check here for the loop

    -- Declare these variables WITHOUT initial values in DECLARE section
    v_common_units_map JSONB;
    v_ordered_unit_keys TEXT[];

BEGIN
    -- This notice will still show due to client_min_messages being WARNING, as it's a general info message.
    RAISE NOTICE 'DEBUG: Executing 04_6_recipe_com_parsing_functions.sql - Version 27 (Reduced Verbosity).';
    -- Initialize variables at the very start of the BEGIN block
    -- These specific DEBUG notices are now suppressed by SET client_min_messages TO WARNING;
    -- RAISE NOTICE 'DEBUG 04_6 [START]: Function parse_ingredient_line_comprehensive_v2 received raw_text: "%"', raw_text;
    trimmed_raw_text := TRIM(raw_text);
    temp_working_text := trimmed_raw_text;

    -- RAISE NOTICE 'DEBUG 04_6 [INIT]: Initializing common_units_map.';
    -- Initialize common_units_map here, inside the BEGIN block
    v_common_units_map := '{
        "fluid ounces": "fluid ounce", "fluid ounce": "fluid ounce", "fl.oz.": "fluid ounce", "fl oz": "fluid ounce",
        "tablespoons": "tablespoon", "tablespoon": "tablespoon", "tbsp": "tablespoon", "tbs": "tablespoon",
        "teaspoons": "teaspoon", "teaspoon": "teaspoon", "tsp": "teaspoon", "tsps": "teaspoon",
        "gallons": "gallon", "gallon": "gallon", "gal": "gallon",
        "pints": "pint", "pint": "pint", "pt": "pint",
        "quarts": "quart", "quart": "quart", "qt": "quart",
        "ounces weight": "ounce", "ounces": "ounce", "ounce": "ounce", "oz": "ounce",
        "pounds": "pound", "pound": "pound", "lb": "pound", "lbs": "pound",
        "liters": "liter", "liter": "liter", "l": "liter",
        "cups": "cup", "cup": "cup", "c": "cup",
        "grams": "gram", "gram": "gram", "g": "gram",
        "milligrams": "milligram", "milligram": "milligram", "mg": "milligram",
        "kilograms": "kilogram", "kilogram": "kilogram", "kg": "kilogram",
        "milliliters": "milliliter", "milliliter": "milliliter", "ml": "milliliter",
        "cloves": "clove", "clove": "clove",
        "sprigs": "sprig", "sprig": "sprig",
        "slices": "slice", "slice": "slice",
        "cans": "can", "can": "can",
        "packages": "package", "package": "package", "pkg": "package", "pkt": "package", "pkgs": "package", "pkg.": "package",
        "bottles": "bottle", "bottle": "bottle", "bot": "bottle",
        "heads": "head", "head": "head",
        "pieces": "piece", "piece": "piece",
        "leaves": "leaf", "leaf": "leaf",
        "stalks": "stalk", "stalk": "stalk", "stk": "stalk",
        "pinches": "pinch", "pinch": "pinch", "pch": "pinch",
        "dashes": "dash", "dash": "dash", "dsh": "dash",
        "splashes": "splash", "splash": "splash",
        "whole": "whole",
        "each": "each",
        "x": "each",
        "micrograms": "microgram", "mcg": "microgram", "µg": "microgram"
    }';

    -- RAISE NOTICE 'DEBUG 04_6 [INIT]: common_units_map initialized. Now initializing ordered_unit_keys.';
    -- Initialize ordered_unit_keys here
    v_ordered_unit_keys := ARRAY(SELECT jsonb_object_keys FROM JSONB_OBJECT_KEYS(v_common_units_map) ORDER BY LENGTH(jsonb_object_keys) DESC, jsonb_object_keys ASC);
    -- RAISE NOTICE 'DEBUG 04_6 [INIT]: ordered_unit_keys initialized. First 5: %', (SELECT array_to_string(v_ordered_unit_keys[1:5], ', '));

    parsed_qty := 1.0; -- Default quantity
    parsed_unit := 'each'; -- Default unit
    temp_cleaned_name := trimmed_raw_text; -- Default cleaned name

    -- RAISE NOTICE 'DEBUG 04_6 [TASTE]: Checking for "to taste".';
    -- 0. Handle "to taste" first as it's a special case for quantity/unit
    IF lower(trimmed_raw_text) LIKE '%to taste%' THEN
        parsed_qty := NULL; -- No specific quantity
        parsed_unit := 'to taste';
        -- RAISE NOTICE 'DEBUG 04_6 [TASTE]: Found "to taste". Removing from text.';
        temp_cleaned_name := TRIM(REGEXP_REPLACE(lower(trimmed_raw_text), '\s*to taste\s*', '', 'gi'));
        temp_cleaned_name := TRIM(REGEXP_REPLACE(temp_cleaned_name, ',\s*$', ''));
        IF temp_cleaned_name = '' THEN
            temp_cleaned_name := 'seasoning';
        END IF;
        quantity_out := parsed_qty;
        unit_name_out := parsed_unit;
        cleaned_name_out := temp_cleaned_name;
        -- RAISE NOTICE 'DEBUG 04_6 [TASTE]: Handled "to taste". Cleaned name: "%"', cleaned_name_out;
        RETURN NEXT;
        RETURN;
    END IF;

    -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Starting quantity extraction. Current text: "%"', temp_working_text;
    -- 1. Extract leading quantity (whole, fraction, decimal, and ranges like "X- Y")
    match_result := REGEXP_MATCH(temp_working_text, '^\s*(\d+)?\s*(\d+\/\d+)?(\.\d+)?(?:\s*-\s*\d+(?:\/\d+)?)?');
    
    IF match_result IS NOT NULL THEN
        -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Quantity match_result: %', match_result;
        whole_num_str := match_result[1];
        fraction_str := match_result[2];
        decimal_str := match_result[3];
        temp_qty_val := 0;

        IF whole_num_str IS NOT NULL THEN
            -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Adding whole number: %', whole_num_str;
            temp_qty_val := temp_qty_val + whole_num_str::DECIMAL(18,4);
        END IF;
        IF fraction_str IS NOT NULL THEN
            -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Processing fraction: %', fraction_str;
            numerator := SPLIT_PART(fraction_str, '/', 1)::DECIMAL(18,4);
            denominator := SPLIT_PART(fraction_str, '/', 2)::DECIMAL(18,4);
            temp_qty_val := temp_qty_val + (numerator / denominator);
        END IF;
        IF decimal_str IS NOT NULL THEN
            -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Processing decimal: %', decimal_str;
            temp_qty_val := temp_qty_val + ('0' || decimal_str)::DECIMAL(18,4);
        END IF;

        IF temp_qty_val > 0 THEN
            parsed_qty := temp_qty_val;
        END IF;

        -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Removing matched quantity part from text.';
        temp_working_text := TRIM(SUBSTRING(temp_working_text FROM LENGTH(match_result[0]) + 1));
        -- RAISE NOTICE 'DEBUG 04_6 [QTY]: Quantity extracted: %. Remaining text: "%"', parsed_qty, temp_working_text;
    ELSE
        -- RAISE NOTICE 'DEBUG 04_6 [QTY]: No quantity extracted. Remaining text: "%"', temp_working_text;
    END IF;

    -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Starting unit extraction. Current text: "%"', temp_working_text;
    -- 2. Extract Unit
    FOR unit_check IN SELECT unnest(v_ordered_unit_keys) LOOP -- Use v_ordered_unit_keys
        -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Checking unit: "%"', unit_check;
        -- Use the new helper function for safer regex construction
        unit_regex := '\y' || escape_regex_chars_v2(LOWER(unit_check)) || '\y'; -- Call v2 of escape function
        -- Adjust for spaces within unit names (e.g., "fluid ounce")
        unit_regex := REPLACE(unit_regex, '\ ', '\s*');
        -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Unit regex: "%"', unit_regex;

        IF LOWER(temp_working_text) ~ ('^\s*' || unit_regex) THEN
            -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Unit match found for "%". Getting from map.', unit_check;
            parsed_unit := v_common_units_map->>unit_check; -- Use v_common_units_map
            -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Removing matched unit part from text.';
            temp_working_text := TRIM(REGEXP_REPLACE(temp_working_text, '^\s*' || unit_regex, '', 'i'));
            -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Unit extracted: %. Remaining text: "%"', parsed_unit, temp_working_text;
            EXIT;
        END IF;
    END LOOP;

    -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Re-checking original for explicit unit if default "each" was set.';
    IF parsed_qty IS NOT NULL AND parsed_qty > 0 AND parsed_unit = 'each' THEN
        found_explicit_unit_in_original := FALSE;
        FOR unit_check IN SELECT unnest(v_ordered_unit_keys) LOOP -- Use v_ordered_unit_keys
            unit_regex := '\y' || escape_regex_chars_v2(LOWER(unit_check)) || '\y'; -- Call v2 of escape function
            unit_regex := REPLACE(unit_regex, '\ ', '\s*');
            IF LOWER(trimmed_raw_text) ~ unit_regex THEN
                found_explicit_unit_in_original := TRUE;
                EXIT;
            END IF;
        END LOOP;
        IF NOT found_explicit_unit_in_original THEN
            parsed_unit := 'each';
        END IF;
    END IF;
    -- RAISE NOTICE 'DEBUG 04_6 [UNIT]: Final parsed unit: "%"', parsed_unit;

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Starting aggressive cleaning. Current cleaned name: "%"', temp_working_text;
    -- 3. Aggressive Cleaning of the Ingredient Name
    temp_cleaned_name := TRIM(temp_working_text);

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing parentheses.';
    -- Remove any text within parentheses and surrounding whitespace
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*\([^)]*\)\s*', ' ', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After parentheses removal: "%"', temp_cleaned_name;

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing common trailing descriptive phrases (1).';
    -- Remove common trailing descriptive phrases.
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*(optional|to taste|for garnish|divided use|divided|for serving|to serve|my favorite|for topping|original|more to taste|see note)\s*$', '', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After common trailing phrases removal (1): "%"', temp_cleaned_name;
    
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing phrases that indicate a specific usage or condition (2).';
    -- Remove phrases that indicate a specific usage or condition
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*(such as|we used|from a can of|not "powdered"|use rasp grater|enough for a \w+ size pizza|if you have fresh, use it!|i used the cheapest one can buy on a newlywed budget, but if you''ve got jack give it a try!|found at trader joes|for the sauce|for frying|green parts only|leaves taken off stems|cooked according to package instructions|skin on, bone in|reserve the fronds for serving|washed, husks discarded|at room temperature|or mayo|or apple cider|or any small\/petite steak)\s*$', '', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After common trailing phrases removal (2): "%"', temp_cleaned_name;
    
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing "more or less".';
    -- Remove "more or less"
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*more or less\s*$', '', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After "more or less" removal: "%"', temp_cleaned_name;

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing "about N word" phrases.';
    -- Remove "about N word" or "about N-N word" at the end, where N is a number and word is any word.
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*about\s+(\d+(?:[.\/]\d+)?(?:\s*-\s*\d+(?:[.\/]\d+)?)?\s*\w+(?:\s+\w+)*)?\s*$', '', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After "about N word" removal: "%"', temp_cleaned_name;
    
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing "approx N word" phrases.';
    -- Remove "approx N word"
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s*approx\s+\d+(?:[.\/]\d+)?\s*\w+\s*each\s*$', '', 'gi');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After "approx N word" removal: "%"', temp_cleaned_name;


    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Removing leading/trailing commas, periods, and extra spaces.';
    -- Remove leading/trailing commas, periods, and extra spaces
    temp_cleaned_name := TRIM(REGEXP_REPLACE(temp_cleaned_name, '^\s*,\s*|\s*,\s*$', '', 'g'));
    temp_cleaned_name := TRIM(REGEXP_REPLACE(temp_cleaned_name, '^\s*\.\s*|\s*\.\s*$', '', 'g'));
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '\s+', ' ', 'g'); -- Replace multiple spaces with single space
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After comma/period/extra space cleanup: "%"', temp_cleaned_name;

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Final alphanumeric cleanup.';
    -- Final cleanup: remove any leading/trailing non-alphanumeric characters that might remain
    temp_cleaned_name := REGEXP_REPLACE(temp_cleaned_name, '^[[:punct:]\s]+|[[:punct:]\s]+$', '', 'g');
    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After final alphanumeric cleanup: "%"', temp_cleaned_name;

    -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Checking if cleaned name is empty.';
    -- Ensure the cleaned name is not empty after all processing
    IF TRIM(temp_cleaned_name) = '' THEN
        temp_cleaned_name := trimmed_raw_text; -- Fallback to original if cleaning resulted in empty string
        -- RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Cleaned name was empty, fell back to original: "%"', temp_cleaned_name;
    END IF;

    quantity_out := parsed_qty;
    unit_name_out := parsed_unit;
    cleaned_name_out := temp_cleaned_name;

    -- RAISE NOTICE 'DEBUG 04_6 [END]: Function finished. Qty: %, Unit: %, Cleaned: "%"', quantity_out, unit_name_out, cleaned_name_out;
    RETURN NEXT;
END;
$$ LANGUAGE plpgsql;
