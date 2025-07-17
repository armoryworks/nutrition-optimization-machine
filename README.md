# Nutritional Optimization Machine (NOM)

> A privacy-first nutritional planning platform that empowers individuals and families to achieve their health and wellness goals through highly personalized, intelligent meal planning.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Angular](https://img.shields.io/badge/Angular-19-red.svg)](https://angular.io/)
[![.NET Core](https://img.shields.io/badge/.NET%20Core-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-blue.svg)](https://www.postgresql.org/)

## Vision

NOM goes beyond generic diet advice by deeply understanding individual dietary needs, preferences, and medical restrictions. We leverage this data to optimize meal plans while maintaining the highest standards of data privacy and user control.

## Key Features

### Current Features (v1.4)

- **Comprehensive Onboarding**: Multi-step workflow collecting personal health data, dietary restrictions, and preferences
- **Multi-Participant Support**: Manage nutritional plans for families or groups with individual preference tracking
- **Dietary Restriction Management**: Intelligent handling of:
  - Societal, religious, and ethical practices
  - Allergies and medical restrictions
  - Personal preferences and dislikes
- **Privacy-First Design**: GDPR compliant architecture with robust data protection mechanisms.

### Roadmap (Upcoming Features)

- **AI-Powered Meal Recommendations**: Advanced algorithms for highly personalized meal suggestions.
- **Wearable Device Integration**: Sync with popular health trackers for real-time data input.
- **Nutrient Tracking & Analytics**: Detailed reporting on macro and micronutrient intake.
- **Community & Sharing Features**: Secure sharing of meal plans with family members or healthcare providers.

## Getting Started

### Prerequisites

Ensure you have the following installed:

- Node.js (LTS version)
- .NET SDK 8.0
- PostgreSQL 17
- Git

### Installation

1. Clone the repository:
   `git clone https://github.com/danielhokanson/nutrition-optimization-machine.git`
2. Navigate to the project directory:
   `cd nutrition-optimization-machine`
3. Install frontend dependencies:
   `cd nom-ui && npm install && cd ..`
4. Restore backend dependencies:
   `cd nom-api && dotnet restore && cd ..`
5. Configure your PostgreSQL database connection string in `nom-api/appsettings.json`.
6. Run database migrations:
   `cd nom-api && dotnet ef database update && cd ..`

### Running the Application

1. Start the backend API:
   `cd nom-api && dotnet run`
2. In a separate terminal, start the frontend application:
   `cd nom-ui && ng serve --open`
   The application will open in your default browser.

## Contributing

We welcome contributions to the Nutritional Optimization Machine! To contribute:

1. Fork the repository.
2. Create your feature branch (`git checkout -b feature/AmazingFeature`).
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request.

### Code Quality

- Write unit tests for new features.
- Ensure privacy impact assessment for personal data handling.
- Follow established naming conventions.
- Document complex logic.

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Angular team for the excellent framework.
- .NET team for robust backend capabilities.
- PostgreSQL community for a reliable database.
- All contributors who help make NOM better.

## Contact & Support

- **Issues**: [GitHub Issues](https://github.com/danielhokanson/nutrition-optimization-machine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/danielhokanson/nutrition-optimization-machine/discussions)
- **Security**: For security issues, please email security@[yourdomain].com

---

For healthier living and data privacy.
