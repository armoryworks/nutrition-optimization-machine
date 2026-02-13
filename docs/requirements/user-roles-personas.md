# User Roles & Personas

## Overview

This document defines the primary user roles and system actors within the Nutritional Optimization Machine (NOM) platform. Each role has specific responsibilities, permissions, and access levels within the system.

## Primary User Roles

### New User / Prospect

**Description**: An individual who has just registered or is in the process of initial account setup, requiring guidance through onboarding questions and initial privacy consent.

**Responsibilities**:

- Complete account registration
- Provide initial personal information
- Grant privacy consents
- Complete multi-step onboarding process
- Set up dietary preferences and restrictions

**System Access**:

- Limited to onboarding workflows
- Access to privacy consent management
- Basic profile creation capabilities

**Key Interactions**:

- Registration and login
- Multi-step onboarding workflow
- Privacy consent collection
- Dietary restriction setup
- Plan invitation (optional)

### Authenticated User

**Description**: A registered and logged-in user of the system who can interact with various features, including submitting personal information and participating in plans.

**Responsibilities**:

- Manage personal profile and preferences
- Create and manage recipes and ingredients
- Participate in nutritional plans
- Manage privacy settings and data rights
- Use household, shopping, and meal planning features

**System Access**:

- Full access to personal features
- Recipe and ingredient creation
- Privacy dashboard access
- Household participation
- Shopping list management
- Meal planning capabilities

**Key Interactions**:

- Profile management
- Recipe creation and editing
- Privacy settings management
- Household participation
- Shopping list creation and management
- Meal planning and scheduling

### Plan Administrator

**Description**: An authenticated user who creates and manages nutritional plans, and can invite others to join.

**Responsibilities**:

- Create and manage nutritional plans
- Invite users to join plans
- Manage plan participants and roles
- Set plan-wide dietary preferences
- Coordinate household activities

**System Access**:

- All authenticated user capabilities
- Plan creation and management
- User invitation capabilities
- Plan participant management
- Household administration

**Key Interactions**:

- Plan creation and configuration
- User invitation management
- Participant role assignment
- Plan-wide preference setting
- Household coordination

### Plan Member

**Description**: An authenticated user who participates in an existing nutritional plan.

**Responsibilities**:

- Contribute to plan activities
- Share recipes and meal ideas
- Participate in household decisions
- Follow plan dietary guidelines
- Collaborate on shopping and meal planning

**System Access**:

- All authenticated user capabilities
- Plan-specific features
- Household collaboration tools
- Shared shopping lists
- Collaborative meal planning

**Key Interactions**:

- Plan participation
- Recipe sharing
- Household collaboration
- Shopping list contribution
- Meal planning participation

### Recipe Author

**Description**: An authenticated user who can create, edit, and manage their own recipes and custom ingredients, and submit them for curation.

**Responsibilities**:

- Create and edit recipes
- Develop custom ingredients
- Submit content for curation
- Manage recipe versions
- Respond to curation feedback

**System Access**:

- All authenticated user capabilities
- Recipe creation and editing
- Ingredient creation and management
- Recipe versioning
- Curation submission

**Key Interactions**:

- Recipe creation and editing
- Ingredient development
- Curation submission
- Version management
- Feedback response

### Site-Wide Admin

**Description**: An authenticated user with the `CanManageCuration` claim. This role is responsible for reviewing, approving, rejecting, and providing feedback on user-submitted recipes and ingredients.

**Responsibilities**:

- Review pending curation submissions
- Approve or reject content
- Provide feedback to authors
- Maintain content quality standards
- Manage curation workflow

**System Access**:

- All authenticated user capabilities
- Curation queue access
- Content approval/rejection
- Feedback provision
- Quality control tools

**Key Interactions**:

- Curation queue management
- Content review and approval
- Feedback provision
- Quality standards enforcement
- Communication with authors

### User Role Manager

**Description**: An authenticated user with the `CanManageUserRoles` claim. This role can grant or revoke the `CanManageCuration` and `CanManageUserRoles` claims for any user in the system.

**Responsibilities**:

- Manage user roles and permissions
- Grant and revoke admin claims
- Maintain system security
- Oversee user access levels
- Ensure proper role distribution

**System Access**:

- All authenticated user capabilities
- User management interface
- Role assignment tools
- Permission management
- Security oversight

**Key Interactions**:

- User role management
- Permission assignment
- Security monitoring
- Access control
- System administration

## System Actors

### System

**Description**: An automated entity responsible for background processes, such as the automatic inference and recording of dietary restrictions based on user input, ensuring data integrity and auditability.

**Responsibilities**:

- Background data processing
- Automated dietary restriction inference
- Data integrity maintenance
- Audit trail generation
- System health monitoring

