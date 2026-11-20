WITH candidates AS (
    SELECT r."Id" AS id
    FROM recipe."Recipe" r
    WHERE NOT r."IsDeleted" AND r."LicenseStatus" = 'PublicDomain'
      AND (SELECT count(*) FROM recipe."RecipeStep" s WHERE s."RecipeId" = r."Id" AND NOT s."IsDeleted") BETWEEN 2 AND 8
      AND EXISTS (SELECT 1 FROM recipe."RecipeStep" s WHERE s."RecipeId" = r."Id" AND NOT s."IsDeleted" AND s."Description" ~ '\m[0-9]{2,3}\M')
)
SELECT json_build_object(
    'recipe', r."Id",
    'description', coalesce(r."Description", ''),
    'steps', (SELECT json_agg(s."Description" ORDER BY s."StepNumber") FROM recipe."RecipeStep" s WHERE s."RecipeId" = r."Id" AND NOT s."IsDeleted")
)
FROM candidates c JOIN recipe."Recipe" r ON r."Id" = c.id
ORDER BY md5(:'seed' || c.id)
LIMIT 12;
