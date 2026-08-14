using System.Runtime.CompilerServices;

// Expose internal members (e.g. MealPlanOrchestrationService.ApplyFoodGroupRulesAsync)
// to the test assembly so food-group top-up logic can be unit-tested directly without
// driving the full shuffle (which uses EF.Functions.Random, unsupported by InMemory).
[assembly: InternalsVisibleTo("Nom.Api.Tests")]
