# Portfolio API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expose the authenticated user's paper-trading portfolio with cash, invested value, total value, and holdings.

**Architecture:** Add an application portfolio query contract, an EF Core read service scoped by the authenticated user, and a protected `GET /api/portfolio` controller. The response derives invested value from persisted holdings' quantity and average cost; no external quote request is added.

**Tech Stack:** ASP.NET Core controllers, JWT authentication, EF Core, SQL Server production provider, SQLite API tests.

**Spec:** Issue #37 (`feature/portfolio-api`).

## Global Constraints

- Portfolio data must be scoped to the authenticated user.
- Holdings and balances are paper-trading data only.
- No external market-data call is required for this endpoint.
- Existing API error and JWT claim conventions remain unchanged.

## Review Focus

- Anonymous, invalid, and unknown JWT subjects must not expose portfolio data.
- A user's response must not include another user's holdings or transactions.
- Empty holdings must return zero invested and total value equal to cash.
- Decimal quantities and average costs must be aggregated without floating-point conversion.
- Portfolio data must not expose password, rowversion, or internal transaction fields.

### Task 1: Query contract and service

**Files:** Create `backend/StockLab.Application/Interfaces/IPortfolioService.cs`, `backend/StockLab.Application/DTOs/Portfolio/PortfolioSummary.cs`, `backend/StockLab.Infrastructure/Portfolio/PortfolioService.cs`, `backend/StockLab.Infrastructure/Portfolio/PortfolioRegistration.cs`.

- [ ] Write service tests for owner scoping, empty holdings, and weighted invested value.
- [ ] Run the focused tests and verify they fail because the service is absent.
- [ ] Implement `GetAsync(Guid userId, CancellationToken)` using `AsNoTracking`, `Include(portfolio => portfolio.Holdings)`, and `UserId` filtering.
- [ ] Calculate `InvestedValue` as the decimal sum of `Quantity * AverageCost` and `TotalValue` as cash plus invested value.
- [ ] Register the service in the API composition root.
- [ ] Run the focused tests and verify they pass.

### Task 2: Protected API endpoint

**Files:** Create `backend/StockLab.Api/Controllers/PortfolioController.cs`, `backend/StockLab.Api/DTOs/Portfolio/PortfolioResponse.cs`.

- [ ] Write API tests for authentication, owner isolation, response fields, empty holdings, and OpenAPI security.
- [ ] Run the tests and verify they fail because `/api/portfolio` is absent.
- [ ] Implement `GET /api/portfolio` using `ClaimsPrincipalExtensions.TryGetUserId` and return `401` for invalid identity, `404` for a missing portfolio, and `200` with the mapped summary.
- [ ] Verify the response contains only portfolio summary and holding fields.
- [ ] Run the full backend build and test suite.
