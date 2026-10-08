BEGIN;

INSERT INTO recipe."Ingredient" ("Name", "PluralName", "FdcDataType", "NameNormalized", "PluralNameNormalized", "CurationStatusId", "AuthorId", "OnHand", "CreatedDate", "CreatedByPersonId", "IsDeleted")
VALUES
    ('Orzo', 'Orzo', '', 'orzo', 'orzo', 9003, 1, false, now(), 1, false),
    ('Brussels Sprouts', 'Brussels Sprouts', '', 'brussels sprouts', 'brussels sprouts', 9003, 1, false, now(), 1, false),
    ('Zucchini', 'Zucchini', '', 'zucchini', 'zucchini', 9003, 1, false, now(), 1, false),
    ('Raisins', 'Raisins', '', 'raisins', 'raisins', 9003, 1, false, now(), 1, false),
    ('Cottage Cheese', 'Cottage Cheese', '', 'cottage cheese', 'cottage cheese', 9003, 1, false, now(), 1, false),
    ('Rice Cakes', 'Rice Cakes', '', 'rice cakes', 'rice cakes', 9003, 1, false, now(), 1, false),
    ('Popcorn', 'Popcorn', '', 'popcorn', 'popcorn', 9003, 1, false, now(), 1, false)
ON CONFLICT ("Name") DO NOTHING;

UPDATE recipe."RecipeIngredient" AS ri
SET "IngredientId" = COALESCE(i."Id", ri."IngredientId"),
    "Quantity" = v.new_qty,
    "MeasurementId" = v.new_measurement,
    "RawLine" = v.new_raw
FROM (VALUES
    (262, 148, 12, 3, '12 oz spaghetti', NULL, 12, 7, '12 oz spaghetti'),
    (268, 148, 12, 3, '12 oz spaghetti', NULL, 12, 7, '12 oz spaghetti'),
    (269, 148, 10, 3, '10 oz spaghetti', NULL, 10, 7, '10 oz spaghetti'),
    (275, 162, 8, 3, '8 oz cremini mushrooms', NULL, 8, 7, '8 oz cremini mushrooms'),
    (277, 148, 8, 3, '8 oz orzo pasta', 'Orzo', 8, 7, '8 oz orzo pasta'),
    (290, 146, 4, 11, '4 cups halved Brussels sprouts (broccoli)', 'Brussels Sprouts', 4, 11, '4 cups halved Brussels sprouts'),
    (307, 174, 4, 3, '4 rice cakes (crackers)', 'Rice Cakes', 4, 3, '4 rice cakes'),
    (292, 133, 4, 3, '4 medium zucchini (cucumber), sliced lengthwise', 'Zucchini', 4, 3, '4 medium zucchini, sliced lengthwise'),
    (303, 171, 2, 12, '2 tbsp dark chocolate chips', 'Raisins', 2, 12, '2 tbsp raisins'),
    (306, 111, 1, 11, '1 cup Greek yogurt', 'Cottage Cheese', 1, 11, '1 cup cottage cheese'),
    (308, 170, 2, 11, '2 cups rolled oats', 'Popcorn', 8, 11, '8 cups popped popcorn')
) AS v(recipe_id, old_ingredient, old_qty, old_measurement, old_raw, new_ingredient, new_qty, new_measurement, new_raw)
LEFT JOIN recipe."Ingredient" AS i ON i."Name" = v.new_ingredient
WHERE ri."RecipeId" = v.recipe_id
  AND ri."IngredientId" = v.old_ingredient
  AND ri."Quantity" = v.old_qty
  AND ri."MeasurementId" = v.old_measurement
  AND ri."RawLine" = v.old_raw
  AND (v.new_ingredient IS NULL OR i."Id" IS NOT NULL);

UPDATE recipe."Recipe" AS r
SET "Description" = v.new_description
FROM (VALUES
    (303, 'Celery sticks filled with peanut butter and topped with sweet raisins (dark chocolate chips as a substitute). A fun and wholesome snack loved by all ages.', 'Celery sticks filled with peanut butter and topped with sweet raisins. A fun and wholesome snack loved by all ages.'),
    (306, 'Creamy Greek yogurt topped with fresh blueberries, sliced banana, chia seeds, and a drizzle of honey. Light, protein-rich, and refreshing.', 'Creamy cottage cheese topped with fresh blueberries, sliced banana, chia seeds, and a drizzle of honey. Light, protein-rich, and refreshing.'),
    (308, 'Light popcorn (using rolled oats as a base) tossed with melted butter, parmesan cheese, and a sprinkle of paprika. A savory, crunchy snack.', 'Light popcorn tossed with melted butter, parmesan cheese, and a sprinkle of paprika. A savory, crunchy snack.')
) AS v(id, old_description, new_description)
WHERE r."Id" = v.id
  AND r."Description" = v.old_description;

