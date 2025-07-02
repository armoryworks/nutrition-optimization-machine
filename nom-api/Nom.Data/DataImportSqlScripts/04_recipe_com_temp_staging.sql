-- 04_recipe_com_temp_staging.sql
-- This script processes raw recipe data from the temporary staging table
-- and then populates other temporary tables with parsed and prepared data.

-- The \copy command to load data into recipe.recipe_com_raw_staging is now handled by import_recipes.sh

-- Function to safely convert text to JSONB array
-- This function will attempt to parse the input as JSONB.
-- If it fails, it will wrap the input string into a JSON array,
-- escaping internal double quotes, and then cast it to JSONB.
-- This handles cases where the input is a plain string instead of a JSON array.
CREATE OR REPLACE FUNCTION jsonb_array_safe_cast(input_text TEXT)
RETURNS JSONB AS $$
BEGIN
    -- Try to cast directly to JSONB
    RETURN input_text::jsonb;
EXCEPTION
    WHEN OTHERS THEN
        -- If direct cast fails, assume it's a plain string and wrap it in a JSON array.
        -- Escape internal double quotes by replacing " with \"
        RETURN ('["' || REPLACE(input_text, '"', '\"') || '"]')::jsonb;
END;
$$ LANGUAGE plpgsql IMMUTABLE;


DO $$
DECLARE
    system_person_id BIGINT := 1;
    -- Declare MeasurementType IDs for parsing common units (needed here for parsed_staging)
    g_id BIGINT;
    mg_id BIGINT;
    mcg_id BIGINT;
    kcal_id BIGINT;
    cup_id BIGINT;
    tbsp_id BIGINT;
    tsp_id BIGINT;
    pound_id BIGINT;
    ounce_id BIGINT;
    ml_id BIGINT;
    l_id BIGINT;
    item_id BIGINT; -- For items counted by piece/unit
    unknown_id BIGINT;
