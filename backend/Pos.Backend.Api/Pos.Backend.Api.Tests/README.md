# Backend integration tests

The integration suite uses PostgreSQL because the production invariants depend on
real transactions, row locks, partial indexes, check constraints and FK behavior.

Set only the dedicated test connection string before running the suite:

```powershell
$env:HF_POS_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=hfpos_test;Username=hfpos_test;Password=test-only-password'
$env:ConnectionStrings__DefaultConnection = $env:HF_POS_TEST_CONNECTION_STRING
$env:ASPNETCORE_ENVIRONMENT = 'Testing'
$env:SeedDemoData = 'false'
$env:Jwt__Key = 'hfpos-local-test-only-jwt-key-long-enough-for-hmac-sha256'
$env:Jwt__Issuer = 'hfpos-local-tests'
$env:Jwt__Audience = 'hfpos-local-tests'
dotnet test backend\Pos.Backend.Api\Pos.Backend.Api.sln --configuration Release
```

The fixture refuses destructive setup unless the database name is `hfpos_test`,
`hfpos_ci`, or a name beginning with either prefix followed by an underscore. It
does not read the application's development connection string or User Secrets.
The HTTP integration host also needs the same dedicated test connection and
test-only JWT settings at startup; do not point either connection at Development.

At startup the fixture recreates the dedicated database and applies every real EF
migration. Between tests it truncates application tables while preserving the EF
migration history. Concurrent operations always use independent DbContexts.
