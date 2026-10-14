import { categorizeDepartment, DEPARTMENT_ORDER, DEPARTMENT_ICONS } from './departments';
import { shelfLifeDaysFor, SHELF_LIFE_DEFAULTS } from './shelf-life';

const CASES: [string, string][] = [
  ['Eggs', 'Dairy & Eggs'],
  ['Eggs [12 ct dozen]', 'Dairy & Eggs'],
  ['Egg, whole, raw, fresh', 'Dairy & Eggs'],
  ['Black Beans [15 oz can]', 'Canned & Jarred'],
  ['Beans, black, mature seeds, canned', 'Canned & Jarred'],
  ['Black Beans', 'Grains & Pasta'],
  ['Cremini Mushrooms', 'Produce'],
  ['Mushrooms, white, raw', 'Produce'],
  ['Blueberry', 'Produce'],
  ['Blueberries', 'Produce'],
  ['Carrots', 'Produce'],
  ['Peppers, bell, red, raw', 'Produce'],
  ['Raisins', 'Nuts & Dried Fruit'],
  ['Sugars, granulated', 'Baking'],
  ['Black Pepper [1.5 oz grinder]', 'Spices & Seasonings'],
  ['Spices, pepper, black', 'Spices & Seasonings'],
  ['Peanut Butter', 'Canned & Jarred'],
  ['Almond Milk', 'Beverages'],
  ['Oat milk', 'Beverages'],
  ['Coconut milk', 'Canned & Jarred'],
  ['boneless chicken breast', 'Meat & Seafood'],
  ['Chicken, broilers or fryers, breast, meat only, raw', 'Meat & Seafood'],
  ['Beef, ground, 85% lean meat / 15% fat, raw', 'Meat & Seafood'],
  ['Ground turkey', 'Meat & Seafood'],
  ['Pork chops', 'Meat & Seafood'],
  ['Bacon', 'Meat & Seafood'],
  ['Italian sausage', 'Meat & Seafood'],
  ['Pepperoni', 'Meat & Seafood'],
  ['Salmon fillets', 'Meat & Seafood'],
  ['Shrimp', 'Meat & Seafood'],
  ['Tuna, light, canned in oil', 'Canned & Jarred'],
  ['whole milk', 'Dairy & Eggs'],
  ['Heavy cream', 'Dairy & Eggs'],
  ['Sour cream', 'Dairy & Eggs'],
  ['Half-and-half', 'Dairy & Eggs'],
  ['Buttermilk', 'Dairy & Eggs'],
  ['Greek yogurt', 'Dairy & Eggs'],
  ['Cheese, cheddar', 'Dairy & Eggs'],
  ['Parmesan cheese', 'Dairy & Eggs'],
  ['Feta', 'Dairy & Eggs'],
  ['Cream cheese', 'Dairy & Eggs'],
  ['Butter, salted', 'Dairy & Eggs'],
  ['Unsalted butter', 'Dairy & Eggs'],
  ['roma tomato', 'Produce'],
  ['Cherry tomatoes', 'Produce'],
  ['Onions, yellow, raw', 'Produce'],
  ['Green onions', 'Produce'],
  ['Garlic', 'Produce'],
  ['Potatoes', 'Produce'],
  ['Sweet potatoes', 'Produce'],
  ['Baby spinach', 'Produce'],
  ['Kale', 'Produce'],
  ['Broccoli', 'Produce'],
  ['Zucchini', 'Produce'],
  ['Avocados', 'Produce'],
  ['Bananas', 'Produce'],
  ['Apples', 'Produce'],
  ['Lemons', 'Produce'],
  ['Lemon juice', 'Produce'],
  ['Strawberries', 'Produce'],
  ['Grapes', 'Produce'],
  ['peach', 'Produce'],
  ['Peaches', 'Produce'],
  ['Fresh basil', 'Produce'],
  ['Cilantro', 'Produce'],
  ['Ginger root', 'Produce'],
  ['Jalapeños', 'Produce'],
  ['Red pepper', 'Produce'],
  ['Brussels sprouts', 'Produce'],
  ['Asparagus', 'Produce'],
  ['Green beans', 'Produce'],
  ['Sugar snap peas', 'Produce'],
  ['Spaghetti squash', 'Produce'],
  ['Butternut squash', 'Produce'],
  ['Corn, sweet, yellow, raw', 'Produce'],
  ['Eggplant', 'Produce'],
  ['Mustard greens', 'Produce'],
  ['Cauliflower rice', 'Produce'],
  ['spaghetti', 'Grains & Pasta'],
  ['Egg noodles', 'Grains & Pasta'],
  ['Brown rice', 'Grains & Pasta'],
  ['Quinoa', 'Grains & Pasta'],
  ['Rolled oats', 'Grains & Pasta'],
  ['Couscous', 'Grains & Pasta'],
  ['Lentils', 'Grains & Pasta'],
  ['Chickpeas', 'Grains & Pasta'],
  ['Chickpeas [15 oz can]', 'Canned & Jarred'],
  ['Split peas', 'Grains & Pasta'],
  ['Corn tortillas', 'Bakery'],
  ['Hamburger buns', 'Bakery'],
  ['Whole wheat bread', 'Bakery'],
  ['Bagels', 'Bakery'],
  ['Chicken broth', 'Canned & Jarred'],
  ['Cream of mushroom soup', 'Canned & Jarred'],
  ['Diced tomatoes', 'Canned & Jarred'],
  ['Tomato paste', 'Canned & Jarred'],
  ['Tomatoes, red, ripe, canned', 'Canned & Jarred'],
  ['Marinara sauce', 'Canned & Jarred'],
  ['Salsa', 'Canned & Jarred'],
  ['Kalamata olives', 'Canned & Jarred'],
  ['Roasted red peppers', 'Canned & Jarred'],
  ['Strawberry jam', 'Canned & Jarred'],
  ['Applesauce', 'Canned & Jarred'],
  ['Soy sauce', 'Condiments & Sauces'],
  ['fish sauce', 'Condiments & Sauces'],
  ['Oyster sauce', 'Condiments & Sauces'],
  ['Dijon mustard', 'Condiments & Sauces'],
  ['Mayonnaise', 'Condiments & Sauces'],
  ['Barbecue sauce', 'Condiments & Sauces'],
  ['Honey', 'Condiments & Sauces'],
  ['Maple syrup', 'Condiments & Sauces'],
  ['Salt', 'Spices & Seasonings'],
  ['Kosher salt', 'Spices & Seasonings'],
  ['Pepper', 'Spices & Seasonings'],
  ['Crushed red pepper flakes', 'Spices & Seasonings'],
  ['smoked paprika', 'Spices & Seasonings'],
  ['Ground cumin', 'Spices & Seasonings'],
  ['Ground ginger', 'Spices & Seasonings'],
  ['Garlic powder', 'Spices & Seasonings'],
  ['Cinnamon', 'Spices & Seasonings'],
  ['Dried oregano', 'Spices & Seasonings'],
  ['Bay leaves', 'Spices & Seasonings'],
  ['Italian seasoning', 'Spices & Seasonings'],
  ['Sesame seeds', 'Spices & Seasonings'],
  ['olive oil', 'Oils & Vinegars'],
  ['Extra virgin olive oil', 'Oils & Vinegars'],
  ['Coconut oil', 'Oils & Vinegars'],
  ['Cooking spray', 'Oils & Vinegars'],
  ['Balsamic vinegar', 'Oils & Vinegars'],
  ['Apple cider vinegar', 'Oils & Vinegars'],
  ['Rice vinegar', 'Oils & Vinegars'],
  ['all-purpose flour', 'Baking'],
  ['Almond flour', 'Baking'],
  ['Brown sugar', 'Baking'],
  ['Powdered sugar', 'Baking'],
  ['Baking powder', 'Baking'],
  ['Baking soda', 'Baking'],
  ['Cornstarch', 'Baking'],
  ['Vanilla extract', 'Baking'],
  ['Cocoa powder', 'Baking'],
  ['Chocolate chips', 'Baking'],
  ['Panko breadcrumbs', 'Baking'],
  ['Active dry yeast', 'Baking'],
  ['Almonds', 'Nuts & Dried Fruit'],
  ['Walnuts', 'Nuts & Dried Fruit'],
  ['Peanuts', 'Nuts & Dried Fruit'],
  ['Pine nuts', 'Nuts & Dried Fruit'],
  ['Chia seeds', 'Nuts & Dried Fruit'],
  ['Sunflower seeds', 'Nuts & Dried Fruit'],
  ['Dried cranberries', 'Nuts & Dried Fruit'],
  ['Medjool dates', 'Nuts & Dried Fruit'],
  ['frozen pizza', 'Frozen'],
  ['Frozen peas', 'Frozen'],
  ['Vanilla ice cream', 'Frozen'],
  ['Orange juice', 'Beverages'],
  ['Coffee', 'Beverages'],
  ['Green tea', 'Beverages'],
  ['Sparkling water', 'Beverages'],
  ['Red wine', 'Beverages'],
  ['Tofu', 'Other'],
];

