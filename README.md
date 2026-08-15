# Luma Calendar

Luma is a .NET 8 Blazor calendar backed by Microsoft SQL Server. It supports private, public, and shared events with cookie-based user accounts.

## Database setup

Development uses SQL Server LocalDB by default:

```text
Server=(localdb)\MSSQLLocalDB;Database=LumaCalendar;Trusted_Connection=True
```

For another SQL Server instance, set the connection string without committing credentials:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=YOUR_SERVER;Database=LumaCalendar;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True"
```

Apply schema changes and run the app:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update
dotnet run
```

The application also applies pending migrations during startup. Production deployments should use a restricted application database account and apply migrations as a separate deployment step.

## Access model

- Private events are visible only to their organizer and invited collaborators.
- Public events are discoverable by every signed-in user.
- Collaborators can view shared event details.
- Only the organizer can edit, delete, change visibility, or manage collaborators.
- Passwords are stored exclusively as ASP.NET Core password hashes; authentication cookies are HTTP-only.
