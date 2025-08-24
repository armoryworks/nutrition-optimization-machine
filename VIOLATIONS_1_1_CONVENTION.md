# Files Violating 1:1 Convention

## Overview

This document identifies all files that contain multiple classes, interfaces, or models in violation of the established 1:1 file convention in the NOM project.

## TypeScript Files (Frontend)

### 1. `nom-ui/src/app/privacy/models/i-privacy-analytics.model.ts`

**Violations**: 5 models in 1 file

- `PrivacyAnalyticsModel` interface
- `ProcessingPurposeModel` interface
- `RiskFactorModel` interface
- `ComplianceMetricModel` interface
- `DataProcessingLogModel` interface

### 2. `nom-ui/src/app/measurement/models/measurement.model.ts`

**Violations**: 9 models in 1 file

- `MeasurementModel` interface
- `MeasurementCategoryModel` interface
- `MeasurementConversionModel` interface
- `IngredientMeasurementModel` interface
- `NutrientMeasurementModel` interface
- `CreateMeasurementRequest` interface
- `UpdateMeasurementRequest` interface
- `CreateConversionRequest` interface
- `CreateCategoryRequest` interface
- `UpdateCategoryRequest` interface

### 3. `nom-ui/src/app/person/models/person.model.ts`

**Violations**: 2 models in 1 file

- `IPersonModel` interface
- `PersonModel` class

### 4. `nom-ui/src/app/shopping/components/shopping-item-dialog/shopping-item-dialog.component.ts`

**Violations**: 3 models in 1 file

- `ShoppingItemDialogData` interface
- `ShoppingItemFormData` interface
- `ShoppingItemDialogComponent` class

### 5. `nom-ui/src/app/recipe/components/ingredient-form/ingredient-form.component.ts`

**Violations**: 3 models in 1 file

- `IngredientFormData` interface
- `IngredientFormConfig` interface
- `IngredientFormComponent` class

### 6. `nom-ui/src/app/recipe/components/ingredient-create-modal/ingredient-create-modal.component.ts`

**Violations**: 2 models in 1 file

- `IngredientCreateModalData` interface
- `IngredientCreateModalComponent` class

### 7. `nom-ui/src/app/utilities/services/event-bus.service.ts`

**Violations**: 2 models in 1 file

- `AppEvent` interface
- `EventBusService` class

### 8. `nom-ui/src/app/utilities/services/user-info.service.ts`

**Violations**: 3 models in 1 file

- `UserClaim` interface
- `UserInfo` interface
- `UserInfoService` class

## C# Files (Backend)

### 1. `nom-api/Nom.Orch/UtilityServices/AdvancedMonitoringService.cs`

**Violations**: 5 models in 1 file

- `AdvancedMonitoringService` class
- `SecurityEvent` class
- `SecurityStatistics` class
- `SecurityEventType` enum
- `SecurityEventSeverity` enum

### 2. `nom-api/Nom.Orch/UtilityServices/VulnerabilityScanningService.cs`

**Violations**: 5 models in 1 file

- `VulnerabilityScanningService` class
- `VulnerabilityScanReport` class
- `Vulnerability` class
- `VulnerabilitySeverity` enum
- `VulnerabilityStatus` enum

### 3. `nom-api/Nom.Orch/UtilityServices/DataRetentionService.cs`

**Violations**: 4 models in 1 file

- `DataRetentionService` class
- `DataRetentionReport` class
- `CleanupResult` class
- `DataRetentionStatistics` class

### 4. `nom-api/Nom.Orch/UtilityServices/SessionManagementService.cs`

**Violations**: 3 models in 1 file

- `SessionManagementService` class
- `SessionInfo` class
- `SessionStatistics` class

### 5. `nom-api/Nom.Orch/UtilityInterfaces/IWebScrapingService.cs`

**Violations**: 2 models in 1 file

- `IWebScrapingService` interface
- `ScrapedRecipeData` class

