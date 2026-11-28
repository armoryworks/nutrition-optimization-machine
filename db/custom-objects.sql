-- Database objects NOT managed by the EF Core model.
-- Applied on top of the EF-generated DDL when re-deriving db/schema.sql.

CREATE OR REPLACE VIEW reference."ReferenceGroupView" AS
 SELECT ref."Id" AS "ReferenceId",
    ref."Name" AS "ReferenceName",
    ref."Description" AS "ReferenceDescription",
    grp."Id" AS "GroupId",
    grp."Name" AS "GroupName",
    grp."Description" AS "GroupDescription"
   FROM reference."Reference" ref
     JOIN reference."ReferenceIndex" idx ON ref."Id" = idx."ReferenceId"
     JOIN reference."Group" grp ON grp."Id" = idx."GroupId";

-- Trigram index: restriction criteria match ingredients with ILIKE '%…%'
-- patterns; without this each probe seq-scans the 200k-row catalog and
-- "Surprise me" took seconds per call (audit N-69).
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX IF NOT EXISTS "IX_Ingredient_Name_trgm"
    ON recipe."Ingredient" USING gin ("Name" gin_trgm_ops);
CREATE INDEX IF NOT EXISTS "IX_Ingredient_NameNormalized_trgm"
    ON recipe."Ingredient" USING gin ("NameNormalized" gin_trgm_ops);
CREATE INDEX IF NOT EXISTS "IX_IngredientAlias_AliasName_trgm"
    ON recipe."IngredientAlias" USING gin ("AliasName" gin_trgm_ops);

CREATE OR REPLACE FUNCTION plan."DietGateClearRecipes"(recipe_ids bigint[]) RETURNS void
    LANGUAGE plpgsql AS $$
BEGIN
    IF recipe_ids IS NULL OR cardinality(recipe_ids) = 0 THEN
        RETURN;
    END IF;
    DELETE FROM plan."DietRecipeState" s
    WHERE s."BuildId" IN (SELECT b."Id" FROM plan."DietGateBuild" b)
      AND s."RecipeId" = ANY (ARRAY(SELECT DISTINCT r FROM unnest(recipe_ids) r ORDER BY 1));
    INSERT INTO plan."DietGateWrite" ("RecipeId")
    SELECT DISTINCT r FROM unnest(recipe_ids) r WHERE r IS NOT NULL ORDER BY 1;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateClearFoods"(food_ids bigint[]) RETURNS void
    LANGUAGE plpgsql AS $$
DECLARE
    foods bigint[];