describe('categorizeDepartment', () => {
  for (const [name, department] of CASES) {
    it(`puts "${name}" in ${department}`, () => {
      expect(categorizeDepartment(name)).toBe(department);
    });
  }

  it('falls back to Other for unknown ingredients', () => {
    expect(categorizeDepartment('mystery ingredient')).toBe('Other');
  });

  it('uses the catalog food group when the name matches nothing', () => {
    expect(categorizeDepartment('Romanesco', 'Vegetables')).toBe('Produce');
    expect(categorizeDepartment('Lingonberry', 'Fruits')).toBe('Produce');
    expect(categorizeDepartment('Skyr', 'Dairy')).toBe('Dairy & Eggs');
    expect(categorizeDepartment('Teff', 'Grains')).toBe('Grains & Pasta');
    expect(categorizeDepartment('Lupini', 'Legumes')).toBe('Grains & Pasta');
    expect(categorizeDepartment('Pastrami', 'Protein Foods')).toBe('Meat & Seafood');
    expect(categorizeDepartment('Lard', 'Fats/Oils')).toBe('Oils & Vinegars');
    expect(categorizeDepartment('Tiger nuts', 'Nuts/Seeds')).toBe('Nuts & Dried Fruit');
    expect(categorizeDepartment('Mystery snack', 'Sweets/Snacks')).toBe('Other');
  });

  it('lets the name win over the food group', () => {
    expect(categorizeDepartment('Butter', 'Fats/Oils')).toBe('Dairy & Eggs');
    expect(categorizeDepartment('Eggs', 'Protein Foods')).toBe('Dairy & Eggs');
    expect(categorizeDepartment('Tofu', 'Protein Foods')).toBe('Other');
  });

  it('still moves a canned food-group fallback to Canned & Jarred', () => {
    expect(categorizeDepartment('Hearts of palm [14 oz can]', 'Vegetables')).toBe('Canned & Jarred');
  });

  it('has an icon and a shelf life for every ordered department', () => {
    for (const dept of DEPARTMENT_ORDER) {
      expect(DEPARTMENT_ICONS[dept]).toBeTruthy();
      expect(SHELF_LIFE_DEFAULTS[dept.toLowerCase()]).toBeGreaterThan(0);
    }
    expect(shelfLifeDaysFor('Nuts & Dried Fruit')).toBe(180);
  });
});
