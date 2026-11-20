WITH segments AS (
    SELECT btrim(split_part(ri."RawLine", ' + ', 1)) AS raw, count(*) AS lines
    FROM recipe."RecipeIngredient" ri
    WHERE NOT ri."IsDeleted" AND ri."RawLine" <> '' AND (ri."LineKind" IS NULL OR ri."LineKind" = 'ingredient')
    GROUP BY 1
),
strata AS (
    SELECT raw, lines,
           CASE
               WHEN raw ~ '&[a-z]+;' THEN 'html-entity'
               WHEN raw ~* '\m(grammi|cucchia|q\.?b\.?|bicchier)' THEN 'italian'
               WHEN raw ~ '^\s*[0-9]+\s*\([^)]*[0-9][^)]*\)' THEN 'pack-parenthetical'
               WHEN raw ~ '^\s*[0-9/.]+\s*(-|to)\s*[0-9]' THEN 'range'
               WHEN raw ~ '[¼½¾⅓⅔⅛⅜⅝⅞⁄]' THEN 'unicode-fraction'
               WHEN raw ~ '^\s*[0-9]+\s+[0-9]+/[0-9]+|^\s*[0-9]+/[0-9]+' THEN 'ascii-fraction'
               WHEN raw ~* '^\s*[0-9.]+\s*(g|kg|ml|l|oz|lb)\M' THEN 'attached-metric'
               WHEN raw ~ '^\s*[0-9]' THEN 'leading-number'
               WHEN raw ~* '^\s*(a|an|one|two|three|four|five|six|half|quarter|a few|several)\M' THEN 'number-word'
               WHEN raw ~* '\m(one|two|three|four|half|quarter)\M' THEN 'inner-number-word'
               WHEN raw ~ '[0-9]' THEN 'trailing-or-inner-number'
               ELSE 'no-number'
           END AS stratum
    FROM segments
),
ranked AS (
    SELECT *, row_number() OVER (PARTITION BY stratum ORDER BY lines DESC, raw) AS by_use,
              row_number() OVER (PARTITION BY stratum ORDER BY md5(:'seed' || raw)) AS by_seed
    FROM strata
)
SELECT 'cand-' || substr(md5(raw), 1, 10) AS id, stratum, replace(raw, E'\t', ' ') AS raw,
       '' AS quantity, '' AS measurement, '' AS size, '' AS wording, '' AS bare,
       'new' AS parse, 'new' AS name, 'capture:lines.sql' AS source, lines || ' lines' AS note
FROM ranked
WHERE by_use <= 10 OR by_seed <= 4
ORDER BY stratum, lines DESC;
