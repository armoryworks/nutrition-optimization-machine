BEGIN;

INSERT INTO measurement."Measurement" ("Name", "Description", "Symbol", "MeasurementCategoryId", "IsBaseUnit", "BaseUnitConversionFactor", "MeasurementType", "CreatedDate", "CreatedByPersonId", "IsDeleted")
SELECT u."Name", u."Description", u."Symbol", u."CategoryId", false, u."Factor", 'Base', now(), 1, false
FROM (VALUES
    ('Clove', 'A single clove, as of garlic', 'clove', 3::bigint, 1.000000::numeric),
    ('Stalk', 'A single stalk, as of celery', 'stalk', 3::bigint, 1.000000::numeric),
    ('Slice', 'A single slice', 'slice', 3::bigint, 1.000000::numeric),
    ('Can', 'A single can or tin', 'can', 3::bigint, 1.000000::numeric),
    ('Pinch', '1/16 teaspoon', 'pinch', 2::bigint, 0.308058::numeric)
) AS u("Name", "Description", "Symbol", "CategoryId", "Factor")
WHERE NOT EXISTS (
    SELECT 1 FROM measurement."Measurement" m
    WHERE lower(m."Name") = lower(u."Name") AND m."MeasurementCategoryId" = u."CategoryId"
);

COMMIT;