BEGIN

    -- Fetch MeasurementType IDs dynamically (needed for final_measurement_type_id in ingredient_parsed_staging)
    SELECT "Id" INTO g_id FROM reference."Reference" WHERE "Name" = 'g' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO mg_id FROM reference."Reference" WHERE "Name" = 'mg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO mcg_id FROM reference."Reference" WHERE "Name" = 'mcg' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO kcal_id FROM reference."Reference" WHERE "Name" = 'kcal' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO cup_id FROM reference."Reference" WHERE "Name" = 'cup' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO tbsp_id FROM reference."Reference" WHERE "Name" = 'tbsp' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO tsp_id FROM reference."Reference" WHERE "Name" = 'tsp' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO pound_id FROM reference."Reference" WHERE "Name" = 'lb' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO ounce_id FROM reference."Reference" WHERE "Name" = 'oz' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO ml_id FROM reference."Reference" WHERE "Name" = 'ml' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO l_id FROM reference."Reference" WHERE "Name" = 'l' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO item_id FROM reference."Reference" WHERE "Name" = 'item' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));
    SELECT "Id" INTO unknown_id FROM reference."Reference" WHERE "Name" = 'unknown' AND "Id" IN (SELECT "ReferenceId" FROM reference."ReferenceIndex" WHERE "GroupId" = (SELECT "Id" FROM reference."Group" WHERE "Name" = 'Measurement Types'));

    RAISE NOTICE 'Count of rows in recipe.recipe_com_raw_staging before processing: %', (SELECT COUNT(*) FROM recipe.recipe_com_raw_staging);

    -- Populate recipe.recipe_com_recipe_staging from raw data, handling duplicates
    INSERT INTO recipe.recipe_com_recipe_staging (source_link, recipe_name, source_site, raw_ingredients_json, raw_directions_json, original_csv_index)
    SELECT DISTINCT ON (TRIM(link))
        TRIM(link),
        TRIM(title),
        TRIM(source),
        TRIM(ingredients), -- Store the raw JSON string
        TRIM(directions), -- Store the raw JSON string
        blank_col -- Using blank_col as a placeholder for original csv index
    FROM recipe.recipe_com_raw_staging
    WHERE TRIM(link) IS NOT NULL AND TRIM(link) != ''
    ORDER BY TRIM(link), blank_col; -- Deterministic selection

    -- Identify and store duplicates within recipe.recipe_com_raw_staging based on 'link'
    INSERT INTO recipe_com_recipe_duplicates_report_staging (source_link, recipe_name, source_site, duplicate_reason)
    SELECT
        r.link,
        r.title,
        r.source,
        'Duplicate link in source CSV, only one kept'
    FROM (
        SELECT
            *,
            ROW_NUMBER() OVER (PARTITION BY TRIM(link) ORDER BY blank_col) as rn
        FROM recipe.recipe_com_raw_staging
        WHERE TRIM(link) IS NOT NULL AND TRIM(link) != ''
    ) r
    WHERE r.rn > 1;

    RAISE NOTICE 'Processed raw recipes into recipe.recipe_com_recipe_staging.';

    -- Populate recipe_com_ingredient_parsed_staging
    -- The INSERT statement now directly follows the CTE for correct scoping
    INSERT INTO recipe_com_ingredient_parsed_staging (source_link, line_order, ingredient_raw_text, ner_ingredient_name, parsed_amount, parsed_unit_name, final_ingredient_name, final_measurement_type_id)
    WITH RawIngredientsAndNER AS (
        SELECT
            rcs.link AS source_link,
            -- Apply aggressive cleansing and then use the safe cast function
            jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.ingredients, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g')) AS ingredients_jsonb_safe,
            jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.ner, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g')) AS ner_jsonb_safe,
            ing_text.value AS raw_ing_text,
            ing_text.ordinality AS ing_line_order,
            ner_text.value AS ner_ing_text,
            ner_text.ordinality AS ner_line_order
        FROM recipe.recipe_com_raw_staging rcs
        LEFT JOIN LATERAL jsonb_array_elements_text(jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.ingredients, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g'))) WITH ORDINALITY AS ing_text(value, ordinality) ON TRUE
        LEFT JOIN LATERAL jsonb_array_elements_text(jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.ner, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g'))) WITH ORDINALITY AS ner_text(value, ordinality) ON TRUE
        WHERE rcs.ingredients IS NOT NULL AND rcs.ingredients != '[]'
    ),
    AlignedIngredients AS (
        SELECT
            r.source_link,
            r.raw_ing_text AS ingredient_raw_text,
            r.ing_line_order AS line_order,
            r.ner_ing_text AS ner_ingredient_name
        FROM RawIngredientsAndNER r
        WHERE r.ing_line_order = r.ner_line_order -- Align by ordinality
    ),
    ParsedIngredients AS (
        SELECT
            ai.source_link,
            ai.line_order,
            ai.ingredient_raw_text,
            ai.ner_ingredient_name,
            -- Prioritize NER name, otherwise attempt basic parsing from raw text
            COALESCE(
                TRIM(ai.ner_ingredient_name), -- Use NER name if available and not empty
                TRIM(regexp_replace(
                    regexp_replace(
                        regexp_replace(
                            regexp_replace(
                                TRIM(ai.ingredient_raw_text),
                                -- Regex to remove common quantities and units at the start of the string
                                '^\\s*([0-9]+(?:\\.[0-9]+)?(?:/[0-9]+)?|\\ba|an|one)\\s+(?:cup|cups|c|tbsp|tablespoon|tablespoons|tsp|teaspoon|teaspoons|lb|lbs|pound|pounds|oz|ounces|g|grams|ml|millilitre|millilitres|l|liters|litre|litres|item|items)\\b\\.?\\s*',
                                '',
                                'i'
                            ),
                            '^\\s*([0-9]+(?:\\.[0-9]+)?(?:/[0-9]+)?)\\s*', -- Remove just numbers at the start
                            '',
                            'i'
                        ),
                        '^(of|to|for)\\s+', -- Remove leading "of ", "to ", "for "
                        '',
                        'i'
                    ),
                    E'[\\\\(\\)\\[\\]\\\\{\\\\}\\<\\\\>/]', -- Remove common punctuation that might be noise
                    '',
                    'g'
                ))
            ) AS final_ingredient_name_candidate,
            -- Attempt to parse amount (very basic, handles simple numbers and fractions)
            CASE
                WHEN ai.ingredient_raw_text ~ '^\\s*([0-9]+(?:\\.[0-9]+)?(?:/[0-9]+)?)\\s*(\\S+)?' THEN
                    (regexp_match(TRIM(ai.ingredient_raw_text), '^\\s*([0-9]+(?:\\.[0-9]+)?(?:/[0-9]+)?))\\s*'))[1]::DECIMAL
                WHEN ai.ingredient_raw_text ~ '^\\s*(a|an|one)\\s+' THEN 1::DECIMAL
                ELSE NULL
            END AS parsed_amount_candidate,
            -- Attempt to parse unit (basic keywords)
            CASE
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\yc\\w*\\s+' THEN 'cup'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\t(b|bsp)\\w*\\s+' THEN 'tbsp'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\t(s|sp)\\w*\\s+' THEN 'tsp'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\bpound(s)?\\b|\\blb(s)?\\b' THEN 'lb'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\boz(s)?\\b|\\bounc(e|es)\\b' THEN 'oz'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\bgram(s)?\\b|\\bg\\b' THEN 'g'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\bmillilitre(s)?\\b|\\bml(s)?\\b' THEN 'ml'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\bliter(s)?\\b|\\bl(s)?\\b' THEN 'l'
                WHEN LOWER(TRIM(ai.ingredient_raw_text)) ~ '\\belement(s)?\\b|\\bitem(s)?\\b' THEN 'item'
                ELSE NULL
            END AS parsed_unit_name_candidate
        FROM AlignedIngredients ai
    )
    SELECT DISTINCT ON (pi.source_link, pi.line_order) -- Ensure unique (recipe, ingredient_raw_text)
        pi.source_link,
        pi.line_order,
        pi.ingredient_raw_text,
        pi.ner_ingredient_name,
        COALESCE(pi.parsed_amount_candidate, 1), -- Default to 1 if amount not parsed
        pi.parsed_unit_name_candidate,
        TRIM(REPLACE(REPLACE(REPLACE(LOWER(pi.final_ingredient_name_candidate), 'freshly ground', ''), 'to taste', ''), 'for garnish', '')) AS final_ingredient_name,
        COALESCE(
            CASE
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'cup' THEN cup_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'tbsp' THEN tbsp_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'tsp' THEN tsp_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'lb' THEN pound_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'oz' THEN ounce_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'g' THEN g_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'ml' THEN ml_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'l' THEN l_id
                WHEN LOWER(pi.parsed_unit_name_candidate) = 'item' THEN item_id
                ELSE unknown_id
            END,
            unknown_id
        ) AS final_measurement_type_id
    FROM ParsedIngredients pi
    WHERE TRIM(COALESCE(pi.final_ingredient_name_candidate, '')) != ''
    ORDER BY pi.source_link, pi.line_order;

    RAISE NOTICE 'Parsed and staged ingredients into recipe_com_ingredient_parsed_staging.';

    -- Populate recipe_com_instruction_parsed_staging
    WITH RawDirectionsExpanded AS (
        SELECT
            rcs.link AS source_link,
            -- Apply aggressive cleansing and then use the safe cast function
            jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.directions, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g')) AS directions_jsonb_safe,
            dir_text.value AS instruction_text,
            dir_text.ordinality AS instruction_ordinality
        FROM recipe.recipe_com_raw_staging rcs
        LEFT JOIN LATERAL jsonb_array_elements_text(jsonb_array_safe_cast(regexp_replace(REPLACE(rcs.directions, '\\u0000', ''), '[\\x00-\\x1F\\x7F]', '', 'g'))) WITH ORDINALITY AS dir_text(value, ordinality) ON TRUE
        WHERE rcs.directions IS NOT NULL AND rcs.directions != '[]'
    )
    INSERT INTO recipe_com_instruction_parsed_staging (source_link, instruction_step_number, instruction_text, summary_text)
    SELECT DISTINCT ON (rde.source_link, rde.instruction_ordinality)
        rde.source_link,
        rde.instruction_ordinality::SMALLINT, -- Cast to SMALLINT for byte mapping
        TRIM(rde.instruction_text),
        TRIM(LEFT(rde.instruction_text, 255)) -- Take first 255 chars as summary
    FROM RawDirectionsExpanded rde
    WHERE TRIM(rde.instruction_text) IS NOT NULL AND TRIM(rde.instruction_text) != ''
    ORDER BY rde.source_link, rde.instruction_ordinality;

    RAISE NOTICE 'Parsed and staged instructions into recipe_com_instruction_parsed_staging.';

EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'CRITICAL ERROR in 04_recipe_com_temp_staging.sql: %', SQLERRM;
        RAISE NOTICE 'SQLSTATE: %', SQLSTATE;
        RAISE NOTICE '--- Max Length Report for recipe.recipe_com_raw_staging ---';
        RAISE NOTICE 'blank_col max length: %', (SELECT MAX(LENGTH(blank_col)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'title max length: %', (SELECT MAX(LENGTH(title)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'ingredients max length: %', (SELECT MAX(LENGTH(ingredients)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'directions max length: %', (SELECT MAX(LENGTH(directions)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'link max length: %', (SELECT MAX(LENGTH(link)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'source max length: %', (SELECT MAX(LENGTH(source)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE 'ner max length: %', (SELECT MAX(LENGTH(ner)) FROM recipe.recipe_com_raw_staging);
        RAISE NOTICE '--- Max Length Report for recipe.recipe_com_recipe_staging ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'recipe_name max length: %', (SELECT MAX(LENGTH(recipe_name)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'source_site max length: %', (SELECT MAX(LENGTH(source_site)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'raw_ingredients_json max length: %', (SELECT MAX(LENGTH(raw_ingredients_json)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE 'raw_directions_json max length: %', (SELECT MAX(LENGTH(raw_directions_json)) FROM recipe.recipe_com_recipe_staging);
        RAISE NOTICE '--- Max Length Report for recipe_com_ingredient_parsed_staging ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'ingredient_raw_text max length: %', (SELECT MAX(LENGTH(ingredient_raw_text)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'ner_ingredient_name max length: %', (SELECT MAX(LENGTH(ner_ingredient_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'final_ingredient_name max length: %', (SELECT MAX(LENGTH(final_ingredient_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE 'parsed_unit_name max length: %', (SELECT MAX(LENGTH(parsed_unit_name)) FROM recipe_com_ingredient_parsed_staging);
        RAISE NOTICE '--- Max Length Report for recipe_com_instruction_parsed_staging ---';
        RAISE NOTICE 'source_link max length: %', (SELECT MAX(LENGTH(source_link)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'instruction_text max length: %', (SELECT MAX(LENGTH(instruction_text)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'summary_text max length: %', (SELECT MAX(LENGTH(summary_text)) FROM recipe_com_instruction_parsed_staging);
        RAISE NOTICE 'A duplicate report for recipes might have been generated (recipe_com_recipe_duplicates_report.csv).';
        RAISE; -- Re-raise the original error
END $$;
