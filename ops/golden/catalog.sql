SELECT i."Id" AS id,
       btrim(replace(i."Name", E'\t', ' ')) AS name,
       CASE WHEN EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" n WHERE n."IngredientId" = i."Id" AND NOT n."IsDeleted") THEN 'true' ELSE 'false' END AS "hasNutrition",
       CASE WHEN i."GramsPerItem" > 0 THEN 'true' ELSE 'false' END AS "countedInPieces"
FROM recipe."Ingredient" i
WHERE NOT i."IsDeleted" AND i."CurationStatusId" = 9003
ORDER BY i."Id";
