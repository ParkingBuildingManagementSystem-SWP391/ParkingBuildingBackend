<div align="center">

# 🅿️ Parking Building Management API

### A modern backend platform for smart parking operations, reservations, and payments

[![C#](https://img.shields.io/badge/C%23-12-512BD4?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_API-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/)
[![SQL Server](https://img.shields.io/badge/SQL_Server-EF_Core_8-CC2927?style=for-the-badge&logo=microsoftsqlserver&logoColor=white)](https://www.microsoft.com/sql-server)
[![Status](https://img.shields.io/badge/Status-Active_Development-22C55E?style=for-the-badge)](#-project-status)
[![License](https://img.shields.io/badge/License-Not_Specified-lightgrey?style=for-the-badge)](#-license--contact)

</div>

---

## 🚀 About the Project

**Parking Building Management API** is a backend system that digitalizes parking facility operations, including parking slot management, reservations, check-in and check-out, license plate recognition, payments, an internal wallet, membership cards, and management reports.

The project follows a layered architecture built with ASP.NET Core Web API, Entity Framework Core, and SQL Server. Business responsibilities are separated across the API, Service, and Repository layers to improve maintainability, testability, and scalability.

### Project Structure

```text
ParkingBuildingBackend/
├── ParkingBuilding.API/          # Controllers, middleware, and application configuration
├── ParkingBuilding.Service/      # Business logic, DTOs, and service contracts
├── ParkingBuilding.Repository/   # Entities, DbContext, and data access
└── ParkingBuilding.slnx          # Solution file
```

## 🛠️ Built With

| Technology | Purpose |
|---|---|
| C# 12 / .NET 8 | Primary language and development platform |
| ASP.NET Core Web API | RESTful API development |
| Entity Framework Core 8 | Object-relational mapping and data access |
| SQL Server | Relational database management system |
| JWT Bearer | API authentication and authorization |
| Swagger / OpenAPI | Endpoint discovery and testing in Development |
| FluentValidation | Request data validation |
| AutoMapper | Entity and DTO mapping |
| BCrypt | Password hashing and protection |
| VNPay | Online payment processing |
| Cloudinary | Image storage |
| MailKit | Email and OTP delivery |
| EPPlus / QuestPDF | Excel and PDF report generation |
| QRCoder | QR code generation for parking workflows |

## ✨ Key Features

| Module | Highlights |
|---|---|
| 🔐 Authentication | Registration, OTP verification, login, Google Login, password recovery/reset, and profile updates |
| 🚘 Parking | Floor and slot lookup, reservations, cancellations, vehicle location, and parking session management |
| 🎫 Check-in / Check-out | Support for reservations and walk-in customers, ticket scanning, and parking fee calculation |
| 🤖 License Plate Recognition | AI service integration for license plate recognition and verification |
| 💳 Payments | Cash, VNPay, internal wallet payments, and invoice status tracking |
| 👛 Digital Wallet | Balance lookup, transaction history, and deposits |
| 🪪 Membership Cards | Registration, vehicle management, membership tiers, and cancellation |
| 🧑‍💼 Management | User, session, pricing, parking slot, and membership card administration |
| 👷 Staff | Staff shift and activity log management |
| 📊 Reporting | Management dashboard, traffic statistics, and report export |
| 🚨 Incidents | Create, track, and resolve incident reports |
| ⏱️ Background Processing | Automatic expired booking cancellation and membership expiration processing |

## 🏁 Getting Started

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server 2019 or later, or SQL Server Developer Edition
- Git
- Appropriate integration accounts when using email, image storage, AI recognition, or online payment services

### 1. Clone the Repository

```bash
git clone <repository-url>
cd ParkingBuildingBackend
```

### 2. Restore Dependencies

```bash
dotnet restore ParkingBuilding.slnx
```

### 3. Configure Secrets Securely

Never store database connection strings, JWT secrets, API keys, SMTP credentials, internal IP addresses, or real payment credentials in Git. For local development, use **.NET User Secrets**:

```bash
dotnet user-secrets init --project ParkingBuilding.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your-connection-string>" --project ParkingBuilding.API
dotnet user-secrets set "JwtSettings:Secret" "<a-strong-secret-at-least-32-characters>" --project ParkingBuilding.API
dotnet user-secrets set "JwtSettings:Issuer" "<your-issuer>" --project ParkingBuilding.API
dotnet user-secrets set "JwtSettings:Audience" "<your-audience>" --project ParkingBuilding.API
```

Optional integrations such as email, Cloudinary, VNPay, and license plate recognition must also be configured through User Secrets, environment variables, or the deployment platform's secret manager.

> [!IMPORTANT]
> Never commit `appsettings.json`, `appsettings.*.json`, `.env` files, certificates, tokens, or service credentials. If a secret has ever been committed, revoke or rotate it immediately. Deleting it in a later commit does not remove it from Git history.

### 4. Prepare the Database

Make sure SQL Server is running and that a database with a schema compatible with `ParkingManagementDbContext` is available. You can then verify the connection by building and running the application.

> [!NOTE]
> The repository currently does not include migrations in the source tree. If the database does not already exist, add the appropriate migrations or database scripts before running the application.

### 5. Build and Run the API

```bash
dotnet build ParkingBuilding.slnx
dotnet run --project ParkingBuilding.API
```

The application will use the local URL displayed in the terminal. In the Development environment, open `/swagger` under that URL to access the API documentation.

> [!TIP]
> This README intentionally does not hard-code deployment hostnames, IP addresses, ports, or infrastructure details. Use the URL provided by the runtime in your environment.

## 📖 Usage

1. Start the API and open Swagger UI in the Development environment.
2. Create an account or sign in through the `api/Auth` endpoints.
3. Copy the access token returned by the login response.
4. Select **Authorize** in Swagger and enter the token as `Bearer <token>`.
5. Call the endpoints available to your account's role.

### Main API Groups

| Route | Purpose |
|---|---|
| `api/Auth` | Authentication and user profiles |
| `api/Parking` | Reservations, parking sessions, check-in/check-out, and slot lookup |
| `api/Payments` | Payments and invoices |
| `api/Wallet` | Wallet balances and transactions |
| `api/MembershipCard` | Membership card management |
| `api/Manager` | Dashboard, pricing, parking slots, shifts, and reports |
| `api/Admin` | User and parking session administration |
| `api/Staff` | Staff shifts |
| `api/incident-reports` | Incident reporting and resolution |

## 🔒 Security Notes

- Use a dedicated secret manager for each environment. Never place secrets in source code or documentation.
- Enforce HTTPS in production.
- Generate long, random JWT secrets and rotate them regularly.
- Restrict CORS to the approved frontend domains for each environment.
- Never log tokens, passwords, connection strings, payment payloads, or personal data.
- Never use sandbox credentials in production or production credentials on developer machines.

## 📸 Screenshots

Add images to `docs/images/` and replace the placeholders below when the user interface or API documentation is ready.

| Swagger API | Manager Dashboard |
|---|---|
| `docs/images/swagger-overview.png` | `docs/images/manager-dashboard.png` |

<!-- Example:
![Swagger API](docs/images/swagger-overview.png)
![Manager Dashboard](docs/images/manager-dashboard.png)
-->

## 🗺️ Project Status

The project is currently under **Active Development**. The API contract and data model may continue to change as the product evolves.

## 🤝 Contributing

1. Create a new branch from the project's development branch.
2. Follow the existing API → Service → Repository structure.
3. Never commit secrets or local environment data.
4. Build the solution and test all affected workflows before opening a Pull Request.

```bash
git checkout -b feature/your-feature
dotnet build ParkingBuilding.slnx
```

## 📝 License & Contact

This repository does not currently declare a license. All rights are reserved until an official `LICENSE` file is added.

For project-related inquiries, contact the **project maintainer** through the team's internal communication channel or the repository's Issues section.

---

<div align="center">
  <sub>Built with ❤️ using ASP.NET Core &amp; .NET 8</sub>
</div>
