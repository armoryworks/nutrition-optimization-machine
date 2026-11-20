\if :{?limit}
\else
\set limit 2000
\endif
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
WITH eligible AS (
    SELECT r."Id"
    FROM recipe."Recipe" r
    WHERE NOT r."IsDeleted"
      AND r."CurationStatusId" = 9000
      AND r."Visibility" = 4
      AND r."DateCurationCompleted" IS NULL
      AND r."VettingIssues" IS NULL
      AND NOT r."ContainsSourceProse"
      AND (r."LicenseStatus" = 'PublicDomain'
           OR (r."LicenseStatus" = 'Unknown' AND r."ScrapedAtUtc" IS NOT NULL AND coalesce(r."Image", '') <> ''))
),
blockers AS (
    SELECT DISTINCT ri."RecipeId", ri."IngredientId"
    FROM recipe."RecipeIngredient" ri
    JOIN eligible e ON e."Id" = ri."RecipeId"
    JOIN recipe."Ingredient" i ON i."Id" = ri."IngredientId"
    WHERE NOT ri."IsDeleted"
      AND i."CurationStatusId" <> 9003
      AND (ri."LineKind" IS NULL OR ri."LineKind" NOT IN ('reference', 'equipment', 'note', 'instruction', 'artefact'))
      AND ri."Quantity" > 0
),
sole AS (
    SELECT min(b."IngredientId") AS "IngredientId", b."RecipeId"
    FROM blockers b
    GROUP BY b."RecipeId"
    HAVING count(*) = 1
),
unresolved AS (
    SELECT i."Id", lower(i."Name") AS wording
    FROM recipe."Ingredient" i
    WHERE NOT i."IsDeleted"
      AND i."FdcId" IS NULL
      AND i."CurationStatusId" <> 9003
      AND NOT EXISTS (SELECT 1 FROM recipe."IngredientWording" w WHERE NOT w."IsDeleted" AND w."Wording" = lower(i."Name"))
),
usage AS (
    SELECT ri."IngredientId",
           count(*) AS lines,
           count(DISTINCT ri."RecipeId") AS recipes,
           count(*) FILTER (WHERE strpos(lower(ri."RawLine"), u.wording) = 0) AS off_name
    FROM recipe."RecipeIngredient" ri
    JOIN unresolved u ON u."Id" = ri."IngredientId"
    JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId"
    WHERE NOT ri."IsDeleted" AND NOT r."IsDeleted" AND r."ScrapedAtUtc" IS NOT NULL
      AND (ri."LineKind" IS NULL OR ri."LineKind" = 'ingredient')
    GROUP BY ri."IngredientId"
),
sole_counts AS (
    SELECT s."IngredientId", count(*) AS recipes
    FROM sole s
    GROUP BY s."IngredientId"
),
ranked AS (
    SELECT u."Id", u.wording, us.lines, us.recipes, us.off_name, coalesce(sc.recipes, 0) AS sole_blocker
    FROM unresolved u
    JOIN usage us ON us."IngredientId" = u."Id"
    LEFT JOIN sole_counts sc ON sc."IngredientId" = u."Id"
    ORDER BY sole_blocker DESC, us.lines DESC, u."Id"
    LIMIT :limit
)
SELECT row_number() OVER (ORDER BY k.sole_blocker DESC, k.lines DESC, k."Id") AS rank,
       k."Id" AS ingredient_id,
       k.wording,
       k.sole_blocker AS sole_blocker_recipes,
       k.lines,
       k.recipes,
       k.off_name,
       (SELECT string_agg(replace(replace(x.line, E'\t', ' '), E'\n', ' '), ' ¦ ' ORDER BY x.n DESC, x.line)
        FROM (SELECT ri."RawLine" AS line, count(*) AS n
              FROM recipe."RecipeIngredient" ri
              WHERE ri."IngredientId" = k."Id" AND NOT ri."IsDeleted"
              GROUP BY ri."RawLine"
              ORDER BY count(*) DESC, ri."RawLine"
              LIMIT 3) x) AS samples,
       (SELECT p."ProposedValue" || '|' || coalesce(t."Name", '') || '|' || p."Source"
        FROM curation."FoodCatalogProposal" p
        LEFT JOIN recipe."Ingredient" t ON t."Id"::text = p."ProposedValue"
        WHERE p."IngredientId" = k."Id" AND p."Field" = 'fdc_link' AND p."Status" = 1 AND NOT p."IsDeleted"
        ORDER BY p."Id" DESC
        LIMIT 1) AS pending_pick
FROM ranked k
ORDER BY rank;
