WITH used AS (
    SELECT i."Name" AS name, count(*) AS lines
    FROM recipe."Ingredient" i
    JOIN recipe."RecipeIngredient" ri ON ri."IngredientId" = i."Id" AND NOT ri."IsDeleted"
    WHERE NOT i."IsDeleted" AND i."CurationStatusId" <> 9003
    GROUP BY i."Name"
),
strata AS (
    SELECT name, lines,
           CASE
               WHEN name ~ '^\s*[0-9]' THEN 'leading-quantity'
               WHEN name ~* '^(and|or|of|with|plus|half|the)\M' THEN 'leading-word-residue'
               WHEN name ~* '\m(cups?|cupful|tablespoons?|teaspoons?|spoonful|ounces?|pounds?|pints?)\M' THEN 'unit-word-inside'
               WHEN name ~ '\(' THEN 'parenthetical'
               WHEN name ~* '\m(and|or)\M|&' THEN 'alternatives-or-compound'
               WHEN name ~ ',' THEN 'comma-prep'
               WHEN name ~* '^(chopped|minced|diced|sliced|grated|fresh|dried|melted|softened|beaten|cold|hot|warm)\M' THEN 'leading-prep-state'
               WHEN name ~ '[^\x01-\x7F]' THEN 'non-ascii'
               ELSE 'plain'
           END AS stratum
    FROM used
),
ranked AS (
    SELECT *, row_number() OVER (PARTITION BY stratum ORDER BY lines DESC, name) AS by_use,
              row_number() OVER (PARTITION BY stratum ORDER BY md5(:'seed' || name)) AS by_seed
    FROM strata
)
SELECT 'cand-' || substr(md5(name), 1, 10) AS id, stratum, lines, replace(name, E'\t', ' ') AS name,
       '' AS normalized, '' AS bare, '' AS residue, 'new' AS status, 'capture:wordings.sql' AS source, '' AS note
FROM ranked
WHERE by_use <= 30 OR by_seed <= 15
ORDER BY stratum, lines DESC;
