// using System;
// using System.Collections.Generic;
// using System.Collections.Concurrent; // Added for ConcurrentBag and ConcurrentDictionary
// using System.Globalization;
// using System.IO;
// using System.Linq;
// using System.Text.RegularExpressions;
// using System.Threading.Tasks;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.EntityFrameworkCore.Design; // Required for IDesignTimeDbContextFactory
// using Microsoft.Extensions.Configuration;
// using EFCore.BulkExtensions; // Required for BulkInsertAsync
// using CsvHelper; // Added for CsvHelper
// using CsvHelper.Configuration; // Added for CsvHelper configuration
// using CsvHelper.Configuration.Attributes; // Added for [Name] and [Index] attributes
// using System.Text.Json; // Added for JsonSerializer

// public class Program
// {
//     // Define paths to your USDA FoodData Central CSV files and the Recipe CSV
//     // IMPORTANT: Adjust these paths if your directory structure changes.
//     private const string BaseDataPath = "/home/dhokanson/Dev/ImportSource/";
//     private const string FoodCsvPath = BaseDataPath + "food.csv";
//     private const string FoodPortionCsvPath = BaseDataPath + "food_portion.csv";
//     private const string MeasureUnitCsvPath = BaseDataPath + "measure_unit.csv";
//     private const string FoodNutrientCsvPath = BaseDataPath + "food_nutrient.csv";
//     private const string NutrientCsvPath = BaseDataPath + "nutrient.csv";
//     private const string RecipeCsvPath = BaseDataPath + "Recipe.csv";
//     private const string IngredientMappingsPath = BaseDataPath + "ingredient_mappings.csv"; // Path for ingredient mappings
//     private const string UnmatchedIngredientsLogPath = BaseDataPath + "unmatched_ingredients.log"; // New: Path for unmatched ingredients log
//     private const string IngredientCleaningRulesPath = BaseDataPath + "ingredient_cleaning_rules.csv"; // New: Path for ingredient cleaning rules

//     // In-memory caches for quick lookups (USDA data)
//     private static Dictionary<int, FoodItem> _foodItems;
//     private static List<FoodPortion> _foodPortions;
//     private static Dictionary<int, string> _measureUnits;
//     private static List<FoodNutrient> _foodNutrients;
//     private static Dictionary<int, Nutrient> _nutrients;
//     private static Dictionary<string, string> _ingredientMappings; // Dictionary for ingredient name mappings
//     private static List<CleaningRule> _cleaningRules; // New: List for ingredient cleaning rules

//     // This HashSet is a static member of the Program class, making it accessible to all static methods within Program.
//     private static readonly HashSet<string> _commonItemNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
//     {
//         "egg", "eggs", "apple", "apples", "banana", "bananas", "orange", "oranges",
//         "lemon", "lemons", "lime", "limes", "potato", "potatoes", "onion", "onions",
//         "clove", "cloves", "chicken breast", "chicken breasts", "fillet", "fillets",
//         "slice", "slices", "piece", "pieces", "loaf", "loaves", "can", "cans",
//         "bottle", "bottles", "package", "packages", "head", "heads", "bunch", "bunches",
//         "ear", "ears", "stalk", "stalks", "sprig", "sprigs", "leaf", "leaves",
//         "cube", "cubes", "tablet", "tablets", "capsule", "capsules", "bar", "bars",
//         "square", "squares", "envelope", "envelopes", "jar", "jars", "box", "boxes",
//         "bag", "bags", "roll", "rolls", "tube", "tubes", "link", "links",
//         "patty", "patties", "wafer", "wafers", "cookie", "cookies", "cracker", "crackers",
//         "chip", "chips", "kernel", "kernels", "pod", "pods", "grain", "grains",
//         "bean", "beans", "berry", "berries", "nut", "nuts", "seed", "seeds",
//         "cherry", "cherries", "grape", "grapes", "plum", "plums", "peach", "peaches",
//         "apricot", "apricots", "date", "dates", "fig", "figs", "kiwi", "kiwis",
//         "mango", "mangoes", "papaya", "papayas", "pear", "pears", "pineapple", "pineapples",
//         "strawberry", "strawberries", "blueberry", "blueberries", "raspberry", "raspberries",
//         "blackberry", "blackberries", "cranberry", "cranberries", "artichoke", "artichokes",
//         "asparagus", "asparagus", "avocado", "avocados", "beet", "beets", "bell pepper", "bell peppers",
//         "broccoli", "broccoli", "brussels sprout", "brussels sprouts", "cabbage", "cabbages",
//         "carrot", "carrots", "cauliflower", "cauliflowers", "celery", "celery", "corn", "corn",
//         "cucumber", "cucumbers", "eggplant", "eggplants", "garlic", "garlic", "ginger", "ginger",
//         "green bean", "green beans", "kale", "kale", "lettuce", "lettuce", "mushroom", "mushrooms",
//         "okra", "okra", "pea", "peas", "pumpkin", "pumpkins", "radish", "radishes",
//         "spinach", "spinach", "squash", "squash", "sweet potato", "sweet potatoes", "tomato", "tomatoes",
//         "turnip", "turnips", "zucchini", "zucchini", "steak", "steaks", "chop", "chops",
//         "rib", "ribs", "wing", "wings", "drumstick", "drumsticks", "thigh", "thighs",
//         "leg", "legs", "breast", "breasts", "loin", "loins", "shoulder", "shoulders",
//         "ham", "hams", "sausage", "sausages", "bacon", "bacon", "frankfurter", "frankfurters",
//         "hot dog", "hot dogs", "salami", "salamis", "pepperoni", "pepperonis", "turkey", "turkeys",
//         "duck", "ducks", "goose", "geese", "lamb", "lambs", "pork", "pork", "beef", "beef",
//         "veal", "veal", "venison", "venison", "fish", "fish", "shrimp", "shrimp", "crab", "crabs",
//         "lobster", "lobsters", "oyster", "oysters", "clam", "clams", "mussel", "mussels",
//         "scallop", "scallops", "squid", "squid", "octopus", "octopuses", "nut", "nuts",
//         "seed", "seeds", "grain", "grains", "berry", "berries", "fruit", "fruits",
//         "vegetable", "vegetables", "herb", "herbs", "spice", "spices", "leaf", "leaves",
//         "stem", "stems", "root", "roots", "flower", "flowers", "bud", "buds",
//         "pod", "pods", "kernel", "kernels", "stalk", "stalks", "bulb", "bulbs",
//         "tuber", "tubers", "rhizome", "rhizomes", "corm", "corms"
//     };


//     public static async Task Main(string[] args)
//     {
//         // Setup configuration
//         var builder = new ConfigurationBuilder()
//             .SetBasePath(Directory.GetCurrentDirectory())
//             .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true);
//         IConfiguration config = builder.Build();

//         string connectionString = config.GetConnectionString("TmpConnection");
//         if (string.IsNullOrEmpty(connectionString))
//         {
//             Console.WriteLine("Error: 'TmpConnection' connection string not found in appsettings.Development.json.");
//             return;
//         }

//         // Configure DbContext options
//         var optionsBuilder = new DbContextOptionsBuilder<RecipeDbContext>();
//         optionsBuilder.UseNpgsql(connectionString);

//         // Ensure database is created and migrations applied
//         using (var dbContext = new RecipeDbContext(optionsBuilder.Options))
//         {
//             try
//             {
//                 Console.WriteLine("Ensuring database is created and up-to-date...");
//                 await dbContext.Database.MigrateAsync(); // Applies any pending migrations
//                 Console.WriteLine("Database ready.");
//             }
//             catch (Exception ex)
//             {
//                 Console.WriteLine($"Error connecting to or migrating database: {ex.Message}");
//                 Console.WriteLine("Please check your PostgreSQL server and connection string in appsettings.Development.json.");
//                 return;
//             }
//         }

//         // Check for a specific argument to trigger data import
//         if (args.Contains("--import-data", StringComparer.OrdinalIgnoreCase))
//         {
//             Console.WriteLine("Loading USDA FoodData Central data...");
//             await LoadUsdaData();
//             Console.WriteLine("USDA FoodData Central data loaded successfully.");

//             Console.WriteLine("\nLoading ingredient mappings...");
//             await LoadIngredientMappings();
//             Console.WriteLine($"Loaded {_ingredientMappings.Count} ingredient mappings.");

//             Console.WriteLine("\nLoading ingredient cleaning rules...");
//             await LoadCleaningRules();
//             Console.WriteLine($"Loaded {_cleaningRules.Count} cleaning rules.");

