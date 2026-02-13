# Business Rules

## Overview

This document defines the core business domains within the NOM program and the key rules that govern their behavior and data integrity. These rules ensure consistent system behavior and maintain data quality across all domains.

## User/Person Management Domain

### Purpose

Manages individual user accounts and associated personal profiles within the system.

### Business Rules

| Rule ID    | Description                     | Implementation Status |
| ---------- | ------------------------------- | --------------------- |
| Rule-5.1.1 | Unique Identification           |  COMPLETE           |
| Rule-5.1.2 | Account Creation                |  COMPLETE           |
| Rule-5.1.3 | Email Confirmation              |  COMPLETE           |
| Rule-5.1.4 | Password Security               |  COMPLETE           |
| Rule-5.1.5 | Two-Factor Authentication (2FA) |  COMPLETE           |
| Rule-5.1.6 | Invitation Code Uniqueness      |  COMPLETE           |

### Rule Details

#### Rule-5.1.1: Unique Identification

- Each Person record shall have a unique identifier
- **Implementation**: Primary key constraint in database
- **Validation**: Entity Framework ensures uniqueness

#### Rule-5.1.2: Account Creation

- Users must register with a unique email address and password meeting minimum complexity requirements
- **Implementation**: Email uniqueness constraint, password validation
- **Validation**: Client-side and server-side validation

#### Rule-5.1.3: Email Confirmation

- User email addresses should be confirmable to ensure validity and security
- **Implementation**: Email confirmation workflow with tokens
- **Validation**: Email format validation and confirmation status tracking

#### Rule-5.1.4: Password Security

- Users must be able to securely reset forgotten passwords and update existing passwords
- **Implementation**: Password reset workflow with secure tokens
- **Validation**: Password complexity requirements and secure reset process

#### Rule-5.1.5: Two-Factor Authentication (2FA)

- Users can opt to enable 2FA for enhanced login security
- **Implementation**: TOTP-based 2FA with authenticator app support
- **Validation**: QR code generation and recovery codes

#### Rule-5.1.6: Invitation Code Uniqueness

- InvitationCodes associated with Person records must be unique
- **Implementation**: Unique constraint in database
- **Validation**: Database-level uniqueness enforcement

## Questionnaire/Onboarding Domain

### Purpose

Guides new users through initial data collection to personalize their experience and gather foundational information.

### Business Rules

| Rule ID    | Description                  | Implementation Status |
| ---------- | ---------------------------- | --------------------- |
| Rule-5.2.1 | Workflow Sequencing          |  COMPLETE           |
| Rule-5.2.2 | Data Aggregation             |  COMPLETE           |
| Rule-5.2.3 | Conditional Step Execution   |  COMPLETE           |
| Rule-5.2.4 | Data Integrity on Navigation |  COMPLETE           |

### Rule Details

#### Rule-5.2.1: Workflow Sequencing

- The onboarding process follows a predefined sequence of steps, leveraging reusable edit components
- **Implementation**: Step-based workflow with navigation controls
- **Validation**: Step validation and progression logic

#### Rule-5.2.2: Data Aggregation

- All data collected across multiple steps and components during onboarding shall be aggregated into a single, comprehensive payload for final submission
- **Implementation**: Centralized data collection in OnboardingWorkflowComponent
- **Validation**: Complete data validation before submission

#### Rule-5.2.3: Conditional Step Execution

- The execution of certain onboarding steps is conditional based on prior user input
- **Implementation**: Conditional step rendering based on user choices
- **Validation**: Step dependency validation

#### Rule-5.2.4: Data Integrity on Navigation

- User inputs must be preserved when navigating back and forth through the onboarding wizard
- **Implementation**: State preservation in component lifecycle
- **Validation**: Data persistence across navigation

## Plan Management Domain

### Purpose

Facilitates the creation, management, and participation in shared nutritional plans.

### Business Rules