BEGIN
    IF food_ids IS NULL OR cardinality(food_ids) = 0 THEN
        RETURN;
    END IF;
    foods := ARRAY(SELECT DISTINCT f FROM unnest(food_ids) f ORDER BY 1);
    DELETE FROM plan."DietFoodState" s
    WHERE s."BuildId" IN (SELECT b."Id" FROM plan."DietGateBuild" b)
      AND s."IngredientId" = ANY (foods);
    INSERT INTO plan."DietGateWrite" ("IngredientId") SELECT f FROM unnest(foods) f;
    INSERT INTO plan."DietGateWrite" ("RecipeId", "IngredientId")
    SELECT DISTINCT ri."RecipeId", ri."IngredientId" FROM recipe."RecipeIngredient" ri WHERE ri."IngredientId" = ANY (foods) ORDER BY 1, 2;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateLinesWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateClearRecipes"(ARRAY(SELECT n."RecipeId" FROM new_rows n));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearRecipes"(ARRAY(SELECT o."RecipeId" FROM old_rows o));
    ELSE
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT n."RecipeId" FROM new_rows n
            WHERE NOT EXISTS (SELECT 1 FROM old_rows o
                              WHERE o."RecipeId" = n."RecipeId" AND o."IngredientId" = n."IngredientId" AND o."SortOrder" = n."SortOrder"
                                AND (o."RawLine", o."Quantity", o."MeasurementId", o."LineKind", o."AlcoholRetention", o."AlcoholRetentionBasis", o."IsDeleted")
                                    IS NOT DISTINCT FROM (n."RawLine", n."Quantity", n."MeasurementId", n."LineKind", n."AlcoholRetention", n."AlcoholRetentionBasis", n."IsDeleted"))
            UNION
            SELECT o."RecipeId" FROM old_rows o
            WHERE NOT EXISTS (SELECT 1 FROM new_rows n
                              WHERE n."RecipeId" = o."RecipeId" AND n."IngredientId" = o."IngredientId" AND n."SortOrder" = o."SortOrder")));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateRecipesWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    PERFORM plan."DietGateClearRecipes"(ARRAY(
        SELECT n."Id" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
        WHERE (o."Servings", o."RecipeServings", o."IsDeleted", o."Name", o."SourceUrl", o."ScrapedAtUtc", o."SourceAttribution")
            IS DISTINCT FROM (n."Servings", n."RecipeServings", n."IsDeleted", n."Name", n."SourceUrl", n."ScrapedAtUtc", n."SourceAttribution")));
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateStepsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT DISTINCT n."RecipeId" FROM new_rows n JOIN recipe."Recipe" r ON r."Id" = n."RecipeId" WHERE r."ScrapedAtUtc" IS NOT NULL));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT DISTINCT o."RecipeId" FROM old_rows o JOIN recipe."Recipe" r ON r."Id" = o."RecipeId" WHERE r."ScrapedAtUtc" IS NOT NULL));
    ELSE
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT DISTINCT x."RecipeId" FROM (
                SELECT n."RecipeId" FROM new_rows n
                WHERE NOT EXISTS (SELECT 1 FROM old_rows o
                                  WHERE o."RecipeId" = n."RecipeId" AND o."StepNumber" = n."StepNumber"
                                    AND (o."Description", o."IsDeleted") IS NOT DISTINCT FROM (n."Description", n."IsDeleted"))
                UNION
                SELECT o."RecipeId" FROM old_rows o
                WHERE NOT EXISTS (SELECT 1 FROM new_rows n WHERE n."RecipeId" = o."RecipeId" AND n."StepNumber" = o."StepNumber")) x
            JOIN recipe."Recipe" r ON r."Id" = x."RecipeId" WHERE r."ScrapedAtUtc" IS NOT NULL));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateFoodsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO plan."DietGateWrite" ("IngredientId") SELECT n."Id" FROM new_rows n ORDER BY 1;
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(SELECT o."Id" FROM old_rows o));
    ELSE
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT n."Id" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
            WHERE (o."Name", o."IsDeleted", o."FdcCategoryId", o."FdcDataType", o."FdcCategory", o."GramsPerMilliliter", o."GramsPerItem", o."ReferenceServingGrams", o."CurationStatusId")
                  IS DISTINCT FROM (n."Name", n."IsDeleted", n."FdcCategoryId", n."FdcDataType", n."FdcCategory", n."GramsPerMilliliter", n."GramsPerItem", n."ReferenceServingGrams", n."CurationStatusId")));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateAliasesWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(SELECT n."IngredientId" FROM new_rows n));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(SELECT o."IngredientId" FROM old_rows o));
    ELSE
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT n."IngredientId" FROM new_rows n
            UNION
            SELECT o."IngredientId" FROM old_rows o));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateNutrientsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT n."IngredientId" FROM new_rows n
            WHERE n."NutrientId" IN (SELECT c."NutrientId" FROM plan."RestrictionCriterion" c WHERE c."NutrientId" IS NOT NULL)));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT o."IngredientId" FROM old_rows o
            WHERE o."NutrientId" IN (SELECT c."NutrientId" FROM plan."RestrictionCriterion" c WHERE c."NutrientId" IS NOT NULL)));
    ELSE
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT x."IngredientId" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
                CROSS JOIN LATERAL (VALUES (o."IngredientId", o."NutrientId"), (n."IngredientId", n."NutrientId")) x("IngredientId", "NutrientId")
            WHERE (o."IngredientId", o."NutrientId", o."Amount", o."IsDeleted") IS DISTINCT FROM (n."IngredientId", n."NutrientId", n."Amount", n."IsDeleted")
              AND x."NutrientId" IN (SELECT c."NutrientId" FROM plan."RestrictionCriterion" c WHERE c."NutrientId" IS NOT NULL)));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateStatementsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(SELECT n."IngredientId" FROM new_rows n WHERE n."HouseholdId" IS NULL));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearFoods"(ARRAY(SELECT o."IngredientId" FROM old_rows o WHERE o."HouseholdId" IS NULL));
    ELSE
        PERFORM plan."DietGateClearFoods"(ARRAY(
            SELECT n."IngredientId" FROM new_rows n WHERE n."HouseholdId" IS NULL
            UNION
            SELECT o."IngredientId" FROM old_rows o WHERE o."HouseholdId" IS NULL));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateCriteriaWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO plan."DietGateWrite" ("RecipeId", "IngredientId") VALUES (NULL, NULL);
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateHoldRecipes"(recipe_ids bigint[]) RETURNS void
    LANGUAGE plpgsql AS $$