//             // Clear the unmatched ingredients log file at the start of a new import
//             if (File.Exists(UnmatchedIngredientsLogPath))
//             {
//                 File.Delete(UnmatchedIngredientsLogPath);
//                 Console.WriteLine($"Cleared previous unmatched ingredients log: {UnmatchedIngredientsLogPath}");
//             }

//             Console.WriteLine("\n--- Parallel Processing Enabled ---");
//             Console.WriteLine("Unmatched ingredients will be logged to 'unmatched_ingredients.log'.");
//             Console.WriteLine("-----------------------------------\n");


//             Console.WriteLine($"\nLoading recipes from {RecipeCsvPath}...");
//             List<Recipe> recipes = await LoadRecipesFromCsv(RecipeCsvPath);
//             Console.WriteLine($"Loaded {recipes.Count} recipes.");

//             Console.WriteLine("\n--- Processing and Collecting Recipes in Parallel ---");

//             // Concurrent collections for thread-safe accumulation of data
//             var recipesToInsertBag = new ConcurrentBag<DbRecipe>();
//             var ingredientsToInsertBag = new ConcurrentBag<DbIngredient>();
//             var unitsToInsertBag = new ConcurrentBag<DbUnit>();
//             var recipeIngredientsToInsertBag = new ConcurrentBag<DbRecipeIngredient>();
//             var nutrientsToInsertBag = new ConcurrentBag<DbNutrient>();
//             var foodNutrientDataToInsertBag = new ConcurrentBag<DbFoodNutrientData>();

//             // Concurrent dictionaries for thread-safe tracking of existing/new entities by name/key
//             var allIngredients = new ConcurrentDictionary<string, DbIngredient>();
//             var allUnits = new ConcurrentDictionary<string, DbUnit>();
//             var allNutrients = new ConcurrentDictionary<string, DbNutrient>(); // Key: "Name-UnitName"
//             var processedFoodNutrientData = new ConcurrentDictionary<(int FdcId, int NutrientId), bool>();


//             // Pre-load existing data from DB to avoid duplicates and get existing IDs
//             using (var initialDbContext = new RecipeDbContext(optionsBuilder.Options))
//             {
//                 Console.WriteLine("Fetching existing database entries...");
//                 foreach (var ing in await initialDbContext.Ingredients.AsNoTracking().ToListAsync())
//                 {
//                     allIngredients.TryAdd(ing.Name, ing);
//                 }
//                 foreach (var unit in await initialDbContext.Units.AsNoTracking().ToListAsync())
//                 {
//                     allUnits.TryAdd(unit.Name, unit);
//                 }
//                 foreach (var nutrient in await initialDbContext.Nutrients.AsNoTracking().ToListAsync())
//                 {
//                     allNutrients.TryAdd($"{nutrient.Name}-{nutrient.UnitName}", nutrient);
//                 }
//                 foreach (var fnd in await initialDbContext.FoodNutrientData.AsNoTracking().ToListAsync())
//                 {
//                     processedFoodNutrientData.TryAdd((fnd.FdcId, fnd.NutrientId), true);
//                 }

//                 Console.WriteLine($"Found {allIngredients.Count} existing ingredients, {allUnits.Count} units, {allNutrients.Count} nutrients, and {processedFoodNutrientData.Count} food nutrient data entries.");
//             }

//             // Use Parallel.ForEachAsync for parallel processing of recipes
//             await Parallel.ForEachAsync(recipes, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (recipe, token) =>
//             {
//                 var dbRecipe = new DbRecipe
//                 {
//                     Title = recipe.Title,
//                     Link = recipe.Link,
//                     Source = recipe.Source
//                 };
//                 recipesToInsertBag.Add(dbRecipe); // Add to concurrent bag

//                 for (int i = 0; i < recipe.Ingredients.Count; i++)
//                 {
//                     var ingredientLine = recipe.Ingredients[i];
//                     var ner = recipe.Ner != null && recipe.Ner.Count > i ? recipe.Ner[i] : string.Empty;

//                     var parsedIngredient = ParseIngredientLine(ingredientLine);

//                     if (parsedIngredient == null)
//                     {
//                         // Try with a default "1 each" prefix if initial parsing fails
//                         parsedIngredient = ParseIngredientLine($"1 each {ingredientLine}");
//                     }

//                     if (parsedIngredient != null)
//                     {
//                         if (string.IsNullOrWhiteSpace(parsedIngredient.Name) && string.IsNullOrWhiteSpace(ner))
//                         {
//                             // Log error, but don't block parallel execution with Console.WriteLine directly
//                             // Consider a thread-safe logging mechanism if frequent logging is needed
//                             await LogUnmatchedIngredient(ingredientLine, 0, "N/A - Empty Name");
//                             continue; // Skip this ingredient
//                         }

//                         DbIngredient dbIngredient;
//                         string canonicalIngredientName = parsedIngredient.Name;

//                         if (!string.IsNullOrWhiteSpace(ner) && !string.Equals(ner, canonicalIngredientName, StringComparison.OrdinalIgnoreCase))
//                         {
//                             canonicalIngredientName = ner;
//                         }

//                         // Use GetOrAdd to safely add or retrieve existing ingredient
//                         dbIngredient = allIngredients.GetOrAdd(canonicalIngredientName, name =>
//                         {
//                             var newIngredient = new DbIngredient { Name = name };
//                             ingredientsToInsertBag.Add(newIngredient); // Add to bag if new
//                             return newIngredient;
//                         });

//                         DbUnit dbUnit;
//                         dbUnit = allUnits.GetOrAdd(parsedIngredient.Unit, name =>
//                         {
//                             var newUnit = new DbUnit { Name = name };
//                             unitsToInsertBag.Add(newUnit); // Add to bag if new
//                             return newUnit;
//                         });

//                         var dbRecipeIngredient = new DbRecipeIngredient
//                         {
//                             Recipe = dbRecipe,
//                             Ingredient = dbIngredient,
//                             Value = parsedIngredient.Value,
//                             Unit = dbUnit,
//                             ApproximateWeightGrams = await GetApproximateWeight(parsedIngredient.Name, ner, parsedIngredient.Value, parsedIngredient.Unit)
//                         };
//                         recipeIngredientsToInsertBag.Add(dbRecipeIngredient);

//                         var bestMatchingFoodItem = _foodItems.Values
//                             .FirstOrDefault(f => f.LowerCaseDescription.Contains(parsedIngredient.Name));
//                         if (bestMatchingFoodItem == null)
//                         {
//                             var matches = _foodItems.Values
//                                 .Where(f => parsedIngredient.Name.Contains(f.LowerCaseDescription)).OrderBy(f => f.Description.Length);
//                             bestMatchingFoodItem = matches.FirstOrDefault();
//                         }

//                         if (bestMatchingFoodItem != null)
//                         {
//                             var nutrientsForFood = GetNutrientsForFood(bestMatchingFoodItem.FdcId);
//                             foreach (var nutrientInfo in nutrientsForFood)
//                             {
//                                 DbNutrient dbNutrientDef;
//                                 string nutrientKey = $"{nutrientInfo.NutrientName}-{nutrientInfo.UnitName}";
//                                 dbNutrientDef = allNutrients.GetOrAdd(nutrientKey, key =>
//                                 {
//                                     var newNutrient = new DbNutrient { Name = nutrientInfo.NutrientName, UnitName = nutrientInfo.UnitName };
//                                     nutrientsToInsertBag.Add(newNutrient); // Add to bag if new
//                                     return newNutrient;
//                                 });

//                                 // Only add DbFoodNutrientData if the FdcId-NutrientId pair hasn't been processed yet
//                                 if (processedFoodNutrientData.TryAdd((bestMatchingFoodItem.FdcId, dbNutrientDef.Id), true))
//                                 {
//                                     var dbFoodNutrientData = new DbFoodNutrientData
//                                     {
//                                         FdcId = bestMatchingFoodItem.FdcId,
//                                         Nutrient = dbNutrientDef,
//                                         AmountPer100g = nutrientInfo.Amount
//                                     };
//                                     foodNutrientDataToInsertBag.Add(dbFoodNutrientData);
//                                 }
//                             }
//                         }
//                     }
//                     else
//                     {
//                         // Log error for unparseable lines
//                         await LogUnmatchedIngredient(ingredientLine, 0, "N/A - Unparseable");
//                     }
//                 }
//             });

//             Console.WriteLine("\nFinished parallel collection. Starting bulk inserts...");