UPDATE recipe."RecipeStep" AS s
SET "Summary" = COALESCE(v.new_summary, s."Summary"),
    "Description" = v.new_description
FROM (VALUES
    (277, 1, NULL, NULL, 'Bring a pot of salted water to a boil. Cook the spaghetti (or orzo pasta) until al dente. Drain and rinse under cold water to stop cooking. Toss with a drizzle of olive oil to prevent sticking.', 'Bring a pot of salted water to a boil. Cook the orzo until al dente. Drain and rinse under cold water to stop cooking. Toss with a drizzle of olive oil to prevent sticking.'),
    (290, 1, NULL, NULL, 'Preheat oven to 425°F. Trim and halve the Brussels sprouts (using broccoli florets as a substitute base). Toss with olive oil, salt, and black pepper.', 'Preheat oven to 425°F. Trim and halve the Brussels sprouts. Toss with olive oil, salt, and black pepper.'),
    (292, 1, NULL, NULL, 'Slice the cucumber lengthwise into 1/4-inch thick planks. Brush both sides with olive oil and season with minced garlic, salt, and pepper.', 'Slice the zucchini lengthwise into 1/4-inch thick planks. Brush both sides with olive oil and season with minced garlic, salt, and pepper.'),
    (303, 2, NULL, NULL, 'Press dark chocolate chips into the peanut butter along the length of each celery stick. Arrange on a plate and serve.', 'Press raisins into the peanut butter along the length of each celery stick. Arrange on a plate and serve.'),
    (306, 1, NULL, NULL, 'Spoon Greek yogurt into a bowl. Top with sliced banana, blueberries, and chia seeds.', 'Spoon cottage cheese into a bowl. Top with sliced banana, blueberries, and chia seeds.'),
    (307, 1, NULL, NULL, 'Spread peanut butter evenly over each rice cake (using crackers as the base). Top with sliced banana rounds.', 'Spread peanut butter evenly over each rice cake. Top with sliced banana rounds.'),
    (308, 1, 'Toast the oats', 'Pop the popcorn', 'In a dry skillet over medium heat, toast rolled oats for 3-4 minutes, stirring frequently, until golden and fragrant.', 'Pop the popcorn in an air popper or a covered pot over medium heat, then transfer it to a large bowl.'),
    (308, 2, NULL, NULL, 'Drizzle melted butter over the toasted oats. Sprinkle with grated parmesan and paprika. Toss until evenly coated and serve warm.', 'Drizzle melted butter over the warm popcorn. Sprinkle with grated parmesan and paprika. Toss until evenly coated and serve warm.')
) AS v(recipe_id, step_number, old_summary, new_summary, old_description, new_description)
WHERE s."RecipeId" = v.recipe_id
  AND s."StepNumber" = v.step_number
  AND s."Description" = v.old_description
  AND (v.old_summary IS NULL OR s."Summary" = v.old_summary);

