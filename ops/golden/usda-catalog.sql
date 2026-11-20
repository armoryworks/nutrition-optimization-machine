SELECT i."Id" AS id,
       i."FdcId" AS "fdcId",
       coalesce(i."FdcDataType", '') AS "dataType",
       btrim(replace(i."Name", E'\t', ' ')) AS name,
       coalesce((SELECT btrim(replace(a."AliasName", E'\t', ' ')) FROM recipe."IngredientAlias" a
                 WHERE a."IngredientId" = i."Id" AND NOT a."IsDeleted" AND a."SourceContext" = 'fdc:description'
                 ORDER BY a."Id" LIMIT 1), '-') AS description
FROM recipe."Ingredient" i
WHERE i."FdcId" IS NOT NULL AND NOT i."IsDeleted" AND coalesce(i."FdcDataType", '') <> 'branded_food'
ORDER BY i."Id";