//             // Convert ConcurrentBags to Lists for BulkInsertAsync
//             var recipesToInsert = recipesToInsertBag.ToList();
//             var ingredientsToInsert = ingredientsToInsertBag.ToList();
//             var unitsToInsert = unitsToInsertBag.ToList();
//             var recipeIngredientsToInsert = recipeIngredientsToInsertBag.ToList();
//             var nutrientsToInsert = nutrientsToInsertBag.ToList();
//             var foodNutrientDataToInsert = foodNutrientDataToInsertBag.ToList();


//             // Perform bulk inserts in order of dependency
//             using (var dbContext = new RecipeDbContext(optionsBuilder.Options))
//             {
//                 // 1. Bulk insert new Ingredients, Units, and Nutrients first
//                 // This will populate their auto-generated IDs
//                 if (ingredientsToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {ingredientsToInsert.Count} new ingredients...");
//                     await dbContext.BulkInsertAsync(ingredientsToInsert);
//                 }
//                 if (unitsToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {unitsToInsert.Count} new units...");
//                     await dbContext.BulkInsertAsync(unitsToInsert);
//                 }
//                 if (nutrientsToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {nutrientsToInsert.Count} new nutrients...");
//                     await dbContext.BulkInsertAsync(nutrientsToInsert);
//                 }

//                 // 2. Bulk insert Recipes
//                 // This will populate their auto-generated IDs
//                 if (recipesToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {recipesToInsert.Count} recipes...");
//                     await dbContext.BulkInsertAsync(recipesToInsert);
//                 }

//                 // 3. Now that all parent IDs are generated, set explicit foreign keys for RecipeIngredients and FoodNutrientData
//                 // This is crucial because BulkInsertAsync for child entities might not automatically resolve navigation properties
//                 // if the parent was just inserted in a separate bulk operation.
//                 foreach (var ri in recipeIngredientsToInsert)
//                 {
//                     // Find the actual DbIngredient/DbUnit/DbRecipe instances from the dictionaries
//                     // or rely on EF Core's change tracking if they were added to the same context (which they aren't here for bulk)
//                     // Since we are using BulkInsertAsync, we must ensure the IDs are set.
//                     // The IDs are populated by the *first* BulkInsertAsync call for parent entities.
//                     // So, we need to ensure the objects in the bags have their IDs set after the first bulk insert.
//                     // This typically requires re-querying or ensuring the objects in the bags are the *same instances*
//                     // that were passed to the initial bulk insert and had their IDs populated.
//                     // ConcurrentBag doesn't guarantee order, so matching by name is safer.

//                     // This part needs careful attention: After BulkInsertAsync, the IDs of the objects *in the list passed to it* are populated.
//                     // The objects in `allIngredients`, `allUnits`, `allNutrients` also need to reflect these new IDs.
//                     // The simplest way to ensure this is to re-fetch the IDs or ensure the `GetOrAdd` returns the *actual* entity with ID.
//                     // For now, assuming the `allIngredients`, etc., dictionaries hold references to the objects whose IDs were populated.
//                     // If not, a separate step to update these dictionaries with actual DB-assigned IDs would be needed.
//                     // For the current setup, `GetOrAdd` will return the *same instance* if it was added, so its ID will be updated by BulkInsertAsync.
//                     ri.RecipeId = recipesToInsert.First(r => r.Title == ri.Recipe.Title && r.Link == ri.Recipe.Link).Id; // This is a simplification; a more robust lookup might be needed for large sets
//                     ri.IngredientId = allIngredients[ri.Ingredient.Name].Id;
//                     ri.UnitId = allUnits[ri.Unit.Name].Id;
//                 }

//                 foreach (var fnd in foodNutrientDataToInsert)
//                 {
//                     fnd.NutrientId = allNutrients[$"{fnd.Nutrient.Name}-{fnd.Nutrient.UnitName}"].Id;
//                 }


//                 // 4. Bulk insert RecipeIngredients
//                 if (recipeIngredientsToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {recipeIngredientsToInsert.Count} recipe ingredients...");
//                     await dbContext.BulkInsertAsync(recipeIngredientsToInsert);
//                 }

//                 // 5. Bulk insert FoodNutrientData
//                 if (foodNutrientDataToInsert.Any())
//                 {
//                     Console.WriteLine($"Bulk inserting {foodNutrientDataToInsert.Count} food nutrient data entries...");
//                     await dbContext.BulkInsertAsync(foodNutrientDataToInsert);
//                 }
//             }
//             Console.WriteLine("\n--- Finished Processing and Inserting Data ---");
//             Console.WriteLine($"Review '{UnmatchedIngredientsLogPath}' for ingredients that could not be matched.");
//         }
//         else
//         {
//             Console.WriteLine("To import data, run the application with the '--import-data' argument.");
//             Console.WriteLine("Example: dotnet run -- --import-data");
//         }
//     }

//     /// <summary>
//     /// Loads necessary data from USDA FoodData Central CSV files into memory.
//     /// </summary>
//     private static async Task LoadUsdaData()
//     {
//         _foodItems = new Dictionary<int, FoodItem>();
//         _foodPortions = new List<FoodPortion>();
//         _measureUnits = new Dictionary<int, string>();
//         _foodNutrients = new List<FoodNutrient>();
//         _nutrients = new Dictionary<int, Nutrient>();

//         var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
//         {
//             PrepareHeaderForMatch = args => args.Header.ToLowerInvariant()
//         };

//         // Load food.csv
//         Console.WriteLine($"Loading {FoodCsvPath}...");
//         if (!File.Exists(FoodCsvPath)) { Console.WriteLine($"Error: {FoodCsvPath} not found."); return; }
//         using (var reader = new StreamReader(FoodCsvPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             var records = csv.GetRecords<FoodCsvRecord>().Where(r => !string.IsNullOrWhiteSpace(r.Description) && r.Description.Length > 1).ToList();
//             foreach (var record in records)
//             {
//                 _foodItems[record.FdcId] = new FoodItem
//                 {
//                     FdcId = record.FdcId,
//                     DataType = record.DataType,
//                     Description = record.Description,
//                     LowerCaseDescription = record.Description.ToLowerInvariant(),
//                     FoodCategory = record.FoodCategory
//                 };
//             }
//         }
//         Console.WriteLine($"Loaded {_foodItems.Count} food items.");

//         // Load measure_unit.csv
//         Console.WriteLine($"Loading {MeasureUnitCsvPath}...");
//         if (!File.Exists(MeasureUnitCsvPath)) { Console.WriteLine($"Error: {MeasureUnitCsvPath} not found."); return; }
//         using (var reader = new StreamReader(MeasureUnitCsvPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             var records = csv.GetRecords<MeasureUnitCsvRecord>().ToList();
//             foreach (var record in records)
//             {
//                 _measureUnits[record.Id] = record.Name.ToLowerInvariant();
//             }
//         }
//         Console.WriteLine($"Loaded {_measureUnits.Count} measure units.");

//         // Load food_portion.csv
//         Console.WriteLine($"Loading {FoodPortionCsvPath}...");
//         if (!File.Exists(FoodPortionCsvPath)) { Console.WriteLine($"Error: {FoodPortionCsvPath} not found."); return; }
//         using (var reader = new StreamReader(FoodPortionCsvPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             // CsvHelper will handle parsing, including nullable types
//             _foodPortions = csv.GetRecords<FoodPortionCsvRecord>()
//                                .Select(r => new FoodPortion
//                                {
//                                    Id = r.Id,
//                                    FdcId = r.FdcId,
//                                    Amount = r.Amount,
//                                    MeasureUnitId = r.MeasureUnitId,
//                                    PortionDescription = r.PortionDescription,
//                                    GramWeight = r.GramWeight
//                                })
//                                .ToList();
//         }
//         Console.WriteLine($"Loaded {_foodPortions.Count} food portions.");

//         // Load nutrient.csv
//         Console.WriteLine($"Loading {NutrientCsvPath}...");
//         if (!File.Exists(NutrientCsvPath)) { Console.WriteLine($"Error: {NutrientCsvPath} not found."); return; }
//         using (var reader = new StreamReader(NutrientCsvPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             var records = csv.GetRecords<NutrientCsvRecord>().ToList();
//             foreach (var record in records)
//             {
//                 _nutrients[record.Id] = new Nutrient
//                 {
//                     Id = record.Id,
//                     Name = record.Name,
//                     UnitName = record.UnitName
//                 };
//             }
//         }
//         Console.WriteLine($"Loaded {_nutrients.Count} nutrients.");

