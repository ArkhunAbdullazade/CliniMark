# MedAnnotateApp

A web application for collecting structured annotations on medical publication images. Medical specialists mark regions associated with terms in the source text; medical students identify visible findings and supply their own labels. Both workflows preserve annotation data for research and future AI evaluation.

MedAnnotateApp was used to collect structured medical image annotations for the evaluation of **OpenMedSeg**, an explainable AI research project for medical imaging involving researchers from **Stanford Medicine, Mount Sinai, the Hasso Plattner Institute, and Charité**.

Built with **ASP.NET Core MVC, .NET 10, and PostgreSQL**, with custom canvas tools for rectangle and freehand annotation.

[Full walkthrough](https://drive.google.com/file/d/1WtMTApfPg1Q_AMeK0-lMWVt-EKZZpWtu/view?usp=sharing)

**The demos and screenshots show an earlier local development version.** Drawn regions and sample labels illustrate the interface, rather than validated medical annotations.

## Annotation workflows

### Medical specialists: annotate against source terms

Specialists work through highlighted medical terms in the publication text and mark the corresponding image regions. They can add comments, flag a term as not visible or abstract, or skip it when uncertain. Saving advances to the next term and records annotation timing.

<img width="1865" height="902" alt="DEMOSS2" src="https://github.com/user-attachments/assets/0bb8cbea-b1e3-4299-8545-15a0b9eda3a2" />

**Specialist demo · 56 seconds.** Drawing regions, using the magnifier, adding comments, and moving between terms.

https://github.com/user-attachments/assets/94dad84d-b075-431b-9890-afe414f0dc13

### Medical students: identify and label findings

Students annotate what they can identify and assign free-text labels. Regions can be grouped under a shared label, edited, or deleted before submission. These annotations provide a separate source of data for supplementary evaluation.

<img width="1866" height="902" alt="DEMOSS3" src="https://github.com/user-attachments/assets/24ac8b9f-e1f4-43b5-9394-5cb080c3b7c5" />

**Student demo · 50 seconds.** Creating regions, assigning labels, and editing annotation groups.

https://github.com/user-attachments/assets/b1e7f00c-6cef-4361-a3d1-9c89e0a28a19

### Drawing tools

Both workflows provide boundary box and freehand contour tools, with zoom to area for inspecting small details.

| Boundary box and freehand contour regions |
| --- |
| <img width="620" height="502" alt="DEMOSS4" src="https://github.com/user-attachments/assets/eb2d3831-7b87-492a-931c-1be9b61ce850" /> |

### Participant access and image assignment

Participants pass a shared research access gate, then register or log in with their own account password through ASP.NET Core Identity. Ordinary email addresses are accepted; ownership confirmation is not required. Each image selection list requires at least one option and supports multiple options. Images match any selected option within each list and must match all three lists. Selections can be changed at `/Identity/Filters`.

Selecting `medical student` assigns the `Medical_Student` role; other positions receive `Professional`. Each role has its own annotation workspace.

<details>
<summary>View participant registration</summary>

<img width="1866" height="902" alt="DEMOSS1" src="https://github.com/user-attachments/assets/0bb96c7e-f2d6-4b18-ad69-87c65388a849" />

The registration screen shows participant preferences and required-field validation. The [full walkthrough](https://drive.google.com/file/d/10KE4MSIjdLQpE0J9nxgl21jFsLJl3jjM/view?usp=sharing) provides the broader application demo.

</details>

## Stored annotations and progress

PostgreSQL stores annotations alongside source-image and participant metadata. Professional records include region coordinates, term decisions, comments, and timing data; student records retain grouped regions and their labels.

| Region coordinates | Annotation timing |
| --- | --- |
| <img width="757" height="216" alt="AnnotatedData1DB" src="https://github.com/user-attachments/assets/737b242b-0302-4df0-9f25-926f202920da" /> | <img width="111" height="215" alt="AnnotatedData2DB" src="https://github.com/user-attachments/assets/369f6c14-270e-41ba-9f27-ed3f43a8f3a3" /> |

Saved keyword states preserve progress when a specialist has not finished every term on an image. Separate ownership fields—`LockedByUserId` for professionals and `LockedByStudentUserId` for students—track image locks independently for each workflow, helping reduce overlapping work within a role. Completion is tracked through `IsAnnotated` and `IsAnnotatedByStudent`.

<img width="1095" height="132" alt="MedDataDB" src="https://github.com/user-attachments/assets/df6f775c-26a4-4d47-b5eb-c78cd420e7b8" />

<details>
<summary>Main database tables</summary>

| Table | Purpose |
| --- | --- |
| `MedDatas` | Source-image metadata, progress, and locks |
| `MedDataKeywords` | Extracted terms associated with images |
| `AnnotatedMedDatas` | Professional annotation records |
| `AnnotatedByStudentsMedDatas` | Student annotation groups |
| ASP.NET Core Identity tables | User accounts and roles |

</details>

## Architecture and stack

| Layer | Project | Contents |
| --- | --- | --- |
| Core | `src/MedAnnotateApp.Core` | Models, repositories, and core services |
| Infrastructure | `src/MedAnnotateApp.Infrastructure` | Data access, repository implementations, services, and settings |
| Presentation | `src/MedAnnotateApp.Presentation` | MVC controllers, Razor views, DTOs, migrations, and static assets |

The application uses ASP.NET Core Identity for accounts, EF Core 10 with Npgsql for PostgreSQL access, and EPPlus for optional Excel seed data. The interface uses locally served Bootstrap 5.3.8, jQuery 3.7.1, and custom canvas tools. Docker Compose runs one web instance and PostgreSQL, with persistent database and data-protection-key volumes.

## Run with Docker

Configure the intended database, shared gate hash, persistent keys, hostname, trusted HTTPS proxy and backups before public deployment. Production does not automatically migrate or seed data.

```powershell
# Create .env from .env.example only if it does not exist; configure real values.
docker compose --project-name medannotateapp build --pull webapp
# Review/apply the schema and load approved image data before starting webapp.
docker compose --project-name medannotateapp up -d
```

Use the public HTTPS hostname through the reverse proxy. Container HTTP is loopback-only on port 8080.

Startup initializes the two roles and fails clearly if PostgreSQL or its migration state is not ready. Sample Excel seeding is opt-in and false by default.

### Configuration

Keep private values in `.env`, environment variables, or .NET user secrets.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:MedDataDb` | PostgreSQL connection string |
| `AuthorizationAccessPasswordHash` | BCrypt hash for the shared authorization gate |
| `DataProtection:KeysPath` | Required persistent key path in production |
| `AllowedHosts` | Public hostname |
| `ReverseProxy:KnownProxies` | Explicit trusted proxy addresses |
| `RateLimiting:AccessAttemptsPerTenMinutes` | Gate, sign-in and sign-up submissions allowed per client address every ten minutes (default 78: about 13 people joining together from one hospital or university network, with a few retries each) |
| `Database:ApplyMigrations` | Defaults to development only; false in production Compose |
| `Database:SeedExcel` | Opt-in sample seed, false by default |

Environment variables use double underscores for nested settings, such as `ConnectionStrings__MedDataDb`. Supply a BCrypt hash for the shared gate. SMTP is not required for research signup/sign-in. Ignored `appsettings.Local.json` is excluded from the image.

<details>
<summary>Run locally with the .NET SDK</summary>

Requires the .NET 10 SDK and a running PostgreSQL instance. The commands below use PowerShell; replace the placeholders with your local values.

```powershell
dotnet user-secrets init --project src/MedAnnotateApp.Presentation
dotnet user-secrets set "ConnectionStrings:MedDataDb" "Host=localhost;Port=5432;Database=meddatadb;Username=postgres;Password=<password>" --project src/MedAnnotateApp.Presentation
dotnet user-secrets set "AuthorizationAccessPasswordHash" '<bcrypt-hash>' --project src/MedAnnotateApp.Presentation

dotnet restore MedAnnotateApp.sln
dotnet build MedAnnotateApp.sln
dotnet run --project src/MedAnnotateApp.Presentation
```

Alternatively, set the database connection and shared gate hash in ignored `src/MedAnnotateApp.Presentation/appsettings.Local.json`. Development launch profiles use [https://localhost:7243](https://localhost:7243) or [http://localhost:5210](http://localhost:5210); use the URL printed at startup.

Entry route: `/Identity/AuthorizationAccess`. After authentication, users are routed to `/Home/Professional` or `/Home/Student`.

</details>

<details>
<summary>Manual database migrations</summary>

Development can apply the existing migrations at startup. Production requires a reviewed script and backup. Generate SQL without applying it:

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/MedAnnotateApp.Presentation --startup-project src/MedAnnotateApp.Presentation --output reviewed-migrations.sql
```

</details>

## Testing

- .NET tests: `dotnet test tests/MedAnnotateApp.Tests/MedAnnotateApp.Tests.csproj`.
- JavaScript tests: `node --test tests/js/*.test.cjs` (PowerShell: `node --test (Get-ChildItem tests/js/*.test.cjs).FullName`).
- PostgreSQL concurrency test: set `MEDANNOTATE_TEST_POSTGRES_CONNECTION` to a disposable `medannotate_audit_*` database.

Images need speciality, body-region and modality metadata to be assigned through the required filters. The app targets .NET 10 (LTS).

## License

Licensed under the [Apache License 2.0](LICENSE).
