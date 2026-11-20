WITH curated AS (
    SELECT lower(btrim(i."Name")) AS name
    FROM recipe."Ingredient" i
    WHERE NOT i."IsDeleted" AND i."CurationStatusId" = 9003
      AND EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" n WHERE n."IngredientId" = i."Id" AND NOT n."IsDeleted")
),
named AS (
    SELECT DISTINCT ON (lower(btrim(ri."RawLine")))
           'veto-' || substr(md5(lower(btrim(ri."RawLine"))), 1, 10) AS id,
           btrim(ri."RawLine") AS raw,
           'ingredient' AS kind,
           'rule:names-a-curated-food' AS "judgedBy",
           'labelled ' || ri."LineKind" || ' on staging; the line is exactly a curated food''s name' AS note
    FROM recipe."RecipeIngredient" ri
    JOIN curated c ON c.name = lower(btrim(ri."RawLine"))
    WHERE NOT ri."IsDeleted" AND ri."Quantity" <= 0 AND ri."LineKind" IS NOT NULL AND ri."LineKind" <> 'ingredient'
    ORDER BY lower(btrim(ri."RawLine")), ri."RecipeId"
),
prepass AS (
    SELECT DISTINCT ON (lower(btrim(split_part(a."NewValue", ': ', 2))))
           'prepass-' || substr(md5(lower(btrim(split_part(a."NewValue", ': ', 2)))), 1, 10) AS id,
           btrim(split_part(a."NewValue", ': ', 2)) AS raw,
           split_part(a."NewValue", ' ', 1) AS kind,
           'rule:deterministic-pre-pass' AS "judgedBy",
           'answered by the deterministic pre-pass on staging' AS note
    FROM audit."AuditLogEntry" a
    WHERE a."ChangeType" = 'LineClassify' AND a."NewValue" LIKE '% (deterministic): %'
      AND split_part(a."NewValue", ' ', 1) IN ('equipment', 'note')
      AND btrim(split_part(a."NewValue", ': ', 2)) <> ''
    ORDER BY lower(btrim(split_part(a."NewValue", ': ', 2))), a."Id"
)
(SELECT id, '-' AS recipe, raw, kind, "judgedBy", note FROM named ORDER BY md5(:'seed' || raw) LIMIT 120)
UNION ALL
(SELECT id, '-' AS recipe, raw, kind, "judgedBy", note FROM prepass ORDER BY md5(:'seed' || raw) LIMIT 60);