//         // Load food_nutrient.csv
//         Console.WriteLine($"Loading {FoodNutrientCsvPath}...");
//         if (!File.Exists(FoodNutrientCsvPath)) { Console.WriteLine($"Error: {FoodNutrientCsvPath} not found."); return; }
//         using (var reader = new StreamReader(FoodNutrientCsvPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             _foodNutrients = csv.GetRecords<FoodNutrientCsvRecord>()
//                                 .Select(r => new FoodNutrient
//                                 {
//                                     Id = r.Id,
//                                     FdcId = r.FdcId,
//                                     NutrientId = r.NutrientId,
//                                     Amount = r.Amount
//                                 })
//                                 .ToList();
//         }
//         Console.WriteLine($"Loaded {_foodNutrients.Count} food nutrient entries.");
//     }

//     /// <summary>
//     /// Loads ingredient mapping data from the specified CSV file.
//     /// </summary>
//     private static async Task LoadIngredientMappings()
//     {
//         _ingredientMappings = new Dictionary<string, string>();
//         if (!File.Exists(IngredientMappingsPath))
//         {
//             Console.WriteLine($"Warning: Ingredient mappings file not found at {IngredientMappingsPath}. No custom mappings will be applied.");
//             return;
//         }

//         var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
//         {
//             PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
//             HasHeaderRecord = false // Mappings file might not have a header
//         };

//         using (var reader = new StreamReader(IngredientMappingsPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             var records = csv.GetRecords<IngredientMappingCsvRecord>().ToList();
//             foreach (var record in records)
//             {
//                 if (!string.IsNullOrWhiteSpace(record.OriginalPhrase) && !string.IsNullOrWhiteSpace(record.CanonicalName))
//                 {
//                     _ingredientMappings[record.OriginalPhrase.ToLowerInvariant()] = record.CanonicalName.ToLowerInvariant();
//                 }
//                 else if (!string.IsNullOrWhiteSpace(record.OriginalPhrase))
//                 {
//                     _ingredientMappings[record.OriginalPhrase.ToLowerInvariant()] = record.OriginalPhrase.ToLowerInvariant();
//                 }
//             }
//         }
//     }

//     /// <summary>
//     /// Loads ingredient cleaning rules from the specified CSV file.
//     /// </summary>
//     private static async Task LoadCleaningRules()
//     {
//         _cleaningRules = new List<CleaningRule>();
//         if (!File.Exists(IngredientCleaningRulesPath))
//         {
//             Console.WriteLine($"Warning: Ingredient cleaning rules file not found at {IngredientCleaningRulesPath}. No custom cleaning rules will be applied.");
//             return;
//         }

//         var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
//         {
//             PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
//             HasHeaderRecord = false // Cleaning rules file might not have a header
//         };

//         using (var reader = new StreamReader(IngredientCleaningRulesPath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             var records = csv.GetRecords<CleaningRuleCsvRecord>().ToList();
//             foreach (var record in records)
//             {
//                 if (!string.IsNullOrWhiteSpace(record.Pattern))
//                 {
//                     _cleaningRules.Add(new CleaningRule
//                     {
//                         Pattern = record.Pattern.Trim(),
//                         Replacement = record.Replacement?.Trim() ?? ""
//                     });
//                 }
//             }
//         }
//     }

//     /// <summary>
//     /// Attempts to parse an ingredient line into its name, numeric value, and unit.
//     /// This is a simplified parser and may need refinement for complex cases.
//     /// </summary>
//     private static ParsedIngredient ParseIngredientLine(string ingredientLine)
//     {
//         // Helper to apply cleaning rules and mappings
//         string ApplyCleaningAndMapping(string name, string fallback)
//         {
//             string cleanedName = name;
//             foreach (var rule in _cleaningRules)
//             {
//                 cleanedName = Regex.Replace(cleanedName, rule.Pattern, rule.Replacement, RegexOptions.IgnoreCase).Trim();
//             }
//             if (_ingredientMappings.TryGetValue(cleanedName, out string mappedName))
//             {
//                 cleanedName = mappedName;
//             }
//             // Ensure it's never empty if original name or fallback wasn't empty
//             return string.IsNullOrWhiteSpace(cleanedName) ? (string.IsNullOrWhiteSpace(name) ? fallback : name) : cleanedName;
//         }

//         // 1. Try to parse quantity first
//         // Regex to capture: (value) (rest_of_line_after_value)
//         var quantityMatch = Regex.Match(ingredientLine, @"^(\d+\s*\d*\/\d+|\d*\.?\d+)\s*(.*)$", RegexOptions.IgnoreCase);

//         if (!quantityMatch.Success)
//         {
//             // Handle cases with no leading number (e.g., "Salt to taste", "large whole chicken")
//             var noValueMatch = Regex.Match(ingredientLine, @"^([a-zA-Z\s]+)\s*(to taste|as needed)$", RegexOptions.IgnoreCase);
//             if (noValueMatch.Success)
//             {
//                 string rawNamePart = noValueMatch.Groups[1].Value.Trim().ToLowerInvariant();
//                 string finalName = ApplyCleaningAndMapping(rawNamePart, rawNamePart);
//                 return new ParsedIngredient { Name = finalName, Value = 0, Unit = "to taste/as needed" };
//             }

//             string rawNamePartOnly = ingredientLine.Trim().ToLowerInvariant();
//             if (!string.IsNullOrWhiteSpace(rawNamePartOnly))
//             {
//                 string finalName = ApplyCleaningAndMapping(rawNamePartOnly, rawNamePartOnly);
//                 return new ParsedIngredient { Name = finalName, Value = 1, Unit = "each" }; // Default value 1, unit "each"
//             }
//             return null; // Cannot parse
//         }

//         double value = ParseFractionOrDecimal(quantityMatch.Groups[1].Value.Trim());
//         string remainingText = quantityMatch.Groups[2].Value.Trim().ToLowerInvariant();

//         string bestUnit = "";
//         string bestName = "";

//         // Combine known USDA measure units with common abbreviations for robust unit detection
//         var allPossibleUnits = new HashSet<string>(_measureUnits.Values);
//         // Add common abbreviations explicitly
//         allPossibleUnits.Add("pound");
//         allPossibleUnits.Add("pounds");
//         allPossibleUnits.Add("lb");
//         allPossibleUnits.Add("lbs");
//         allPossibleUnits.Add("ounce");
//         allPossibleUnits.Add("ounces");
//         allPossibleUnits.Add("oz");
//         allPossibleUnits.Add("gram");
//         allPossibleUnits.Add("grams");
//         allPossibleUnits.Add("g");
//         allPossibleUnits.Add("kilogram");
//         allPossibleUnits.Add("kilograms");
//         allPossibleUnits.Add("kg");
//         allPossibleUnits.Add("milliliter");
//         allPossibleUnits.Add("milliliters");
//         allPossibleUnits.Add("ml");
//         allPossibleUnits.Add("liter");
//         allPossibleUnits.Add("liters");
//         allPossibleUnits.Add("l");
//         allPossibleUnits.Add("c");
//         allPossibleUnits.Add("cup");
//         allPossibleUnits.Add("cups");
//         allPossibleUnits.Add("tbsp");
//         allPossibleUnits.Add("tsp");
//         allPossibleUnits.Add("tablespoon");
//         allPossibleUnits.Add("tablespoons");
//         allPossibleUnits.Add("teaspoon");
//         allPossibleUnits.Add("teaspoons");
//         allPossibleUnits.Add("each");
//         allPossibleUnits.Add("unit");
//         allPossibleUnits.Add("units");
//         allPossibleUnits.Add("slice");
//         allPossibleUnits.Add("slices");
//         allPossibleUnits.Add("clove");
//         allPossibleUnits.Add("cloves");
//         allPossibleUnits.Add("stick");
//         allPossibleUnits.Add("sticks");
//         allPossibleUnits.Add("bunch");
//         allPossibleUnits.Add("bunches");
//         allPossibleUnits.Add("head");
//         allPossibleUnits.Add("heads");
//         allPossibleUnits.Add("piece");
//         allPossibleUnits.Add("pieces");
//         allPossibleUnits.Add("bag");
//         allPossibleUnits.Add("bags");
//         allPossibleUnits.Add("box");
//         allPossibleUnits.Add("boxes");
//         allPossibleUnits.Add("pint");
//         allPossibleUnits.Add("pints");
//         allPossibleUnits.Add("pt"); // pint
//         allPossibleUnits.Add("quart");
//         allPossibleUnits.Add("quarts");
//         allPossibleUnits.Add("qt"); // quart
//         allPossibleUnits.Add("gal"); // gallon
//         allPossibleUnits.Add("fluid ounce");
//         allPossibleUnits.Add("fl oz"); // fluid ounce
//         allPossibleUnits.Add("pkg"); // package
//         allPossibleUnits.Add("can");
//         allPossibleUnits.Add("dash");
//         allPossibleUnits.Add("pinch");
//         allPossibleUnits.Add("smidgen");


