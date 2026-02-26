# Moneybox.App

Moneybox.App is a .NET Core sample banking application demonstrating clean architecture, domain-driven design principles, and safe transactional money operations.

## What It Does

The application supports:

- Transferring money between accounts
- Withdrawing money from an account
- Balance validation and business rule enforcement
- Notifications when:
  - Account balance falls below a threshold
  - Pay-in limit is nearly reached
- Concurrency handling with retry logic

## Architecture

The solution follows a layered design:

- **Domain** – Business models and rules (`Account`, `User`)
- **DataAccess** – Repository abstraction for persistence
- **Features** – Use cases (`TransferMoney`, `WithdrawMoney`)
- **Infrastructure** – Logging and notifications
- **Tests** – Unit tests validating business logic

Business logic is kept inside the domain layer, while persistence and external services are abstracted.

## Concurrency & Transactions

- Uses `TransactionScope` to ensure atomic operations
- Detects concurrency conflicts
- Retries failed operations (up to 3 attempts)
- Logs all key events using `ILogger`

## Test Project

The solution includes a dedicated test project that verifies:

- Successful money transfers
- Insufficient balance scenarios
- Invalid input handling
- Notification triggering logic
- Concurrency behavior (where applicable)

### Testing Approach

- Mocking dependencies such as:
  - `IAccountRepository`
  - `INotificationService`
  - `ILogger`
- Focused tests on business rules

## How to Run

```bash
git clone https://github.com/SathyapriyaSV/Moneybox.App.git
cd Moneybox.App
dotnet restore
dotnet build
dotnet test --logger "console;verbosity=detailed"