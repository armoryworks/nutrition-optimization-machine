\if :{?names}
\else
\set names 'Halal,Kosher,Whole30,Paleo,Vegan,Vegetarian,Gluten-Free,Dairy-Free,Celiac Disease,Gluten Sensitivity,Lactose Intolerance,Lactose-Intolerant,Milk Allergy,Egg Allergy,Peanut Allergy,Tree Nut Allergy,Nut Allergy,Shellfish Allergy,Fish Allergy,Soy Allergy,Wheat Allergy,Sesame Allergy'
\endif
\if :{?sample}
\else
\set sample 20
\endif
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
BEGIN READ ONLY;
WITH consts AS (
    SELECT '\m(?:wines?|vins?|beers?|ales?|lager|stout|porter|pilsn?er|ipa|bock|guinness|ciders?|perry|scrumpy|vodka|rum|whiske?y|bourbon|scotch|brandy|brandies|cognac|armagnac|calvados|applejack|kirsch|kirschwasser|grappa|gin|hollands|tequila|mezcal|arrack|arak|pisco|everclear|eau|schnapps?|liqueurs?|liquor|sake|mirin|shaoxing|shaohsing|hsing|sherry|sack|port|madeira|marsala|tokay|tokaji|malaga|muscatel|moscatel|amontillado|fino|oloroso|manzanilla|sercial|malmsey|vermouth|champagne|prosecco|cava|sekt|asti|claret|burgundy|bordeaux|chablis|hocks?|rhenish|moselle|riesling|chardonnay|sauvignon|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot|sangiovese|gamay|grenache|tempranillo|barolo|valpolicella|beaujolais|sauternes?|muscadet|sancerre|soave|gew[uü]rztraminer|s[eé]millon|frascati|orvieto|catawba|ros[eé]|kahlua|baileys|bailey|cura[cç]ao|cura[cç]oa|cointreau|marnier|amaretto|frangelico|chartreuse|b[eé]n[eé]dictine|drambuie|limoncello|sambuca|anisette|ratafia|cassis|cr[eè]me|menthe|ouzo|pastis|absinthe|galliano|strega|advocaat|noyau|noyeau|maraschino|bitters|angostura|spirits?|alcohol|mead|eiswein|icewine)\M'::text AS drink,
           '\m(?:vinegars?|vinaigrettes?|extracts?|essences?|flavou?r\w*|yeasts?|juices?|root beer|ginger ale|ginger beer|birch beer|non-?alcoholic|alcohol-free|dealcoholi[sz]ed|cr[eè]me fra[iî]che|port salut)\M'::text AS not_drink
),
types AS (
    SELECT r."Id" AS type_id, r."Name" AS type_name
    FROM reference."Reference" r
    WHERE NOT r."IsDeleted"
      AND (r."Name" = ANY (string_to_array(:'names', ','))
           OR r."Id" IN (SELECT x."RestrictionTypeId" FROM plan."Restriction" x WHERE x."RestrictionTypeId" IS NOT NULL AND NOT x."IsDeleted"))
),
all_crit AS (
    SELECT t.type_id, t.type_name, c."Severity" AS severity, c."NutrientId" AS nutrient_id,
           btrim(c."IngredientPattern") AS pattern,
           CASE WHEN c."NutrientId" IS NOT NULL THEN 'nutrient'
                WHEN left(btrim(c."IngredientPattern"), 2) = '\m' THEN 'word'
                ELSE 'like' END AS kind,
           coalesce(lower(btrim(n."Name")) = 'alcohol', false) AS alcohol
    FROM types t
    JOIN plan."RestrictionCriterion" c ON c."RestrictionTypeId" = t.type_id
    LEFT JOIN nutrient."Nutrient" n ON n."Id" = c."NutrientId"
    WHERE NOT c."IsDeleted"
      AND ((c."NutrientId" IS NOT NULL AND c."MaxAmountPerServing" IS NULL)
           OR (c."NutrientId" IS NULL AND btrim(coalesce(c."IngredientPattern", '')) <> ''))
),
crit AS (
    SELECT type_id, type_name, pattern, severity FROM all_crit WHERE kind = 'like'
),
word_crit AS (
    SELECT type_id, pattern FROM all_crit WHERE kind = 'word'
),
nut_crit AS (
    SELECT DISTINCT type_id, nutrient_id, alcohol FROM all_crit WHERE kind = 'nutrient'
),
alcohol_types AS (
    SELECT DISTINCT type_id FROM nut_crit WHERE alcohol
),
pats AS (
    SELECT DISTINCT pattern FROM crit
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
word_name AS (
    SELECT DISTINCT c.type_id, i."Id" AS food
    FROM word_crit c
    CROSS JOIN consts k
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ~* c.pattern
    WHERE NOT (i."Name" ~* k.drink AND i."Name" ~* k.not_drink)
),
word_alias AS (
    SELECT DISTINCT c.type_id, a."IngredientId" AS food,
           a."AliasName" !~* k.drink AS counts_old,
           a."AliasName" !~* k.drink
               AND coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                            OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS counts_new
    FROM word_crit c
    CROSS JOIN consts k
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ~* c.pattern
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
),
nutrient_foods AS (
    SELECT DISTINCT n.type_id, i."Id" AS food
    FROM nut_crit n
    JOIN nutrient."IngredientNutrient" x ON x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted" AND x."Amount" > 0.5
    JOIN recipe."Ingredient" i ON i."Id" = x."IngredientId" AND NOT i."IsDeleted"
    WHERE NOT n.alcohol
       OR (i."FdcCategoryId" IS DISTINCT FROM 2 AND i."Name" !~* '\m(?:extracts?|essences?|flavou?r\w*)\M')
    UNION
    SELECT n.type_id, i."Id"
    FROM nut_crit n
    CROSS JOIN consts k
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ~* k.drink AND i."Name" !~* k.not_drink
    WHERE n.alcohol
      AND NOT EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" x WHERE x."IngredientId" = i."Id" AND x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted")
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
drink_lines AS (
    SELECT DISTINCT t.type_id, l."RecipeId", l.approved
    FROM alcohol_types t
    CROSS JOIN consts k
    JOIN live l ON l."RawLine" ~* k.drink AND l."RawLine" !~* k.not_drink
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
by_line AS (
    SELECT DISTINCT t.type_id, l."RecipeId", l.approved
    FROM touched t
    JOIN live l ON l."RecipeId" = t."RecipeId"
    JOIN crit c ON c.type_id = t.type_id AND l."RawLine" ILIKE c.pattern
    UNION
    SELECT DISTINCT t.type_id, l."RecipeId", l.approved
    FROM touched t
    CROSS JOIN consts k
    JOIN live l ON l."RecipeId" = t."RecipeId"
    JOIN word_crit c ON c.type_id = t.type_id AND l."RawLine" ~* c.pattern
    WHERE NOT (l."RawLine" ~* k.drink AND l."RawLine" ~* k.not_drink)
),
new_recipes AS (
    SELECT * FROM new_by_food UNION SELECT * FROM by_line UNION SELECT * FROM drink_lines
),
lost AS (
    SELECT * FROM old_recipes EXCEPT SELECT * FROM new_recipes
),
gained AS (
    SELECT * FROM new_recipes EXCEPT SELECT * FROM old_recipes
),
summary AS (
    SELECT t.type_id, t.type_name,
           (SELECT max(c.severity) FROM all_crit c WHERE c.type_id = t.type_id) AS severity,
           (SELECT count(*) FROM old_foods x WHERE x.type_id = t.type_id) AS foods_before,
           (SELECT count(*) FROM new_foods x WHERE x.type_id = t.type_id) AS foods_after,
           (SELECT count(*) FROM alias_only x WHERE x.type_id = t.type_id) AS foods_alias_only,
           (SELECT count(*) FROM old_recipes x WHERE x.type_id = t.type_id) AS recipes_before,
           (SELECT count(*) FROM new_recipes x WHERE x.type_id = t.type_id) AS recipes_after,
           (SELECT count(*) FROM by_line x WHERE x.type_id = t.type_id) AS kept_by_line,
           (SELECT count(*) FROM lost x WHERE x.type_id = t.type_id) AS lost,
           (SELECT count(*) FROM lost x WHERE x.type_id = t.type_id AND x.approved) AS lost_approved,
           (SELECT count(*) FROM gained x WHERE x.type_id = t.type_id) AS gained
    FROM types t
    WHERE EXISTS (SELECT 1 FROM crit c WHERE c.type_id = t.type_id) OR EXISTS (SELECT 1 FROM word_crit c WHERE c.type_id = t.type_id)
),
lost_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT l."RawLine", ' | ') AS lines_on_alias_only_foods,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, x."RecipeId") AS n
    FROM lost x
    JOIN summary s ON s.type_id = x.type_id
    JOIN live l ON l."RecipeId" = x."RecipeId"
    JOIN alias_only a ON a.type_id = x.type_id AND a.food = l."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
)
SELECT 'summary' AS kind, type_name, severity::text,
       concat_ws(' ', 'foods', foods_before, '->', foods_after, '(alias only', foods_alias_only || ')',
                 'recipes', recipes_before, '->', recipes_after, '(kept by a line', kept_by_line || ')',
                 'lost', lost, '(approved', lost_approved || ')', 'gained', gained) AS detail
FROM summary
UNION ALL
SELECT 'lost', type_name, severity::text, "RecipeId" || ': ' || lines_on_alias_only_foods
FROM lost_sample
WHERE n <= :sample AND severity >= 4
ORDER BY 1 DESC, 2, 4;
ROLLBACK;
