SELECT i."Id" AS id,
       coalesce(i."FdcId", '-') AS "fdcId",
       coalesce(nullif(i."FdcDataType", ''), '-') AS "dataType",
       CASE i."CurationStatusId" WHEN 9003 THEN 'curated' WHEN 9001 THEN 'pending' ELSE i."CurationStatusId"::text END AS "status",
       btrim(replace(i."Name", E'\t', ' ')) AS name,
       coalesce((SELECT btrim(replace(a."AliasName", E'\t', ' ')) FROM recipe."IngredientAlias" a
                 WHERE a."IngredientId" = i."Id" AND NOT a."IsDeleted" AND a."SourceContext" = 'fdc:description'
                 ORDER BY a."Id" LIMIT 1), '-') AS description,
       coalesce(i."FdcCategoryId"::text, '-') AS "categoryId",
       coalesce(nullif(btrim(replace(i."AdmissionReason", E'\t', ' ')), ''), '-') AS admission,
       (SELECT count(*) FROM nutrient."IngredientNutrient" n WHERE n."IngredientId" = i."Id" AND NOT n."IsDeleted") AS nutrients
FROM recipe."Ingredient" i
WHERE NOT i."IsDeleted"
  AND (i."CurationStatusId" = 9003
       OR (i."CurationStatusId" = 9001 AND i."FdcDataType" IN ('sr_legacy_food', 'foundation_food') AND i."FdcId" IS NOT NULL))
ORDER BY i."Id";