| Rule ID    | Description             | Implementation Status |
| ---------- | ----------------------- | --------------------- |
| Rule-5.3.1 | Plan Uniqueness         |  COMPLETE           |
| Rule-5.3.2 | Plan Administrator      |  COMPLETE           |
| Rule-5.3.3 | Participant Association |  COMPLETE           |
| Rule-5.3.4 | Participant Roles       |  COMPLETE           |
| Rule-5.3.5 | Curated Plan Concept    |  COMPLETE           |

### Rule Details

#### Rule-5.3.1: Plan Uniqueness

- Each Plan shall have a unique identifier and a unique InvitationCode (if applicable) for joining
- **Implementation**: Unique constraints in database
- **Validation**: Database-level uniqueness enforcement

#### Rule-5.3.2: Plan Administrator

- Every Plan must be associated with a Person designated as its CreatedByPersonId
- **Implementation**: Foreign key relationship with Person entity
- **Validation**: Required field validation

#### Rule-5.3.3: Participant Association

- PlanParticipant records define the relationship between a Person and a Plan, and must link to a valid Plan and Person
- **Implementation**: Junction table with foreign key constraints
- **Validation**: Referential integrity enforcement

#### Rule-5.3.4: Participant Roles

- Each PlanParticipant must have a defined Role (e.g., Admin, Member) governed by a reference data type
- **Implementation**: Role enumeration and validation
- **Validation**: Role assignment validation

#### Rule-5.3.5: Curated Plan Concept

- The system supports curated plans, which are centrally managed, clonable by users, and can be customized and submitted for curation
- **Implementation**: Curated plan entities and relationships
- **Validation**: Plan curation workflow validation

## Reference Data Management Domain

### Purpose

Provides a centralized, extensible system for managing predefined, static lists and types used across various domains.

### Business Rules

| Rule ID    | Description                | Implementation Status |
| ---------- | -------------------------- | --------------------- |
| Rule-5.4.1 | Categorization             |  COMPLETE           |
| Rule-5.4.2 | Many-to-Many Association   |  COMPLETE           |
| Rule-5.4.3 | View Entity Discriminators |  COMPLETE           |

### Rule Details

#### Rule-5.4.1: Categorization

- Reference items are organized into Groups (e.g., "Measurement Type", "Nutrient Type", "Meal Type") to provide context
- **Implementation**: Group-based organization system
- **Validation**: Group assignment validation

#### Rule-5.4.2: Many-to-Many Association

- A Reference item can belong to multiple Groups, and a Group can contain multiple Reference items
- **Implementation**: Many-to-many relationship with junction table
- **Validation**: Relationship integrity enforcement

#### Rule-5.4.3: View Entity Discriminators

- Specialized "view" entities are used to allow frontend systems to easily filter and categorize reference data by their associated Groups
- **Implementation**: View entities for optimized data access
- **Validation**: View data consistency

## Dietary Restriction Management Domain

### Purpose

Records and applies dietary constraints for individuals or entire plans to tailor nutritional recommendations.

### Business Rules

| Rule ID    | Description            | Implementation Status |
| ---------- | ---------------------- | --------------------- |
| Rule-5.5.1 | Association Constraint |  COMPLETE           |
| Rule-5.5.2 | Type Definition        |  COMPLETE           |
| Rule-5.5.3 | Uniqueness per Context |  COMPLETE           |
| Rule-5.5.4 | Audit Trail            |  COMPLETE           |

### Rule Details

#### Rule-5.5.1: Association Constraint

- Every Restriction must be explicitly linked to either a Person OR a Plan (or both), but not neither
- **Implementation**: Conditional foreign key relationships
- **Validation**: Association validation logic

#### Rule-5.5.2: Type Definition

- Each Restriction must be categorized by a RestrictionType defined in the Reference Data domain
- **Implementation**: Reference data relationship
- **Validation**: Type assignment validation

#### Rule-5.5.3: Uniqueness per Context

