WITH sized AS (
    SELECT replace(ri."RawLine", E'\t', ' ') AS line,
           i."FdcId" AS fdc_id,
           CASE WHEN coalesce(r."SourceSite", '') <> '' THEN 'modern' ELSE coalesce(e."Era", 'unknown') END AS era,
           ri."Quantity" AS quantity,
           m."Name" AS unit,
           coalesce(r."LicenseStatus", '') AS licence
    FROM recipe."RecipeIngredient" ri
    JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId" AND NOT r."IsDeleted"
    JOIN recipe."Ingredient" i ON i."Id" = ri."IngredientId"
    JOIN measurement."Measurement" m ON m."Id" = ri."MeasurementId"
    LEFT JOIN recipe."RecipeSourceEra" e ON e."SourceAttribution" = r."SourceAttribution" AND NOT e."IsDeleted"
    WHERE NOT ri."IsDeleted"
      AND i."FdcId" IS NOT NULL
      AND i."FdcDataType" IN ('sr_legacy_food', 'foundation_food')
      AND m."MeasurementCategoryId" IN (1, 2)
      AND ri."RawLine" ~* '\m(cans?|tins?|jars?|bottles?|box(es)?|bags?|packets?|pouch(es)?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?)\M'
      AND ri."RawLine" ~ '\(\s*[0-9]'
),
ranked AS (
    SELECT *, row_number() OVER (PARTITION BY era, fdc_id ORDER BY md5(:'seed' || line)) AS n
    FROM sized
)
SELECT 'h-' || substr(md5(line || fdc_id), 1, 10) AS id, line, era, fdc_id, quantity, unit, licence
FROM ranked
WHERE n <= 5
ORDER BY era, fdc_id, n
LIMIT 400;
