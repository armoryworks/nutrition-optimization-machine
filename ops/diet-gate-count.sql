\if :{?names}
\else
\set names ''
\endif
\if :{?sample}
\else
\set sample 20
\endif
\if :{?seed}
\else
\set seed 'diet-gate-count'
\endif
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
BEGIN READ ONLY;
WITH consts AS (
    SELECT '\m(?:wines?|vins?|beers?|ales?|lager|stout|porter|pilsn?er|ipa|bock|guinness|ciders?|perry|scrumpy|vodka|rum|whiske?y|bourbon|scotch|brandy|brandies|cognac|armagnac|calvados|applejack|kirsch|kirschwasser|grappa|gin|hollands|tequila|mezcal|arrack|arak|pisco|everclear|eau|schnapps?|liqueurs?|liquor|sake|mirin|shaoxing|shaohsing|hsing|sherry|sack|port|madeira|marsala|tokay|tokaji|malaga|muscatel|moscatel|amontillado|fino|oloroso|manzanilla|sercial|malmsey|vermouth|champagne|prosecco|cava|sekt|asti|claret|burgundy|bordeaux|chablis|hocks?|rhenish|moselle|riesling|chardonnay|sauvignon|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot|sangiovese|gamay|grenache|tempranillo|barolo|valpolicella|beaujolais|sauternes?|muscadet|sancerre|soave|gew[uü]rztraminer|s[eé]millon|frascati|orvieto|catawba|ros[eé]|kahlua|baileys|bailey|cura[cç]ao|cura[cç]oa|cointreau|marnier|amaretto|frangelico|chartreuse|b[eé]n[eé]dictine|drambuie|limoncello|sambuca|anisette|ratafia|cassis|cr[eè]me|menthe|ouzo|pastis|absinthe|galliano|strega|advocaat|noyau|noyeau|maraschino|bitters|angostura|spirits?|alcohol|mead|eiswein|icewine)\M'::text AS drink,
           '\m(?:vinegars?|vinaigrettes?|extracts?|essences?|flavou?r\w*|yeasts?|juices?|root beer|ginger ale|ginger beer|birch beer|non-?alcoholic|alcohol-free|dealcoholi[sz]ed|cr[eè]me fra[iî]che|port salut)\M'::text AS not_drink
),
named AS (
    SELECT r."Id" AS ref_id, r."Name" AS type_name
    FROM reference."Reference" r
    WHERE NOT r."IsDeleted"
      AND ((:'names' = '' AND EXISTS (SELECT 1 FROM plan."RestrictionCriterion" c WHERE c."RestrictionTypeId" = r."Id" AND NOT c."IsDeleted"))
           OR r."Name" = ANY (string_to_array(:'names', ','))
           OR r."Id" IN (SELECT x."RestrictionTypeId" FROM plan."Restriction" x WHERE x."RestrictionTypeId" IS NOT NULL AND NOT x."IsDeleted"))
),
type_groups AS (
    SELECT min(ref_id) AS type_id, type_name, string_agg(ref_id::text, ',' ORDER BY ref_id) AS ref_ids
    FROM named
    GROUP BY type_name
),
types AS (
    SELECT n.ref_id, g.type_id, g.type_name
    FROM named n
    JOIN type_groups g ON g.type_name = n.type_name
),
all_crit AS (
    SELECT t.type_id, t.type_name, c."Severity" AS severity, c."NutrientId" AS nutrient_id,
           btrim(c."IngredientPattern") AS pattern,
           CASE WHEN c."NutrientId" IS NOT NULL THEN 'nutrient'
                WHEN left(btrim(c."IngredientPattern"), 2) = '\m' THEN 'word'
                ELSE 'like' END AS kind,
           coalesce(lower(btrim(n."Name")) = 'alcohol', false) AS alcohol,
           c."MaxAmountPerServing" IS NULL AS uncapped
    FROM types t
    JOIN plan."RestrictionCriterion" c ON c."RestrictionTypeId" = t.ref_id
    LEFT JOIN nutrient."Nutrient" n ON n."Id" = c."NutrientId"
    WHERE NOT c."IsDeleted"
      AND (c."NutrientId" IS NOT NULL
           OR (c."NutrientId" IS NULL AND btrim(coalesce(c."IngredientPattern", '')) <> ''))
),
crit AS (
    SELECT DISTINCT type_id, type_name, pattern, severity FROM all_crit WHERE kind = 'like'
),
word_crit AS (
    SELECT DISTINCT type_id, pattern FROM all_crit WHERE kind = 'word'
),
nut_crit AS (
    SELECT DISTINCT type_id, nutrient_id, alcohol, uncapped FROM all_crit WHERE kind = 'nutrient'
),
alcohol_types AS (
    SELECT DISTINCT type_id FROM nut_crit WHERE alcohol
),
pats AS (
    SELECT DISTINCT pattern FROM crit
),
word_pats AS (
    SELECT DISTINCT pattern FROM word_crit
),
by_name AS (
    SELECT p.pattern, i."Id" AS food
    FROM pats p
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ILIKE p.pattern
),
by_alias AS (
    SELECT p.pattern, a."IngredientId" AS food,
           coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                    OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS trusted
    FROM pats p
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ILIKE p.pattern
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
),
by_category AS (
    SELECT p.pattern, i."Id" AS food
    FROM pats p
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."FdcCategoryId" = 10
    WHERE lower(p.pattern) = '%pork%'
),
stored AS (
    SELECT DISTINCT n.type_id, x."IngredientId" AS food
    FROM nut_crit n
    JOIN nutrient."IngredientNutrient" x ON x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted"
),
word_name_hits AS (
    SELECT DISTINCT c.pattern, i."Id" AS food
    FROM word_pats c
    CROSS JOIN consts k
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ~* c.pattern
    WHERE NOT (i."Name" ~* k.drink AND i."Name" ~* k.not_drink)
),
word_name AS (
    SELECT DISTINCT c.type_id, h.food
    FROM word_crit c
    JOIN word_name_hits h ON h.pattern = c.pattern
),
word_alias_hits AS (
    SELECT DISTINCT c.pattern, a."IngredientId" AS food,
           a."AliasName" !~* k.drink AS counts_old,
           a."AliasName" !~* k.drink
               AND coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                            OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS counts_new
    FROM word_pats c
    CROSS JOIN consts k
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ~* c.pattern
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
),
word_alias AS (
    SELECT DISTINCT c.type_id, h.food, h.counts_old, h.counts_new
    FROM word_crit c
    JOIN word_alias_hits h ON h.pattern = c.pattern
),
drink_foods AS (
    SELECT i."Id" AS food, i."FdcCategoryId", i."Name"
    FROM recipe."Ingredient" i
    CROSS JOIN consts k
    WHERE NOT i."IsDeleted" AND i."Name" ~* k.drink AND i."Name" !~* k.not_drink
),
nutrient_foods AS (
    SELECT DISTINCT n.type_id, i."Id" AS food
    FROM nut_crit n
    JOIN nutrient."IngredientNutrient" x ON x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted" AND x."Amount" > 0.5
    JOIN recipe."Ingredient" i ON i."Id" = x."IngredientId" AND NOT i."IsDeleted"
    WHERE n.uncapped
      AND (NOT n.alcohol
       OR (i."FdcCategoryId" IS DISTINCT FROM 2 AND i."Name" !~* '\m(?:extracts?|essences?|flavou?r\w*)\M'))
    UNION
    SELECT n.type_id, d.food
    FROM nut_crit n
    JOIN drink_foods d ON true
    WHERE n.alcohol
      AND NOT EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" x WHERE x."IngredientId" = d.food AND x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted")
),
other_old AS (
    SELECT w.type_id, w.food FROM word_name w WHERE NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE w.counts_old AND NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT type_id, food FROM nutrient_foods
),
other_new AS (
    SELECT w.type_id, w.food FROM word_name w WHERE NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE w.counts_new AND NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT type_id, food FROM nutrient_foods
),
old_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name UNION SELECT pattern, food FROM by_alias) f ON f.pattern = c.pattern
    UNION
    SELECT type_id, food FROM other_old
),
new_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name
          UNION SELECT pattern, food FROM by_alias WHERE trusted
          UNION SELECT pattern, food FROM by_category) f ON f.pattern = c.pattern
    UNION
    SELECT type_id, food FROM other_new
),
alias_only AS (
    SELECT DISTINCT c.type_id, a.food
    FROM crit c
    JOIN by_alias a ON a.pattern = c.pattern AND NOT a.trusted
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE NOT w.counts_new
    EXCEPT
    SELECT type_id, food FROM new_foods
),
live AS (
    SELECT ri."RecipeId", ri."IngredientId", ri."RawLine", s."Name" = 'Approved' AS approved
    FROM recipe."RecipeIngredient" ri
    JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId" AND NOT r."IsDeleted"
    LEFT JOIN reference."Reference" s ON s."Id" = r."CurationStatusId"
    WHERE NOT ri."IsDeleted"
),
drink_recipes AS (
    SELECT DISTINCT l."RecipeId", l.approved
    FROM live l
    CROSS JOIN consts k
    WHERE EXISTS (SELECT 1 FROM alcohol_types)
      AND l."RawLine" ~* k.drink AND l."RawLine" !~* k.not_drink
),
drink_lines AS (
    SELECT t.type_id, d."RecipeId", d.approved
    FROM alcohol_types t
    CROSS JOIN drink_recipes d
),
old_recipes AS (
    SELECT DISTINCT o.type_id, l."RecipeId", l.approved
    FROM old_foods o JOIN live l ON l."IngredientId" = o.food
    UNION
    SELECT * FROM drink_lines
),
new_by_food AS (
    SELECT DISTINCT n.type_id, l."RecipeId", l.approved
    FROM new_foods n JOIN live l ON l."IngredientId" = n.food
),
touched AS (
    SELECT DISTINCT a.type_id, l."RecipeId"
    FROM alias_only a JOIN live l ON l."IngredientId" = a.food
),
touched_lines AS (
    SELECT DISTINCT l."RecipeId", l."RawLine", l.approved
    FROM live l
    WHERE l."RecipeId" IN (SELECT "RecipeId" FROM touched)
),
like_hits AS (
    SELECT DISTINCT p.pattern, tl."RecipeId", tl.approved
    FROM pats p
    JOIN touched_lines tl ON tl."RawLine" ILIKE p.pattern
),
word_hits AS (
    SELECT DISTINCT c.pattern, tl."RecipeId", tl.approved
    FROM word_pats c
    CROSS JOIN consts k
    JOIN touched_lines tl ON tl."RawLine" ~* c.pattern
    WHERE NOT (tl."RawLine" ~* k.drink AND tl."RawLine" ~* k.not_drink)
),
by_line AS (
    SELECT DISTINCT t.type_id, h."RecipeId", h.approved
    FROM touched t
    JOIN crit c ON c.type_id = t.type_id
    JOIN like_hits h ON h.pattern = c.pattern AND h."RecipeId" = t."RecipeId"
    UNION
    SELECT DISTINCT t.type_id, h."RecipeId", h.approved
    FROM touched t
    JOIN word_crit c ON c.type_id = t.type_id
    JOIN word_hits h ON h.pattern = c.pattern AND h."RecipeId" = t."RecipeId"
),
new_recipes AS (
    SELECT * FROM new_by_food UNION SELECT * FROM by_line UNION SELECT * FROM drink_lines
),
lost AS (
    SELECT * FROM old_recipes EXCEPT SELECT * FROM new_recipes
),
line_pat AS (
    SELECT p.pattern, l."RecipeId", l."IngredientId", l."RawLine", l.approved
    FROM pats p
    JOIN live l ON l."RawLine" ILIKE p.pattern
),
line_word_pats AS (
    SELECT DISTINCT pattern FROM word_crit WHERE type_id NOT IN (SELECT type_id FROM alcohol_types)
),
line_word AS (
    SELECT c.pattern, l."RecipeId", l."IngredientId", l."RawLine", l.approved
    FROM line_word_pats c
    CROSS JOIN consts k
    JOIN live l ON l."RawLine" ~* c.pattern
    WHERE NOT (l."RawLine" ~* k.drink AND l."RawLine" ~* k.not_drink)
),
any_line_hits AS (
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM crit c
    JOIN line_pat x ON x.pattern = c.pattern
    UNION
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM word_crit c
    JOIN line_word x ON x.pattern = c.pattern
    WHERE c.type_id NOT IN (SELECT type_id FROM alcohol_types)
),
any_line AS (
    SELECT DISTINCT type_id, "RecipeId", approved FROM any_line_hits
),
by_any_line AS (
    SELECT * FROM any_line EXCEPT SELECT * FROM new_recipes
),
gained AS (
    SELECT * FROM new_recipes EXCEPT SELECT * FROM old_recipes
),
counts AS (
    SELECT type_id, 'foods_before' AS k, count(*) AS n FROM old_foods GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_after', count(*) FROM new_foods GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_alias_only', count(*) FROM alias_only GROUP BY type_id
    UNION ALL SELECT type_id, 'recipes_before', count(*) FROM old_recipes GROUP BY type_id
    UNION ALL SELECT type_id, 'recipes_after', count(*) FROM new_recipes GROUP BY type_id
    UNION ALL SELECT type_id, 'kept_by_line', count(*) FROM by_line GROUP BY type_id
    UNION ALL SELECT type_id, 'lost', count(*) FROM lost GROUP BY type_id
    UNION ALL SELECT type_id, 'lost_approved', count(*) FROM lost WHERE approved GROUP BY type_id
    UNION ALL SELECT type_id, 'gained', count(*) FROM gained GROUP BY type_id
    UNION ALL SELECT type_id, 'kept_by_any_line', count(*) FROM any_line GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_any_line', count(*) FROM by_any_line GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_any_line_approved', count(*) FROM by_any_line WHERE approved GROUP BY type_id
),
severities AS (
    SELECT type_id, max(severity) AS severity FROM all_crit GROUP BY type_id
),
summary AS (
    SELECT g.type_id, g.type_name, g.ref_ids, v.severity,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_before'), 0) AS foods_before,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_after'), 0) AS foods_after,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_alias_only'), 0) AS foods_alias_only,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'recipes_before'), 0) AS recipes_before,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'recipes_after'), 0) AS recipes_after,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'kept_by_line'), 0) AS kept_by_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'lost'), 0) AS lost,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'lost_approved'), 0) AS lost_approved,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained'), 0) AS gained,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'kept_by_any_line'), 0) AS kept_by_any_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_any_line'), 0) AS gained_by_any_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_any_line_approved'), 0) AS gained_by_any_line_approved
    FROM type_groups g
    JOIN severities v ON v.type_id = g.type_id
    LEFT JOIN counts c ON c.type_id = g.type_id
    GROUP BY g.type_id, g.type_name, g.ref_ids, v.severity
),
lost_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT l."RawLine", ' | ') AS lines_on_alias_only_foods,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM lost x
    JOIN summary s ON s.type_id = x.type_id
    JOIN live l ON l."RecipeId" = x."RecipeId"
    JOIN alias_only a ON a.type_id = x.type_id AND a.food = l."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
),
line_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT h."RawLine" || ' [on ' || coalesce(i."Name", '#' || h."IngredientId") || ']', ' | ') AS lines,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM by_any_line x
    JOIN summary s ON s.type_id = x.type_id
    JOIN any_line_hits h ON h.type_id = x.type_id AND h."RecipeId" = x."RecipeId"
    LEFT JOIN recipe."Ingredient" i ON i."Id" = h."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
)
SELECT 'summary' AS kind, type_name, severity::text,
       concat_ws(' ', 'ids', ref_ids, 'foods', foods_before, '->', foods_after, '(alias only', foods_alias_only || ')',
                 'recipes', recipes_before, '->', recipes_after, '(kept by a line', kept_by_line || ')',
                 'lost', lost, '(approved', lost_approved || ')', 'gained', gained,
                 'any-line', kept_by_any_line, 'gained by any line', gained_by_any_line, '(approved', gained_by_any_line_approved || ')') AS detail
FROM summary
UNION ALL
SELECT 'lost', type_name, severity::text, "RecipeId" || ': ' || lines_on_alias_only_foods
FROM lost_sample
WHERE n <= :sample AND severity >= 4
UNION ALL
SELECT 'line', type_name, severity::text, "RecipeId" || ': ' || lines
FROM line_sample
WHERE n <= :sample AND severity >= 4
ORDER BY 1 DESC, 2, 4;
ROLLBACK;