//         // Sort by length descending to prefer longer matches (e.g., "tablespoon" over "tbsp")
//         var sortedPossibleUnits = allPossibleUnits.OrderByDescending(u => u.Length);

//         // First pass: try to find a direct unit match
//         foreach (string unitCandidate in sortedPossibleUnits)
//         {
//             // Regex to match the unit at the beginning of the remaining text, followed by a word boundary or end of string
//             // This allows "lb." to match "lb." and not just "lb"
//             // Changed regex to capture the remainder more reliably in Group 2
//             var unitRegex = new Regex($@"^{Regex.Escape(unitCandidate)}\.?\b*\s*(.*)", RegexOptions.IgnoreCase);
//             var unitTextMatch = unitRegex.Match(remainingText);

//             if (unitTextMatch.Success)
//             {
//                 bestUnit = unitCandidate; // Use the canonical form from our list
//                 bestName = unitTextMatch.Groups[1].Value.Trim(); // Groups[1] now contains the rest of the string
//                 break; // Found a unit, stop searching
//             }
//         }

//         // If a unit was found in the first pass
//         if (!string.IsNullOrEmpty(bestUnit))
//         {
//             // Clean and map the name part
//             string finalName = ApplyCleaningAndMapping(bestName, remainingText); // Use remainingText as fallback if bestName becomes empty
//             return new ParsedIngredient { Name = finalName, Value = value, Unit = bestUnit };
//         }

//         // Second pass: If no explicit unit was found, check for common item nouns
//         // This handles cases like "1 egg", where "egg" is the item, not a unit.
//         // Sort by length descending to prefer "chicken breast" over "chicken"
//         var sortedCommonItemNouns = _commonItemNouns.OrderByDescending(n => n.Length);
//         foreach (string nounCandidate in sortedCommonItemNouns)
//         {
//             // Check if the remaining text starts with the noun, potentially followed by other descriptors
//             // Use a word boundary to ensure "egg" doesn't match "eggplant"
//             var nounRegex = new Regex($@"^{Regex.Escape(nounCandidate)}\b\s*(.*)", RegexOptions.IgnoreCase);
//             var nounTextMatch = nounRegex.Match(remainingText);

//             if (nounTextMatch.Success)
//             {
//                 // The ingredient name starts with the noun, and includes the rest of the descriptive text
//                 string fullIngredientNameCandidate = (nounCandidate + " " + nounTextMatch.Groups[1].Value).Trim();
//                 string finalNameFromNoun = ApplyCleaningAndMapping(fullIngredientNameCandidate, fullIngredientNameCandidate);

//                 return new ParsedIngredient { Name = finalNameFromNoun, Value = value, Unit = "each" };
//             }
//         }

//         // Third pass: If no explicit unit and no common item noun, treat the entire remaining text as the name
//         // and default the unit to "each". This is a broad fallback.
//         string finalNameFallback = ApplyCleaningAndMapping(remainingText, remainingText);

//         return new ParsedIngredient { Name = finalNameFallback, Value = value, Unit = "each" };
//     }

//     /// <summary>
//     /// Helper method to parse a string that might contain a fraction or a decimal into a double.
//     /// </summary>
//     /// <param name="valueStr">The string representation of the quantity (e.g., "1", "1/2", "1 1/2", "0.5").</param>
//     /// <returns>The parsed double value.</returns>
//     private static double ParseFractionOrDecimal(string valueStr)
//     {
//         if (valueStr.Contains('/'))
//         {
//             if (valueStr.Contains(' '))
//             {
//                 var wholeAndFraction = valueStr.Split(' ');
//                 double whole = double.Parse(wholeAndFraction[0], CultureInfo.InvariantCulture);
//                 var fractionParts = wholeAndFraction[1].Split('/');
//                 return whole + (double.Parse(fractionParts[0], CultureInfo.InvariantCulture) / double.Parse(fractionParts[1], CultureInfo.InvariantCulture));
//             }
//             else
//             {
//                 var fractionParts = valueStr.Split('/');
//                 return double.Parse(fractionParts[0], CultureInfo.InvariantCulture) / double.Parse(fractionParts[1], CultureInfo.InvariantCulture);
//             }
//         }
//         else
//         {
//             return double.Parse(valueStr, CultureInfo.InvariantCulture);
//         }
//     }

//     /// <summary>
//     /// Retrieves the approximate weight in grams for a given ingredient and its measurement.
//     /// This now prioritizes exact matches, then fuzzy matches, and logs unmatched ingredients.
//     /// </summary>
//     private static async Task<double> GetApproximateWeight(string ingredientName, string ner, double value, string unit)
//     {
//         // Special handling for "to taste" or "as needed"
//         if (unit == "to taste/as needed" || unit == "as needed" || unit == "your choice")
//         {
//             return 0; // Cannot determine weight for these
//         }

//         FoodItem bestMatchingFoodItem = null;
//         string nameToSearchInFdc = ingredientName; // Start with the cleaned/mapped name from ParseIngredientLine

//         // Helper function to find a food item
//         Func<string, FoodItem> findFoodItem = (name) =>
//         {
//             if (string.IsNullOrWhiteSpace(name))
//                 return null;
//             // 1. Exact, case-insensitive match
//             var exactMatch = _foodItems.Values
//                 .FirstOrDefault(f => f.Description.Equals(name, StringComparison.OrdinalIgnoreCase));
//             if (exactMatch != null) return exactMatch;

//             // 2. Fuzzy match: Check if the food description contains the ingredient name or vice versa
//             var result = _foodItems.Values
//                 .FirstOrDefault(f => f.Description.Contains(name, StringComparison.OrdinalIgnoreCase));
//             if (result == null)
//             {
//                 var results = _foodItems.Values.Where(f => name.Contains(f.Description, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Description.Length);
//                 result = results.FirstOrDefault();
//             }

//             if (result == null)
//             {
//                 // Additional fuzzy match: Check if the food description contains the ingredient name
//                 var fuzzyMatches = _foodItems.Values
//                     .Where(f => f.LowerCaseDescription.Contains(name.ToLowerInvariant()))
//                     .OrderBy(f => f.Description.Length); // Prefer shorter descriptions

//                 result = fuzzyMatches.FirstOrDefault();
//             }

//             return result;
//         };

//         // Attempt 1: Try to find a match with the already parsed/cleaned ingredientName
//         bestMatchingFoodItem = findFoodItem(nameToSearchInFdc);
//         if (bestMatchingFoodItem == null)
//         {
//             bestMatchingFoodItem = findFoodItem(ner);
//         }

//         // If no match, log it as unmatched (interactive mode is disabled for parallel processing)
//         if (bestMatchingFoodItem == null)
//         {
//             if (string.IsNullOrWhiteSpace(nameToSearchInFdc) && string.IsNullOrWhiteSpace(ner))
//             {
//                 // Log if both parsed name and NER are empty
//                 await LogUnmatchedIngredient(ingredientName, value, unit);
//             }
//             else
//             {
//                 // Log the original ingredient line if no match found
//                 await LogUnmatchedIngredient(ingredientName, value, unit);
//             }
//             return 0; // Return 0 weight if unmatched
//         }

//         // If a bestMatchingFoodItem was found, proceed with portion matching
//         if (bestMatchingFoodItem != null)
//         {
//             // Find portions for this food item that match the unit
//             var matchingPortions = _foodPortions
//                 .Where(p => p.FdcId == bestMatchingFoodItem.FdcId && p.GramWeight.HasValue) // Only consider portions with gram_weight
//                 .ToList();

//             foreach (var portion in matchingPortions)
//             {
//                 string measureUnitName = null;
//                 _measureUnits.TryGetValue(portion.MeasureUnitId, out measureUnitName);

