-- 04_6_recipe_com_parsing_functions.sql
-- This script is now used primarily for dropping functions to ensure a clean state
-- before individual parsing functions are created.

SET client_min_messages TO NOTICE; -- Set to NOTICE; will be overridden by DEBUG from 05_5 if needed
SET search_path TO public, recipe, reference, nutrient, audit, plan, shopping, person, auth;

-- Explicitly drop the functions if they exist to ensure a clean re-creation
DROP FUNCTION IF EXISTS escape_regex_chars(TEXT) CASCADE;
DROP FUNCTION IF EXISTS regexp_escape(TEXT) CASCADE; -- Drop the new one if it exists
DROP FUNCTION IF EXISTS parse_ingredient_line_comprehensive(TEXT) CASCADE;
DROP FUNCTION IF EXISTS parse_ingredient_line_comprehensive_v2(TEXT) CASCADE; -- Drop V2 as well