BEGIN
    IF recipe_ids IS NULL OR cardinality(recipe_ids) = 0 THEN
        RETURN;
    END IF;
    INSERT INTO plan."DietGateWrite" ("RecipeId")
    SELECT DISTINCT r FROM unnest(recipe_ids) r WHERE r IS NOT NULL ORDER BY 1;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateWordingsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
DECLARE
    wordings text[];
    foods bigint[];
BEGIN
    IF TG_OP = 'INSERT' THEN
        wordings := ARRAY(SELECT DISTINCT lower(n."Wording") FROM new_rows n WHERE n."Verdict" = 'category' AND NOT n."IsDeleted");
        foods := ARRAY(SELECT DISTINCT n."IngredientId" FROM new_rows n WHERE n."Verdict" = 'food' AND n."IngredientId" IS NOT NULL AND NOT n."IsDeleted");
    ELSIF TG_OP = 'DELETE' THEN
        wordings := ARRAY(SELECT DISTINCT lower(o."Wording") FROM old_rows o WHERE o."Verdict" = 'category' AND NOT o."IsDeleted");
        foods := ARRAY(SELECT DISTINCT o."IngredientId" FROM old_rows o WHERE o."Verdict" = 'food' AND o."IngredientId" IS NOT NULL AND NOT o."IsDeleted");
    ELSE
        wordings := ARRAY(
            SELECT DISTINCT lower(x."Wording") FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
                CROSS JOIN LATERAL (VALUES (o."Wording", o."Verdict", o."IsDeleted"), (n."Wording", n."Verdict", n."IsDeleted")) x("Wording", "Verdict", "IsDeleted")
            WHERE x."Verdict" = 'category' AND NOT x."IsDeleted"
              AND (o."Wording", o."Verdict", o."IngredientId", o."FdcCategoryId", o."CategoryHead", o."IsDeleted")
                  IS DISTINCT FROM (n."Wording", n."Verdict", n."IngredientId", n."FdcCategoryId", n."CategoryHead", n."IsDeleted"));
        foods := ARRAY(
            SELECT DISTINCT x."IngredientId" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
                CROSS JOIN LATERAL (VALUES (o."IngredientId", o."Verdict", o."IsDeleted"), (n."IngredientId", n."Verdict", n."IsDeleted")) x("IngredientId", "Verdict", "IsDeleted")
            WHERE x."Verdict" = 'food' AND x."IngredientId" IS NOT NULL AND NOT x."IsDeleted"
              AND (o."Wording", o."Verdict", o."IngredientId", o."FdcCategoryId", o."CategoryHead", o."IsDeleted")
                  IS DISTINCT FROM (n."Wording", n."Verdict", n."IngredientId", n."FdcCategoryId", n."CategoryHead", n."IsDeleted"));
    END IF;
    IF coalesce(current_setting('nom.diet_wording_holds', true), '') = 'writer' THEN
        foods := '{}';
    ELSE
        foods := ARRAY(
            SELECT f FROM unnest(foods) f
            WHERE EXISTS (SELECT 1 FROM curation."FoodCatalogProposal" p
                          WHERE p."ProposedValue" = f::text AND p."Field" = 'fdc_link' AND p."Status" = 4 AND p."Source" LIKE 'ai:%' AND NOT p."IsDeleted"));
    END IF;
    IF cardinality(wordings) = 0 AND cardinality(foods) = 0 THEN
        RETURN NULL;
    END IF;
    PERFORM plan."DietGateHoldRecipes"(ARRAY(
        SELECT ri."RecipeId" FROM unnest(wordings) w("Wording")
            JOIN recipe."Ingredient" i ON i."Name" ILIKE replace(replace(replace(w."Wording", '\', '\\'), '%', '\%'), '_', '\_') AND lower(i."Name") = w."Wording"
            JOIN recipe."RecipeIngredient" ri ON ri."IngredientId" = i."Id"
        UNION
        SELECT ri."RecipeId" FROM recipe."RecipeIngredient" ri WHERE ri."IngredientId" = ANY (foods)));
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateProposalsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
DECLARE
    picked text[];
BEGIN
    IF TG_OP = 'INSERT' THEN
        picked := ARRAY(SELECT n."ProposedValue" FROM new_rows n
                        WHERE n."Field" = 'fdc_link' AND n."Status" = 4 AND n."Source" LIKE 'ai:%' AND NOT n."IsDeleted");
    ELSIF TG_OP = 'DELETE' THEN
        picked := ARRAY(SELECT o."ProposedValue" FROM old_rows o
                        WHERE o."Field" = 'fdc_link' AND o."Status" = 4 AND o."Source" LIKE 'ai:%' AND NOT o."IsDeleted");
    ELSE
        picked := ARRAY(
            SELECT x."ProposedValue" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
                CROSS JOIN LATERAL (VALUES (o."ProposedValue"), (n."ProposedValue")) x("ProposedValue")
            WHERE ((o."Field" = 'fdc_link' AND o."Status" = 4 AND o."Source" LIKE 'ai:%' AND NOT o."IsDeleted")
                   OR (n."Field" = 'fdc_link' AND n."Status" = 4 AND n."Source" LIKE 'ai:%' AND NOT n."IsDeleted"))
              AND (o."Field", o."Status", o."Source", o."CurrentValue", o."ProposedValue", o."IsDeleted")
                  IS DISTINCT FROM (n."Field", n."Status", n."Source", n."CurrentValue", n."ProposedValue", n."IsDeleted"));
    END IF;
    IF cardinality(picked) = 0 THEN
        RETURN NULL;
    END IF;
    PERFORM plan."DietGateHoldRecipes"(ARRAY(
        SELECT ri."RecipeId" FROM recipe."RecipeIngredient" ri
        WHERE ri."IngredientId" = ANY (ARRAY(SELECT DISTINCT v::bigint FROM unnest(picked) v WHERE v ~ '^[0-9]{1,18}$'))));
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateReadingsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM auth."PlatformFeature" f
                   WHERE f."Key" = 'diet-second-reader-gate' AND f."IsEnabled" AND NOT f."IsDeleted") THEN
        RETURN NULL;
    END IF;
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateHoldRecipes"(ARRAY(SELECT n."RecipeId" FROM new_rows n));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateHoldRecipes"(ARRAY(SELECT o."RecipeId" FROM old_rows o));
    ELSE
        PERFORM plan."DietGateHoldRecipes"(ARRAY(
            SELECT n."RecipeId" FROM new_rows n JOIN old_rows o ON o."RecipeId" = n."RecipeId" AND o."Model" = n."Model"
            WHERE (o."PromptSignature", o."TextSignature", o."Readable", o."Verdicts")
                  IS DISTINCT FROM (n."PromptSignature", n."TextSignature", n."Readable", n."Verdicts")
            UNION
            SELECT n."RecipeId" FROM new_rows n
            WHERE NOT EXISTS (SELECT 1 FROM old_rows o WHERE o."RecipeId" = n."RecipeId" AND o."Model" = n."Model")
            UNION
            SELECT o."RecipeId" FROM old_rows o
            WHERE NOT EXISTS (SELECT 1 FROM new_rows n WHERE n."RecipeId" = o."RecipeId" AND n."Model" = o."Model")));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateJudgedHoldsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        PERFORM plan."DietGateHoldRecipes"(ARRAY(SELECT n."RecipeId" FROM new_rows n));
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateHoldRecipes"(ARRAY(SELECT o."RecipeId" FROM old_rows o));
    ELSE
        PERFORM plan."DietGateHoldRecipes"(ARRAY(
            SELECT n."RecipeId" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
            WHERE (o."RecipeId", o."RestrictionTypeId") IS DISTINCT FROM (n."RecipeId", n."RestrictionTypeId")
            UNION
            SELECT o."RecipeId" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
            WHERE (o."RecipeId", o."RestrictionTypeId") IS DISTINCT FROM (n."RecipeId", n."RestrictionTypeId")));
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateFeaturesWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
DECLARE
    moved boolean;
BEGIN
    IF TG_OP = 'INSERT' THEN
        moved := EXISTS (SELECT 1 FROM new_rows n WHERE n."Key" = 'diet-second-reader-gate');
    ELSIF TG_OP = 'DELETE' THEN
        moved := EXISTS (SELECT 1 FROM old_rows o WHERE o."Key" = 'diet-second-reader-gate');
    ELSE
        moved := EXISTS (SELECT 1 FROM new_rows n FULL JOIN old_rows o ON o."Id" = n."Id"
                         WHERE 'diet-second-reader-gate' IN (n."Key", o."Key")
                           AND (o."Key", o."IsEnabled", o."IsDeleted") IS DISTINCT FROM (n."Key", n."IsEnabled", n."IsDeleted"));
    END IF;
    IF moved THEN
        INSERT INTO plan."DietGateWrite" ("RecipeId", "IngredientId") VALUES (NULL, NULL);
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION plan."DietGateUnitsWritten"() RETURNS trigger
    LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT DISTINCT ri."RecipeId" FROM recipe."RecipeIngredient" ri
            WHERE ri."MeasurementId" IN (SELECT o."Id" FROM old_rows o)));
    ELSE
        PERFORM plan."DietGateClearRecipes"(ARRAY(
            SELECT DISTINCT ri."RecipeId" FROM recipe."RecipeIngredient" ri
            WHERE ri."MeasurementId" IN (
                SELECT n."Id" FROM new_rows n JOIN old_rows o ON o."Id" = n."Id"
                WHERE (o."MeasurementCategoryId", o."BaseUnitConversionFactor", o."IsContainer", o."IsDeleted")
                      IS DISTINCT FROM (n."MeasurementCategoryId", n."BaseUnitConversionFactor", n."IsContainer", n."IsDeleted"))));
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS "DietGateCriteriaWritten" ON plan."RestrictionCriterion";
CREATE TRIGGER "DietGateCriteriaWritten" AFTER INSERT OR UPDATE OR DELETE ON plan."RestrictionCriterion"
    FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateCriteriaWritten"();

