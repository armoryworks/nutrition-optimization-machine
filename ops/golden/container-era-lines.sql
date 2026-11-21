SELECT 'l-' || ri."RecipeId" || '-' || ri."IngredientId" || '-' || ri."SortOrder" AS id,
       replace(r."SourceAttribution", E'\t', ' ') AS book,
       coalesce(e."Era", 'unknown') AS era,
       coalesce(i."FdcId", '#' || i."Id") AS "fdcId",
       replace(ri."RawLine", E'\t', ' ') AS line,
       ri."Quantity" AS quantity,
       m."Name" AS unit,
       coalesce(i."GramsPerMilliliter"::text, '-') AS density
FROM recipe."RecipeIngredient" ri
JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId" AND NOT r."IsDeleted"
JOIN recipe."Ingredient" i ON i."Id" = ri."IngredientId"
JOIN measurement."Measurement" m ON m."Id" = ri."MeasurementId"
LEFT JOIN recipe."RecipeSourceEra" e ON e."SourceAttribution" = r."SourceAttribution" AND NOT e."IsDeleted"
WHERE NOT ri."IsDeleted"
  AND coalesce(r."SourceSite", '') = ''
  AND coalesce(r."SourceAttribution", '') <> ''
  AND coalesce(e."Era", 'unknown') IN ('historic', 'unknown')
  AND m."MeasurementCategoryId" IN (1, 2)
  AND ri."RawLine" ~* '\m(cans?|tins?|jars?|bottles?|box(es)?|bags?|packets?|pouch(es)?|sachets?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\M'
ORDER BY ri."RecipeId", ri."SortOrder";