- A specific RestrictionType can only be applied once per Person or once per Plan to avoid redundant entries
- **Implementation**: Unique constraint on combination of fields
- **Validation**: Duplicate prevention logic

#### Rule-5.5.4: Audit Trail

- Every Restriction record must capture who CreatedByPersonId it and CreatedDate
- **Implementation**: Audit fields in entity
- **Validation**: Audit field population

## Data Privacy & Compliance Domain

### Purpose

Ensures compliance with applicable data privacy regulations and provides users with control over their personal data.

### Business Rules

| Rule ID    | Description            | Implementation Status |
| ---------- | ---------------------- | --------------------- |
| Rule-5.6.1 | Lawful Basis           |  COMPLETE           |
| Rule-5.6.2 | Consent Documentation  |  COMPLETE           |
| Rule-5.6.3 | Data Minimization      |  COMPLETE           |
| Rule-5.6.4 | Purpose Limitation     |  COMPLETE           |
| Rule-5.6.5 | Retention Limitation   |  COMPLETE           |
| Rule-5.6.6 | Security by Design     |  COMPLETE           |
| Rule-5.6.7 | Breach Notification    |  COMPLETE           |
| Rule-5.6.8 | Cross-Border Transfers |  COMPLETE           |

### Rule Details

#### Rule-5.6.1: Lawful Basis

- All personal data processing must have a valid lawful basis under applicable privacy laws
- **Implementation**: Lawful basis tracking in consent records
- **Validation**: Basis validation for all data processing

#### Rule-5.6.2: Consent Documentation

- All user consents must be documented with timestamp, version, and specific scope of consent granted
- **Implementation**: Comprehensive consent tracking system
- **Validation**: Consent documentation completeness

#### Rule-5.6.3: Data Minimization

- Only personal data necessary for the specified purpose may be collected and processed
- **Implementation**: Purpose-based data collection
- **Validation**: Data necessity validation

#### Rule-5.6.4: Purpose Limitation

- Personal data may only be used for the purposes explicitly communicated to the user at the time of collection
- **Implementation**: Purpose tracking in data processing
- **Validation**: Purpose compliance validation

#### Rule-5.6.5: Retention Limitation

- Personal data must be deleted or anonymized when no longer necessary for the original purpose
- **Implementation**: Automated retention policies
- **Validation**: Retention period compliance

#### Rule-5.6.6: Security by Design

- Appropriate technical and organizational measures must be implemented to protect personal data
- **Implementation**: Encryption, access controls, audit logging
- **Validation**: Security measure effectiveness

#### Rule-5.6.7: Breach Notification

- Data breaches affecting personal data must be documented and reported as required by law
- **Implementation**: Breach detection and notification system
- **Validation**: Breach reporting compliance

#### Rule-5.6.8: Cross-Border Transfers

- Transfer of personal data outside the user's jurisdiction must comply with applicable legal requirements
- **Implementation**: Transfer compliance mechanisms
- **Validation**: Transfer legality verification

## Recipe & Ingredient Domain

### Purpose

Manages the creation, versioning, and ownership of recipes and custom ingredients.

### Business Rules

| Rule ID    | Description        | Implementation Status |
| ---------- | ------------------ | --------------------- |
| Rule-5.7.1 | Author Ownership   |  COMPLETE           |
| Rule-5.7.2 | Default Status     |  COMPLETE           |
| Rule-5.7.3 | Version Integrity  |  COMPLETE           |
| Rule-5.7.4 | Immutable Curation |  COMPLETE           |

### Rule Details

#### Rule-5.7.1: Author Ownership

- Every `RecipeEntity` and author-created `IngredientEntity` must be associated with an `AuthorId` (a `PersonEntity`)
- **Implementation**: Foreign key relationship to Person entity
- **Validation**: Author assignment validation

#### Rule-5.7.2: Default Status

