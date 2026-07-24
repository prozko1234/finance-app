# finance-app — Coding Conventions

Rules for writing code in this repo. Read before adding or changing code.

## Language
- **Code comments and XML doc comments: ALWAYS English.** No exceptions.
- **User-facing strings stay Ukrainian** — UI text, and API error messages that reach the
  user (the app's language is Ukrainian). Only comments are English.
- Identifiers, file names, commit messages: English.

## Architecture (Clean Architecture, dependencies point inward)
```
Api  →  Infrastructure  →  Application  →  Domain
Api  →  Application
```
- **Domain** — entities, enums, pure logic (e.g. `SafeToSpendCalculator`), ports
  (`IFxConverter`), `Result<T>`/`Error`. No framework dependencies.
- **Application** — use-case services (`*Service`), DTOs (`Contracts`), validators,
  mapping, `IAppDbContext` abstraction. Depends on Domain only.
- **Infrastructure** — EF Core `AppDbContext`, FX providers (NBP/ECB), external I/O.
  Implements Application/Domain ports.
- **Api** — thin Minimal API endpoints: parse → call service → map `Result` to HTTP.
  No business logic here.

## Patterns (MVP-appropriate — do NOT over-engineer)
- **`Result<T>` for expected failures** (not found, validation, unsupported currency).
  Never use exceptions for control flow. Exceptions are for bugs/unexpected only.
- **Business rules live in Application services** and return `Result<T>` — so they are
  testable without HTTP.
- **Input-shape validation via FluentValidation** (`AbstractValidator<T>`), enforced by
  `ValidationFilter<T>` endpoint filter. Business rules (category exists, fx) go in services.
- **Errors as ProblemDetails** (RFC 7807). Map `Error` → HTTP in `ResultExtensions.ToProblem`.
  Unexpected exceptions → `GlobalExceptionHandler` → 500 ProblemDetails + log.
- **`IAppDbContext`** abstraction over EF — no repository classes (EF DbContext already is
  Unit of Work + repositories).
- **Manual mapping** in `Application/Mapping` — no AutoMapper.
- **Typed `HttpClient`** per external source, short timeout.
- **Do NOT add** CQRS/MediatR, repository interfaces over EF, DDD aggregates, mapping
  libraries. This is a solo MVP; that ceremony is not worth it.

## Money & currency
- `decimal` for all money. Store `AmountOriginal + CurrencyOriginal + AmountBase + FxRate + FxDate`.
- Base currency = PLN. Fx rate and date are **fixed at creation, never recomputed retroactively**.
- Round money half-up (`MidpointRounding.AwayFromZero`) to 2 decimals; safe-to-spend rounds down.
- Rates: NBP primary (only source with UAH), ECB fallback (no UAH).

## EF Core
- Migrations are committed to git (schema versioning). Create with
  `dotnet ef migrations add <Name> --project Infrastructure --startup-project Api --output-dir Migrations`.
- Enums stored as strings; decimals stored as text in SQLite.

## Testing
- Pure logic (`SafeToSpendCalculator`) and services: unit tests.
- FX/HTTP parsing: test with a stub `HttpMessageHandler` — **no real network in tests**.
- Endpoints: integration tests via `WebApplicationFactory` with in-memory SQLite and a
  fake `IFxConverter`.
- Money math must always be covered.

## Naming
- Types/methods `PascalCase`; locals/params `camelCase`; interfaces `I`-prefixed.
- Async methods end with `Async`. One public type per file; file name = type name.

## Security
- CI must be clean of NU1903 advisories. Pin vulnerable transitive packages to a patched
  version (same major where possible).

## Frontend (React + TS + Vite + Tailwind)
- Server state via TanStack Query; typed `api` client; no `any`.
- Tailwind utility classes; mobile-first; dark mode supported.
- User-facing text Ukrainian; comments English.
