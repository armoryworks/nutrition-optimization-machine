-- Combined USDA + OFF Transform
-- Upserts from staging tables into NOM production tables.
-- Idempotent — safe to re-run.

-- ─────────────────────────────────────────────────────────────
-- 1. MERGE ingredients into recipe."Ingredient"
-- ─────────────────────────────────────────────────────────────
MERGE INTO recipe."Ingredient" AS target
USING (
    SELECT DISTINCT ON (LOWER(TRIM(description)))
        fdc_id,
        description,
        data_type,
        source_priority
    FROM "Staging_Combined_Food"
    ORDER BY LOWER(TRIM(description)), source_priority ASC
) AS source
ON target."FdcId" = source.fdc_id
   OR target."Name" = source.description
WHEN MATCHED THEN
    UPDATE SET
        "FdcId" = COALESCE(target."FdcId", source.fdc_id),
        "Name" = source.description,
        "NameNormalized" = LOWER(TRIM(source.description)),
        "FdcDataType" = source.data_type
WHEN NOT MATCHED THEN
    INSERT ("FdcId", "Name", "NameNormalized", "Description", "FdcDataType", "CurationStatusId", "CreatedDate")
    VALUES (
        source.fdc_id,
        source.description,
        LOWER(TRIM(source.description)),
        source.description,
        source.data_type,
        9000,  -- Non-Curated
        NOW()
    );

-- ─────────────────────────────────────────────────────────────
-- 2. MERGE ingredient nutrients into nutrient."IngredientNutrient"
-- ─────────────────────────────────────────────────────────────
-- First, delete existing nutrient rows for ingredients we're updating
-- (simpler than MERGE for many-to-many with compound key)
DELETE FROM nutrient."IngredientNutrient" ine
USING recipe."Ingredient" i,
      "Staging_Combined_Food" scf
WHERE ine."IngredientId" = i."Id"
  AND i."FdcId" = scf.fdc_id;

-- Then insert fresh
INSERT INTO nutrient."IngredientNutrient" (
    "IngredientId", "NutrientId", "Amount", "MeasurementId", "FdcId", "CreatedDate"
)
SELECT
    i."Id" AS "IngredientId",
    scfn.nutrient_id AS "NutrientId",
    scfn.amount AS "Amount",
    scfn.measurement_id AS "MeasurementId",
    scfn.fdc_id AS "FdcId",
    NOW()
FROM "Staging_Combined_Food_Nutrient" scfn
JOIN recipe."Ingredient" i ON i."FdcId" = scfn.fdc_id
JOIN nutrient."Nutrient" n ON n."Id" = scfn.nutrient_id
WHERE scfn.amount IS NOT NULL;

-- ─────────────────────────────────────────────────────────────
-- 3. MERGE aliases into recipe."IngredientAlias"
-- ─────────────────────────────────────────────────────────────
MERGE INTO recipe."IngredientAlias" AS target
USING (
    SELECT
        i."Id" AS ingredient_id,
        sca.alias_name,
        sca.source_context
    FROM "Staging_Combined_Alias" sca
    JOIN recipe."Ingredient" i ON i."FdcId" = sca.fdc_id
) AS source
ON target."IngredientId" = source.ingredient_id
   AND target."AliasName" = source.alias_name
WHEN NOT MATCHED THEN
    INSERT ("IngredientId", "AliasName", "SourceContext", "CreatedDate")
    VALUES (source.ingredient_id, source.alias_name, source.source_context, NOW());

-- ─────────────────────────────────────────────────────────────
-- 4. MERGE packaging into reference."RetailPackaging"
--    Only inserts new patterns; does not overwrite existing seed data.
-- ─────────────────────────────────────────────────────────────
MERGE INTO reference."RetailPackaging" AS target
USING (
    SELECT
        ingredient_pattern AS "IngredientPattern",
        package_name AS "PackageName",
        package_size AS "PackageSize",
        package_size_unit AS "PackageSizeUnit",
        size_category AS "SizeCategory",
        size_in_base_units AS "SizeInBaseUnits",
        is_default AS "IsDefault",
        source AS "Source"
    FROM "Staging_Combined_Packaging"
) AS source
ON LOWER(target."IngredientPattern") = LOWER(source."IngredientPattern")
   AND target."PackageName" = source."PackageName"
WHEN NOT MATCHED THEN
    INSERT ("IngredientPattern", "PackageName", "PackageSize", "PackageSizeUnit",
            "SizeCategory", "SizeInBaseUnits", "IsDefault", "Source", "CreatedDate")
    VALUES (source."IngredientPattern", source."PackageName", source."PackageSize",
            source."PackageSizeUnit", source."SizeCategory", source."SizeInBaseUnits",
            source."IsDefault", source."Source", NOW());

-- ─────────────────────────────────────────────────────────────
-- 5. Create performance indexes (IF NOT EXISTS)
-- ─────────────────────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_ingredient_fdc_id ON recipe."Ingredient" ("FdcId");
CREATE INDEX IF NOT EXISTS idx_ingredient_name_normalized ON recipe."Ingredient" ("NameNormalized");
CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_ingredient ON nutrient."IngredientNutrient" ("IngredientId");
CREATE INDEX IF NOT EXISTS idx_ingredient_nutrient_nutrient ON nutrient."IngredientNutrient" ("NutrientId");
CREATE INDEX IF NOT EXISTS idx_ingredient_alias_ingredient ON recipe."IngredientAlias" ("IngredientId");

-- ─────────────────────────────────────────────────────────────
-- 6. Summary
-- ─────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_ingredients BIGINT;
    v_nutrients BIGINT;
    v_aliases BIGINT;
    v_packaging BIGINT;
BEGIN
    SELECT COUNT(*) INTO v_ingredients FROM recipe."Ingredient" WHERE "CurationStatusId" = 9000;
    SELECT COUNT(*) INTO v_nutrients FROM nutrient."IngredientNutrient";
    SELECT COUNT(*) INTO v_aliases FROM recipe."IngredientAlias";
    SELECT COUNT(*) INTO v_packaging FROM reference."RetailPackaging" WHERE "Source" = 'off-etl';

    RAISE NOTICE '══════════════════════════════════════════════';
    RAISE NOTICE '  COMBINED IMPORT COMPLETE';
    RAISE NOTICE '  Ingredients (non-curated): %', v_ingredients;
    RAISE NOTICE '  Nutrient values: %', v_nutrients;
    RAISE NOTICE '  Aliases: %', v_aliases;
    RAISE NOTICE '  Packaging (OFF-derived): %', v_packaging;
    RAISE NOTICE '══════════════════════════════════════════════';
END $$;