//                 // Option 1: Direct match of parsed unit with USDA measure_unit_name
//                 // This covers cases like "1 cup" where Amount is also present or implied
//                 if (!string.IsNullOrEmpty(measureUnitName) && (measureUnitName == unit || IsUnitEquivalent(measureUnitName, unit)))
//                 {
//                     // If USDA 'amount' is present and non-zero, use it
//                     if (portion.Amount.HasValue && portion.Amount.Value > 0)
//                     {
//                         return (portion.GramWeight.Value / portion.Amount.Value) * value;
//                     }
//                     // If USDA 'amount' is missing/zero but portion_description implies '1 unit'
//                     else if (!string.IsNullOrEmpty(portion.PortionDescription) &&
//                              Regex.IsMatch(portion.PortionDescription, @"^1\s+" + Regex.Escape(measureUnitName) + @"(\s|,|$)", RegexOptions.IgnoreCase))
//                     {
//                         // Assume 1 unit if description matches "1 [unit]" and amount is missing
//                         return portion.GramWeight.Value * value;
//                     }
//                 }
//                 // Option 2: Handle cases where 'Amount' is empty and 'portion_description' contains the quantity/unit.
//                 // This covers "Quantity not specified", "1 small", "1 chip", "1 patty" etc.
//                 else if (!portion.Amount.HasValue || portion.Amount.Value == 0)
//                 {
//                     if (!string.IsNullOrEmpty(portion.PortionDescription))
//                     {
//                         // Regex to extract "1 small", "1 medium", "1 large", "1 chip", "1 patty", "1 piece", "1 slice"
//                         // Or "Quantity not specified", "Guideline amount..."
//                         var specificUnitMatch = Regex.Match(portion.PortionDescription,
//                             @"^(1\s+(small|medium|large|chip|patty|piece|slice|thin|regular|thick|fry|stack|bag|can|order|potato|stick))|^(Quantity not specified|Guideline amount)",
//                             RegexOptions.IgnoreCase);

//                         if (specificUnitMatch.Success)
//                         {
//                             // If it's "Quantity not specified" or "Guideline amount", assume 1 "each"
//                             if (portion.PortionDescription.Contains("Quantity not specified", StringComparison.OrdinalIgnoreCase) ||
//                                 portion.PortionDescription.Contains("Guideline amount", StringComparison.OrdinalIgnoreCase))
//                             {
//                                 // If the recipe unit is empty or generic, and portion description is "Quantity not specified"
//                                 // or "Guideline amount", assume 1 "each"
//                                 if (string.IsNullOrEmpty(unit) || unit == "each" || unit == "unit")
//                                 {
//                                     return portion.GramWeight.Value * value; // Assume recipe quantity maps to this 1-unit portion
//                                 }
//                             }
//                             // If it's "1 small", "1 chip", "1 patty", etc.
//                             else
//                             {
//                                 // Extract the implied unit from the description (e.g., "small", "chip", "patty")
//                                 string impliedUnit = specificUnitMatch.Groups[2].Success ? specificUnitMatch.Groups[2].Value.ToLowerInvariant() : "each";

//                                 // If the recipe unit is empty or equivalent to the implied unit, use this portion
//                                 if (string.IsNullOrEmpty(unit) || unit == impliedUnit || IsUnitEquivalent(unit, impliedUnit) || unit == "each")
//                                 {
//                                     return portion.GramWeight.Value * value; // Assume recipe quantity maps to this 1-unit portion
//                                 }
//                             }
//                         }
//                         // Fallback: Try to extract a number and unit from portion_description for other cases
//                         else
//                         {
//                             var portionDescMatch = Regex.Match(portion.PortionDescription, @"^(\d+\s*\d*\/\d+|\d*\.?\d+)\s*([a-zA-Z\.]*)");
//                             if (portionDescMatch.Success)
//                             {
//                                 double portionDescValue;
//                                 string portionDescValueStr = portionDescMatch.Groups[1].Value.Trim();
//                                 if (portionDescValueStr.Contains('/'))
//                                 {
//                                     if (portionDescValueStr.Contains(' '))
//                                     {
//                                         var wholeAndFraction = portionDescValueStr.Split(' ');
//                                         double whole = double.Parse(wholeAndFraction[0], CultureInfo.InvariantCulture);
//                                         var fractionParts = wholeAndFraction[1].Split('/');
//                                         portionDescValue = whole + (double.Parse(fractionParts[0], CultureInfo.InvariantCulture) / double.Parse(fractionParts[1], CultureInfo.InvariantCulture));
//                                     }
//                                     else
//                                     {
//                                         var fractionParts = portionDescValueStr.Split('/');
//                                         portionDescValue = double.Parse(fractionParts[0], CultureInfo.InvariantCulture) / double.Parse(fractionParts[1], CultureInfo.InvariantCulture);
//                                     }
//                                 }
//                                 else
//                                 {
//                                     portionDescValue = double.Parse(portionDescValueStr, CultureInfo.InvariantCulture);
//                                 }

//                                 string portionDescUnit = portionDescMatch.Groups[2].Value.Trim().ToLowerInvariant();

//                                 // If the parsed unit from description is equivalent to the USDA measure unit
//                                 if (!string.IsNullOrEmpty(measureUnitName) && (measureUnitName == portionDescUnit || IsUnitEquivalent(measureUnitName, portionDescUnit)))
//                                 {
//                                     if (portionDescValue > 0)
//                                     {
//                                         return (portion.GramWeight.Value / portionDescValue) * value;
//                                     }
//                                 }
//                             }
//                         }
//                     }
//                 }
//             }
//         }

//         // If no match was found for weight, return 0
//         return 0;
//     }

//     /// <summary>
//     /// Checks if two units are equivalent or very similar (e.g., "cup" and "cups").
//     /// </summary>
//     private static bool IsUnitEquivalent(string unit1, string unit2)
//     {
//         if (unit1 == unit2) return true;

//         // Handle pluralization
//         if (unit1 + "s" == unit2 || unit2 + "s" == unit1) return true;

//         // Handle common abbreviations
//         if ((unit1 == "tablespoon" && (unit2 == "tbsp" || unit2 == "tbsp.")) ||
//             (unit2 == "tablespoon" && (unit1 == "tbsp" || unit1 == "tbsp."))) return true;
//         if ((unit1 == "teaspoon" && (unit2 == "tsp" || unit2 == "tsp.")) ||
//             (unit2 == "teaspoon" && (unit1 == "tsp" || unit1 == "tsp."))) return true;
//         if ((unit1 == "cup" && (unit2 == "c" || unit2 == "c.")) ||
//             (unit2 == "cup" && (unit1 == "c" || unit1 == "c."))) return true;
//         if ((unit1 == "pound" && (unit2 == "lb" || unit2 == "lb.")) ||
//             (unit2 == "pound" && (unit1 == "lb" || unit1 == "lb."))) return true;
//         if ((unit1 == "ounce" && (unit2 == "oz" || unit2 == "oz.")) ||
//             (unit2 == "ounce" && (unit1 == "oz" || unit1 == "oz."))) return true;
//         if ((unit1 == "package" && (unit2 == "pkg" || unit2 == "pkg.")) ||
//             (unit2 == "package" && (unit1 == "pkg" || unit1 == "pkg."))) return true;
//         if ((unit1 == "each" && (unit2 == "ea" || unit2 == "item" || unit2 == "unit")) ||
//             (unit2 == "each" && (unit1 == "ea" || unit1 == "item" || unit1 == "unit"))) return true;


//         return false;
//     }

//     /// <summary>
//     /// Retrieves a list of nutrients and their amounts for a given FDC ID.
//     /// </summary>
//     /// <param name="fdcId">The FDC ID of the food item.</param>
//     /// <returns>A list of NutrientInfo objects.</returns>
//     private static List<NutrientInfo> GetNutrientsForFood(int fdcId)
//     {
//         var nutrientsList = new List<NutrientInfo>();

//         var foodNutrientEntries = _foodNutrients.Where(fn => fn.FdcId == fdcId).ToList();

//         foreach (var fn in foodNutrientEntries)
//         {
//             if (_nutrients.TryGetValue(fn.NutrientId, out Nutrient nutrientDetails))
//             {
//                 nutrientsList.Add(new NutrientInfo
//                 {
//                     NutrientName = nutrientDetails.Name,
//                     Amount = fn.Amount,
//                     UnitName = nutrientDetails.UnitName
//                 });
//             }
//         }
//         return nutrientsList;
//     }

//     /// <summary>
//     /// Logs an ingredient that could not be matched to a USDA food item.
//     /// </summary>
//     private static async Task LogUnmatchedIngredient(string ingredientName, double value, string unit)
//     {
//         // Use a lock or a thread-safe writer if this is called frequently in parallel
//         // For simple logging to a file, File.AppendAllTextAsync is generally safe for concurrent writes
//         // as the OS handles atomicity for appends, but ordering might not be guaranteed.
//         await File.AppendAllTextAsync(UnmatchedIngredientsLogPath,
//             $"{ingredientName},{value},{unit}{Environment.NewLine}");
//     }

//     /// <summary>
//     /// Loads recipe data from the specified CSV file.
//     /// </summary>
//     /// <param name="filePath">The path to the Recipe CSV file.</param>
//     /// <returns>A list of Recipe objects.</returns>
//     private static async Task<List<Recipe>> LoadRecipesFromCsv(string filePath)
//     {
//         var recipes = new List<Recipe>();
//         if (!File.Exists(filePath))
//         {
//             Console.WriteLine($"Error: Recipe CSV file not found at {filePath}");
//             return recipes;
//         }

