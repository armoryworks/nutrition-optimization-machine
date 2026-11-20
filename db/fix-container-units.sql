BEGIN;

INSERT INTO measurement."Measurement" ("Name", "Description", "Symbol", "MeasurementCategoryId", "IsBaseUnit", "BaseUnitConversionFactor", "MeasurementType", "CreatedDate", "CreatedByPersonId", "IsDeleted")
SELECT u."Name", u."Description", u."Symbol", u."CategoryId", false, u."Factor", 'Base', now(), 1, false
FROM (VALUES
    ('Package', 'A single package, jar, bottle, box or bag', 'pkg', 3::bigint, 1.000000::numeric)
) AS u("Name", "Description", "Symbol", "CategoryId", "Factor")
WHERE NOT EXISTS (
    SELECT 1 FROM measurement."Measurement" m
    WHERE lower(m."Name") = lower(u."Name") AND m."MeasurementCategoryId" = u."CategoryId"
);

UPDATE measurement."Measurement"
SET "IsContainer" = true
WHERE "MeasurementCategoryId" = 3 AND "Name" IN ('Can', 'Package') AND "IsContainer" = false;

COMMIT;
