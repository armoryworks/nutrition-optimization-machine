using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Integration;

public class RecipeManagementIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RecipeManagementIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the database with an in-memory database for testing
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestRecipeDb");
                });

                // Ensure the database is created
                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateRecipe_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange
        var recipeRequest = new RecipeCreateRequestModel
        {
            Name = "Test Recipe",
            Description = "A test recipe for integration testing",
            Instructions = "1. Mix ingredients\n2. Cook for 30 minutes\n3. Serve hot",
            PrepTimeMinutes = 15,
            CookTimeMinutes = 30,
            TotalTimeMinutes = 45,
            Servings = 4,
            ServingsText = "4 servings",
            RecipeYield = "4 servings",
            RecipeCategory = "Main Course",
            Tags = new List<string> { "test", "integration" },
            RecipeIngredient = new List<RecipeIngredientCreateRequestModel>
            {
                new()
                {
                    Title = "Test Ingredient",
                    Note = "Optional note",
                    Unit = "cup",
                    Food = "flour",
                    DisableAmount = false,
                    Amount = 2.0
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recipe", recipeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var responseContent = await response.Content.ReadAsStringAsync();
        var createdRecipe = JsonSerializer.Deserialize<RecipeModel>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        createdRecipe.Should().NotBeNull();
        createdRecipe!.Name.Should().Be("Test Recipe");
        createdRecipe.Description.Should().Be("A test recipe for integration testing");
        createdRecipe.RecipeIngredient.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRecipe_WithValidId_ReturnsRecipe()
    {
        // Arrange - First create a recipe
        var recipeRequest = new RecipeCreateRequestModel
        {
            Name = "Test Recipe for Get",
            Description = "A test recipe for get operation",
            Instructions = "1. Mix ingredients\n2. Cook",
            PrepTimeMinutes = 10,
            CookTimeMinutes = 20,
            TotalTimeMinutes = 30,
            Servings = 2,
            ServingsText = "2 servings",
            RecipeYield = "2 servings",
            RecipeCategory = "Appetizer"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/recipe", recipeRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdRecipe = await createResponse.Content.ReadFromJsonAsync<RecipeModel>();
        createdRecipe.Should().NotBeNull();

        // Act
        var getResponse = await _client.GetAsync($"/api/recipe/{createdRecipe!.Id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrievedRecipe = await getResponse.Content.ReadFromJsonAsync<RecipeModel>();
        retrievedRecipe.Should().NotBeNull();
        retrievedRecipe!.Name.Should().Be("Test Recipe for Get");
    }

    [Fact]
    public async Task UpdateRecipe_WithValidData_ReturnsUpdatedRecipe()
    {
        // Arrange - First create a recipe
        var recipeRequest = new RecipeCreateRequestModel
        {
            Name = "Original Recipe Name",
            Description = "Original description",
            Instructions = "Original instructions",
            PrepTimeMinutes = 10,
            CookTimeMinutes = 20,
            TotalTimeMinutes = 30,
            Servings = 2,
            ServingsText = "2 servings",
            RecipeYield = "2 servings",
            RecipeCategory = "Appetizer"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/recipe", recipeRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdRecipe = await createResponse.Content.ReadFromJsonAsync<RecipeModel>();
        createdRecipe.Should().NotBeNull();

        // Act - Update the recipe
        var updateRequest = new RecipeUpdateRequestModel
        {
            Name = "Updated Recipe Name",
            Description = "Updated description",
            Instructions = "Updated instructions",
            PrepTimeMinutes = 15,
            CookTimeMinutes = 25,
            TotalTimeMinutes = 40,
            Servings = 4,
            ServingsText = "4 servings",
            RecipeYield = "4 servings",
            RecipeCategory = "Main Course"
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/recipe/{createdRecipe!.Id}", updateRequest);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedRecipe = await updateResponse.Content.ReadFromJsonAsync<RecipeModel>();
        updatedRecipe.Should().NotBeNull();
        updatedRecipe!.Name.Should().Be("Updated Recipe Name");
        updatedRecipe.Description.Should().Be("Updated description");
        updatedRecipe.Servings.Should().Be(4);
    }

    [Fact]
    public async Task DeleteRecipe_WithValidId_ReturnsNoContent()
    {
        // Arrange - First create a recipe
        var recipeRequest = new RecipeCreateRequestModel
        {
            Name = "Recipe to Delete",
            Description = "This recipe will be deleted",
            Instructions = "Instructions",
            PrepTimeMinutes = 10,
            CookTimeMinutes = 20,
            TotalTimeMinutes = 30,
            Servings = 2,
            ServingsText = "2 servings",
            RecipeYield = "2 servings",
            RecipeCategory = "Appetizer"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/recipe", recipeRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdRecipe = await createResponse.Content.ReadFromJsonAsync<RecipeModel>();
        createdRecipe.Should().NotBeNull();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/recipe/{createdRecipe!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the recipe is actually deleted
        var getResponse = await _client.GetAsync($"/api/recipe/{createdRecipe.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecipes_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange - Create multiple recipes
        for (int i = 1; i <= 5; i++)
        {
            var recipeRequest = new RecipeCreateRequestModel
            {
                Name = $"Test Recipe {i}",
                Description = $"Description for recipe {i}",
                Instructions = $"Instructions for recipe {i}",
                PrepTimeMinutes = 10,
                CookTimeMinutes = 20,
                TotalTimeMinutes = 30,
                Servings = 2,
                ServingsText = "2 servings",
                RecipeYield = "2 servings",
                RecipeCategory = "Appetizer"
            };

            await _client.PostAsJsonAsync("/api/recipe", recipeRequest);
        }

        // Act
        var response = await _client.GetAsync("/api/recipe?page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var recipes = await response.Content.ReadFromJsonAsync<PaginatedResponseModel<RecipeModel>>();
        recipes.Should().NotBeNull();
        recipes!.Items.Should().HaveCount(3);
        recipes.TotalCount.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task CreateRecipe_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange
        var recipeRequest = new RecipeCreateRequestModel
        {
            Name = "", // Invalid: empty name
            Description = "A test recipe",
            Instructions = "Instructions",
            PrepTimeMinutes = -1, // Invalid: negative time
            CookTimeMinutes = 30,
            TotalTimeMinutes = 30,
            Servings = 0, // Invalid: zero servings
            ServingsText = "0 servings",
            RecipeYield = "0 servings",
            RecipeCategory = "Appetizer"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recipe", recipeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRecipe_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/recipe/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateRecipe_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var updateRequest = new RecipeUpdateRequestModel
        {
            Name = "Updated Recipe",
            Description = "Updated description",
            Instructions = "Updated instructions",
            PrepTimeMinutes = 10,
            CookTimeMinutes = 20,
            TotalTimeMinutes = 30,
            Servings = 2,
            ServingsText = "2 servings",
            RecipeYield = "2 servings",
            RecipeCategory = "Appetizer"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/recipe/99999", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRecipe_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/recipe/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class PaginatedResponseModel<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
} 