- All new recipes and ingredients shall have a default `CurationStatus` of `NonCurated`
- **Implementation**: Default value assignment in entity creation
- **Validation**: Status initialization validation

#### Rule-5.7.3: Version Integrity

- When a new version of a recipe is created, its `Version` number must be incremented, and it must be linked to its `ParentRecipeId`
- **Implementation**: Version management system
- **Validation**: Version integrity validation

#### Rule-5.7.4: Immutable Curation

- Once a recipe or ingredient is `Curated`, its core data cannot be directly edited. Any modification must result in a new version
- **Implementation**: Immutability enforcement in business logic
- **Validation**: Curation state validation

## Curation Domain

### Purpose

Governs the process of reviewing and validating user-submitted content.

### Business Rules

| Rule ID    | Description         | Implementation Status |
| ---------- | ------------------- | --------------------- |
| Rule-5.8.1 | State Transitions   |  COMPLETE           |
| Rule-5.8.2 | Required Feedback   |  COMPLETE           |
| Rule-5.8.3 | Curation Dependency |  COMPLETE           |

### Rule Details

#### Rule-5.8.1: State Transitions

- The `CurationStatus` can only transition through a defined lifecycle (e.g., `NonCurated` -> `PendingCuration`, `PendingCuration` -> `Curated`/`Rejected`/`RequiresRevision`)
- **Implementation**: State machine for curation workflow
- **Validation**: State transition validation

#### Rule-5.8.2: Required Feedback

- A status change to `Rejected` or `RequiresRevision` requires the admin to provide feedback notes
- **Implementation**: Feedback requirement enforcement
- **Validation**: Feedback completeness validation

#### Rule-5.8.3: Curation Dependency

- The system must prevent a recipe's status from changing to `Curated` if any of its associated ingredients are not also `Curated`
- **Implementation**: Dependency validation in business logic
- **Validation**: Curation dependency checking

## Communication Domain

### Purpose

Manages user-to-user and admin-to-user messaging.

### Business Rules

| Rule ID    | Description             | Implementation Status |
| ---------- | ----------------------- | --------------------- |
| Rule-5.9.1 | Thread Context          |  COMPLETE           |
| Rule-5.9.2 | Participant Permissions |  COMPLETE           |
| Rule-5.9.3 | Initiation Control      |  COMPLETE           |

### Rule Details

#### Rule-5.9.1: Thread Context

- A `MessageThreadEntity` can be linked to a specific system entity (recipe, plan) or exist as a context-free conversation
- **Implementation**: Optional foreign key relationships
- **Validation**: Context relationship validation

#### Rule-5.9.2: Participant Permissions

- Users can only initiate arbitrary conversations with other users they are permitted to see (members of shared plans, or any user if the initiator is an admin)
- **Implementation**: Permission-based conversation initiation
- **Validation**: Participant permission validation

#### Rule-5.9.3: Initiation Control

- Curation-related message threads cannot be initiated by the author; they can only reply to threads started by an admin
- **Implementation**: Initiation control in business logic
- **Validation**: Thread initiation validation

## Household Management Domain

### Purpose

Manages household groups and collaborative features for families and shared living situations.

### Business Rules

| Rule ID     | Description        | Implementation Status |
| ----------- | ------------------ | --------------------- |
| Rule-5.10.1 | Household Creation |  COMPLETE           |
| Rule-5.10.2 | Member Management  |  COMPLETE           |
| Rule-5.10.3 | Invitation System  |  COMPLETE           |
| Rule-5.10.4 | Shared Resources   |  COMPLETE           |

### Rule Details

#### Rule-5.10.1: Household Creation

- Each household must have a unique name and be associated with a creator
- **Implementation**: Unique constraint and creator relationship
- **Validation**: Household creation validation

#### Rule-5.10.2: Member Management

- Household members must be explicitly added and can have different roles
- **Implementation**: Member relationship with role assignment
- **Validation**: Member management validation

