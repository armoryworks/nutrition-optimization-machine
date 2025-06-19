using Nom.Data;
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nom.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "question");

            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "plan");

            migrationBuilder.EnsureSchema(
                name: "reference");

            migrationBuilder.EnsureSchema(
                name: "recipe");

            migrationBuilder.EnsureSchema(
                name: "nutrient");

            migrationBuilder.EnsureSchema(
                name: "shopping");

            migrationBuilder.EnsureSchema(
                name: "person");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Group",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Group", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredient",
                schema: "recipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredient", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nutrient",
                schema: "nutrient",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nutrient", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Person",
                schema: "person",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    InvitationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reference",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "auth",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "auth",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "auth",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutrientComponent",
                schema: "nutrient",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MacroNutrientId = table.Column<long>(type: "bigint", nullable: false),
                    MicroNutrientId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutrientComponent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutrientComponent_Nutrient_MacroNutrientId",
                        column: x => x.MacroNutrientId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NutrientComponent_Nutrient_MicroNutrientId",
                        column: x => x.MicroNutrientId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogEntry",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PropertyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OldValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByPersonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogEntry_Person_ChangedByPersonId",
                        column: x => x.ChangedByPersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Plan",
                schema: "plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByPersonId = table.Column<long>(type: "bigint", nullable: false),
                    InvitationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plan_Person_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingPreference",
                schema: "shopping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<long>(type: "bigint", nullable: false),
                    AutoGenerateShoppingList = table.Column<bool>(type: "boolean", nullable: false),
                    IncludePantryItems = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingPreference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingPreference_Person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IngredientNutrient",
                schema: "recipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IngredientId = table.Column<long>(type: "bigint", nullable: false),
                    NutrientId = table.Column<long>(type: "bigint", nullable: false),
                    MeasurementTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Measurement = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientNutrient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientNutrient_Ingredient_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "recipe",
                        principalTable: "Ingredient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IngredientNutrient_Nutrient_NutrientId",
                        column: x => x.NutrientId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IngredientNutrient_Reference_MeasurementTypeId",
                        column: x => x.MeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NutrientGuideline",
                schema: "nutrient",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuidelineBasisTypeId = table.Column<long>(type: "bigint", nullable: false),
                    MeasurementTypeId = table.Column<long>(type: "bigint", nullable: false),
                    MinimumMeasurement = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    MaximumMeasurement = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NutrientEntityId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutrientGuideline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NutrientGuideline_Nutrient_NutrientEntityId",
                        column: x => x.NutrientEntityId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NutrientGuideline_Reference_GuidelineBasisTypeId",
                        column: x => x.GuidelineBasisTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NutrientGuideline_Reference_MeasurementTypeId",
                        column: x => x.MeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonAttribute",
                schema: "person",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OnDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAttribute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonAttribute_Person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonAttribute_Reference_AttributeTypeId",
                        column: x => x.AttributeTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Question",
                schema: "question",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Hint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QuestionCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    AnswerTypeRefId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultAnswer = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    ValidationRegex = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Options = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    NextQuestionOnTrue = table.Column<long>(type: "bigint", nullable: true),
                    NextQuestionOnFalse = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Question", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Question_Group_QuestionCategoryId",
                        column: x => x.QuestionCategoryId,
                        principalSchema: "reference",
                        principalTable: "Group",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Question_Reference_AnswerTypeRefId",
                        column: x => x.AnswerTypeRefId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recipe",
                schema: "recipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    Instructions = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    QuantityMeasurementTypeId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
                    IsCurated = table.Column<bool>(type: "boolean", nullable: false),
                    CuratedById = table.Column<long>(type: "bigint", nullable: true),
                    CuratedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipe_Person_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Recipe_Person_CuratedById",
                        column: x => x.CuratedById,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Recipe_Reference_QuantityMeasurementTypeId",
                        column: x => x.QuantityMeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReferenceIndex",
                schema: "reference",
                columns: table => new
                {
                    ReferenceId = table.Column<long>(type: "bigint", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceIndex", x => new { x.ReferenceId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_ReferenceIndex_GroupEntity_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "reference",
                        principalTable: "Group",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReferenceIndex_ReferenceEntity_ReferenceId",
                        column: x => x.ReferenceId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingTrip",
                schema: "shopping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PersonId = table.Column<long>(type: "bigint", nullable: false),
                    StatusId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingTrip", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingTrip_Person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShoppingTrip_Reference_StatusId",
                        column: x => x.StatusId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Goal",
                schema: "plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: false),
                    GoalTypeId = table.Column<long>(type: "bigint", nullable: true),
                    BeginDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goal_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "plan",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Goal_Reference_GoalTypeId",
                        column: x => x.GoalTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Meal",
                schema: "plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    MealTypeId = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meal_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "plan",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Meal_Reference_MealTypeId",
                        column: x => x.MealTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanParticipant",
                schema: "plan",
                columns: table => new
                {
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    PersonId = table.Column<long>(type: "bigint", nullable: false),
                    RoleRefId = table.Column<long>(type: "bigint", nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByPersonId = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanParticipant", x => new { x.PlanId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_PlanParticipant_Person_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanParticipant_Person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanParticipant_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "plan",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanParticipant_Reference_RoleRefId",
                        column: x => x.RoleRefId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Restriction",
                schema: "plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<long>(type: "bigint", nullable: true),
                    PersonId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true),
                    RestrictionTypeId = table.Column<long>(type: "bigint", nullable: true),
                    IngredientId = table.Column<long>(type: "bigint", nullable: true),
                    NutrientId = table.Column<long>(type: "bigint", nullable: true),
                    BeginDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByPersonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Restriction", x => x.Id);
                    table.CheckConstraint("CHK_Restriction_PersonOrPlan", "\"PersonId\" IS NOT NULL OR \"PlanId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Restriction_Ingredient_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "recipe",
                        principalTable: "Ingredient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Restriction_Nutrient_NutrientId",
                        column: x => x.NutrientId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Restriction_Person_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Restriction_Person_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Restriction_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "plan",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Restriction_Reference_RestrictionTypeId",
                        column: x => x.RestrictionTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Answer",
                schema: "question",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<long>(type: "bigint", nullable: false),
                    AnswerText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByPersonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Answer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Answer_Person_CreatedByPersonId",
                        column: x => x.CreatedByPersonId,
                        principalSchema: "person",
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Answer_Question_QuestionId",
                        column: x => x.QuestionId,
                        principalSchema: "question",
                        principalTable: "Question",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_type_index",
                schema: "recipe",
                columns: table => new
                {
                    RecipeId = table.Column<long>(type: "bigint", nullable: false),
                    RecipeTypeId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_type_index", x => new { x.RecipeId, x.RecipeTypeId });
                    table.ForeignKey(
                        name: "FK_RecipeTypeIndex_RecipeEntity_RecipeId",
                        column: x => x.RecipeId,
                        principalSchema: "recipe",
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeTypeIndex_ReferenceEntity_RecipeTypeId",
                        column: x => x.RecipeTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredient",
                schema: "recipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipeId = table.Column<long>(type: "bigint", nullable: false),
                    IngredientId = table.Column<long>(type: "bigint", nullable: false),
                    MeasurementTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Measurement = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIngredient_Ingredient_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "recipe",
                        principalTable: "Ingredient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredient_Recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalSchema: "recipe",
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredient_Reference_MeasurementTypeId",
                        column: x => x.MeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeStep",
                schema: "recipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipeId = table.Column<long>(type: "bigint", nullable: false),
                    StepTypeId = table.Column<long>(type: "bigint", nullable: true),
                    Summary = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StepNumber = table.Column<byte>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeStep", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeStep_Recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalSchema: "recipe",
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeStep_Reference_StepTypeId",
                        column: x => x.StepTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PantryItem",
                schema: "shopping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    ShoppingTripId = table.Column<long>(type: "bigint", nullable: true),
                    IngredientId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    MeasurementTypeId = table.Column<long>(type: "bigint", nullable: false),
                    ItemStatusTypeId = table.Column<long>(type: "bigint", nullable: false),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceLocation = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PantryItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PantryItem_Ingredient_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "recipe",
                        principalTable: "Ingredient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PantryItem_Plan_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "plan",
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PantryItem_Reference_ItemStatusTypeId",
                        column: x => x.ItemStatusTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PantryItem_Reference_MeasurementTypeId",
                        column: x => x.MeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PantryItem_ShoppingTrip_ShoppingTripId",
                        column: x => x.ShoppingTripId,
                        principalSchema: "shopping",
                        principalTable: "ShoppingTrip",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GoalItem",
                schema: "plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GoalId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2047)", maxLength: 2047, nullable: false),
                    IsQuantifiable = table.Column<bool>(type: "boolean", nullable: false),
                    IngredientId = table.Column<long>(type: "bigint", nullable: true),
                    NutrientId = table.Column<long>(type: "bigint", nullable: true),
                    TimeframeTypeId = table.Column<long>(type: "bigint", nullable: true),
                    MeasurementTypeId = table.Column<long>(type: "bigint", nullable: true),
                    MeasurementMinimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MeasurementMaximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalItem_Goal_GoalId",
                        column: x => x.GoalId,
                        principalSchema: "plan",
                        principalTable: "Goal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalItem_Ingredient_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "recipe",
                        principalTable: "Ingredient",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GoalItem_Nutrient_NutrientId",
                        column: x => x.NutrientId,
                        principalSchema: "nutrient",
                        principalTable: "Nutrient",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GoalItem_Reference_MeasurementTypeId",
                        column: x => x.MeasurementTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GoalItem_Reference_TimeframeTypeId",
                        column: x => x.TimeframeTypeId,
                        principalSchema: "reference",
                        principalTable: "Reference",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MealEntityRecipeEntity",
                schema: "auth",
                columns: table => new
                {
                    MealsId = table.Column<long>(type: "bigint", nullable: false),
                    RecipesId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealEntityRecipeEntity", x => new { x.MealsId, x.RecipesId });
                    table.ForeignKey(
                        name: "FK_MealEntityRecipeEntity_Meal_MealsId",
                        column: x => x.MealsId,
                        principalSchema: "plan",
                        principalTable: "Meal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MealEntityRecipeEntity_Recipe_RecipesId",
                        column: x => x.RecipesId,
                        principalSchema: "recipe",
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopping_trip_meal_index",
                schema: "shopping",
                columns: table => new
                {
                    ShoppingTripId = table.Column<long>(type: "bigint", nullable: false),
                    MealId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_trip_meal_index", x => new { x.ShoppingTripId, x.MealId });
                    table.ForeignKey(
                        name: "FK_ShoppingTripMealIndex_MealEntity_MealId",
                        column: x => x.MealId,
                        principalSchema: "plan",
                        principalTable: "Meal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShoppingTripMealIndex_ShoppingTripEntity_ShoppingTripId",
                        column: x => x.ShoppingTripId,
                        principalSchema: "shopping",
                        principalTable: "ShoppingTrip",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Answer_CreatedByPersonId",
                schema: "question",
                table: "Answer",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Answer_QuestionId",
                schema: "question",
                table: "Answer",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "auth",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "auth",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "auth",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "auth",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "auth",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "auth",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "auth",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntry_ChangedByPersonId",
                schema: "audit",
                table: "AuditLogEntry",
                column: "ChangedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_GoalTypeId",
                schema: "plan",
                table: "Goal",
                column: "GoalTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Goal_PlanId",
                schema: "plan",
                table: "Goal",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalItem_GoalId",
                schema: "plan",
                table: "GoalItem",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalItem_IngredientId",
                schema: "plan",
                table: "GoalItem",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalItem_MeasurementTypeId",
                schema: "plan",
                table: "GoalItem",
                column: "MeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalItem_NutrientId",
                schema: "plan",
                table: "GoalItem",
                column: "NutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalItem_TimeframeTypeId",
                schema: "plan",
                table: "GoalItem",
                column: "TimeframeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientNutrient_IngredientId",
                schema: "recipe",
                table: "IngredientNutrient",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientNutrient_MeasurementTypeId",
                schema: "recipe",
                table: "IngredientNutrient",
                column: "MeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientNutrient_NutrientId",
                schema: "recipe",
                table: "IngredientNutrient",
                column: "NutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_Meal_MealTypeId",
                schema: "plan",
                table: "Meal",
                column: "MealTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Meal_PlanId",
                schema: "plan",
                table: "Meal",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MealEntityRecipeEntity_RecipesId",
                schema: "auth",
                table: "MealEntityRecipeEntity",
                column: "RecipesId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientComponent_MacroNutrientId",
                schema: "nutrient",
                table: "NutrientComponent",
                column: "MacroNutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientComponent_MicroNutrientId",
                schema: "nutrient",
                table: "NutrientComponent",
                column: "MicroNutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientGuideline_GuidelineBasisTypeId",
                schema: "nutrient",
                table: "NutrientGuideline",
                column: "GuidelineBasisTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientGuideline_MeasurementTypeId",
                schema: "nutrient",
                table: "NutrientGuideline",
                column: "MeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientGuideline_NutrientEntityId",
                schema: "nutrient",
                table: "NutrientGuideline",
                column: "NutrientEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_PantryItem_IngredientId",
                schema: "shopping",
                table: "PantryItem",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_PantryItem_ItemStatusTypeId",
                schema: "shopping",
                table: "PantryItem",
                column: "ItemStatusTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PantryItem_MeasurementTypeId",
                schema: "shopping",
                table: "PantryItem",
                column: "MeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PantryItem_PlanId",
                schema: "shopping",
                table: "PantryItem",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PantryItem_ShoppingTripId",
                schema: "shopping",
                table: "PantryItem",
                column: "ShoppingTripId");

            migrationBuilder.CreateIndex(
                name: "IX_Person_InvitationCode",
                schema: "person",
                table: "Person",
                column: "InvitationCode",
                unique: true,
                filter: "\"InvitationCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAttribute_AttributeTypeId",
                schema: "person",
                table: "PersonAttribute",
                column: "AttributeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAttribute_PersonId",
                schema: "person",
                table: "PersonAttribute",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Plan_CreatedByPersonId",
                schema: "plan",
                table: "Plan",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Plan_InvitationCode",
                schema: "plan",
                table: "Plan",
                column: "InvitationCode",
                unique: true,
                filter: "\"InvitationCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlanParticipant_CreatedByPersonId",
                schema: "plan",
                table: "PlanParticipant",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanParticipant_PersonId",
                schema: "plan",
                table: "PlanParticipant",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanParticipant_RoleRefId",
                schema: "plan",
                table: "PlanParticipant",
                column: "RoleRefId");

            migrationBuilder.CreateIndex(
                name: "IX_Question_AnswerTypeRefId",
                schema: "question",
                table: "Question",
                column: "AnswerTypeRefId");

            migrationBuilder.CreateIndex(
                name: "IX_Question_QuestionCategoryId",
                schema: "question",
                table: "Question",
                column: "QuestionCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipe_CreatedById",
                schema: "recipe",
                table: "Recipe",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Recipe_CuratedById",
                schema: "recipe",
                table: "Recipe",
                column: "CuratedById");

            migrationBuilder.CreateIndex(
                name: "IX_Recipe_QuantityMeasurementTypeId",
                schema: "recipe",
                table: "Recipe",
                column: "QuantityMeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_type_index_RecipeTypeId",
                schema: "recipe",
                table: "recipe_type_index",
                column: "RecipeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredient_IngredientId",
                schema: "recipe",
                table: "RecipeIngredient",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredient_MeasurementTypeId",
                schema: "recipe",
                table: "RecipeIngredient",
                column: "MeasurementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredient_RecipeId",
                schema: "recipe",
                table: "RecipeIngredient",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeStep_RecipeId",
                schema: "recipe",
                table: "RecipeStep",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeStep_StepTypeId",
                schema: "recipe",
                table: "RecipeStep",
                column: "StepTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceIndex_GroupId",
                schema: "reference",
                table: "ReferenceIndex",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_CreatedByPersonId",
                schema: "plan",
                table: "Restriction",
                column: "CreatedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_IngredientId",
                schema: "plan",
                table: "Restriction",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_NutrientId",
                schema: "plan",
                table: "Restriction",
                column: "NutrientId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_PersonId",
                schema: "plan",
                table: "Restriction",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_PlanId",
                schema: "plan",
                table: "Restriction",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Restriction_RestrictionTypeId",
                schema: "plan",
                table: "Restriction",
                column: "RestrictionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_trip_meal_index_MealId",
                schema: "shopping",
                table: "shopping_trip_meal_index",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingPreference_PersonId",
                schema: "shopping",
                table: "ShoppingPreference",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingTrip_PersonId",
                schema: "shopping",
                table: "ShoppingTrip",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingTrip_StatusId",
                schema: "shopping",
                table: "ShoppingTrip",
                column: "StatusId");
            migrationBuilder.ApplyCustomUpOperations();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.ApplyCustomDownOperations();
            migrationBuilder.DropTable(
                name: "Answer",
                schema: "question");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AuditLogEntry",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "GoalItem",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "IngredientNutrient",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "MealEntityRecipeEntity",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "NutrientComponent",
                schema: "nutrient");

            migrationBuilder.DropTable(
                name: "NutrientGuideline",
                schema: "nutrient");

            migrationBuilder.DropTable(
                name: "PantryItem",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "PersonAttribute",
                schema: "person");

            migrationBuilder.DropTable(
                name: "PlanParticipant",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "recipe_type_index",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "RecipeIngredient",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "RecipeStep",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "ReferenceIndex",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Restriction",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "shopping_trip_meal_index",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "ShoppingPreference",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "Question",
                schema: "question");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "Goal",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "Recipe",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "Ingredient",
                schema: "recipe");

            migrationBuilder.DropTable(
                name: "Nutrient",
                schema: "nutrient");

            migrationBuilder.DropTable(
                name: "Meal",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "ShoppingTrip",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "Group",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Plan",
                schema: "plan");

            migrationBuilder.DropTable(
                name: "Reference",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Person",
                schema: "person");
            migrationBuilder.ApplyCustomUpOperations();
        }
    }
}
