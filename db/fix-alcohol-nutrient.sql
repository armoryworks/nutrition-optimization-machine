BEGIN;

INSERT INTO nutrient."Nutrient" ("Name", "Description", "DefaultMeasurementId", "ParentNutrientId", "CreatedDate", "CreatedByPersonId", "IsDeleted")
SELECT 'Alcohol', 'Ethyl alcohol (FDC 1018).', g."Id", NULL, now(), 1, false
FROM measurement."Measurement" g
WHERE g."Name" = 'Gram' AND g."IngredientId" IS NULL
  AND NOT EXISTS (SELECT 1 FROM nutrient."Nutrient" n WHERE lower(n."Name") = 'alcohol')
ORDER BY g."Id"
LIMIT 1;

COMMIT;