DROP TRIGGER IF EXISTS "DietGateUnitsUpdated" ON measurement."Measurement";
CREATE TRIGGER "DietGateUnitsUpdated" AFTER UPDATE ON measurement."Measurement"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateUnitsWritten"();
DROP TRIGGER IF EXISTS "DietGateUnitsDeleted" ON measurement."Measurement";
CREATE TRIGGER "DietGateUnitsDeleted" AFTER DELETE ON measurement."Measurement"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateUnitsWritten"();

DROP TRIGGER IF EXISTS "DietGateLinesInserted" ON recipe."RecipeIngredient";
CREATE TRIGGER "DietGateLinesInserted" AFTER INSERT ON recipe."RecipeIngredient"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateLinesWritten"();
DROP TRIGGER IF EXISTS "DietGateLinesUpdated" ON recipe."RecipeIngredient";
CREATE TRIGGER "DietGateLinesUpdated" AFTER UPDATE ON recipe."RecipeIngredient"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateLinesWritten"();
DROP TRIGGER IF EXISTS "DietGateLinesDeleted" ON recipe."RecipeIngredient";
CREATE TRIGGER "DietGateLinesDeleted" AFTER DELETE ON recipe."RecipeIngredient"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateLinesWritten"();

DROP TRIGGER IF EXISTS "DietGateRecipesUpdated" ON recipe."Recipe";
CREATE TRIGGER "DietGateRecipesUpdated" AFTER UPDATE ON recipe."Recipe"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateRecipesWritten"();

