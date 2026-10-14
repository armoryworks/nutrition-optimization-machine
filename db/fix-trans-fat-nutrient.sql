BEGIN;

INSERT INTO nutrient."Nutrient" ("Name", "Description", "DefaultMeasurementId", "ParentNutrientId", "CreatedDate", "CreatedByPersonId", "IsDeleted")
SELECT 'Trans Fat', 'Trans fatty acids (FDC 1257).', g."Id",
       (SELECT f."Id" FROM nutrient."Nutrient" f WHERE f."Name" = 'Fat' ORDER BY f."Id" LIMIT 1),
       now(), 1, false
FROM measurement."Measurement" g
WHERE g."Name" = 'Gram' AND g."IngredientId" IS NULL
  AND NOT EXISTS (SELECT 1 FROM nutrient."Nutrient" n WHERE lower(n."Name") = 'trans fat')
ORDER BY g."Id"
LIMIT 1;

COMMIT;
