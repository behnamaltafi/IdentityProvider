# Tests

The solution has two .NET 10 test projects:

- `IdentityProvider.Application.UnitTests` covers application helpers and validation.
- `IdentityProvider.Infrastructure.IntegrationTests` covers layer boundaries, generic persistence with EF Core InMemory, and the hosted API.

## Run the tests

From the repository root, run:

```powershell
dotnet restore IdentityProvider.slnx
dotnet test IdentityProvider.slnx
```

To collect coverage, run:

```powershell
dotnet test IdentityProvider.slnx --collect:"XPlat Code Coverage"
```

The API fixture uses the `Testing` environment with placeholder JWT settings and no external services or database connection. The smoke tests check `/health/live` and `/swagger/v1/swagger.json`.

Pure application tests belong in the unit test project. Tests that need EF Core or an HTTP host belong in the integration test project. Keep test entities and fixtures neutral; add domain-specific test data alongside the feature that needs it.

Priority CI coverage uses `Tests/coverlet.priority.runsettings` to measure the existing application validators with an 80% line minimum. The CI discovers inherited test properties by MSBuild evaluation rather than searching project text. See [CI documentation](../docs/ci.md) for fresh TRX/Cobertura artifacts and the separate live OpenAPI check.
