# NOM Measurement System

## Overview

The NOM Measurement System is a comprehensive solution for managing measurement units, conversions, and unit preferences across the application. It replaces the previous reference-based measurement approach with a dedicated, extensible measurement domain.

## Architecture

### Table-Per-Hierarchy (TPH) Pattern

The measurement system uses Entity Framework Core's Table-Per-Hierarchy pattern to efficiently store different types of measurements in a single table while maintaining type-specific properties.

```
MeasurementEntity (abstract)
├── BaseMeasurementEntity (basic units)
├── IngredientMeasurementEntity (ingredient-specific)
└── NutrientMeasurementEntity (nutrient-specific)
```

### Core Entities

- **`MeasurementEntity`**: Abstract base class for all measurement types
- **`MeasurementCategoryEntity`**: Categories like Mass, Volume, Temperature, Count
- **`MeasurementConversionEntity`**: Conversion rules between units
- **`IngredientMeasurementEntity`**: Ingredient-specific measurement preferences
- **`NutrientMeasurementEntity`**: Nutrient-specific measurement standards

## Database Schema

### Tables

- **`measurement.Measurement`**: Main measurement table (TPH)
- **`measurement.MeasurementCategory`**: Measurement categories
- **`measurement.MeasurementConversion`**: Conversion rules
- **`measurement.IngredientMeasurement`**: Ingredient preferences
- **`measurement.NutrientMeasurement`**: Nutrient standards

### Key Properties

- **Base Units**: Each category has a base unit (e.g., gram for mass)
- **Conversion Factors**: Direct conversion factors between units
- **Multi-step Conversions**: BFS algorithm for complex conversion paths
- **Audit Trail**: Created/modified timestamps and user tracking

## Services

### MeasurementOrchestrationService

Core service for measurement operations:

- **CRUD Operations**: Create, read, update, delete measurements
- **Conversion Logic**: Direct, base unit, and multi-step conversions
- **Category Management**: Manage measurement categories
- **Ingredient/Nutrient Measurements**: Handle specialized measurement types

### Key Methods

```csharp
// Basic operations
Task<List<MeasurementModel>> GetMeasurementsByCategoryAsync(long categoryId);
Task<MeasurementModel?> GetMeasurementByIdAsync(long id);
Task<decimal> ConvertMeasurementAsync(long fromId, long toId, decimal value);

// Advanced features
Task<IngredientMeasurementModel> CreateIngredientMeasurementAsync(CreateIngredientMeasurementRequest request);
Task<NutrientMeasurementModel> CreateNutrientMeasurementAsync(CreateNutrientMeasurementRequest request);
```

## Conversion Algorithm

### Three-Tier Approach

1. **Direct Conversion**: Check for existing conversion rule
2. **Base Unit Conversion**: Convert via category's base unit
3. **Multi-step Path**: Use BFS algorithm to find conversion path

### Example Conversion Path

```
Ounces → Pounds → Kilograms → Grams
```

## API Endpoints

### Measurement Controller

- `GET /api/Measurement/category/{categoryId}` - Get measurements by category
- `GET /api/Measurement/convert` - Convert between units
- `POST /api/Measurement` - Create new measurement
- `POST /api/Measurement/conversion` - Create conversion rule

### Measurement Category Controller

- `GET /api/MeasurementCategory` - Get all categories
- `POST /api/MeasurementCategory` - Create new category

## Frontend Components

### Angular Module

- **Measurement Module**: Standalone Angular module
- **Measurement Service**: HTTP client for API communication
- **Measurement Converter Component**: UI for unit conversions
- **Models**: TypeScript interfaces for all measurement types

### Key Components

- `MeasurementConverterComponent`: Main conversion interface
- `MeasurementService`: API communication service
- `MeasurementCategoryService`: Category management service

## Data Seeding

### Initial Data

The system comes pre-seeded with:

- **Categories**: Mass, Volume, Temperature, Count
- **Base Units**: Gram, Milliliter, Celsius, Piece
- **Common Units**: Kilogram, Pound, Liter, Cup, Fahrenheit
- **Conversion Rules**: Common unit conversions

### Seeding Process

Use the `MeasurementSeeder` project to populate initial data:

```bash
cd MeasurementSeeder
dotnet run
```

## Testing

### Unit Tests

Comprehensive test coverage for:

- Service methods and business logic
- Conversion algorithms
- Entity validation
- Error handling

### Test Structure

```
Nom.Api.Tests/Services/Measurement/
└── MeasurementOrchestrationServiceTests.cs
```

## Migration

### Database Updates

The system includes migrations for:

- Initial schema creation
- Advanced measurement properties
- Data seeding and validation

### Migration Commands

```bash
# Create new migration
dotnet ef migrations add MigrationName --context ApplicationDbContext --project Nom.Data --startup-project Nom.Api

# Apply migrations
dotnet ef database update --context ApplicationDbContext --project Nom.Data --startup-project Nom.Api

# Or use the provided script
./refresh_db_and_migration.sh
```

## Configuration

### Connection Strings

Database connections are configured in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "NomConnection": "Host=localhost;Database=nomdb;Username=NomUser;Password=..."
  }
}
```

### Dependencies

- **Entity Framework Core**: ORM and migrations
- **PostgreSQL**: Database provider
- **ASP.NET Core**: Web framework
- **Angular**: Frontend framework

## Usage Examples

### Basic Conversion

```csharp
var result = await measurementService.ConvertMeasurementAsync(
    fromId: gramId,
    toId: poundId,
    value: 1000
);
// Returns: 2.20462 (1000 grams = 2.20462 pounds)
```

### Creating Ingredient Measurement

```csharp
var request = new CreateIngredientMeasurementRequest
{
    IngredientId = 1,
    PreferredMeasurementId = gramId,
    IsPreferred = true
};

var result = await measurementService.CreateIngredientMeasurementAsync(request);
```

## Best Practices

### Performance

- Use base unit conversions when possible
- Implement caching for frequently accessed conversions
- Optimize database queries with proper indexing

### Data Integrity

- Validate conversion factors
- Prevent circular conversion references
- Maintain audit trails for all changes

### Extensibility

- Follow TPH pattern for new measurement types
- Use interfaces for service contracts
- Implement proper error handling and logging

## Troubleshooting

### Common Issues

1. **Conversion Not Found**: Check if conversion rules exist
2. **Database Migration Errors**: Verify connection strings and permissions
3. **Test Failures**: Ensure test data includes required navigation properties

### Debug Tips

- Enable EF Core logging for SQL queries
- Use in-memory database for unit tests
- Check navigation property loading with `Include()` statements

## Future Enhancements

### Planned Features

- **Advanced Conversions**: Complex mathematical formulas
- **Unit Preferences**: User-specific measurement preferences
- **Bulk Operations**: Import/export measurement data
- **Performance Monitoring**: Conversion performance metrics

### Extension Points

- **Custom Conversion Rules**: User-defined conversions
- **Measurement Plugins**: Third-party measurement systems
- **Real-time Updates**: Live conversion rate updates

## Contributing

### Development Workflow

1. Create feature branch from main
2. Implement changes with tests
3. Update documentation
4. Submit pull request

### Code Standards

- Follow C# naming conventions
- Use async/await for all I/O operations
- Implement proper error handling
- Write comprehensive unit tests

## Support

For questions or issues:

1. Check this documentation
2. Review unit tests for examples
3. Check migration logs
4. Contact the development team

---

**Last Updated**: Today  
**Version**: 1.0.0  
**Status**: Production Ready