### 6. `nom-api/Nom.Orch/UtilityInterfaces/ITesseractOcrService.cs`

**Violations**: 2 models in 1 file

- `ITesseractOcrService` interface
- `OcrRecipeData` class

### 7. `nom-api/Nom.Api/Events/IEventBus.cs`

**Violations**: 6 models in 1 file

- `IEvent` interface
- `IEventHandler<TEvent>` interface
- `BaseEventHandler<TEvent>` abstract class
- `IEventBus` interface
- `EventBusOptions` class
- `EventBusStatistics` class

## Summary

**Total Files Violating 1:1 Convention**: 15
**Total Models/Classes/Interfaces**: 73
**Average Violations per File**: 4.9

## Priority for Fixing

### High Priority (Frontend - TypeScript)

1. `measurement.model.ts` - 9 models (most violations)
2. `i-privacy-analytics.model.ts` - 5 models
3. `person.model.ts` - 2 models
4. Component files with multiple interfaces

### Medium Priority (Backend - C#)

1. `IEventBus.cs` - 6 models (newly discovered)
2. `AdvancedMonitoringService.cs` - 5 models
3. `VulnerabilityScanningService.cs` - 5 models
4. `DataRetentionService.cs` - 4 models
5. `SessionManagementService.cs` - 3 models

### Low Priority (Backend - C#)

1. Interface files with single additional class
2. Service files with single additional enum

## Additional Violation Types Found

### Classes Mixed with Enums

- `AdvancedMonitoringService.cs` - Class + 2 enums
- `VulnerabilityScanningService.cs` - Class + 2 enums

### Multiple Interfaces + Classes

- `IEventBus.cs` - 3 interfaces + 2 classes + 1 abstract class

### Multiple Enums in Same File

- No files found with multiple enums only

## Recommended Action Plan

1. **Start with Frontend**: Fix TypeScript files first as they're easier to split
2. **Focus on Models**: Prioritize files with many model definitions
3. **Maintain Functionality**: Ensure all imports and references are updated
4. **Follow Naming Convention**: Use consistent naming for split files
5. **Update Documentation**: Keep track of all changes made

## Files to Create

### TypeScript Models (Frontend)

- `processing-purpose.model.ts`
- `risk-factor.model.ts`
- `compliance-metric.model.ts`
- `data-processing-log.model.ts`
- `measurement-category.model.ts`
- `measurement-conversion.model.ts`
- `ingredient-measurement.model.ts`
- `nutrient-measurement.model.ts`
- `create-measurement-request.model.ts`
- `update-measurement-request.model.ts`
- `create-conversion-request.model.ts`
- `create-category-request.model.ts`
- `update-category-request.model.ts`
- `person.interface.ts`
- `shopping-item-dialog-data.interface.ts`
- `shopping-item-form-data.interface.ts`
- `ingredient-form-data.interface.ts`
- `ingredient-form-config.interface.ts`
- `ingredient-create-modal-data.interface.ts`
- `app-event.interface.ts`
- `user-claim.interface.ts`
- `user-info.interface.ts`

### C# Models (Backend)

- `SecurityEvent.cs`
- `SecurityStatistics.cs`
- `SecurityEventType.cs`
- `SecurityEventSeverity.cs`
- `VulnerabilityScanReport.cs`
- `Vulnerability.cs`
- `VulnerabilitySeverity.cs`
- `VulnerabilityStatus.cs`
- `DataRetentionReport.cs`
- `CleanupResult.cs`
- `DataRetentionStatistics.cs`
- `SessionInfo.cs`
- `SessionStatistics.cs`
- `ScrapedRecipeData.cs`
- `OcrRecipeData.cs`
- `IEvent.cs`
- `IEventHandler.cs`
- `BaseEventHandler.cs`
- `IEventBus.cs`
- `EventBusOptions.cs`
- `EventBusStatistics.cs`