//         var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
//         {
//             PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
//             Delimiter = "," // Ensure correct delimiter
//         };

//         using (var reader = new StreamReader(filePath))
//         using (var csv = new CsvReader(reader, csvConfig))
//         {
//             // Read header to determine column indices dynamically
//             csv.Read();
//             csv.ReadHeader();
//             var headerNames = csv.HeaderRecord.Select(h => h.ToLowerInvariant()).ToList();

//             int idCol = headerNames.IndexOf("id");
//             int titleCol = headerNames.IndexOf("title");
//             int ingredientsCol = headerNames.IndexOf("ingredients");
//             int directionsCol = headerNames.IndexOf("directions");
//             int linkCol = headerNames.IndexOf("link");
//             int sourceCol = headerNames.IndexOf("source");
//             int nerCol = headerNames.IndexOf("ner");

//             // Assuming totalLines is still needed for progress reporting,
//             // but GetRecords() processes lazily, so we might not know total count upfront without
//             // reading all into memory first or doing a separate file line count.
//             // For now, let's keep the progress reporting based on a pre-calculated totalLines
//             // but acknowledge it might not be perfectly accurate with lazy CsvHelper.
//             int totalLines = File.ReadLines(filePath).Count() - 1; // -1 for header
//             int loadedRecipesCount = 0;
//             int reportInterval = Math.Max(1, totalLines / 100);

//             while (csv.Read())
//             {
//                 loadedRecipesCount++;
//                 if (loadedRecipesCount % reportInterval == 0 || loadedRecipesCount == totalLines)
//                 {
//                     double percentage = (double)loadedRecipesCount / totalLines * 100;
//                     Console.Write($"\rLoading recipes: {loadedRecipesCount}/{totalLines} - {percentage:F2}% complete.");
//                 }

//                 var record = csv.GetRecord<RecipeCsvRecord>();

//                 // Detailed check for malformed or incomplete lines
//                 string reason = "";
//                 if (idCol == -1) reason = "Missing 'id' column in header.";
//                 else if (titleCol == -1) reason = "Missing 'title' column in header.";
//                 else if (ingredientsCol == -1) reason = "Missing 'ingredients' column in header.";
//                 else if (directionsCol == -1) reason = "Missing 'directions' column in header.";
//                 else if (linkCol == -1) reason = "Missing 'link' column in header.";
//                 else if (sourceCol == -1) reason = "Missing 'source' column in header.";
//                 else if (nerCol == -1) reason = "Missing 'ner' column in header.";
//                 else
//                 {
//                     // CsvHelper handles column count issues better, but we can still check for nulls if fields are non-nullable
//                     // For now, assuming CsvHelper's default error handling is sufficient for basic malformed lines.
//                 }

//                 if (!string.IsNullOrEmpty(reason))
//                 {
//                     Console.WriteLine($"\nWarning: Skipping malformed or incomplete recipe line (ID: {record?.Id}). Reason: {reason}.");
//                     continue; // Skip to next line
//                 }

//                 var recipe = new Recipe();
//                 recipe.Id = record.Id;
//                 recipe.Title = record.Title?.Trim();
//                 recipe.Ingredients = record.Ingredients;
//                 recipe.Directions = record.Directions;
//                 recipe.Link = record.Link?.Trim();
//                 recipe.Source = record.Source?.Trim();
//                 recipe.Ner = record.Ner;

//                 recipes.Add(recipe);
//             }
//         }
//         Console.WriteLine(); // Newline after progress tracker
//         return recipes;
//     }


//     // --- Data Models for USDA CSVs (in-memory caches) ---

//     // Made public to resolve CS0246 error
//     public class ParsedIngredient
//     {
//         public string Name { get; set; }
//         public double Value { get; set; }
//         public string Unit { get; set; }
//     }

//     // Made public to resolve CS0246 error
//     public class Recipe
//     {
//         public int Id { get; set; }
//         public string Title { get; set; }
//         public List<string> Ingredients { get; set; } = new List<string>();
//         public List<string> Directions { get; set; } = new List<string>();
//         public string Link { get; set; }
//         public string Source { get; set; }
//         public List<string> Ner { get; set; } = new List<string>();
//     }
//     public class FoodItem
//     {
//         public int FdcId { get; set; }
//         public string DataType { get; set; } // e.g., "Branded", "Foundation", "Survey (FNDDS)", "SR Legacy"
//         public string Description { get; set; }
//         public string LowerCaseDescription { get; set; }
//         public string FoodCategory { get; set; }
//     }

//     public class FoodPortion
//     {
//         public int Id { get; set; }
//         public int FdcId { get; set; }
//         public double? Amount { get; set; } // Nullable
//         public int MeasureUnitId { get; set; }
//         public string PortionDescription { get; set; } // New property
//         public double? GramWeight { get; set; } // Nullable
//     }

//     public class FoodNutrient
//     {
//         public int Id { get; set; }
//         public int FdcId { get; set; }
//         public int NutrientId { get; set; }
//         public double Amount { get; set; } // Amount of nutrient per 100g of food
//     }

//     public class Nutrient // USDA Nutrient definition
//     {
//         public int Id { get; set; }
//         public string Name { get; set; }
//         public string UnitName { get; set; }
//     }

//     public class NutrientInfo // Combined nutrient information for display/transfer
//     {
//         public string NutrientName { get; set; }
//         public double Amount { get; set; }
//         public string UnitName { get; set; }
//     }

//     // --- CsvHelper Record Classes ---
//     // These classes represent the structure of your CSV files for CsvHelper to map.
//     // Ensure property names match CSV headers (case-insensitive due to config).

//     private class FoodCsvRecord
//     {
//         [Name("fdc_id")]
//         public int FdcId { get; set; }
//         [Name("data_type")]
//         public string DataType { get; set; }
//         [Name("description")]
//         public string Description { get; set; }
//         // LowerCaseDescription is derived, not directly from CSV
//         public string LowerCaseDescription => Description?.ToLowerInvariant();
//         [Name("food_category_id")]
//         public string FoodCategory { get; set; } // Optional, might be missing in some food.csv versions

//     }

//     private class FoodPortionCsvRecord
//     {
//         [Name("id")]
//         public int Id { get; set; }
//         [Name("fdc_id")]
//         public int FdcId { get; set; }
//         [Name("seq_num")]
//         public int? SeqNum { get; set; } // Made nullable to handle empty strings
//         [Name("amount")]
//         public double? Amount { get; set; }
//         [Name("measure_unit_id")]
//         public int MeasureUnitId { get; set; }
//         [Name("portion_description")]
//         public string PortionDescription { get; set; } // portion_description
//         [Name("data_points")]
//         public int? DataPoints { get; set; } // Made nullable
//         [Name("gram_weight")]
//         public double? GramWeight { get; set; } // Made nullable
//     }

//     private class MeasureUnitCsvRecord
//     {
//         [Name("id")]
//         public int Id { get; set; }
//         [Name("name")]
//         public string Name { get; set; }
//     }

//     private class NutrientCsvRecord
//     {
//         [Name("id")] public int Id { get; set; }
//         [Name("name")]
//         public string Name { get; set; }
//         [Name("unit_name")]
//         public string UnitName { get; set; } // unit_name
//     }

//     private class FoodNutrientCsvRecord
//     {
//         [Name("id")]
//         public int Id { get; set; }
//         [Name("fdc_id")]
//         public int FdcId { get; set; }
//         [Name("nutrient_id")]
//         public int NutrientId { get; set; }
//         [Name("amount")]
//         public double Amount { get; set; }
//     }

//     private class RecipeCsvRecord
//     {
//         [Name("id")] // Map to "id" column
//         public int Id { get; set; }
//         [Name("title")] // Map to "title" column
//         public string Title { get; set; }
//         [Name("ingredients")] // Map to "ingredients" column
//         public string IngredientsJson { get; set; } // Raw JSON string from CSV
//         [Name("directions")] // Map to "directions" column
//         public string DirectionsJson { get; set; } // Raw JSON string from CSV
//         [Name("link")] // Map to "link" column
//         public string Link { get; set; }
//         [Name("source")] // Map to "source" column
//         public string Source { get; set; }
//         [Name("ner")] // Map to "ner" column
//         public string NerJson { get; set; } // Raw JSON string from CSV

