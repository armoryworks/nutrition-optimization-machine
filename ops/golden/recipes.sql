WITH cited(id, origin) AS (
    VALUES (62963::bigint, 'cited'), (67498, 'cited'), (41537, 'cited'), (42114, 'cited'), (61142, 'cited'),
           (37855, 'cited'), (33365, 'cited'), (78498, 'cited'), (273, 'cited')
),
curated AS (
    SELECT r."Id" AS id, coalesce(CASE WHEN r."Servings" > 0 THEN r."Servings"::numeric END, CASE WHEN r."RecipeServings" > 0 THEN r."RecipeServings" END) AS servings,
           EXISTS (SELECT 1 FROM recipe."RecipeNutrition" n WHERE n."RecipeId" = r."Id" AND NOT n."IsDeleted" AND n."DateCalculated" IS NULL) AS hand,
           (SELECT count(*) FROM recipe."RecipeIngredient" ri JOIN measurement."Measurement" m ON m."Id" = ri."MeasurementId"
             WHERE ri."RecipeId" = r."Id" AND NOT ri."IsDeleted" AND ri."Quantity" > 0 AND m."MeasurementCategoryId" = 3) AS counts
    FROM recipe."Recipe" r
    WHERE NOT r."IsDeleted" AND r."CurationStatusId" = 9003
      AND EXISTS (SELECT 1 FROM recipe."RecipeNutrition" n WHERE n."RecipeId" = r."Id" AND NOT n."IsDeleted")
),
sampled AS (
    (SELECT id, 'mass-volume' AS origin FROM curated WHERE NOT hand AND servings > 1 AND counts = 0 ORDER BY md5(:'seed' || id) LIMIT 12)
    UNION ALL
    (SELECT id, 'count-heavy' FROM curated WHERE NOT hand AND servings > 1 AND counts >= 3 ORDER BY md5(:'seed' || id) LIMIT 8)
    UNION ALL
    (SELECT id, 'servings-unknown' FROM curated WHERE NOT hand AND servings IS NULL ORDER BY md5(:'seed' || id) LIMIT 4)
    UNION ALL
    (SELECT id, 'hand-label' FROM curated WHERE hand ORDER BY md5(:'seed' || id) LIMIT 2)
),
chosen AS (
    SELECT id, origin FROM cited
    UNION ALL
    SELECT s.id, s.origin FROM sampled s WHERE s.id NOT IN (SELECT id FROM cited)
)
SELECT json_build_object(
    'id', r."Id",
    'origin', c.origin,
    'licence', coalesce(r."LicenseStatus", 'Unknown'),
    'name', r."Name",
    'servings', r."Servings",
    'recipeServings', r."RecipeServings",
    'lines', (SELECT coalesce(json_agg(json_build_object(
                  'sort', ri."SortOrder",
                  'qty', ri."Quantity",
                  'unit', m."Name",
                  'kind', ri."LineKind",
                  'raw', ri."RawLine",
                  'food', json_build_object(
                      'id', i."Id",
                      'name', i."Name",
                      'curation', i."CurationStatusId",
                      'gramsPerItem', i."GramsPerItem",
                      'gramsPerMl', i."GramsPerMilliliter",
                      'referenceServingGrams', i."ReferenceServingGrams",
                      'hasNutrition', EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" n WHERE n."IngredientId" = i."Id" AND NOT n."IsDeleted"),
                      'per100', (SELECT json_object_agg(n."NutrientId", n."Amount") FROM nutrient."IngredientNutrient" n
                                 WHERE n."IngredientId" = i."Id" AND NOT n."IsDeleted" AND n."NutrientId" IN (5035, 5006, 5000)))
              ) ORDER BY ri."SortOrder", ri."IngredientId"), '[]'::json)
              FROM recipe."RecipeIngredient" ri
              JOIN recipe."Ingredient" i ON i."Id" = ri."IngredientId"
              JOIN measurement."Measurement" m ON m."Id" = ri."MeasurementId"
              WHERE ri."RecipeId" = r."Id" AND NOT ri."IsDeleted"),
    'stored', (SELECT json_object_agg(n."NutrientId", json_build_object('amount', n."Amount", 'calculated', n."DateCalculated" IS NOT NULL))
               FROM recipe."RecipeNutrition" n WHERE n."RecipeId" = r."Id" AND NOT n."IsDeleted" AND n."NutrientId" IN (5035, 5006, 5000))
)
FROM chosen c JOIN recipe."Recipe" r ON r."Id" = c.id
ORDER BY c.origin, r."Id";