#### Rule-5.10.3: Invitation System

- Household invitations must use secure tokens and have expiration dates
- **Implementation**: Secure invitation token system
- **Validation**: Invitation security validation

#### Rule-5.10.4: Shared Resources

- Household members can share shopping lists, meal plans, and recipes
- **Implementation**: Shared resource relationships
- **Validation**: Resource sharing validation

## Shopping List Domain

### Purpose

Manages shopping lists and items for households and individuals.

### Business Rules

| Rule ID     | Description        | Implementation Status |
| ----------- | ------------------ | --------------------- |
| Rule-5.11.1 | List Ownership     |  COMPLETE           |
| Rule-5.11.2 | Item Management    |  COMPLETE           |
| Rule-5.11.3 | Household Sharing  |  COMPLETE           |
| Rule-5.11.4 | Recipe Integration |  COMPLETE           |

### Rule Details

#### Rule-5.11.1: List Ownership

- Shopping lists must be owned by a person or household
- **Implementation**: Ownership relationship in entity
- **Validation**: Ownership assignment validation

#### Rule-5.11.2: Item Management

- Shopping list items must have names and can have quantities and categories
- **Implementation**: Item entity with required fields
- **Validation**: Item data validation

#### Rule-5.11.3: Household Sharing

- Shopping lists can be shared with household members
- **Implementation**: Sharing relationship system
- **Validation**: Sharing permission validation

#### Rule-5.11.4: Recipe Integration

- Shopping lists can be generated from recipes
- **Implementation**: Recipe-to-shopping-list conversion
- **Validation**: Recipe integration validation

## Meal Planning Domain

### Purpose

Manages meal planning and scheduling for individuals and households.

### Business Rules

| Rule ID     | Description            | Implementation Status |
| ----------- | ---------------------- | --------------------- |
| Rule-5.12.1 | Plan Ownership         |  COMPLETE           |
| Rule-5.12.2 | Entry Management       |  COMPLETE           |
| Rule-5.12.3 | Recipe Integration     |  COMPLETE           |
| Rule-5.12.4 | Household Coordination |  COMPLETE           |

### Rule Details

#### Rule-5.12.1: Plan Ownership

- Meal plans must be owned by a person or household
- **Implementation**: Ownership relationship in entity
- **Validation**: Ownership assignment validation

#### Rule-5.12.2: Entry Management

- Meal plan entries must have dates and can have recipes assigned
- **Implementation**: Entry entity with date and recipe relationships
- **Validation**: Entry data validation

#### Rule-5.12.3: Recipe Integration

- Meal plan entries can be linked to specific recipes
- **Implementation**: Recipe relationship in meal plan entries
- **Validation**: Recipe integration validation

#### Rule-5.12.4: Household Coordination

- Meal plans can be shared and coordinated within households
- **Implementation**: Household sharing system
- **Validation**: Coordination permission validation

## Implementation Status Summary

### Backend Business Rules: COMPLETE 

- All domain rules implemented in business logic layer
- Database constraints enforce data integrity
- Validation logic ensures rule compliance
- Audit trails track rule enforcement

### Frontend Business Rules: COMPLETE 

- Client-side validation enforces business rules
- UI reflects business rule constraints
- User experience follows business rule workflows
- Error handling communicates rule violations

### Rule Enforcement Mechanisms

- **Database Constraints**: Primary keys, foreign keys, unique constraints
- **Business Logic**: Service-layer rule enforcement
- **Validation**: Multi-layer validation (client, API, database)
- **Audit Logging**: Comprehensive tracking of rule enforcement
- **Error Handling**: User-friendly error messages for rule violations

### Next Priorities

1. **Rule Testing**: Comprehensive testing of all business rules
2. **Rule Documentation**: Detailed documentation of rule implementations
3. **Rule Monitoring**: Monitoring and alerting for rule violations
4. **Rule Evolution**: Process for updating business rules as requirements change
