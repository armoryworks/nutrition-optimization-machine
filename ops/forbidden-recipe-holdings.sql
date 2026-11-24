\pset footer off
BEGIN READ ONLY;
WITH host AS (
    SELECT regexp_replace(lower(s."Domain"), '^www\.', '') AS bare
    FROM recipe."ScrapingSource" s
    WHERE s."Kind" = 'web' AND s."IsDeleted" = false
    GROUP BY regexp_replace(lower(s."Domain"), '^www\.', '')
    HAVING bool_and(s."RightsClass" = 'forbidden' AND s."PermissionRecordedAtUtc" IS NULL)
), taken AS (
    SELECT h.bare, r."Id", r."IsDeleted", r."CurationStatusId"
    FROM host h
    JOIN recipe."Recipe" r
      ON r."SourceUrl" LIKE 'https://' || h.bare || '/%' OR r."SourceUrl" LIKE 'http://' || h.bare || '/%'
      OR r."SourceUrl" LIKE 'https://www.' || h.bare || '/%' OR r."SourceUrl" LIKE 'http://www.' || h.bare || '/%'
), approval AS (
    SELECT f."EntityId", f."AdminId"
    FROM curation."CurationFeedback" f
    JOIN reference."Reference" et ON et."Id" = f."EntityTypeId" AND et."Name" = 'Recipe' AND NOT et."IsDeleted"
    JOIN reference."Reference" ft ON ft."Id" = f."FeedbackTypeId" AND ft."Name" = 'Approval' AND NOT ft."IsDeleted"
    WHERE NOT f."IsDeleted"
)
SELECT t.bare AS host,
       count(*) AS recipes,
       count(*) FILTER (WHERE t."IsDeleted") AS already_deleted,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM recipe."RecipeRemoval" x WHERE x."RecipeId" = t."Id" AND x."UndoneAtUtc" IS NULL)) AS removed,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM plan."MealPlan" m WHERE m."RecipeId" = t."Id" AND NOT m."IsDeleted")) AS in_meal_plans,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM recipe."RecipeRating" x WHERE x."RecipeId" = t."Id" AND NOT x."IsDeleted" AND x."Rating" >= 4)) AS favourited,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM plan."HouseholdCookbookRecipe" x WHERE x."RecipeId" = t."Id" AND NOT x."IsDeleted")) AS in_cookbooks,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM shopping."ShoppingListItem" x WHERE x."RecipeId" = t."Id" AND NOT x."IsDeleted")) AS in_shopping_lists,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM audit."AuditLogEntry" a WHERE a."EntityId" = t."Id"
                        AND ((a."EntityType" = 'RecipeStep' AND a."ChangeType" = 'PersonEditBase')
                          OR (a."EntityType" = 'Recipe' AND a."ChangeType" = 'ProseDescriptionByPerson')))) AS edited_by_person,
       count(*) FILTER (WHERE t."CurationStatusId" = 9003) AS approved,
       count(*) FILTER (WHERE t."CurationStatusId" = 9003
                        AND EXISTS (SELECT 1 FROM approval f WHERE f."EntityId" = t."Id" AND f."AdminId" <> 1)) AS approved_by_person,
       count(*) FILTER (WHERE t."CurationStatusId" = 9003
                        AND NOT EXISTS (SELECT 1 FROM approval f WHERE f."EntityId" = t."Id")) AS approved_unrecorded
FROM taken t
GROUP BY ROLLUP (t.bare)
ORDER BY t.bare NULLS LAST;
ROLLBACK;
