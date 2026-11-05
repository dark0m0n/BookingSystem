# Development admin initialization

Status: **implemented**, working tree inspected. This explicit operator command creates a new local admin; it is neither an HTTP route nor automatic startup seeding. Production provisioning and role administration remain outside this feature.

## Run and configure

Apply the existing migrations first; see [local startup](../../../README.md#local-startup). Keep `ConnectionStrings:DefaultConnection` in API User Secrets/environment configuration. Supply `DevelopmentAdmin:Email` and `DevelopmentAdmin:Password` through the same providers; optional `DevelopmentAdmin:DisplayName` defaults to `Development Admin`. Environment variables use double underscores, such as `DevelopmentAdmin__Password`. Do not add credential values to appsettings, launch profiles, scripts or Git.

Use the [interactive Bash command](../../../README.md#development-admin) to avoid literal credentials in shell history and discard its environment on exit. With credentials already supplied externally, the command is:

```bash
dotnet run --project src/TicketBooking.Api --no-launch-profile -- --environment Development --initialize-admin
```

`--no-launch-profile` prevents profile settings from obscuring the explicitly selected environment. Development configuration loads API User Secrets through the standard ASP.NET Core builder; see [Microsoft's configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0). The initializer does not require JWT settings, start HTTP or apply migrations. Normal API startup does not invoke it.

## Rules and outcomes

[DevelopmentAdminCommand.RunAsync](../../../src/TicketBooking.Api/Auth/DevelopmentAdminCommand.cs) rejects every environment except Development before reading credentials or resolving persistence. Email is trimmed, valid and at most 256 characters; password is untrimmed, required and at most 128. Display name is trimmed, nonblank and at most 200. Identity enforces its existing password/username rules and normalization.

The pre-existing Admin definition is required. A shared-context transaction creates the account with UserManager and assigns only Admin with AddToRoleAsync; either both commit or neither does. Identity persists the password hash. No access/refresh tokens are issued. Afterward, use normal login with the entered credentials; Admin also satisfies the Customer policy.

- Exit 0: a new admin committed, or the normalized email already belongs to an admin. Reruns never reset credentials, change the profile or add memberships.
- Exit 1: wrong environment, invalid/missing configuration, missing migrations/role, account conflict, Identity validation or database failure. Output uses fixed messages without credential values or provider exception details.
- An existing Customer/roleless account with that email is refused, as is an existing username associated with another email. Choose a distinct admin email; this is not a promotion or password-reset command.
- Concurrent creation of the same normalized account can produce one success and one safe failure at the unique constraint. Rerun after a committed success for the unchanged-account result; no automatic retry occurs.

## Evidence and limits

[Program](../../../src/TicketBooking.Api/Program.cs) strips the flag before configuration parsing and exits through the command branch before HTTP/JWT registration. [Command tests](../../../tests/TicketBooking.IntegrationTests/DevelopmentAdminCommandTests.cs) exercise environment/configuration guards and real CLI rejection. [PostgreSQL tests](../../../tests/TicketBooking.IntegrationTests/DevelopmentAdminPersistenceTests.cs) cover real CLI creation/exit, hashing, reruns, conflicts, weak passwords, missing role data and transaction rollback after forced role-write failure.

Tests use disposable schemas, not the development database. TLS/browser trust, production admin operations and concurrent initializer processes are not verified here. There are no new tables, migrations, default credentials or public privilege-granting routes.
