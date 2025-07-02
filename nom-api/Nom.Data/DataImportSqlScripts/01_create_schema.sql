-- 01_create_schema.sql
-- This script sets up the database schema for the Nom application,
-- including schemas, tables, and initial reference data.

-- Create Schemas
CREATE SCHEMA IF NOT EXISTS reference;
CREATE SCHEMA IF NOT EXISTS recipe;
CREATE SCHEMA IF NOT EXISTS nutrition;
CREATE SCHEMA IF NOT EXISTS identity;

-- Create Tables with appropriate schemas
-- reference.Group
CREATE TABLE IF NOT EXISTS reference."Group" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL UNIQUE,
    "Description" TEXT,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT
);

-- reference.Reference
CREATE TABLE IF NOT EXISTS reference."Reference" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL UNIQUE,
    "Description" TEXT,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT
);

-- reference.ReferenceIndex (linking Groups and References)
CREATE TABLE IF NOT EXISTS reference."ReferenceIndex" (
    "Id" BIGSERIAL PRIMARY KEY,
    "GroupId" BIGINT NOT NULL,
    "ReferenceId" BIGINT NOT NULL,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_group
        FOREIGN KEY ("GroupId")
        REFERENCES reference."Group"("Id"),
    CONSTRAINT fk_reference
        FOREIGN KEY ("ReferenceId")
        REFERENCES reference."Reference"("Id"),
    CONSTRAINT uc_group_reference UNIQUE ("GroupId", "ReferenceId")
);

-- identity.Person
CREATE TABLE IF NOT EXISTS identity."Person" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Auth0Id" VARCHAR(255) UNIQUE, -- Auth0 user ID
    "Email" VARCHAR(255) UNIQUE NOT NULL,
    "Username" VARCHAR(255) UNIQUE,
    "FirstName" VARCHAR(255),
    "LastName" VARCHAR(255),
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id")
);


-- recipe.Recipe
CREATE TABLE IF NOT EXISTS recipe."Recipe" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Name" VARCHAR(500) NOT NULL UNIQUE, -- Increased length for recipe names
    "Description" TEXT,
    "Instructions" TEXT, -- Changed from VARCHAR(4000) to TEXT
    "PrepTimeMinutes" INT,
    "CookTimeMinutes" INT,
    "Servings" DECIMAL(10, 2),
    "ServingQuantity" DECIMAL(10, 2),
    "ServingQuantityMeasurementTypeId" BIGINT,
    "RawIngredientsString" TEXT, -- Changed from VARCHAR(4000) to TEXT
    "IsCurated" BOOLEAN NOT NULL DEFAULT FALSE,
    "CuratedById" BIGINT,
    "CuratedDate" TIMESTAMP WITH TIME ZONE,
    "SourceUrl" VARCHAR(2048), -- Standard URL max length
    "SourceSite" VARCHAR(255),
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_serving_quantity_measurement_type FOREIGN KEY ("ServingQuantityMeasurementTypeId") REFERENCES reference."Reference"("Id"),
    CONSTRAINT fk_curated_by_person FOREIGN KEY ("CuratedById") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id")
);

-- recipe.Ingredient
CREATE TABLE IF NOT EXISTS recipe."Ingredient" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Name" VARCHAR(500) NOT NULL UNIQUE,
    "Description" TEXT,
    "FdcId" BIGINT UNIQUE,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id")
);

-- recipe.RecipeIngredient
CREATE TABLE IF NOT EXISTS recipe."RecipeIngredient" (
    "Id" BIGSERIAL PRIMARY KEY,
    "RecipeId" BIGINT NOT NULL,
    "IngredientId" BIGINT NOT NULL,
    "Quantity" DECIMAL(10, 4),
    "MeasurementTypeId" BIGINT,
    "RawLine" TEXT,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_recipe FOREIGN KEY ("RecipeId") REFERENCES recipe."Recipe"("Id") ON DELETE CASCADE,
    CONSTRAINT fk_ingredient FOREIGN KEY ("IngredientId") REFERENCES recipe."Ingredient"("Id"),
    CONSTRAINT fk_measurement_type FOREIGN KEY ("MeasurementTypeId") REFERENCES reference."Reference"("Id"),
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT uc_recipe_ingredient UNIQUE ("RecipeId", "IngredientId")
);

-- recipe.RecipeStep
CREATE TABLE IF NOT EXISTS recipe."RecipeStep" (
    "Id" BIGSERIAL PRIMARY KEY,
    "RecipeId" BIGINT NOT NULL,
    "StepNumber" INT NOT NULL,
    "Summary" VARCHAR(255),
    "Description" TEXT NOT NULL,
    "StepTypeId" BIGINT,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_recipe FOREIGN KEY ("RecipeId") REFERENCES recipe."Recipe"("Id") ON DELETE CASCADE,
    CONSTRAINT fk_step_type FOREIGN KEY ("StepTypeId") REFERENCES reference."Reference"("Id"),
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT uc_recipe_step UNIQUE ("RecipeId", "StepNumber")
);

-- nutrition.Nutrient
CREATE TABLE IF NOT EXISTS nutrition."Nutrient" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL UNIQUE,
    "UnitTypeId" BIGINT NOT NULL,
    "Description" TEXT,
    "FdcId" BIGINT UNIQUE,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_unit_type FOREIGN KEY ("UnitTypeId") REFERENCES reference."Reference"("Id"),
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id")
);

-- nutrition.Food
CREATE TABLE IF NOT EXISTS nutrition."Food" (
    "Id" BIGSERIAL PRIMARY KEY,
    "Description" VARCHAR(1000) NOT NULL UNIQUE,
    "FdcId" BIGINT UNIQUE,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id")
);

-- nutrition.FoodNutrient
CREATE TABLE IF NOT EXISTS nutrition."FoodNutrient" (
    "Id" BIGSERIAL PRIMARY KEY,
    "FoodId" BIGINT NOT NULL,
    "NutrientId" BIGINT NOT NULL,
    "Amount" DECIMAL(10, 4) NOT NULL,
    "CreatedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByPersonId" BIGINT,
    "LastModifiedDate" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastModifiedByPersonId" BIGINT,
    CONSTRAINT fk_food FOREIGN KEY ("FoodId") REFERENCES nutrition."Food"("Id") ON DELETE CASCADE,
    CONSTRAINT fk_nutrient FOREIGN KEY ("NutrientId") REFERENCES nutrition."Nutrient"("Id"),
    CONSTRAINT fk_created_by_person FOREIGN KEY ("CreatedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT fk_last_modified_by_person FOREIGN KEY ("LastModifiedByPersonId") REFERENCES identity."Person"("Id"),
    CONSTRAINT uc_food_nutrient UNIQUE ("FoodId", "NutrientId")
);