**System Access**:

- Internal system processes
- Database operations
- Background task execution
- Logging and monitoring
- Data processing workflows

**Key Interactions**:

- Automated data processing
- Background task execution
- System monitoring
- Data integrity checks
- Audit trail generation

### Privacy Officer

**Description**: A designated role responsible for overseeing data protection compliance, handling user privacy rights requests, and maintaining compliance documentation.

**Responsibilities**:

- Oversee GDPR compliance
- Handle data subject rights requests
- Maintain privacy documentation
- Conduct privacy audits
- Ensure data protection standards

**System Access**:

- Privacy dashboard access
- Data processing logs
- Privacy request management
- Compliance reporting
- Audit trail access

**Key Interactions**:

- Privacy compliance oversight
- Data subject rights handling
- Privacy documentation
- Compliance auditing
- Data protection monitoring

## Role Hierarchy

```
System
├── Privacy Officer
├── User Role Manager
├── Site-Wide Admin
├── Plan Administrator
├── Recipe Author
├── Plan Member
├── Authenticated User
└── New User / Prospect
```

## Permission Matrix

| Feature              | New User | Auth User | Plan Member | Recipe Author | Plan Admin | Site Admin | Role Manager | Privacy Officer |
| -------------------- | -------- | --------- | ----------- | ------------- | ---------- | ---------- | ------------ | --------------- |
| Registration         |        |         |           |             |          |          |            |               |
| Profile Management   |        |         |           |             |          |          |            |               |
| Recipe Creation      |        |         |           |             |          |          |            |               |
| Recipe Curation      |        |         |           |             |          |          |            |               |
| Plan Creation        |        |         |           |             |          |          |            |               |
| Plan Participation   |        |         |           |             |          |          |            |               |
| Household Management |        |         |           |             |          |          |            |               |
| Shopping Lists       |        |         |           |             |          |          |            |               |
| Meal Planning        |        |         |           |             |          |          |            |               |
| Privacy Management   |        |         |           |             |          |          |            |               |
| User Role Management |        |         |           |             |          |          |            |               |
| System Monitoring    |        |         |           |             |          |          |            |               |

## User Journey Examples

### New User Journey

1. **Registration**: User creates account with email and password
2. **Onboarding**: Multi-step process collecting personal information
3. **Privacy Consent**: Grant necessary privacy consents
4. **Dietary Setup**: Configure dietary restrictions and preferences
5. **Plan Invitation**: Optionally join existing plan via invitation
6. **Feature Access**: Gain access to full system features

### Recipe Author Journey

1. **Content Creation**: Create recipes and custom ingredients
2. **Quality Assurance**: Review and refine content
3. **Curation Submission**: Submit content for admin review
4. **Feedback Response**: Address admin feedback and make revisions
5. **Version Management**: Create new versions of curated content
6. **Community Engagement**: Share and collaborate with other users

### Site Admin Journey

1. **Queue Monitoring**: Review pending curation submissions
2. **Content Review**: Evaluate recipe and ingredient quality
3. **Decision Making**: Approve, reject, or request revisions
4. **Feedback Provision**: Provide constructive feedback to authors
5. **Quality Control**: Maintain content quality standards
6. **Communication**: Engage with authors and community

### Plan Administrator Journey

1. **Plan Creation**: Create new nutritional plans
2. **User Invitation**: Invite users to join the plan
3. **Participant Management**: Manage plan participants and roles
4. **Household Coordination**: Coordinate household activities
5. **Preference Setting**: Set plan-wide dietary preferences
6. **Collaboration**: Facilitate collaborative meal planning

## Security Considerations

### Role-Based Access Control

- **Claims-Based Authorization**: System uses claims for role assignment
- **Granular Permissions**: Specific permissions for each feature
- **Audit Logging**: All role changes and access attempts logged
- **Session Management**: Secure session handling for all roles

### Data Protection

- **Privacy by Role**: Different privacy levels based on user role
- **Data Minimization**: Users only access necessary data
- **Audit Trails**: Comprehensive logging of all user actions
- **GDPR Compliance**: All roles respect data protection regulations

## Future Role Considerations

### Potential New Roles

- **Nutritionist**: Professional nutrition guidance and meal planning
- **Community Moderator**: Content moderation and community management
- **Data Analyst**: Analytics and reporting capabilities
- **Integration Manager**: External system integration management
- **Support Specialist**: User support and issue resolution

### Role Evolution

- **Role Progression**: Users can advance through roles based on activity
- **Temporary Roles**: Time-limited roles for specific projects
- **Custom Roles**: Organization-specific role customization
- **Role Inheritance**: Hierarchical role relationships
