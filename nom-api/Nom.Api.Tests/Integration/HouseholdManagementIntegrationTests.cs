using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api;
using Nom.Data;
using Nom.Orch.Models.Household;
using Xunit;

namespace Nom.Api.Tests.Integration;

public class HouseholdManagementIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HouseholdManagementIntegrationTests(WebApplicationFactory<Program> factory)
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
                    options.UseInMemoryDatabase("TestHouseholdDb");
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
    public async Task CreateHousehold_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Test Household",
            Description = "A test household for integration testing",
            IsActive = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/household", householdRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdHousehold = await response.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();
        createdHousehold!.Name.Should().Be("Test Household");
        createdHousehold.Description.Should().Be("A test household for integration testing");
        createdHousehold.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetHousehold_WithValidId_ReturnsHousehold()
    {
        // Arrange - First create a household
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Test Household for Get",
            Description = "A test household for get operation",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Act
        var getResponse = await _client.GetAsync($"/api/household/{createdHousehold!.Id}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrievedHousehold = await getResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        retrievedHousehold.Should().NotBeNull();
        retrievedHousehold!.Name.Should().Be("Test Household for Get");
    }

    [Fact]
    public async Task UpdateHousehold_WithValidData_ReturnsUpdatedHousehold()
    {
        // Arrange - First create a household
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Original Household Name",
            Description = "Original description",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Act - Update the household
        var updateRequest = new HouseholdUpdateRequestModel
        {
            Name = "Updated Household Name",
            Description = "Updated description",
            IsActive = false
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/household/{createdHousehold!.Id}", updateRequest);

        // Assert
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedHousehold = await updateResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        updatedHousehold.Should().NotBeNull();
        updatedHousehold!.Name.Should().Be("Updated Household Name");
        updatedHousehold.Description.Should().Be("Updated description");
        updatedHousehold.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteHousehold_WithValidId_ReturnsNoContent()
    {
        // Arrange - First create a household
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Household to Delete",
            Description = "This household will be deleted",
            IsActive = true
        };

        var createResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/household/{createdHousehold!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the household is actually deleted
        var getResponse = await _client.GetAsync($"/api/household/{createdHousehold.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHouseholds_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange - Create multiple households
        for (int i = 1; i <= 5; i++)
        {
            var householdRequest = new HouseholdCreateRequestModel
            {
                Name = $"Test Household {i}",
                Description = $"Description for household {i}",
                IsActive = true
            };

            await _client.PostAsJsonAsync("/api/household", householdRequest);
        }

        // Act
        var response = await _client.GetAsync("/api/household?page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var households = await response.Content.ReadFromJsonAsync<PaginatedResponseModel<HouseholdModel>>();
        households.Should().NotBeNull();
        households!.Items.Should().HaveCount(3);
        households.TotalCount.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task CreateHouseholdMember_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange - First create a household
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Test Household for Member",
            Description = "A test household for member creation",
            IsActive = true
        };

        var createHouseholdResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createHouseholdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createHouseholdResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Create member request
        var memberRequest = new HouseholdMemberCreateRequestModel
        {
            PersonName = "Test Member",
            Email = "testmember@example.com",
            Role = "Member",
            IsActive = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/household/{createdHousehold!.Id}/members", memberRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdMember = await response.Content.ReadFromJsonAsync<HouseholdMemberModel>();
        createdMember.Should().NotBeNull();
        createdMember!.PersonName.Should().Be("Test Member");
        createdMember.Email.Should().Be("testmember@example.com");
        createdMember.Role.Should().Be("Member");
    }

    [Fact]
    public async Task GetHouseholdMembers_WithValidHouseholdId_ReturnsMembers()
    {
        // Arrange - First create a household with members
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Test Household for Members",
            Description = "A test household for member listing",
            IsActive = true
        };

        var createHouseholdResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createHouseholdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createHouseholdResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Add members
        for (int i = 1; i <= 3; i++)
        {
            var memberRequest = new HouseholdMemberCreateRequestModel
            {
                PersonName = $"Test Member {i}",
                Email = $"testmember{i}@example.com",
                Role = "Member",
                IsActive = true
            };

            await _client.PostAsJsonAsync($"/api/household/{createdHousehold!.Id}/members", memberRequest);
        }

        // Act
        var response = await _client.GetAsync($"/api/household/{createdHousehold!.Id}/members");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = await response.Content.ReadFromJsonAsync<List<HouseholdMemberModel>>();
        members.Should().NotBeNull();
        members!.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateHouseholdInvitation_WithValidData_ReturnsCreatedResponse()
    {
        // Arrange - First create a household
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "Test Household for Invitation",
            Description = "A test household for invitation creation",
            IsActive = true
        };

        var createHouseholdResponse = await _client.PostAsJsonAsync("/api/household", householdRequest);
        createHouseholdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var createdHousehold = await createHouseholdResponse.Content.ReadFromJsonAsync<HouseholdModel>();
        createdHousehold.Should().NotBeNull();

        // Create invitation request
        var invitationRequest = new HouseholdInvitationCreateRequestModel
        {
            Email = "invitee@example.com",
            Message = "Please join our household",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/household/{createdHousehold!.Id}/invitations", invitationRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdInvitation = await response.Content.ReadFromJsonAsync<HouseholdInvitationModel>();
        createdInvitation.Should().NotBeNull();
        createdInvitation!.Email.Should().Be("invitee@example.com");
        createdInvitation.Message.Should().Be("Please join our household");
    }

    [Fact]
    public async Task CreateHousehold_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange
        var householdRequest = new HouseholdCreateRequestModel
        {
            Name = "", // Invalid: empty name
            Description = "A test household",
            IsActive = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/household", householdRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHousehold_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/household/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateHousehold_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var updateRequest = new HouseholdUpdateRequestModel
        {
            Name = "Updated Household",
            Description = "Updated description",
            IsActive = true
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/household/99999", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteHousehold_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/household/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
} 