DROP TRIGGER IF EXISTS "DietGateStepsInserted" ON recipe."RecipeStep";
CREATE TRIGGER "DietGateStepsInserted" AFTER INSERT ON recipe."RecipeStep"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStepsWritten"();
DROP TRIGGER IF EXISTS "DietGateStepsUpdated" ON recipe."RecipeStep";
CREATE TRIGGER "DietGateStepsUpdated" AFTER UPDATE ON recipe."RecipeStep"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStepsWritten"();
DROP TRIGGER IF EXISTS "DietGateStepsDeleted" ON recipe."RecipeStep";
CREATE TRIGGER "DietGateStepsDeleted" AFTER DELETE ON recipe."RecipeStep"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStepsWritten"();

DROP TRIGGER IF EXISTS "DietGateFoodsInserted" ON recipe."Ingredient";
CREATE TRIGGER "DietGateFoodsInserted" AFTER INSERT ON recipe."Ingredient"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFoodsWritten"();
DROP TRIGGER IF EXISTS "DietGateFoodsUpdated" ON recipe."Ingredient";
CREATE TRIGGER "DietGateFoodsUpdated" AFTER UPDATE ON recipe."Ingredient"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFoodsWritten"();
DROP TRIGGER IF EXISTS "DietGateFoodsDeleted" ON recipe."Ingredient";
CREATE TRIGGER "DietGateFoodsDeleted" AFTER DELETE ON recipe."Ingredient"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFoodsWritten"();

