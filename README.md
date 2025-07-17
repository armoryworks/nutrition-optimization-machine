Based on your comprehensive specification, here's a professional README for your repository:

````markdown
# Nutritional Optimization Machine (NOM)

> A privacy-first nutritional planning platform that empowers individuals and families to achieve their health and wellness goals through highly personalized, intelligent meal planning.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Angular](https://img.shields.io/badge/Angular-17.x-red.svg)](https://angular.io/)
[![.NET Core](https://img.shields.io/badge/.NET%20Core-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)

## 🎯 Vision

NOM goes beyond generic diet advice by deeply understanding individual dietary needs, preferences, and medical restrictions. We leverage this data to optimize meal plans while maintaining the highest standards of data privacy and user control.

## ✨ Key Features

### Current Features (v1.4)

- **Comprehensive Onboarding**: Multi-step workflow collecting personal health data, dietary restrictions, and preferences
- **Multi-Participant Support**: Manage nutritional plans for families or groups with individual preference tracking
- **Dietary Restriction Management**: Intelligent handling of:
  - Societal, religious, and ethical practices
  - Allergies and medical restrictions
  - Personal preferences and dislikes
- **Privacy-First Design**: GDPR-compliant architecture with granular consent management
- **Secure Authentication**: JWT-based auth with optional two-factor authentication

### Coming Soon

- USDA FoodData Central integration
- AI-powered meal recommendations
- Automated grocery list generation
- Nutritionist consultation integration
- Community recipe sharing

## 🏗️ Architecture

### Frontend (Angular)

- **Framework**: Angular 17 with standalone components
- **UI Library**: Angular Material
- **Styling**: SCSS with BEM methodology
- **State Management**: RxJS for reactive programming
- **Structure**: Domain-driven design with modular architecture

### Backend (.NET Core)

- **API**: RESTful ASP.NET Core Web API
- **ORM**: Entity Framework Core with PostgreSQL
- **Authentication**: ASP.NET Core Identity with JWT tokens
- **Architecture**: Clean architecture with orchestration services

### Database

- **PostgreSQL 16**: Primary data store with encryption at rest
- **Schema**: Normalized design supporting multi-tenant architecture

## 🚀 Getting Started

### Prerequisites

- Node.js 18+ and npm
- .NET 8.0 SDK
- PostgreSQL 16
- Angular CLI (`npm install -g @angular/cli`)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/danielhokanson/nutrition-optimization-machine.git
   cd nutrition-optimization-machine
   ```
````

2. **Backend Setup**

   ```bash
   # Navigate to API directory
   cd Nom.Api

   # Restore dependencies
   dotnet restore

   # Update database connection string in appsettings.json
   # Run migrations
   dotnet ef database update

   # Run the API
   dotnet run
   ```

3. **Frontend Setup**

   ```bash
   # Navigate to UI directory
   cd nom-ui

   # Install dependencies
   npm install

   # Start development server
   ng serve
   ```

4. **Access the application**
   - Frontend: http://localhost:4200
   - API: http://localhost:5000
   - Swagger: http://localhost:5000/swagger

## 📝 Development Guidelines

### Naming Conventions

#### ⚠️ CRITICAL RULE

**NO MODEL OR DATA TRANSPORT CLASS SHALL EVER USE THE SUFFIX "DTO"**

✅ **Acceptable suffixes:**

- `Model` - Core domain entities
- `Request` - Inbound API payloads
- `Response` - Outbound API responses

#### Frontend (TypeScript/Angular)

- Components: `kebab-case.component.ts`
- Services: `kebab-case.service.ts`
- Models: `PascalCaseModel`
- Methods: `camelCase()`

#### Backend (C#/.NET)

- Controllers: `PascalCaseController.cs`
- Services: `PascalCaseService.cs`
- Entities: `PascalCaseEntity.cs`
- Methods: `PascalCase()`

### Git Workflow

1. Create feature branch from `main`
2. Follow conventional commits
3. Submit PR with at least one reviewer
4. Ensure all tests pass
5. Merge after approval

## 🔐 Privacy & Compliance

NOM is built with privacy by design principles:

- **GDPR Compliant**: Full support for EU data protection requirements
- **Data Minimization**: Only collect necessary data
- **User Control**: Granular consent management and data portability
- **Audit Trail**: Comprehensive logging of data access and modifications
- **Right to Erasure**: Users can delete their data at any time

### Data Protection Features

- Encryption at rest and in transit
- Secure authentication with 2FA support
- Regular security audits
- Privacy dashboard for user control

## 📊 Project Status

### ✅ Completed

- Core user onboarding workflow
- Person and restriction management
- JWT authentication system
- Reference data infrastructure
- Privacy compliance framework design

### 🚧 In Progress

- Multi-participant restriction collection
- Privacy dashboard implementation
- Consent management system
- Data subject rights endpoints

### 📅 Roadmap

- **Phase 1** (30 days): Basic consent collection and privacy policy
- **Phase 2** (60 days): Privacy dashboard and GDPR rights implementation
- **Phase 3** (90 days): Advanced privacy features and monitoring

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Setup

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Code Quality

- Write unit tests for new features
- Ensure privacy impact assessment for personal data handling
- Follow established naming conventions
- Document complex logic

## 📄 License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Angular team for the excellent framework
- .NET team for robust backend capabilities
- PostgreSQL community for a reliable database
- All contributors who help make NOM better

## 📞 Contact & Support

- **Issues**: [GitHub Issues](https://github.com/danielhokanson/nutrition-optimization-machine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/danielhokanson/nutrition-optimization-machine/discussions)
- **Security**: For security issues, please email security@[yourdomain].com

---

<p align="center">
  Built with ❤️ for healthier living and data privacy
</p>
```

This README:

- Provides a clear project overview aligned with your vision
- Highlights the privacy-first approach as a key differentiator
- Includes practical setup instructions
- Emphasizes the critical naming convention rule
- Shows current status and roadmap
- Encourages contributions while maintaining standards
- Uses badges for quick technology identification
- Maintains a professional yet approachable tone

Would you like me to adjust any sections or add additional information?
