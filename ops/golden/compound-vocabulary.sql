SELECT 'alias' AS kind, a."IngredientId" AS id, btrim(replace(a."AliasName", E'\t', ' ')) AS name, a."SourceContext" AS context
FROM recipe."IngredientAlias" a
JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId"
WHERE NOT a."IsDeleted" AND NOT i."IsDeleted" AND i."CurationStatusId" = 9003
  AND (a."SourceContext" = 'fdc:description' OR a."SourceContext" LIKE 'retired:%')
UNION ALL
SELECT 'known', i."Id", btrim(replace(i."Name", E'\t', ' ')), '-'
FROM recipe."Ingredient" i
WHERE NOT i."IsDeleted"
ORDER BY 1, 2, 3;
