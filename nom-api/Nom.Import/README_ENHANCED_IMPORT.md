# Enhanced FDC Import System

## Overview

The Enhanced FDC Import System is a comprehensive upgrade to the original Nom.Import project that provides quality-filtered data import with performance optimization and advanced features.

## Key Features

### 🎯 Quality-First Approach

- **Foundation Foods Priority**: Focuses on 342 high-quality foundation foods
- **Quality Scoring**: Automated scoring based on data points, freshness, and food type
- **Filtering**: Removes low-quality branded foods and overly long names
- **Configurable Thresholds**: Adjustable quality filters for different use cases

### ⚡ Performance Optimization

- **Batch Processing**: Processes large datasets in configurable batches
- **Parallel Processing**: Uses multiple CPU cores for faster imports
- **Automatic Indexing**: Creates quality and performance indexes
- **Materialized Views**: Pre-computed views for common queries

### 📊 Comprehensive Data Support

- **Measurement System**: 123 measurement units and 47K portions
- **Food Categories**: 29 categories for better organization
- **Recipe Import**: 2.2M recipes with ingredient extraction (Phase 2)
- **Guidelines**: FDA dietary guidelines integration

### 🔧 Advanced Configuration

- **Flexible Settings**: Extensive configuration for different import scenarios
- **Selective Import**: Choose which data sources to import
- **Quality Thresholds**: Configurable quality filtering
- **Performance Tuning**: Adjustable batch sizes and parallelism

## Configuration

### Enhanced Settings Structure

```json
{
  "ImportSettings": {
    "UseEnhancedImport": true,
    "SourceDirectory": "path/to/your/dir",
    "QualityFilter": {
      "MinimumDataPoints": 1,
      "MinimumYearAcquired": 2010,
      "PrioritizeFoundationFoods": true,
      "MaximumIngredientNameLength": 150
    },
    "DataSources": {
      "ImportFoundationFoods": true,
      "ImportBrandedFoods": false,
      "ImportSurveyFoods": true,
      "ImportRecipes": false,
      "MaxIngredientsToImport": 10000
    },
    "Performance": {
      "BatchSize": 5000,
      "UseParallelProcessing": true,
      "MaxDegreeOfParallelism": 2
    }
  }
}
```

### Quality Scoring Algorithm

The enhanced system uses a weighted quality scoring algorithm:

```
Quality Score = (DataPoints * 0.3) + (DataFreshness * 0.2) + (FoodType * 0.3) + (NameQuality * 0.2)
```

Where:

- **DataPoints**: Number of supporting data points (higher = better)
- **DataFreshness**: Year of data acquisition (newer = better)
- **FoodType**: Foundation foods (0.9) > Survey foods (0.7) > Branded foods (0.5)
- **NameQuality**: Length and clarity of ingredient names

## Usage

### Phase 1: Foundation Enhancement (Current)

1. **Test the System**:

   ```bash
   cd nom-api/Nom.Import
   ./test_enhanced_import.sh
   ```

2. **Run Enhanced Import**:

   ```bash
   cd nom-api/Nom.Import
   dotnet run
   ```

3. **Validate Results**:

   ```sql
   -- Check quality distribution
   SELECT "FdcDataType", COUNT(*), AVG("QualityScore")
   FROM recipe."Ingredient"
   GROUP BY "FdcDataType";

   -- Check ingredient quality summary
   SELECT * FROM recipe."IngredientQualitySummary";
   ```

### Phase 2: Advanced Features (Next)

1. **Enable Recipe Import**:

   ```json
   "Recipe": {
     "ImportRecipes": true,
     "CategorizeRecipes": true,
     "ExtractIngredientsFromNER": true
   }
   ```

2. **Enable Advanced Features**:
   ```json
   "DataSources": {
     "ImportFoodAttributes": true,
     "MaxIngredientsToImport": 50000
   }
   ```

### Phase 3: Advanced Analytics (Future)

1. **Ingredient Substitution Engine**
2. **Seasonal Recommendations**
3. **Personalized Nutrition Algorithms**
4. **Community Features**

## File Structure

```
nom-api/Nom.Import/
├── Services/
│   ├── FdcFoodImporterService.cs          # Original service
│   └── EnhancedFdcImporterService.cs      # Enhanced service
├── Settings/
│   └── ImportSettings.cs                   # Enhanced settings
├── DataImportScripts/
│   ├── 01_create_staging_tables.sql       # Original staging
│   ├── 01_create_enhanced_staging_tables.sql  # Enhanced staging
│   ├── 03_transform_from_staging.sql      # Original transform
│   └── 03_transform_enhanced.sql          # Enhanced transform
├── appsettings.json                        # Original config
├── appsettings.enhanced.json              # Enhanced config
├── test_enhanced_import.sh                # Test script
└── README_ENHANCED_IMPORT.md              # This file
```

## Data Flow

### Enhanced Import Process

1. **Create Enhanced Staging Tables**

   - Quality-scored staging tables
   - Indexed for performance
   - Support for multiple data types

