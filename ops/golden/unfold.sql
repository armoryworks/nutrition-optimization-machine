WITH cited(id) AS (
    VALUES (34986::bigint), (61682), (60811), (98371), (93258), (54546), (36088), (61606), (70179), (33036),
           (61764), (31922), (63051), (83024), (23963), (40675), (35658), (40785), (94292), (126506), (141734)
),
sampled AS (
    SELECT DISTINCT ri."RecipeId" AS id
    FROM recipe."RecipeIngredient" ri
    JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId"
    WHERE NOT ri."IsDeleted" AND r."ScrapedAtUtc" IS NOT NULL AND ri."RawLine" LIKE '% + %'
      AND (ri."LineKind" IS NULL OR ri."LineKind" = 'ingredient')
),
chosen AS (
    SELECT id, 'cited' AS origin FROM cited
    UNION ALL
    (SELECT id, 'sampled' FROM sampled WHERE id NOT IN (SELECT id FROM cited) ORDER BY md5(:'seed' || id) LIMIT 40)
)
SELECT json_build_object(
    'recipe', c.id,
    'origin', c.origin,
    'licence', coalesce(r."LicenseStatus", 'Unknown'),
    'lines', (SELECT json_agg(ri."RawLine" ORDER BY ri."SortOrder", ri."IngredientId") FROM recipe."RecipeIngredient" ri
              WHERE ri."RecipeId" = c.id AND NOT ri."IsDeleted" AND ri."RawLine" LIKE '% + %'
                AND (ri."LineKind" IS NULL OR ri."LineKind" = 'ingredient')),
    'sources', (SELECT json_agg(d."RawJsonLd") FROM recipe."ScrapedDocument" d WHERE d."RecipeId" = c.id AND NOT d."IsDeleted")
)
FROM chosen c JOIN recipe."Recipe" r ON r."Id" = c.id
ORDER BY c.origin, c.id;
