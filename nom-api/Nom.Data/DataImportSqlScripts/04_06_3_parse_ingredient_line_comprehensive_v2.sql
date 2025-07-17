-- 04_06_3_parse_ingredient_line_comprehensive_v2.sql
-- Comprehensive function for parsing an ingredient line (Version 40 - Case-Insensitive Substring Removal)
CREATE OR REPLACE FUNCTION parse_ingredient_line_comprehensive_v2(
    raw_text TEXT
) RETURNS TABLE(
    quantity_out DECIMAL(18,4),
    unit_name_out TEXT,
    cleaned_name_out TEXT
) AS $$
DECLARE
    trimmed_raw_text TEXT := TRIM(raw_text);
    temp_working_text TEXT := trimmed_raw_text; -- This will be modified to derive cleaned_name_out
    quantity_match_str TEXT;
    measurement_match_str TEXT;
    decimal_quantity DECIMAL(18,4);
    unit_mapped_name TEXT;

    -- Define common measurement units and their abbreviations as a constant array of arrays.
    -- Each inner array represents {pattern_regex, canonical_name}.
    -- Order matters for overlapping terms (e.g., "tbsp" before "tsp").
    -- Using word boundaries \y to prevent partial matches (e.g., 'can' matching 'candy')
    measurements_map_data CONSTANT TEXT[][] := ARRAY[
        ['\y(?:tablespoons?|tbsp\.?|T)\y', 'tablespoon'],
        ['\y(?:teaspoons?|tsp\.?|t)\y', 'teaspoon'],
        ['\y(?:cups?|c\.?)\y', 'cup'],
        ['\y(?:ounces?|oz\.?)\y', 'ounce'],
        ['\y(?:pounds?|lbs?\.?|#)\y', 'pound'],
        ['\y(?:grams?|g\.?)\y', 'gram'],
        ['\y(?:kilograms?|kg\.?)\y', 'kilogram'],
        ['\y(?:milliliters?|ml\.?)\y', 'milliliter'],
        ['\y(?:liters?|L\.?)\y', 'liter'],
        ['\y(?:pints?|pt\.?)\y', 'pint'],
        ['\y(?:quarts?|qt\.?)\y', 'quart'],
        ['\y(?:gallons?|gal\.?)\y', 'gallon'],
        ['\y(?:dashes?|drops?|pinches?|sprinkles?)\y', 'dash'], -- Using dash as canonical for these small units
        ['\y(?:cloves?|stalks?|leaves?|sprigs?|slices?|strips?)\y', 'piece'], -- Using piece as canonical for these
        ['\y(?:cans?|box(?:es)?|packages?|bunches?|heads?)\y', 'package'], -- Using package as canonical for these
        ['\y(?:large|medium|small|extra-large|extra-small)\y', 'size'], -- Often describes size of produce
        ['\y(?:sheets?|fillets?|loins?|breasts?|thighs?)\y', 'piece'], -- Using piece as canonical for these
        ['\y(?:bags?|bottles?|jars?|containers?)\y', 'container'], -- Using container as canonical for these
        ['\y(?:to taste|as needed|optional)\y', 'to taste'] -- Special "measurements"
    ];

