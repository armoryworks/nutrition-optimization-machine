using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api;
using Nom.Data;
using Nom.Orch.Models.Shopping;
using Xunit;

namespace Nom.Api.Tests.Integration;

public class ShoppingListIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ShoppingListIntegrationTests(WebApplicationFactory<Program> factory)
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
                    options.UseInMemoryDatabase("TestShoppingListDb");
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
    public async Task CreateShoppingList_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List",
            Description = "A test shopping list for integration testing",
            IsActive = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdShoppingList = await response.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();
        createdShoppingList!.Name.Should().Be("Test Shopping List");
        createdShoppingList.Description.Should().Be("A test shopping list for integration testing");
        createdShoppingList.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetShoppingList_WithValidId_ReturnsShoppingList()
    {
        // Arrange - First create a shopping list
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List for Get",
            Description = "A test shopping list for get operation",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        // Act
        var getResponse = await _client.GetAsync($"/api/shopping/{createdShoppingList!.Id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrievedShoppingList = await getResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        retrievedShoppingList.Should().NotBeNull();
        retrievedShoppingList!.Name.Should().Be("Test Shopping List for Get");
    }

    [Fact]
    public async Task UpdateShoppingList_WithValidData_ReturnsUpdatedShoppingList()
    {
        // Arrange - First create a shopping list
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Original Shopping List Name",
            Description = "Original description",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        // Act - Update the shopping list
        var updateRequest = new ShoppingListUpdateRequestModel
        {
            Name = "Updated Shopping List Name",
            Description = "Updated description",
            IsActive = false
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/shopping/{createdShoppingList!.Id}", updateRequest);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedShoppingList = await updateResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        updatedShoppingList.Should().NotBeNull();
        updatedShoppingList!.Name.Should().Be("Updated Shopping List Name");
        updatedShoppingList.Description.Should().Be("Updated description");
        updatedShoppingList.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteShoppingList_WithValidId_ReturnsNoContent()
    {
        // Arrange - First create a shopping list
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Shopping List to Delete",
            Description = "This shopping list will be deleted",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/shopping/{createdShoppingList!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the shopping list is actually deleted
        var getResponse = await _client.GetAsync($"/api/shopping/{createdShoppingList.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShoppingLists_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange - Create multiple shopping lists
        for (int i = 1; i <= 5; i++)
        {
            var shoppingListRequest = new ShoppingListCreateRequestModel
            {
                Name = $"Test Shopping List {i}",
                Description = $"Description for shopping list {i}",
                IsActive = true
            };

            await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        }

        // Act
        var response = await _client.GetAsync("/api/shopping?page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var shoppingLists = await response.Content.ReadFromJsonAsync<PaginatedResponseModel<ShoppingListModel>>();
        shoppingLists.Should().NotBeNull();
        shoppingLists!.Items.Should().HaveCount(3);
        shoppingLists.TotalCount.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task CreateShoppingItem_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange - First create a shopping list
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List for Item",
            Description = "A test shopping list for item creation",
            IsActive = true
        };

        var createShoppingListResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createShoppingListResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createShoppingListResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        // Create item request
        var itemRequest = new ShoppingItemCreateRequestModel
        {
            Name = "Test Shopping Item",
            Description = "A test shopping item",
            Quantity = 2,
            Unit = "pieces",
            IsCompleted = false,
            Notes = "Optional notes"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/shopping/{createdShoppingList!.Id}/items", itemRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdItem = await response.Content.ReadFromJsonAsync<ShoppingItemModel>();
        createdItem.Should().NotBeNull();
        createdItem!.Name.Should().Be("Test Shopping Item");
        createdItem.Description.Should().Be("A test shopping item");
        createdItem.Quantity.Should().Be(2);
        createdItem.Unit.Should().Be("pieces");
        createdItem.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetShoppingItems_WithValidShoppingListId_ReturnsItems()
    {
        // Arrange - First create a shopping list with items
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List for Items",
            Description = "A test shopping list for item listing",
            IsActive = true
        };

        var createShoppingListResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createShoppingListResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createShoppingListResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        // Add items
        for (int i = 1; i <= 3; i++)
        {
            var itemRequest = new ShoppingItemCreateRequestModel
            {
                Name = $"Test Shopping Item {i}",
                Description = $"Description for item {i}",
                Quantity = i,
                Unit = "pieces",
                IsCompleted = false
            };

            await _client.PostAsJsonAsync($"/api/shopping/{createdShoppingList!.Id}/items", itemRequest);
        }

        // Act
        var response = await _client.GetAsync($"/api/shopping/{createdShoppingList!.Id}/items");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<ShoppingItemModel>>();
        items.Should().NotBeNull();
        items!.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateShoppingItem_WithValidData_ReturnsUpdatedItem()
    {
        // Arrange - First create a shopping list and item
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List for Item Update",
            Description = "A test shopping list for item updates",
            IsActive = true
        };

        var createShoppingListResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createShoppingListResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createShoppingListResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        var itemRequest = new ShoppingItemCreateRequestModel
        {
            Name = "Original Item Name",
            Description = "Original description",
            Quantity = 1,
            Unit = "piece",
            IsCompleted = false
        };

        var createItemResponse = await _client.PostAsJsonAsync($"/api/shopping/{createdShoppingList!.Id}/items", itemRequest);
        createItemResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdItem = await createItemResponse.Content.ReadFromJsonAsync<ShoppingItemModel>();
        createdItem.Should().NotBeNull();

        // Act - Update the item
        var updateRequest = new ShoppingItemUpdateRequestModel
        {
            Name = "Updated Item Name",
            Description = "Updated description",
            Quantity = 3,
            Unit = "pieces",
            IsCompleted = true,
            Notes = "Updated notes"
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/shopping/{createdShoppingList.Id}/items/{createdItem!.Id}", updateRequest);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedItem = await updateResponse.Content.ReadFromJsonAsync<ShoppingItemModel>();
        updatedItem.Should().NotBeNull();
        updatedItem!.Name.Should().Be("Updated Item Name");
        updatedItem.Description.Should().Be("Updated description");
        updatedItem.Quantity.Should().Be(3);
        updatedItem.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteShoppingItem_WithValidId_ReturnsNoContent()
    {
        // Arrange - First create a shopping list and item
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "Test Shopping List for Item Delete",
            Description = "A test shopping list for item deletion",
            IsActive = true
        };

        var createShoppingListResponse = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);
        createShoppingListResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdShoppingList = await createShoppingListResponse.Content.ReadFromJsonAsync<ShoppingListModel>();
        createdShoppingList.Should().NotBeNull();

        var itemRequest = new ShoppingItemCreateRequestModel
        {
            Name = "Item to Delete",
            Description = "This item will be deleted",
            Quantity = 1,
            Unit = "piece",
            IsCompleted = false
        };

        var createItemResponse = await _client.PostAsJsonAsync($"/api/shopping/{createdShoppingList!.Id}/items", itemRequest);
        createItemResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdItem = await createItemResponse.Content.ReadFromJsonAsync<ShoppingItemModel>();
        createdItem.Should().NotBeNull();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/shopping/{createdShoppingList.Id}/items/{createdItem!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the item is actually deleted
        var getResponse = await _client.GetAsync($"/api/shopping/{createdShoppingList.Id}/items/{createdItem.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateShoppingList_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange
        var shoppingListRequest = new ShoppingListCreateRequestModel
        {
            Name = "", // Invalid: empty name
            Description = "A test shopping list",
            IsActive = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/shopping", shoppingListRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetShoppingList_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/shopping/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateShoppingList_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var updateRequest = new ShoppingListUpdateRequestModel
        {
            Name = "Updated Shopping List",
            Description = "Updated description",
            IsActive = true
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/shopping/99999", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteShoppingList_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/shopping/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
} 