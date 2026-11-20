WITH unmeasured AS (
    SELECT btrim(ri."RawLine") AS raw, count(*) AS lines
    FROM recipe."RecipeIngredient" ri
    WHERE NOT ri."IsDeleted" AND ri."Quantity" <= 0 AND ri."RawLine" <> ''
    GROUP BY 1
),
lookalikes AS (
    SELECT raw, lines,
           CASE
               WHEN raw ~* '\m(pan|mould|mold|sieve|cloth|paper|skewer|tin|basin)s?\M' THEN 'equipment-like'
               WHEN raw ~ ':\s*$' THEN 'heading-like'
               WHEN raw ~* '\m(serving|servings|portion|portions|garnish)\M' THEN 'note-like'
               WHEN raw ~ ' \+ ' THEN 'merge-like'
               ELSE NULL
           END AS stratum
    FROM unmeasured
),
ranked AS (
    SELECT *, row_number() OVER (PARTITION BY stratum ORDER BY md5(:'seed' || raw)) AS n
    FROM lookalikes
    WHERE stratum IS NOT NULL
)
SELECT 'cand-' || substr(md5(raw), 1, 10) AS id, replace(raw, E'\t', ' ') AS raw, '' AS kind, 'new' AS status,
       'capture:line-kinds.sql' AS source, stratum || ', ' || lines || ' lines' AS note
FROM ranked
WHERE n <= 50
ORDER BY stratum, raw;
