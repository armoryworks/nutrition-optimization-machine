BEGIN;

INSERT INTO reference."Reference" ("Id", "Name", "Description", "CreatedDate", "CreatedByPersonId", "IsDeleted")
VALUES
    (60085, 'Gluten-Free', 'Excludes all gluten-containing grains (wheat, barley, rye)', now(), 1, false),
    (60086, 'Dairy-Free', 'Excludes all dairy products (milk, cheese, yogurt)', now(), 1, false)
ON CONFLICT DO NOTHING;

INSERT INTO reference."ReferenceIndex" ("ReferenceId", "GroupId")
VALUES (60085, 6005), (60086, 6005)
ON CONFLICT DO NOTHING;

UPDATE reference."Reference"
SET "Description" = 'Autoimmune disorder triggered by gluten (also spelled coeliac)'
WHERE "Id" = 60800 AND "Description" = 'Autoimmune disorder triggered by gluten';

COMMIT;
