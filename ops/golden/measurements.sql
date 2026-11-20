SELECT m."Id" AS id, m."Name" AS name, m."MeasurementCategoryId" AS category,
       coalesce(m."BaseUnitConversionFactor"::text, '-') AS factor,
       CASE WHEN m."IsBaseUnit" THEN 'true' ELSE 'false' END AS "isBase",
       coalesce(to_jsonb(m) ->> 'IsContainer', CASE WHEN m."Name" IN ('Can', 'Package') THEN 'true' ELSE 'false' END) AS "isContainer"
FROM measurement."Measurement" m
WHERE m."MeasurementType" = 'Base'
  AND (m."Id" IN (SELECT DISTINCT ri."MeasurementId" FROM recipe."RecipeIngredient" ri WHERE NOT ri."IsDeleted") OR m."Name" = 'Package')
ORDER BY m."Id";
