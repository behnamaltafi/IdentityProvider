# IdentityProvider

A reusable .NET 10 clean architecture starting point. The solution keeps the domain, application, infrastructure, and API layers separate and leaves the application database schema empty.

For the GitHub-backed portable Jenkins platform, start with the [migration runbook](docs/platform-migration.md). GitHub source migration is approved; the previous local Git lab is retained until infrastructure cutover and credential setup are approved and verified.

## Projects

- `IdentityProvider.Domain` contains domain entities and rules.
- `IdentityProvider.Application` contains application contracts and reusable validation helpers.
- `IdentityProvider.Infrastructure` contains EF Core persistence implementations and dependency registration.
- `IdentityProvider.Infrastructure.Identity` contains isolated local identity persistence, password/SMS sign-in, user/role/permission management, and external IdP authentication composition.
- `IdentityProvider.Api` is the ASP.NET Core composition root, with Swagger, JWT authentication, Problem Details, and a live health endpoint.
- `Tests` contains unit, integration, and deployment-script tests.

For local identity setup, SMS gateway configuration, endpoint contracts, and migration to a separate IdP, see the [Persian identity guide](docs/identity.fa.md). External IdP validation remains the default until local database and signing secrets are configured.

## Requirements

- .NET SDK 10
- SQL Server only when adding database-backed application features

## Run the API

From the repository root:

```powershell
dotnet run --project IdentityProvider.Api --urls http://localhost:8080
```

The API starts without a database connection. The live health check is available at `/health/live`; Swagger UI is at `/swagger` and its JSON document is at `/swagger/v1/swagger.json`.

Configure deployment values outside source control. For local development, initialize .NET User Secrets and set the database connection string and token issuer:

```powershell
dotnet user-secrets init --project IdentityProvider.Api
dotnet user-secrets set "ConnectionStrings:ApplicationDbContext" "<local-development-connection-string>" --project IdentityProvider.Api
dotnet user-secrets set "Authentication:Authority" "https://<your-identity-provider>" --project IdentityProvider.Api
```

Environment variables can also provide configuration, using `ConnectionStrings__ApplicationDbContext`, `Authentication__Authority`, and `Authentication__Audience`. HTTPS metadata validation is enabled by default. Keep real credentials out of committed settings files.

## Database schema

`ApplicationDbContext` intentionally has no entity sets, and the template does not create a database or apply migrations at startup. Add entities to the Domain layer, add `DbSet<TEntity>` properties and EF Core configurations in Infrastructure, and then create migrations only for a configured development database. For example:

```powershell
dotnet ef migrations add InitialCreate --project IdentityProvider.Infrastructure --startup-project IdentityProvider.Api --output-dir DataBase/Migrations
```

Review the generated migration before applying it. `dotnet ef database update` is a separate, explicit operation and should only be run against the intended development database.

## Build and test

```powershell
dotnet restore IdentityProvider.slnx
dotnet build IdentityProvider.slnx --no-restore
dotnet test IdentityProvider.slnx --no-build
```

See [`Tests/README.md`](Tests/README.md) for test projects and focused test commands. See [`docs/versioning.md`](docs/versioning.md) for the generic versioning workflow.

## Repository workflow

See the complete [Persian CI/CD guide](docs/CI-CD.fa.md), [Git Flow](docs/git-flow.md), [versioning](docs/versioning.md), and [CI checks](docs/ci.md). CLI release tooling defaults to preview. Official master publication requires the guarded Jenkins job and explicit approval; Production has a separate restricted approval.