DROP TRIGGER IF EXISTS "DietGateAliasesInserted" ON recipe."IngredientAlias";
CREATE TRIGGER "DietGateAliasesInserted" AFTER INSERT ON recipe."IngredientAlias"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateAliasesWritten"();
DROP TRIGGER IF EXISTS "DietGateAliasesUpdated" ON recipe."IngredientAlias";
CREATE TRIGGER "DietGateAliasesUpdated" AFTER UPDATE ON recipe."IngredientAlias"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateAliasesWritten"();
DROP TRIGGER IF EXISTS "DietGateAliasesDeleted" ON recipe."IngredientAlias";
CREATE TRIGGER "DietGateAliasesDeleted" AFTER DELETE ON recipe."IngredientAlias"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateAliasesWritten"();

DROP TRIGGER IF EXISTS "DietGateNutrientsInserted" ON nutrient."IngredientNutrient";
CREATE TRIGGER "DietGateNutrientsInserted" AFTER INSERT ON nutrient."IngredientNutrient"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateNutrientsWritten"();
DROP TRIGGER IF EXISTS "DietGateNutrientsUpdated" ON nutrient."IngredientNutrient";
CREATE TRIGGER "DietGateNutrientsUpdated" AFTER UPDATE ON nutrient."IngredientNutrient"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateNutrientsWritten"();
DROP TRIGGER IF EXISTS "DietGateNutrientsDeleted" ON nutrient."IngredientNutrient";
CREATE TRIGGER "DietGateNutrientsDeleted" AFTER DELETE ON nutrient."IngredientNutrient"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateNutrientsWritten"();

DROP TRIGGER IF EXISTS "DietGateStatementsInserted" ON recipe."IngredientContents";
CREATE TRIGGER "DietGateStatementsInserted" AFTER INSERT ON recipe."IngredientContents"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStatementsWritten"();
DROP TRIGGER IF EXISTS "DietGateStatementsUpdated" ON recipe."IngredientContents";
CREATE TRIGGER "DietGateStatementsUpdated" AFTER UPDATE ON recipe."IngredientContents"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStatementsWritten"();
DROP TRIGGER IF EXISTS "DietGateStatementsDeleted" ON recipe."IngredientContents";
CREATE TRIGGER "DietGateStatementsDeleted" AFTER DELETE ON recipe."IngredientContents"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateStatementsWritten"();

DROP TRIGGER IF EXISTS "DietGateWordingsInserted" ON recipe."IngredientWording";
CREATE TRIGGER "DietGateWordingsInserted" AFTER INSERT ON recipe."IngredientWording"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateWordingsWritten"();
DROP TRIGGER IF EXISTS "DietGateWordingsUpdated" ON recipe."IngredientWording";
CREATE TRIGGER "DietGateWordingsUpdated" AFTER UPDATE ON recipe."IngredientWording"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateWordingsWritten"();
DROP TRIGGER IF EXISTS "DietGateWordingsDeleted" ON recipe."IngredientWording";
CREATE TRIGGER "DietGateWordingsDeleted" AFTER DELETE ON recipe."IngredientWording"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateWordingsWritten"();