BEGIN
    RAISE NOTICE 'DEBUG 04_6 [PARSE]: Processing raw text: "%"', trimmed_raw_text;

    -- Initialize outputs
    quantity_out := 1.0; -- Default quantity to 1.0 if not found
    unit_name_out := NULL;
    cleaned_name_out := trimmed_raw_text; -- Start with the full raw text for fallback

    -- Step 1: Attempt to extract Quantity from anywhere in the string
    -- This regex captures:
    --   - A whole number (e.g., "1", "12")
    --   - Optional decimal part (e.g., ".5", ".25")
    --   - Optional space and fraction (e.g., " 1/2", " 3/4")
    --   - OR just a fraction (e.g., "1/2")
    -- The outer capturing group `(...)` captures the entire numeric part.
    IF trimmed_raw_text ~* '(\d+(?:\.\d+)?(?:\s+\d+\/\d+)?|\d+\/\d+)' THEN
        quantity_match_str := (REGEXP_MATCH(trimmed_raw_text, '(\d+(?:\.\d+)?(?:\s+\d+\/\d+)?|\d+\/\d+)'))[1];

        -- Parse the extracted quantity string
        IF POSITION(' ' IN quantity_match_str) > 0 AND POSITION('/' IN quantity_match_str) > 0 THEN
            -- Mixed number (e.g., "1 1/2")
            decimal_quantity := SPLIT_PART(quantity_match_str, ' ', 1)::DECIMAL;
            DECLARE
                fraction_part TEXT := SPLIT_PART(quantity_match_str, ' ', 2);
            BEGIN
                decimal_quantity := decimal_quantity + (SPLIT_PART(fraction_part, '/', 1)::DECIMAL / SPLIT_PART(fraction_part, '/', 2)::DECIMAL);
            END;
        ELSIF POSITION('/' IN quantity_match_str) > 0 THEN
            -- Fraction (e.g., "1/2")
            decimal_quantity := (SPLIT_PART(quantity_match_str, '/', 1)::DECIMAL / SPLIT_PART(quantity_match_str, '/', 2)::DECIMAL);
        ELSE
            -- Whole number or decimal (e.g., "1", "1.5")
            decimal_quantity := quantity_match_str::DECIMAL;
        END IF;

        quantity_out := decimal_quantity;
        RAISE NOTICE 'DEBUG 04_6 [QUANTITY]: Found quantity: "%" (%): "%"', quantity_match_str, quantity_out, trimmed_raw_text;
    ELSE
        RAISE NOTICE 'DEBUG 04_6 [QUANTITY]: No quantity found. Defaulting to 1.0. Raw text: "%"', trimmed_raw_text;
    END IF;

    -- Step 2: Attempt to extract Measurement Type from anywhere in the string
    -- Iterate through defined measurement patterns to find the best match (longest pattern first)
    FOR i IN 1..ARRAY_LENGTH(measurements_map_data, 1) LOOP
        DECLARE
            current_pattern TEXT := measurements_map_data[i][1];
            current_canonical_name TEXT := measurements_map_data[i][2];
        BEGIN
            -- Check if the pattern exists anywhere in the original raw text
            IF trimmed_raw_text ~* current_pattern THEN
                -- Capture the exact matched string for removal
                measurement_match_str := (REGEXP_MATCH(trimmed_raw_text, '(' || current_pattern || ')', 'i'))[1];
                unit_mapped_name := current_canonical_name;

                unit_name_out := unit_mapped_name;
                RAISE NOTICE 'DEBUG 04_6 [MEASUREMENT]: Found unit: "%" (Canonical: "%"): "%"', measurement_match_str, unit_name_out, trimmed_raw_text;
                EXIT; -- Exit loop once a measurement is found
            END IF;
        END;
    END LOOP;

    -- Step 3: Derive cleaned_name_out by manually removing identified quantity and measurement from original text
    temp_working_text := trimmed_raw_text;

    -- Remove quantity_match_str first, if found
    IF quantity_match_str IS NOT NULL THEN
        DECLARE
            -- Use LOWER() for case-insensitive POSITION search
            q_pos INT := POSITION(LOWER(quantity_match_str) IN LOWER(temp_working_text));
            q_len INT := LENGTH(quantity_match_str);
        BEGIN
            IF q_pos > 0 THEN
                temp_working_text := SUBSTRING(temp_working_text FROM 1 FOR q_pos - 1) || SUBSTRING(temp_working_text FROM q_pos + q_len);
                RAISE NOTICE 'DEBUG 04_6 [CLEAN_Q_POST]: After removing quantity "%": "%"', quantity_match_str, temp_working_text;
            END IF;
        END;
    END IF;

    -- Remove measurement_match_str next, if found
    IF measurement_match_str IS NOT NULL THEN
        DECLARE
            -- Use LOWER() for case-insensitive POSITION search
            m_pos INT := POSITION(LOWER(measurement_match_str) IN LOWER(temp_working_text));
            m_len INT := LENGTH(measurement_match_str);
        BEGIN
            IF m_pos > 0 THEN
                temp_working_text := SUBSTRING(temp_working_text FROM 1 FOR m_pos - 1) || SUBSTRING(temp_working_text FROM m_pos + m_len);
                RAISE NOTICE 'DEBUG 04_6 [CLEAN_U_POST]: After removing unit: "%"', temp_working_text;
            END IF;
        END;
    END IF;

    -- Final cleanup for the ingredient name
    cleaned_name_out := TRIM(temp_working_text);

    -- Basic cleanup: remove leading/trailing commas/periods/extra spaces
    cleaned_name_out := TRIM(REGEXP_REPLACE(cleaned_name_out, '^[\s,.]+|[\s,.]+$', '', 'g'));
    cleaned_name_out := REGEXP_REPLACE(cleaned_name_out, '\s+', ' ', 'g'); -- Replace multiple spaces with single space

    RAISE NOTICE 'DEBUG 04_6 [CLEAN]: After basic cleanup: "%"', cleaned_name_out;

    -- Fallback if cleaned name is empty (should be rare with less aggressive cleaning)
    IF TRIM(cleaned_name_out) = '' THEN
        -- If after removing quantity and measurement, the string is empty,
        -- fall back to the original raw text, but remove "to taste" if it was the only thing.
        IF TRIM(REGEXP_REPLACE(trimmed_raw_text, '\s*to taste\s*', '', 'gi')) = '' THEN
            cleaned_name_out := 'pepper'; -- Specific fallback for "pepper to taste"
        ELSE
            cleaned_name_out := trimmed_raw_text; -- Fallback to original if cleaning resulted in empty string
        END IF;
        RAISE NOTICE 'DEBUG 04_6 [CLEAN]: Cleaned name was empty, fell back to original or specific: "%"', cleaned_name_out;
    END IF;

    RAISE NOTICE 'DEBUG 04_6 [FINAL]: Quantity: %, Unit: %, Cleaned Name: "%"', quantity_out, unit_name_out, cleaned_name_out;

END;
$$ LANGUAGE plpgsql;
