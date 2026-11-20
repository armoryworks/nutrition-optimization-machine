SELECT DISTINCT lower(btrim(p."CurrentValue")) AS wording, p."FdcId" AS "fdcId"
FROM curation."FoodCatalogProposal" p
WHERE p."Field" = 'fdc_link' AND p."Source" = 'deterministic:name-match' AND p."Status" IN (2, 4) AND NOT p."IsDeleted"
ORDER BY 1, 2;