DROP TRIGGER IF EXISTS "DietGateProposalsInserted" ON curation."FoodCatalogProposal";
CREATE TRIGGER "DietGateProposalsInserted" AFTER INSERT ON curation."FoodCatalogProposal"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateProposalsWritten"();
DROP TRIGGER IF EXISTS "DietGateProposalsUpdated" ON curation."FoodCatalogProposal";
CREATE TRIGGER "DietGateProposalsUpdated" AFTER UPDATE ON curation."FoodCatalogProposal"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateProposalsWritten"();
DROP TRIGGER IF EXISTS "DietGateProposalsDeleted" ON curation."FoodCatalogProposal";
CREATE TRIGGER "DietGateProposalsDeleted" AFTER DELETE ON curation."FoodCatalogProposal"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateProposalsWritten"();

DROP TRIGGER IF EXISTS "DietGateNutrientNamesWritten" ON nutrient."Nutrient";
CREATE TRIGGER "DietGateNutrientNamesWritten" AFTER UPDATE OR DELETE ON nutrient."Nutrient"
    FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateCriteriaWritten"();

DROP TRIGGER IF EXISTS "DietGateTypeNamesWritten" ON reference."Reference";
CREATE TRIGGER "DietGateTypeNamesWritten" AFTER UPDATE OR DELETE ON reference."Reference"
    FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateCriteriaWritten"();

DROP TRIGGER IF EXISTS "DietGateReadingsInserted" ON plan."RecipeDietReading";
CREATE TRIGGER "DietGateReadingsInserted" AFTER INSERT ON plan."RecipeDietReading"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateReadingsWritten"();
DROP TRIGGER IF EXISTS "DietGateReadingsUpdated" ON plan."RecipeDietReading";
CREATE TRIGGER "DietGateReadingsUpdated" AFTER UPDATE ON plan."RecipeDietReading"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateReadingsWritten"();
DROP TRIGGER IF EXISTS "DietGateReadingsDeleted" ON plan."RecipeDietReading";
CREATE TRIGGER "DietGateReadingsDeleted" AFTER DELETE ON plan."RecipeDietReading"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateReadingsWritten"();

DROP TRIGGER IF EXISTS "DietGateFeaturesInserted" ON auth."PlatformFeature";
CREATE TRIGGER "DietGateFeaturesInserted" AFTER INSERT ON auth."PlatformFeature"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFeaturesWritten"();
DROP TRIGGER IF EXISTS "DietGateFeaturesUpdated" ON auth."PlatformFeature";
CREATE TRIGGER "DietGateFeaturesUpdated" AFTER UPDATE ON auth."PlatformFeature"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFeaturesWritten"();
DROP TRIGGER IF EXISTS "DietGateFeaturesDeleted" ON auth."PlatformFeature";
CREATE TRIGGER "DietGateFeaturesDeleted" AFTER DELETE ON auth."PlatformFeature"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateFeaturesWritten"();

DROP TRIGGER IF EXISTS "DietGateJudgedHoldsInserted" ON plan."RecipeDietJudgedHold";
CREATE TRIGGER "DietGateJudgedHoldsInserted" AFTER INSERT ON plan."RecipeDietJudgedHold"
    REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateJudgedHoldsWritten"();
DROP TRIGGER IF EXISTS "DietGateJudgedHoldsUpdated" ON plan."RecipeDietJudgedHold";
CREATE TRIGGER "DietGateJudgedHoldsUpdated" AFTER UPDATE ON plan."RecipeDietJudgedHold"
    REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateJudgedHoldsWritten"();
DROP TRIGGER IF EXISTS "DietGateJudgedHoldsDeleted" ON plan."RecipeDietJudgedHold";
CREATE TRIGGER "DietGateJudgedHoldsDeleted" AFTER DELETE ON plan."RecipeDietJudgedHold"
    REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT EXECUTE FUNCTION plan."DietGateJudgedHoldsWritten"();