2. **Import Measurement Units & Categories**

   - 123 measurement units
   - 29 food categories
   - Reference table population

3. **Quality-Filtered Ingredient Import**

   - Foundation foods (priority)
   - Survey foods (medium priority)
   - Branded foods (if enabled)
   - Quality scoring and filtering

4. **Quality-Filtered Nutrient Import**

   - 479 nutrients with quality scoring
   - Rank-based quality assessment
   - Measurement unit mapping

5. **Quality-Filtered Relationships**

   - Food-nutrient relationships
   - Data point filtering
   - Year-based filtering

6. **Guidelines & Transformation**
   - FDA dietary guidelines
   - Quality indexes creation
   - Materialized views

## Quality Metrics

### Expected Results

After running the enhanced import, you should see:

- **Foundation Foods**: ~342 high-quality ingredients (quality score 0.8-0.9)
- **Survey Foods**: ~7,800 medium-quality ingredients (quality score 0.6-0.8)
- **Branded Foods**: Limited or none (quality score 0.4-0.6)
- **Total Ingredients**: 8,000-15,000 (vs 490K in original)
- **Nutrients**: 200-300 high-quality nutrients
- **Relationships**: 50K-100K quality-filtered relationships

### Quality Validation

```sql
-- Check quality distribution
SELECT
    "FdcDataType",
    COUNT(*) as total,
    AVG("QualityScore") as avg_quality,
    COUNT(CASE WHEN "QualityScore" >= 0.8 THEN 1 END) as high_quality,
    COUNT(CASE WHEN "QualityScore" >= 0.6 AND "QualityScore" < 0.8 THEN 1 END) as medium_quality
FROM recipe."Ingredient"
GROUP BY "FdcDataType"
ORDER BY avg_quality DESC;
```

## Performance Comparison

| Metric                 | Original Import | Enhanced Import |
| ---------------------- | --------------- | --------------- |
| **Data Sources**       | 4 files         | 8+ files        |
| **Quality Filtering**  | None            | Comprehensive   |
| **Processing Time**    | 30-60 minutes   | 5-15 minutes    |
| **Memory Usage**       | High            | Optimized       |
| **Result Quality**     | Mixed           | High            |
| **Search Performance** | Slow            | Fast            |

## Troubleshooting

### Common Issues

1. **Missing Source Files**:

   ```bash
   ls -la /path/to/your/source
   ```

2. **Database Connection Issues**:

   ```bash
   # Check connection string in appsettings.enhanced.json
   cat nom-api/Nom.Import/appsettings.enhanced.json
   ```

3. **Build Errors**:

   ```bash
   cd nom-api/Nom.Import
   dotnet clean
   dotnet build
   ```

4. **Import Failures**:
   ```bash
   # Check logs
   dotnet run --verbosity detailed
   ```

### Validation Commands

```bash
# Test the system
./test_enhanced_import.sh

# Check database after import
psql -h localhost -U nomuser -d nomdb -c "SELECT COUNT(*) FROM recipe.\"Ingredient\";"

# Check quality scores
psql -h localhost -U nomuser -d nomdb -c "SELECT \"FdcDataType\", COUNT(*), AVG(\"QualityScore\") FROM recipe.\"Ingredient\" GROUP BY \"FdcDataType\";"
```

## Migration from Original Import

### Step-by-Step Migration

1. **Backup Current Data**:

   ```sql
   CREATE TABLE recipe."Ingredient_Backup" AS SELECT * FROM recipe."Ingredient";
   ```

2. **Run Enhanced Import**:

   ```bash
   cd nom-api/Nom.Import
   dotnet run
   ```

3. **Compare Results**:

   ```sql
   SELECT
       'Original' as source,
       COUNT(*) as ingredient_count,
       NULL as avg_quality
   FROM recipe."Ingredient_Backup"
   UNION ALL
   SELECT
       'Enhanced' as source,
       COUNT(*) as ingredient_count,
       AVG("QualityScore") as avg_quality
   FROM recipe."Ingredient";
   ```

4. **Switch to Enhanced Data**:
   ```sql
   -- After validation, drop backup
   DROP TABLE recipe."Ingredient_Backup";
   ```

## Future Enhancements

### Planned Features

1. **Recipe Import System**

   - 2.2M recipe import
   - Ingredient extraction from NER
   - Recipe categorization

2. **Advanced Search**

   - Semantic search
   - Fuzzy matching
   - Ingredient aliases

3. **Performance Optimization**

   - Materialized views
   - Query optimization
   - Caching strategies

4. **Community Features**
   - Recipe sharing
   - Quality curation
   - User feedback

## Support

For issues or questions about the enhanced import system:

1. Check the test script output: `./test_enhanced_import.sh`
2. Review the logs: `dotnet run --verbosity detailed`
3. Validate database state: Check quality summary views
4. Compare with original import: Use migration commands above

The enhanced import system provides a solid foundation for high-quality, performant nutritional data that will significantly improve your application's user experience and data reliability.
