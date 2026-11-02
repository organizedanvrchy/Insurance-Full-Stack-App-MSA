# Online Insurance Management System

A full-stack insurance platform. Customers get **personalized quotes**, enroll in policies, pay monthly premiums and file claims, and are kept informed by **email** (receipts, payment reminders, claim updates). Administrators manage the plan catalog, review claims and manage customer accounts.

![.NET](https://img.shields.io/badge/.NET-ASP.NET%20Core%20Web%20API-512BD4?logo=dotnet&logoColor=white)
![Angular](https://img.shields.io/badge/Angular-TypeScript-DD0031?logo=angular&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-ADO.NET%20%2B%20stored%20procedures-CC2927?logo=microsoftsqlserver&logoColor=white)
![Azure](https://img.shields.io/badge/Hosted%20on-Azure-0078D4?logo=microsoftazure&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit-5E2D79)

> **Live demo:** [Project Website](https://projectdeploy-ewbahrbra0abcuam.westus3-01.azurewebsites.net/)
> *(Hosted on a development-tier Azure plan, so the first request after a quiet period may take a few seconds.)*
> Demo logins are listed under [Demo accounts](#demo-accounts).

## Screenshots

| Personalized quote | Customer policies | Claims review (admin) |
|---|---|---|
| ![Quote breakdown](docs/images/quote.png) | ![My policies](docs/images/my-policies.png) | ![Admin claims](docs/images/admin-claims.png) |

| Premium schedule | Overdue alert | Customer management (admin) |
|---|---|---|
| ![Payments](docs/images/payments.png) | ![Overdue alert](docs/images/overdue-alert.png) | ![Customers](docs/images/customers.png) |

## Table of contents

- [Overview](#overview)
- [Features](#features)
- [Tech stack](#tech-stack)
- [Architecture at a glance](#architecture-at-a-glance)
- [Key business rules](#key-business-rules)
- [Getting started](#getting-started)
- [Demo accounts](#demo-accounts)
- [Configuration reference](#configuration-reference)
- [API overview](#api-overview)
- [Testing](#testing)
- [Project structure](#project-structure)
- [Deploying to Azure](#deploying-to-azure)
- [Email notifications](#email-notifications)
- [Known limitations](#known-limitations)
- [Roadmap](#roadmap)
- [Documentation](#documentation)

## Overview

The application simulates the core operations of an insurer:

1. An admin publishes **plans** (Health, Auto, Life, Home, Travel).
2. A customer answers a short, plan-specific questionnaire. The server **rates** the applicant and issues a **quote**: a price with a full breakdown of how it was calculated, or an underwriting decline.
3. The customer **enrolls** from the quote. A **saga** across three services reserves the quote, creates the policy and its monthly **premium schedule**, and confirms the quote.
4. The customer pays installments, and can **file claims** against active, paid-up policies.
5. Admins move claims through a **review workflow**, and manage customer access.
6. Along the way, customers get **emails**: a verification link at sign-up, a confirmation for each policy, a receipt for each payment (or a notice if it's declined), reminders before and after premiums are due, and an update each time a claim moves.

The backend is a set of **microservices**: seven ASP.NET Core services, each with its own database, behind an **API gateway + BFF**. Services call each other over HTTP for queries and exchange **events over RabbitMQ** for everything that changes state in another service. The Angular frontend talks only to the gateway. Inside each service, data access uses **ADO.NET with parameterized stored procedures**.

## Features

### Customers
- Register, sign in and sign out. Passwords are hashed with BCrypt and sessions use JWTs.
- **Change their email or password** from the profile page, confirming with the current password. A new email works at once but has to be verified, and the old address gets a "was this you?" email; a password change is announced by email too.
- **Verify their email address** from a link sent at sign-up. Until then they can browse and get quotes, but not enroll or file claims.
- **Choose their emails:** a "Email me payment reminders" box on the sign-up form, changeable any time on the profile page.
- Browse the plan catalog and filter by type.
- **Get a personalized quote:** the API supplies the questions for each plan type, and the form is built dynamically from them. The result shows the base rate, every rating factor, the monthly price, the total over the term and the quote's expiry date.
- Enroll from a quote with a chosen start date, and cancel a policy.
- **Pay premiums** in order from a payment schedule, with paid, pending and overdue states.
- **Simulated checkout:** pay by credit/debit card, PayPal, Apple Pay, Google Pay or Venmo. Card numbers are validated (brand, checksum, expiry, security code) and turned into a token in the browser; the server never sees them. Name and billing address are filled in from sign-up, cards can be saved for later, and every attempt gets a printable receipt.
- **Payment history:** every attempt on a policy, including declined ones, with the reason the customer was given.
- **Payment alerts:** a "payment due soon" or "payment overdue" badge on each policy.
- **File claims** with incident date, amount and description. Eligibility is validated against the policy.
- Track claims through the review workflow and read the reviewer's notes.
- **Email notifications:** a receipt for every payment and a notice when one is declined; reminders 3 days before a premium is due, on the due date, when it becomes overdue, and 5 and 10 days after that; claim received and every claim status change; policy activated or cancelled. Payment reminders are opt-in/out at sign-up and on the profile page, which also lists recent emails.

### Administrators
- Create, edit and deactivate plans. Deactivated plans disappear for customers but remain on existing policies.
- **Claims review queue** with a status filter. Buttons show only the legal next step, and a rejection requires a reason.
- **Customer management:** search, filter by status, see active-policy and claim counts and whether the email is verified, and deactivate or reactivate accounts (takes effect on the customer's next request).
- **Payment log:** every payment attempt from every customer, filterable by status, with amounts collected, declines and refunds.

### Platform
- Role-based authorization (`Customer`, `Admin`), with ownership checks on every record.
- Centralized error handling that returns consistent RFC 7807 `ProblemDetails` responses.
- Swagger/OpenAPI with JWT support for testing the API.
- Seed data for a 20-plan catalog and a set of demo customers with realistic histories.
- **Microservices:** seven independently deployable services with a database each, an API gateway (YARP) with BFF composition, an event bus (MassTransit on RabbitMQ), an orchestrated saga with compensation, a transactional outbox, idempotent consumers, and graceful degradation when a service is down.
- **Notifications service:** listens to the other services' events and sends HTML + plain-text emails over SMTP (MailKit), exactly once per event, with a record of each email.
- `docker compose up` starts the whole system: SQL Server, RabbitMQ, Mailpit (a local test inbox), seven services and the gateway.

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Angular (standalone components, signals, lazy-loaded routes), TypeScript, HTML5, CSS3, RxJS |
| Services | C#, ASP.NET Core Web API (one per service), RESTful design, Swagger/OpenAPI (Swashbuckle) |
| API gateway | YARP reverse proxy, JWT validation, BFF composition endpoints |
| Messaging | RabbitMQ through MassTransit (retries, faults), transactional outbox |
| Email | SMTP through MailKit; Mailpit as the development inbox |
| Data access | ADO.NET (`Microsoft.Data.SqlClient`), parameterized stored procedures, views, LINQ in the service layer |
| Database | SQL Server / Azure SQL Database: **one database per service**, created and migrated by the service at startup |
| Security | JWT bearer authentication (validated by the gateway and every service), BCrypt, role-based authorization |
| Patterns | Microservices, database per service, API gateway + BFF, saga (orchestration), transactional outbox, idempotent consumer, dependency injection, repository, strategy (rating engine), DTOs |
| Testing | xUnit with hand-written fakes, architecture tests for service boundaries |
| Containers | Docker, Docker Compose |
| Tooling | Visual Studio Code, Git and GitHub |

## Architecture at a glance

```mermaid
flowchart TD
    SPA["Angular SPA"] --> GW["API gateway + BFF<br/>YARP routing, JWT, composition"]
    subgraph Microservices["Microservices, one database each"]
        ID["Identity<br/>users, auth, JWT"]
        CA["Catalog<br/>plans"]
        QU["Quotes<br/>rating, quotes"]
        EN["Enrollments<br/>policies, saga"]
        BI["Billing<br/>premium payments"]
        CL["Claims<br/>claims workflow"]
        NO["Notifications<br/>emails, preferences"]
    end
    GW --> ID & CA & QU & EN & BI & CL & NO
    ID --- IDB[("InsuranceIdentity")]
    CA --- CDB[("InsuranceCatalog")]
    QU --- QDB[("InsuranceQuotes")]
    EN --- EDB[("InsuranceEnrollments")]
    BI --- BDB[("InsuranceBilling")]
    CL --- KDB[("InsuranceClaims")]
    NO --- NDB[("InsuranceNotifications")]
    Microservices <--> BUS[["Event bus<br/>RabbitMQ (MassTransit)"]]
    NO --> SMTP[["SMTP server<br/>(Mailpit in development)"]]
```

| Service | Owns | Talks to |
|---|---|---|
| **Identity** | Users, sign-in, JWT issuing, email verification | Nobody (others call it). Publishes `CustomerRegistered`, `EmailVerificationRequested` |
| **Catalog** | Plans | Nobody (others call it) |
| **Quotes** (Quotes) | Rating engine, quotes | Catalog (HTTP). Consumes `EnrollmentActivated`, `EnrollmentFailed` |
| **Enrollments** (Enrollments) | Policies, **the enrollment saga** | Identity, Quotes, Catalog, Billing (HTTP). Publishes `EnrollmentRequested`, `EnrollmentActivated`, `EnrollmentFailed`, `EnrollmentCancelled` through an outbox |
| **Billing** | Premium schedules, payments, payment reminders | Enrollments (HTTP). Consumes `EnrollmentRequested`, `EnrollmentCancelled`, `EnrollmentFailed`; publishes `PremiumScheduleCreated`, and `PaymentReceived`, `PaymentDeclined` and `PaymentReminderDue` through an outbox |
| **Claims** (Claims) | Claims, review workflow | Enrollments, Billing, Catalog, Identity (HTTP). Publishes `ClaimSubmitted`, `ClaimStatusChanged` through an outbox |
| **Notifications** | Emails sent, customers' email preferences | Identity, Enrollments, Catalog (HTTP). Consumes the account, enrollment, claim and payment events above and emails the customer. Nothing depends on it |
| **Gateway + BFF** | Nothing | Routes `/api/*`; checks that the caller's account is active (Identity); composes the admin customer list (Identity + Enrollments + Claims) |

**Rules that keep them independent:** a service never reads another service's database, and never references another service's code, only its `*.Contracts` package (events, internal API shapes and a typed HTTP client). `Insurance.Architecture.Tests` fails the build if either rule is broken. Queries go over HTTP; anything that changes another service's data goes through an event.

Inside each service, a request flows one way:

```mermaid
flowchart TD
    C["Controllers<br/>HTTP, routing, authorization"] --> S["Services<br/>business rules, calls to other services"]
    S --> R["Repositories<br/>ADO.NET, error translation, outbox"]
    R --> P["Stored procedures<br/>data rules, transactions"]
    P --> T[("The service's own tables and views")]
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the enrollment saga, the outbox, sequence diagrams, the data model of each service, the claim workflow, the rating engine, the security model and the complete error-number registry.

## Key business rules

| Area | Rule |
|---|---|
| Quotes | Priced on the server from the customer's answers. The client never sends a price. Valid for 30 days and usable once. |
| Rating | `premium = base rate x every factor`, rounded to cents, minimum $1.00. Some answers trigger an underwriting decline. |
| Enrollment | Start date from yesterday (time-zone slack) to 90 days ahead. One active enrollment per plan per customer. The premium is copied from the quote, so later plan price changes never alter existing policies. |
| Payments | Installments must be paid **in order**. An unpaid installment becomes **Overdue** more than **5 days** past its due date. A payment alert shows when the next due date is within **7 days**. Every successful payment is emailed as a receipt, and every declined or failed one as a notice. |
| Reminders | Emailed 3 days before the due date, on the due date, the day the installment turns Overdue, then 5 and 10 days after that. Each one at most once per installment. Customers choose at sign-up (on by default) and can change it on their profile; receipts, declines, claim and account emails always go out. |
| Email verification | New accounts must confirm their email before **enrolling** or **filing a claim** (`403` until then). Links are single-use, expire after 24 hours, and only their hash is stored. Resending is limited to one per minute and six a day. |
| Claims | Active policy only. The incident must fall inside the coverage period and not be in the future. No overdue premiums. The amount cannot exceed the remaining coverage (policy coverage minus approved and paid claims). |
| Claim workflow | Submitted, Under review, then Approved or Rejected, then (approved only) Paid. A rejection needs a reason. Concurrent edits are detected. |
| Accounts | Deactivated accounts cannot sign in and existing tokens stop working. Admin accounts cannot be deactivated from the UI. |

## Getting started

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose (for the services, SQL Server and RabbitMQ)
- [.NET SDK](https://dotnet.microsoft.com/download) (current LTS), to build, test or run a service outside Docker
- [Node.js](https://nodejs.org/) 20 or later, and the Angular CLI (`npm install -g @angular/cli`)
- Recommended VS Code extensions: C# Dev Kit, SQL Server (mssql), Angular Language Service

### 1. Clone

```bash
git clone https://github.com/<your-username>/insurance-management-system.git
cd insurance-management-system
```

### 2. Start the backend

```bash
cp deploy/.env.example deploy/.env
```

Edit `deploy/.env` to set your own `SA_PASSWORD` and `JWT_KEY`, then:

```bash
docker compose -f deploy/docker-compose.yml up --build
```

This starts SQL Server, RabbitMQ, Mailpit, the seven services and the gateway. Each service creates its own database on first start (its scripts are in `src/Services/<Service>/Insurance.<Service>/Database/`), and the Catalog service seeds the 20 plans. The API is at <http://localhost:5080/api>, the RabbitMQ dashboard at <http://localhost:15672> (guest / guest) shows the queues and the events flowing, and **every email the system sends lands in Mailpit at <http://localhost:8025>**, so you can open the verification link from there after registering.

### 3. Run the frontend

```bash
cd insurance-web
npm install
ng serve
```

Open <http://localhost:4200>. The API address is set in `src/environments/environment.development.ts` (`http://localhost:5080/api`, the gateway).

### 4. Create the admin account

There is intentionally no public admin sign-up. Register a normal account in the app, then promote it in the Identity service's database:

```bash
docker compose -f deploy/docker-compose.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "<your SA_PASSWORD>" -Q "UPDATE InsuranceIdentity.accounts.Users SET Role = 'Admin' WHERE Email = 'you@example.com'"
```

Sign in again so the new role is placed in your token.

### 5. Optional: load the demo data

```bash
docker compose -f deploy/docker-compose.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "<your SA_PASSWORD>" -i /seed/demo-data.sql
```

`deploy/seed/remove-demo-data.sql` removes it again. Demo customers are already verified. These dev-only scripts write to all the databases at once, which works only because Docker Compose puts them on one SQL Server instance.

### Running a service outside Docker

Each service is a normal ASP.NET Core project with its own port (Identity 5101, Catalog 5102, Quotes 5103, Enrollments 5104, Billing 5105, Claims 5106, Notifications 5107, gateway 5080, set in each `Properties/launchSettings.json`). With SQL Server, RabbitMQ and Mailpit running (for example `docker compose -f deploy/docker-compose.yml up sqlserver rabbitmq mailpit`), set the service's connection string and the JWT key, then run it:

```bash
cd src/Services/Catalog/Insurance.Catalog
dotnet user-secrets set "ConnectionStrings:Database" "Server=localhost,1433;Database=InsuranceCatalog;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;"
dotnet user-secrets set "Jwt:Key" "<the same JWT_KEY as every other service>"
dotnet run
```

Each service has Swagger at `/swagger` in the Development environment.

## Demo accounts

After loading `deploy/seed/demo-data.sql`, every demo customer signs in with the password **`Demo#2026pass`**.

| Customer | Email | What to look at |
|---|---|---|
| Emma Carter | `emma.carter@demo.example` | Fully paid up. Claims in **Approved** and **Under review**. |
| Marcus Johnson | `marcus.johnson@demo.example` | Home and Life policies. Claims in **Paid** and **Submitted**. |
| Priya Patel | `priya.patel@demo.example` | **Overdue premiums** (red alert, claims blocked on that policy). |
| Daniel Kim | `daniel.kim@demo.example` | Travel policies, a **Rejected** claim with the reviewer's reason, and an **Expired** policy. |
| Sofia Rodriguez | `sofia.rodriguez@demo.example` | New customer with a **payment due today** alert and no claims. |
| Tyler Brooks | `tyler.brooks@demo.example` | **Inactive** account (sign-in is refused) with a cancelled policy. |

The admin account is the one you promoted in step 4. These credentials exist for the development environment only.

## Configuration reference

Every setting can also be given as an environment variable (`:` becomes `__`), which is how `deploy/docker-compose.yml` configures the containers.

| Setting | Used by | Notes |
|---|---|---|
| `ConnectionStrings:Database` | Each service | That service's own database. **Never shared between services** |
| `Jwt:Key` | Every service and the gateway | **User secrets, `.env` or a secret store only. Never commit it.** At least 32 characters, the same everywhere |
| `Jwt:Issuer`, `Jwt:Audience` | Every service and the gateway | Must match between token creation (Identity) and validation |
| `Jwt:ExpiryMinutes` | Identity | Default 60 |
| `RabbitMq:Host`, `Username`, `Password` | Identity, Quotes, Enrollments, Billing, Claims, Notifications | The event bus |
| `Email:Host`, `Port`, `Security`, `Username`, `Password` | Notifications | The SMTP server. `Security` is `None` (Mailpit), `StartTls` (port 587) or `SslOnConnect` (port 465). **Password in user secrets or `.env` only.** An empty `Host` logs emails instead of sending them |
| `Email:FromAddress`, `FromName` | Notifications | The sender |
| `Email:AppBaseUrl` | Notifications | The Angular app's address; links in emails point there |
| `Email:SupportEmail` | Notifications | Where customers get help. Every email says it's automated ("don't reply") and points here |
| `BusinessTimeZone` | Every service | The insurer's time zone (IANA id, default `America/New_York`, i.e. EST/EDT): what "today" means for due dates, overdue premiums, coverage and claims, and the zone emails show times in |
| `Email:SkipDomains` | Notifications | Recipient domains never emailed (e.g. `demo.example` against a real mail server) |
| `Reminders:Enabled`, `Interval` | Billing | The payment reminder job (default on, every hour) |
| `EmailVerification:LinkLifetime`, `ResendCooldown`, `MaxEmailsPerDay` | Identity | Defaults 24 hours, 1 minute, 6 |
| `Services:<Name>` | Callers of another service | Base address of that service's internal API, for example `Services:Catalog` |
| `ReverseProxy:Clusters:<name>:Destinations:d1:Address` | Gateway | Where each service runs |
| `Cors:AllowedOrigins` | Gateway | Frontend origins allowed to call the API |
| `Enrollment:SagaTimeout` | Enrollments | How long `POST /api/enrollments` waits for the saga before answering `202` (default 10 seconds) |
| `Payments:Provider` | Billing | Which payment processor to use. Only `Mock` (the simulation) exists; a real one is a new `IPaymentGateway` class |
| `Payments:Mock:LatencyMs` | Billing | How long the simulated processor takes to answer (default 700 ms) |
| `apiUrl` | `insurance-web/src/environments/environment.development.ts` (dev) and `environment.ts` (production build) | The gateway's address |

## API overview

Every endpoint is reached through the gateway at `/api`. Each service documents its own endpoints in its Swagger UI. Statuses follow REST conventions: `201` on create, `202` when an enrollment is accepted but still being set up, `204` on updates with no body, `400` validation or business rule, `401` not signed in, `403` wrong role or email not verified yet, `404` not found **or not yours**, `409` state conflict, `429` too many verification emails requested, `503` a service the request needs is unavailable.

| Area (service) | Endpoint | Access |
|---|---|---|
| **Auth** (Identity) | `POST /api/auth/register` | Public |
| | `POST /api/auth/login` | Public |
| | `GET /api/auth/me` | Signed in |
| | `POST /api/auth/verify-email` | Public (the token from the emailed link) |
| | `POST /api/auth/resend-verification` | Signed in |
| | `POST /api/auth/change-email` | Signed in (`newEmail`, `currentPassword`; the new address must then be verified) |
| | `POST /api/auth/change-password` | Signed in (`currentPassword`, `newPassword`, `confirmNewPassword`) |
| **Policies** (Catalog) | `GET /api/policies` | Signed in (`?includeInactive=true` for admins) |
| | `GET /api/policies/{id}` | Signed in |
| | `POST /api/policies`, `PUT /api/policies/{id}`, `DELETE /api/policies/{id}` | Admin (delete is a soft delete) |
| **Quotes** (Quotes) | `GET /api/quotes/questions/{policyId}` | Customer |
| | `POST /api/quotes` | Customer |
| | `GET /api/quotes/{id}` | Customer (owner) |
| **Enrollments** (Enrollments) | `GET /api/enrollments/mine` | Signed in (own records) |
| | `GET /api/enrollments/{id}` | Owner or admin |
| | `POST /api/enrollments` | Customer (`201`, or `202` while the saga is still running) |
| | `PUT /api/enrollments/{id}/cancel` | Owner |
| **Payments** (Billing) | `GET /api/enrollments/{id}/payments` | Owner or admin |
| | `POST /api/payments/{id}/pay` | Customer (owner). Body: a payment-method token or a saved method, and an idempotency key. Answers `200` with `outcome: Succeeded` (a receipt) or `Declined` (the reason) |
| | `GET /api/payments/transactions?enrollmentId=&status=` | The payment log: one policy (owner or admin), your own attempts, or everyone's (admin) |
| | `GET /api/payments/transactions/{id}` | A receipt (the customer who paid, or an admin) |
| | `GET /api/payment-methods`, `DELETE /api/payment-methods/{id}` | Customer: saved payment methods (brand, last 4 digits, expiry) |
| **Claims** (Claims) | `POST /api/claims` | Customer |
| | `GET /api/claims/mine` | Customer |
| | `GET /api/claims?status=` | Admin |
| | `GET /api/claims/{id}` | Owner or admin |
| | `PUT /api/claims/{id}/review` | Admin |
| **Notifications** (Notifications) | `GET /api/notifications/preferences`, `PUT /api/notifications/preferences` | Customer |
| | `GET /api/notifications/history?take=` | Customer (own emails) |
| **Customers** (gateway BFF) | `GET /api/admin/customers?search=&isActive=` | Admin |
| | `PUT /api/admin/customers/{id}/active` | Admin |

Services also expose `/internal/*` endpoints for each other (for example `GET /internal/plans?ids=`). The gateway never routes them, so they are not reachable from outside.

## Testing

Unit tests cover the business logic with in-memory fakes of the repositories and of the other services' clients, so no database, broker or other service is needed. There is one test project per service, plus architecture tests that guard the service boundaries:

```bash
dotnet test Insurance.sln
```

| Test class | Covers |
|---|---|
| `RatingStrategyTests` | Prices for every policy type, underwriting declines, answer validation |
| `ClaimStatusTransitionsTests` | Every allowed and blocked workflow move |
| `AuthServiceTests` | Password hashing, email normalizing, role forcing, inactive users |
| `EmailVerificationServiceTests` | Verification links (hashing, single use, expiry, replacement), resend limits, the sign-up email choice, changing the email (password, conflicts, re-verification, notice to the old address) and the password |
| `EnrollmentServiceTests` | Start-date window, payment alerts, ownership, cancellation, the enrollment saga (completed, still running, failed, quote released), degradation when Billing is down |
| `ClaimServiceTests` | Review rules, the concurrency guard, claim visibility, claim eligibility, names and plans from other services |
| `CheckoutServiceTests` | Approved, declined and failed payments; retries never charge twice; every rule checked before charging; automatic refund when an approved charge can't be applied; saved cards; the receipt and declined emails saved with each outcome |
| `MockPaymentGatewayTests` | Test cards and their decline reasons, expired cards, idempotent charges, malformed tokens |
| `PaymentServiceTests` | Who sees schedules, the payment log and receipts; cancelled policies |
| `ReminderScheduleTests`, `PaymentReminderServiceTests` | Which reminder goes out on which day, no repeats or stale reminders, only for policies in force |
| `TemplateTests` | Every email's subject and content, the "automated message" notice on all of them, HTML encoding of customer text, links into the app, times in Eastern time |
| `NotificationDispatcherTests`, `ConsumerTests` | Exactly-once sending, reminder opt-in/out (sign-up and profile), deactivated accounts, failures and retries, events on MassTransit's in-memory test bus |
| `InternalQuotesControllerTests` | A quote can only be reserved for the caller |
| `ServiceBoundaryTests` | No service references another's implementation, no service's SQL touches another service's schema, and nothing depends on the Notifications service |

An end-to-end test drives the whole running system through the gateway, the way the Angular app does: accounts, quotes, the enrollment saga, payments (declines, retries, receipts), claims, the admin screens and cancellation. With the stack running (`docker compose -f deploy/docker-compose.yml up --build -d`):

```bash
python tests/e2e/e2e_test.py
```

The stored procedures, the saga over RabbitMQ and the gateway were also verified end to end with Docker Compose: a scripted run of 95 requests returns the same responses as the original single API, and the saga recovers when Billing is stopped and restarted, and compensates when Billing keeps failing.

Email notifications were verified end to end the same way (SQL Server, RabbitMQ and an SMTP inbox): a scripted run of 40 checks covers registration, the `403` before verifying, verifying, enrolling, paying, filing and reviewing a claim, due-soon and overdue reminders with the opt-out, receipts and declined-payment notices from checkout, and checks that every email arrives exactly once.

## Project structure

```
insurance-management-system/
├── Insurance.sln
├── src/
│   ├── Gateway/Insurance.Gateway/   API gateway + BFF: YARP routes, JWT and active-account check, /api/admin/customers
│   ├── BuildingBlocks/              Shared plumbing: hosting defaults, JWT, database migrator, HTTP clients, messaging, outbox
│   └── Services/                    One folder per microservice
│       ├── Identity/                Users, sign-in, JWT
│       ├── Catalog/                 Plans
│       ├── Quotes/                  Rating engine and quotes
│       ├── Enrollments/             Policies and the enrollment saga
│       ├── Billing/                 Premium schedules and payments
│       ├── Claims/                  Claims and the review workflow
│       └── Notifications/           Emails: event consumers, templates, SMTP, preferences
│           ├── Insurance.Claims/            The service: Program.cs, Controllers, Services, Repositories,
│           │                                Messaging (consumers), Database/ (its own SQL scripts)
│           └── Insurance.Claims.Contracts/  What other services may use: events, internal API shapes, HTTP client
├── tests/                           xUnit tests per service, plus Insurance.Architecture.Tests
├── deploy/
│   ├── docker-compose.yml           SQL Server, RabbitMQ, Mailpit, seven services, gateway
│   ├── Dockerfile                   One image recipe for every service
│   └── seed/                        Dev-only demo data
├── insurance-web/                   Angular client
│   └── src/app/
│       ├── core/                    Services, models, guards, interceptor, utilities
│       ├── features/                auth (incl. verify-email), dashboard, policies, enrollments, payments,
│       │                            claims, customers, profiles (incl. email settings)
│       └── layout/                  App shell and navigation
└── docs/                            Architecture document and screenshots
```

## Deploying to Azure

The containers built by `deploy/Dockerfile` run on **Azure Container Apps** (or AKS), one container app per service plus the gateway:

1. Create one **Azure SQL database per service** (`InsuranceIdentity`, `InsuranceCatalog`, `InsuranceQuotes`, `InsuranceEnrollments`, `InsuranceBilling`, `InsuranceClaims`, `InsuranceNotifications`). The services create their tables on first start.
2. Provide the event bus: RabbitMQ in a container app, or switch `Insurance.BuildingBlocks/Messaging/Messaging.cs` to Azure Service Bus (`UsingAzureServiceBus`, one line in MassTransit).
3. Push the images to Azure Container Registry. Give only the **gateway** external ingress; the seven services get internal ingress, so they are reachable only from inside the environment.
4. Set each container's settings as in `deploy/docker-compose.yml`: its own `ConnectionStrings__Database`, the shared `Jwt__Key` (from Key Vault), `RabbitMq__*`, `Services__*` and, for the gateway, the `ReverseProxy__Clusters__*` addresses and `Cors__AllowedOrigins__0`. The Notifications service also needs an SMTP provider in `Email__*` (password from Key Vault) and `Email__AppBaseUrl` set to the deployed frontend; without a host it only logs emails.
5. Point `apiUrl` in `insurance-web/src/environments/environment.ts` (the production build) at the gateway, run `ng build`, and host the output (for example Azure Static Web Apps). Angular's client-side routes need a fallback to `index.html`.

The previous single-API deployment used one database. Its data is not migrated automatically; the services start with empty databases (plus the plan catalog).

## Email notifications

The **Notifications service** sends every email. It listens to events the other services already publish on RabbitMQ, so no service waits for an email and none of them knows it exists. If the mail server is down, MassTransit retries the event and nothing else is affected.

```mermaid
flowchart LR
    ID["Identity"] -- "CustomerRegistered<br/>EmailVerificationRequested" --> BUS[["RabbitMQ"]]
    EN["Enrollments"] -- "EnrollmentActivated<br/>EnrollmentCancelled" --> BUS
    BI["Billing<br/>(checkout + hourly reminder job)"] -- "PaymentReceived, PaymentDeclined<br/>PaymentReminderDue" --> BUS
    CL["Claims"] -- "ClaimSubmitted<br/>ClaimStatusChanged" --> BUS
    BUS --> NO["Notifications"]
    NO -- "recipient, plan name (HTTP)" --> LOOK["Identity, Enrollments, Catalog"]
    NO -- "SMTP (MailKit)" --> MAIL[["Mail server / Mailpit"]]
```

| Email | Sent when | Can be turned off |
|---|---|---|
| Verify your email / Confirm your new email address | Sign-up, resend, email change | No |
| Your email address was changed | To the **old** address after a change: "if this was you, ignore it; if not, contact support immediately" | No |
| Your password was changed | After a password change, with the same warning | No |
| Policy active / Policy cancelled | The enrollment saga completes, the customer cancels | No |
| Payment receipt | Every installment paid at checkout (receipt number, payment method, installment *n* of *N*, next payment, link to the in-app receipt) | No |
| Payment didn't go through | A declined or failed checkout attempt (the reason, nothing charged, a "try again" link) | No |
| Payment reminder | 3 days before, on the due date, when Overdue, +5 days, +10 days | **Yes**: at sign-up, and in the profile's "Email settings" |
| Claim received / Claim under review / approved / rejected (with the reason) / paid | Each step of the claim workflow | No |

How it stays reliable:

- **Exactly once.** Each email has a key (e.g. `receipt:123`, `reminder:123:DueToday`). A repeated event finds the key already sent and does nothing; a failed send is recorded and retried.
- **No lost events.** Billing and Claims publish through the same **transactional outbox** as Enrollments, so an event exists exactly when its change committed. The receipt is saved in the same database transaction that marks the installment paid, and the declined notice with the declined outcome; a replayed checkout request (same idempotency key) sends nothing new.
- **Reminders once per stage.** Billing records each (installment, stage) under a primary key before publishing, so several Billing instances never double-send.
- **Background calls.** Consumers and the reminder job have no signed-in user, so they call other services' internal APIs with a short-lived **service token** (role `Service`, no user id: useless on public endpoints and refused by the gateway).
- **Safe content.** All customer-entered text (names, reviewer notes) is HTML-encoded in the templates, and every email also has a plain-text version.

## Known limitations

- **Simulated payments.** The payment processor is a simulation (`MockPaymentGateway`): no money moves. Everything around it is built as for a real processor (tokens instead of card numbers, idempotency keys, a transaction log, refunds, saved methods), so connecting one is a new `IPaymentGateway` class plus its browser library. Not yet simulated: webhooks for asynchronous confirmations, 3-D Secure, disputes, and a reconciliation job for a charge that succeeds while the service crashes before recording it. Cancelling a policy removes its unpaid installments instead of voiding them.
- **Illustrative pricing.** Rating multipliers are hard-coded in the strategy classes. They are not actuarial data and cannot be edited from the UI.
- **One business time zone.** "Today" (due dates, overdue premiums, coverage and claim dates) is US Eastern for every customer, wherever they are (`BusinessTimeZone`). Screens show timestamps in the browser's own time zone.
- **Existing sessions survive a password change.** Tokens already issued stay valid until they expire (60 minutes); there is no "sign out everywhere".
- **Token storage.** The JWT is kept in `localStorage` with no refresh-token flow. Production systems often use httpOnly cookies.
- **Per-request account check.** The gateway asks the Identity service, on every request, whether the account is still active with the same role. Deactivation is immediate, but it costs one internal HTTP call per request.
- **Eventual consistency.** The premium schedule is created by Billing a moment after the enrollment is saved, and unpaid installments are removed a moment after a cancellation. The API hides both gaps (it waits for the saga, and treats a cancelled policy's unpaid installments as gone), but other services' views of each other can briefly lag.
- **Shared JWT key.** All services validate tokens with one symmetric key. Asymmetric keys (Identity signs, everyone else only verifies) would be stronger.
- **Synchronous queries between services.** Claims needs Enrollments, Billing, Catalog and Identity to be up to file a claim. Policy cards degrade gracefully without Billing; other screens return `503` if a service they need is down.
- **No pagination, file attachments or push notifications** yet.
- **Development emails go to Mailpit.** Sending to real inboxes needs an SMTP provider (see `Email:*` in the configuration reference); the live demo doesn't send email yet.
- **Reminders are not sent late.** The reminder job runs hourly and sends a reminder on its day or the day after. If Billing is down for longer, that reminder is skipped rather than sent late (the next stage still goes out).
- **Test scope.** Business logic is unit-tested. The end-to-end run with Docker Compose is a script, not yet part of an automated pipeline. There are no frontend tests.
- **Single admin role** with no admin-management screen. Admins are promoted by SQL.

## Roadmap

- Admin-editable rating factors stored in the database
- Pagination and sorting on the large lists
- A "My quotes" screen and admin dashboard statistics
- Claim attachments
- Email delivery for the live demo through an SMTP provider, and SMS or push notifications
- Refresh tokens or httpOnly cookie sessions
- A GitHub Actions pipeline that builds the images and runs the end-to-end suite against Docker Compose
- Distributed tracing (OpenTelemetry) across the gateway, services and the bus
- Asymmetric JWT signing, and a refresh-token flow

## Documentation

- [Architecture document](docs/ARCHITECTURE.md): services, the enrollment saga, the outbox, data model, workflows, rating engine, security, **error-number registry**
- Each service's Swagger UI at `/swagger` (Development environment)

## Author

Built by Vimal Ramnarain as a full-stack portfolio project. [GitHub](https://github.com/<your-username>) · [LinkedIn](https://www.linkedin.com/in/YOUR-PROFILE)