UPDATE recipe."RecipeNutrition" AS n
SET "Amount" = CASE n."NutrientId" WHEN 5000 THEN v.seeded_fat ELSE v.seeded_protein END
FROM (VALUES
    (200, 3.5, 5.5),
    (201, 5, 11),
    (202, 12, 17),
    (203, 2.5, 7.5),
    (204, 24, 2),
    (205, 6, 10),
    (206, 4, 14),
    (208, 20, 9),
    (209, 8, 9.5),
    (210, 4, 22),
    (211, 4, 11),
    (212, 3.5, 3),
    (213, 6.5, 1.5),
    (215, 7, 18),
    (216, 7, 14),
    (217, 2.5, 7.5),
    (218, 5, 8.5),
    (219, 3, 10),
    (221, 22, 15),
    (222, 26, 25),
    (224, 20, 10),
    (225, 14, 26),
    (226, 28, 16),
    (228, 40, 22),
    (229, 38, 20),
    (230, 30, 16),
    (231, 15, 14),
    (232, 38, 18),
    (233, 38, 22),
    (234, 14, 18),
    (235, 36, 28),
    (236, 18, 24),
    (237, 32, 22),
    (238, 28, 18),
    (240, 42, 22),
    (241, 30, 22),
    (242, 30, 22),
    (243, 34, 18),
    (244, 32, 22),
    (245, 28, 20),
    (246, 28, 22),
    (247, 30, 16),
    (248, 35, 14),
    (249, 12, 14),
    (250, 42, 16),
    (251, 36, 20),
    (252, 34, 24),
    (253, 36, 12),
    (254, 38, 16),
    (255, 28, 20),
    (256, 36, 16),
    (257, 34, 20),
    (258, 40, 20),
    (259, 28, 10),
    (260, 4, 0.5),
    (261, 4, 3.5),
    (263, 2.5, 7),
    (264, 6, 12),
    (265, 7, 6),
    (266, 6, 8),
    (267, 4.5, 5),
    (268, 16, 18),
    (269, 10, 8),
    (270, 3, 10),
    (271, 5, 12),
    (272, 5, 3.5),
    (273, 8, 9),
    (274, 5, 8),
    (276, 3, 5),
    (278, 12, 10),
    (279, 9, 3),
    (280, 5, 10),
    (281, 4, 10),
    (282, 3, 7),
    (283, 3, 4),
    (284, 1.5, 3.5),
    (285, 2.5, 5),
    (286, 1, 5),
    (287, 3, 3.5),
    (288, 1.5, 5),
    (289, 1.5, 5),
    (290, 3, 7),
    (291, 2.5, 5),
    (292, 1.5, 7),
    (293, 6, 10),
    (294, 2.5, 17),
    (295, 7, 16),
    (296, 15, 4),
    (297, 10, 16),
    (298, 4, 7),
    (299, 12, 3),
    (300, 2.5, 1.5),
    (301, 5, 8),
    (302, 5, 7),
    (303, 7, 14),
    (304, 4.5, 6),
    (305, 5, 8),
    (306, 18, 3),
    (307, 7, 10),
    (309, 7, 3),
    (310, 4, 26),
    (311, 4, 16),
    (312, 3, 24),
    (313, 2.5, 4),
    (314, 3, 7),
    (315, 4, 10),
    (316, 1.5, 6),
    (317, 6, 4),
    (318, 7, 24),
    (319, 3, 8)
) AS v(recipe_id, seeded_protein, seeded_fat)
WHERE n."RecipeId" = v.recipe_id
  AND n."DateCalculated" IS NULL
  AND n."NutrientId" IN (5000, 5006)
  AND EXISTS (SELECT 1 FROM recipe."RecipeNutrition" f WHERE f."RecipeId" = v.recipe_id AND f."NutrientId" = 5000 AND f."Amount" = v.seeded_protein AND f."DateCalculated" IS NULL)
  AND EXISTS (SELECT 1 FROM recipe."RecipeNutrition" p WHERE p."RecipeId" = v.recipe_id AND p."NutrientId" = 5006 AND p."Amount" = v.seeded_fat AND p."DateCalculated" IS NULL);

DELETE FROM recipe."RecipeNutrition" AS n
USING (VALUES
    (200, 5),
    (201, 10.5),
    (202, 16),
    (203, 7),
    (204, 1.5),
    (205, 9),
    (206, 13.5),
    (207, 6.5),
    (208, 8),
    (209, 9),
    (210, 20),
    (211, 10),
    (212, 2.5),
    (213, 1),
    (214, 10),
    (215, 17),
    (216, 13),
    (217, 7),
    (218, 8),
    (219, 9),
    (260, 0.5),
    (280, 10),
    (281, 10),
    (282, 7),
    (283, 4),
    (284, 3.5),
    (285, 5),
    (286, 5),
    (287, 3.5),
    (288, 5),
    (289, 5),
    (290, 7),
    (291, 5),
    (292, 7),
    (293, 10),
    (294, 17),
    (295, 16),
    (296, 4),
    (297, 16),
    (298, 7),
    (299, 3),
    (300, 1.5),
    (301, 8),
    (302, 7),
    (303, 14),
    (304, 6),
    (305, 8),
    (306, 3),
    (307, 10),
    (308, 5),
    (309, 3),
    (310, 26),
    (311, 16),
    (312, 24),
    (313, 4),
    (314, 7),
    (315, 10),
    (316, 6),
    (317, 4),
    (318, 24),
    (319, 8)
) AS v(recipe_id, seeded_amount)
WHERE n."RecipeId" = v.recipe_id
  AND n."NutrientId" = 5001
  AND n."DateCalculated" IS NULL
  AND n."Amount" = v.seeded_amount;

COMMIT;