//         // Properties to expose deserialized lists
//         private List<string> _ingredients;
//         public List<string> Ingredients
//         {
//             get
//             {
//                 if (_ingredients == null && !string.IsNullOrWhiteSpace(IngredientsJson))
//                 {
//                     try
//                     {
//                         _ingredients = JsonSerializer.Deserialize<List<string>>(IngredientsJson);
//                     }
//                     catch (Exception ex)
//                     {
//                         Console.WriteLine($"Error deserializing ingredients JSON for recipe ID {Id}: {ex.Message}");
//                         _ingredients = new List<string>();
//                     }
//                 }
//                 return _ingredients ?? new List<string>();
//             }
//         }

//         private List<string> _directions;
//         public List<string> Directions
//         {
//             get
//             {
//                 if (_directions == null && !string.IsNullOrWhiteSpace(DirectionsJson))
//                 {
//                     try
//                     {
//                         _directions = JsonSerializer.Deserialize<List<string>>(DirectionsJson);
//                     }
//                     catch (Exception ex)
//                     {
//                         Console.WriteLine($"Error deserializing directions JSON for recipe ID {Id}: {ex.Message}");
//                         _directions = new List<string>();
//                     }
//                 }
//                 return _directions ?? new List<string>();
//             }
//         }

//         private List<string> _ner;
//         public List<string> Ner
//         {
//             get
//             {
//                 if (_ner == null && !string.IsNullOrWhiteSpace(NerJson))
//                 {
//                     try
//                     {
//                         _ner = JsonSerializer.Deserialize<List<string>>(NerJson);
//                     }
//                     catch (Exception ex)
//                     {
//                         Console.WriteLine($"Error deserializing NER JSON for recipe ID {Id}: {ex.Message}");
//                         _ner = new List<string>();
//                     }
//                 }
//                 return _ner ?? new List<string>();
//             }
//         }
//     }

//     // No header for these, so order matters or use index mapping if needed
//     private class IngredientMappingCsvRecord
//     {
//         [Name("raw_ingredient_phrase")]
//         public string OriginalPhrase { get; set; }
//         [Name("canonical_ingredient_name")]
//         public string CanonicalName { get; set; }
//     }

//     private class CleaningRuleCsvRecord
//     {
//         [CsvHelper.Configuration.Attributes.Index(0)]
//         public string Pattern { get; set; }
//         [CsvHelper.Configuration.Attributes.Index(1)]
//         public string Replacement { get; set; }
//     }


//     // --- Entity Framework Core Database Models ---
//     public class DbRecipe
//     {
//         public int Id { get; set; } // Primary Key
//         public string Title { get; set; }
//         public string Link { get; set; }
//         public string Source { get; set; }

//         public ICollection<DbRecipeIngredient> RecipeIngredients { get; set; }
//     }

//     public class DbIngredient
//     {
//         public int Id { get; set; } // Primary Key
//         public string Name { get; set; } // e.g., "brown sugar", "chicken breasts"

//         public ICollection<DbRecipeIngredient> RecipeIngredients { get; set; }
//     }

//     public class DbUnit
//     {
//         public int Id { get; set; } // Primary Key
//         public string Name { get; set; } // e.g., "cup", "tablespoon", "gram"

//         public ICollection<DbRecipeIngredient> RecipeIngredients { get; set; }
//     }

//     public class DbRecipeIngredient
//     {
//         public int RecipeId { get; set; } // Foreign Key to DbRecipe
//         public DbRecipe Recipe { get; set; }

//         public int IngredientId { get; set; } // Foreign Key to DbIngredient
//         public DbIngredient Ingredient { get; set; }

//         public double Value { get; set; } // e.g., 1.5 for "1 1/2 cups"

//         public int UnitId { get; set; } // Foreign Key to DbUnit
//         public DbUnit Unit { get; set; }

//         public double ApproximateWeightGrams { get; set; }
//     }

//     public class DbNutrient
//     {
//         public int Id { get; set; } // Primary Key
//         public string Name { get; set; }
//         public string UnitName { get; set; }

//         public ICollection<DbFoodNutrientData> FoodNutrientData { get; set; }
//     }

//     public class DbFoodNutrientData
//     {
//         public int Id { get; set; } // Primary Key
//         public int FdcId { get; set; } // The USDA FDC ID for the food item this nutrient data belongs to
//         public int NutrientId { get; set; } // Foreign Key to DbNutrient
//         public DbNutrient Nutrient { get; set; }

//         public double AmountPer100g { get; set; } // Amount of nutrient per 100g of the FDC food
//     }


//     // --- Entity Framework Core DbContext ---
//     public class RecipeDbContext : DbContext
//     {
//         public DbSet<DbRecipe> Recipes { get; set; }
//         public DbSet<DbIngredient> Ingredients { get; set; }
//         public DbSet<DbUnit> Units { get; set; }
//         public DbSet<DbRecipeIngredient> RecipeIngredients { get; set; }
//         public DbSet<DbNutrient> Nutrients { get; set; }
//         public DbSet<DbFoodNutrientData> FoodNutrientData { get; set; }


//         public RecipeDbContext(DbContextOptions<RecipeDbContext> options) : base(options) { }

//         protected override void OnModelCreating(ModelBuilder modelBuilder)
//         {
//             // Configure composite primary key for RecipeIngredients
//             modelBuilder.Entity<DbRecipeIngredient>()
//                 .HasKey(ri => new { ri.RecipeId, ri.IngredientId, ri.UnitId }); // UnitId added to key for uniqueness if same ingredient/recipe has different units

//             // Configure relationships
//             modelBuilder.Entity<DbRecipeIngredient>()
//                 .HasOne(ri => ri.Recipe)
//                 .WithMany(r => r.RecipeIngredients)
//                 .HasForeignKey(ri => ri.RecipeId);

//             modelBuilder.Entity<DbRecipeIngredient>()
//                 .HasOne(ri => ri.Ingredient)
//                 .WithMany(i => i.RecipeIngredients)
//                 .HasForeignKey(ri => ri.IngredientId);

//             modelBuilder.Entity<DbRecipeIngredient>()
//                 .HasOne(ri => ri.Unit)
//                 .WithMany(u => u.RecipeIngredients)
//                 .HasForeignKey(ri => ri.UnitId);

//             // Ensure unique names for Ingredients and Units
//             modelBuilder.Entity<DbIngredient>()
//                 .HasIndex(i => i.Name)
//                 .IsUnique();

//             modelBuilder.Entity<DbUnit>()
//                 .HasIndex(u => u.Name)
//                 .IsUnique();

//             // Configure composite primary key for FoodNutrientData if needed, or ensure uniqueness
//             modelBuilder.Entity<DbFoodNutrientData>()
//                 .HasIndex(fnd => new { fnd.FdcId, fnd.NutrientId })
//                 .IsUnique(); // Ensures one entry per FDC food for each nutrient

//             modelBuilder.Entity<DbFoodNutrientData>()
//                 .HasOne(fnd => fnd.Nutrient) // Corrected from fnd.Nutient
//                 .WithMany(n => n.FoodNutrientData)
//                 .HasForeignKey(fnd => fnd.NutrientId);

//             // You might want to add an index for FdcId in FoodNutrientData for faster lookups
//             modelBuilder.Entity<DbFoodNutrientData>()
//                 .HasIndex(fnd => fnd.FdcId);

//             // Configure DbNutrient to have unique Name and UnitName combination
//             modelBuilder.Entity<DbNutrient>()
//                 .HasIndex(n => new { n.Name, n.UnitName })
//                 .IsUnique();

//             base.OnModelCreating(modelBuilder);
//         }
//     }

//     // --- Design-time DbContext Factory ---
//     /// <summary>
//     /// This factory is used by the Entity Framework Core tools (e.g., dotnet ef migrations add)
//     /// to create an instance of RecipeDbContext at design time.
//     /// </summary>
//     public class RecipeDbContextFactory : IDesignTimeDbContextFactory<RecipeDbContext>
//     {
//         public RecipeDbContext CreateDbContext(string[] args)
//         {
//             // Build configuration for design-time context creation
//             var configuration = new ConfigurationBuilder()
//                 .SetBasePath(Directory.GetCurrentDirectory())
//                 .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true)
//                 .Build();

//             var optionsBuilder = new DbContextOptionsBuilder<RecipeDbContext>();
//             var connectionString = configuration.GetConnectionString("TmpConnection");

//             if (string.IsNullOrEmpty(connectionString))
//             {
//                 throw new InvalidOperationException("Could not find connection string 'TmpConnection' for design time.");
//             }

//             optionsBuilder.UseNpgsql(connectionString);

//             return new RecipeDbContext(optionsBuilder.Options);
//         }
//     }

//     // New: Class to represent a cleaning rule
//     public class CleaningRule
//     {
//         public string Pattern { get; set; }
//         public string Replacement { get; set; }
//     }
// }
