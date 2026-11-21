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
WITH types AS (
    SELECT r."Id" AS type_id, r."Name" AS type_name
    FROM reference."Reference" r
    WHERE NOT r."IsDeleted"
      AND (r."Name" = ANY (string_to_array(:'names', ','))
           OR r."Id" IN (SELECT x."RestrictionTypeId" FROM plan."Restriction" x WHERE x."RestrictionTypeId" IS NOT NULL AND NOT x."IsDeleted"))
),
crit AS (
    SELECT t.type_id, t.type_name, btrim(c."IngredientPattern") AS pattern, c."Severity" AS severity
    FROM types t
    JOIN plan."RestrictionCriterion" c ON c."RestrictionTypeId" = t.type_id
    WHERE NOT c."IsDeleted" AND c."NutrientId" IS NULL AND c."IngredientPattern" IS NOT NULL
      AND btrim(c."IngredientPattern") <> '' AND left(btrim(c."IngredientPattern"), 2) <> '\m'
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
old_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name UNION SELECT pattern, food FROM by_alias) f ON f.pattern = c.pattern
),
new_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name
          UNION SELECT pattern, food FROM by_alias WHERE trusted
          UNION SELECT pattern, food FROM by_category) f ON f.pattern = c.pattern
),
alias_only AS (
    SELECT DISTINCT c.type_id, a.food
    FROM crit c
    JOIN by_alias a ON a.pattern = c.pattern AND NOT a.trusted
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
old_recipes AS (
    SELECT DISTINCT o.type_id, l."RecipeId", l.approved
    FROM old_foods o JOIN live l ON l."IngredientId" = o.food
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
),
new_recipes AS (
    SELECT * FROM new_by_food UNION SELECT * FROM by_line
),
lost AS (
    SELECT * FROM old_recipes EXCEPT SELECT * FROM new_recipes
),
gained AS (
    SELECT * FROM new_recipes EXCEPT SELECT * FROM old_recipes
),
summary AS (
    SELECT t.type_id, t.type_name,
           (SELECT max(c.severity) FROM crit c WHERE c.type_id = t.type_id) AS severity,
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
    WHERE EXISTS (SELECT 1 FROM crit c WHERE c.type_id = t.type_id)
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
