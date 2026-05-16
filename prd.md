Berikut adalah dokumen **Product Requirements Document (PRD) Final** yang merangkum seluruh arsitektur, filosofi, dan spesifikasi teknis. Dokumen ini dirancang dengan struktur yang sangat deterministik agar dapat dieksekusi langsung oleh ekosistem *multi-agent AI* maupun tim *engineer* manusia, memastikan sinkronisasi kode yang presisi dengan *overhead* serendah mungkin.

> Versi 2.1 menambal gap dari v2.0: kunci versi dependensi, sikap AOT/MediatR, detail otorisasi & transaksi, strategi invalidasi cache, *cross-cutting concerns*, *canonical slice example*, *Definition of Done* per *slice*, dan *escape hatches* terkontrol.

---

# PRODUCT REQUIREMENTS DOCUMENT (PRD)

**Project Name:** .NET 10 Ultimate Boilerplate (VSA Edition)
**Target Framework:** .NET 10.0 (LTS) & C# 14
**Document Version:** 2.1 (Final, Hardened)
**Target Audience:** AI Agent Orchestrators, Backend Developers, System Architects

## 1. Ringkasan Eksekutif (Executive Summary)

Proyek ini membangun *starter template backend* "batteries-included" berbasis .NET 10 yang berfokus pada kinerja tinggi dan jejak memori yang sangat rendah. *Boilerplate* ini meninggalkan abstraksi tradisional berlapis (*Layered Architecture*) demi mengadopsi **Vertical Slice Architecture (VSA)**. Dilengkapi dengan ketahanan infrastruktur bawaan (*graceful degradation*) dan strategi pengujian integrasi murni, sistem ini dirancang agar siap untuk *deployment cloud-native*, kompilasi *Ahead-of-Time* (AOT) opsional, dan otomasi generasi kode oleh agen AI.

## 2. Prinsip & Filosofi Desain (Core Principles)

* **Locality of Behavior (VSA):** Seluruh siklus hidup sebuah fitur (HTTP *Routing*, Validasi, Logika Bisnis, Akses Data) dienkapsulasi dalam satu direktori mandiri.
* **Zero-Overhead Abstraction:** Menghilangkan *pattern* `IRepository` global. Menggunakan *tools* spesifik untuk tugas spesifik (EF Core murni untuk *Write*, Dapper murni untuk *Read*).
* **Graceful Degradation:** Kegagalan dependensi pihak ketiga (seperti Redis) ditangani secara siluman melalui *resilience pipeline* dan *fallback* ke memori lokal/database, menjamin *uptime* 100% pada logika inti.
* **Deterministic Testing:** Nol toleransi terhadap *mocking* objek *database*. Semua pengujian wajib menggunakan *Testcontainers* untuk memastikan validitas perilaku di lingkungan nyata.
* **Spec-First, Escape Hatches Documented:** Penyimpangan dari aturan harus didokumentasikan inline (komentar `// ESCAPE:`) dengan justifikasi singkat — tidak boleh diam-diam.

## 3. Spesifikasi Teknis & Stack Teknologi

### 3.1 Stack Inti (Versi Terkunci)

| Kategori | Teknologi | Versi (Pinned) | Deskripsi Implementasi |
| --- | --- | --- | --- |
| **Framework Inti** | .NET | `10.0.x` (LTS) | Minimal APIs, target `net10.0`. |
| **Bahasa** | C# | `14` | Fitur preview dimatikan secara default. |
| **Pola Komunikasi** | `MediatR` | `12.4.*` | CQRS dispatcher + *pipeline behaviors*. Lihat §3.2 untuk catatan AOT/lisensi. |
| **Validasi** | `FluentValidation` | `11.*` | Eksekusi via MediatR `ValidationBehavior`. |
| **Akses Data (Command)** | `Microsoft.EntityFrameworkCore` | `10.*` | *Write* (Insert/Update/Delete) + migrasi. Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`. |
| **Akses Data (Query)** | `Dapper` | `2.1.*` | *Read* via `IDbConnectionFactory` (Npgsql). |
| **Database** | PostgreSQL | `16+` | Single relational DB. |
| **Auth** | `Microsoft.AspNetCore.Authentication.JwtBearer` | `10.*` | JWT + custom permission filter (§4.1). |
| **Caching** | `StackExchange.Redis` + `Microsoft.Extensions.Caching.Memory` | terbaru stabil | L1 in-memory, L2 Redis, L3 DB fallback. |
| **Resilience** | `Polly` | `8.*` | `ResiliencePipeline` per dependensi. |
| **Rate Limiting** | `Microsoft.AspNetCore.RateLimiting` | bawaan .NET 10 | Fixed window default; dapat di-override per endpoint. |
| **Testing** | `xUnit` + `Testcontainers.PostgreSql` + `Testcontainers.Redis` | terbaru stabil | *Pure integration testing*. |
| **HTTP Client Test** | `Microsoft.AspNetCore.Mvc.Testing` | `10.*` | `WebApplicationFactory<Program>`. |
| **Observability** | `OpenTelemetry` (Traces/Metrics) + `Serilog` | terbaru stabil | OTLP exporter; structured logs JSON. |
| **Health Checks** | `AspNetCore.HealthChecks.*` | terbaru stabil | `/health/live`, `/health/ready` (§9.5). |

> Semua versi di atas dikunci di `Directory.Packages.props` (Central Package Management). AI agent dilarang menambah package di luar daftar ini tanpa update PRD.

### 3.2 Sikap terhadap AOT & Pilihan MediatR

* **AOT Stance:** *Boilerplate* ini menargetkan **JIT-mode by default** untuk Milestone 1–5. *AOT compatibility* adalah *stretch goal* (Milestone 6, opsional).
* **Implikasi MediatR:** MediatR v12 berbasis refleksi → tidak sepenuhnya AOT-friendly. Untuk profil AOT, agen wajib mengganti dispatcher dengan **source-generator-based mediator** (mis. `Mediator` by Jimmy Bogard fork atau implementasi internal). Keputusan ini tidak diaktifkan default.
* **Lisensi MediatR:** versi yang dipakai (`12.4.*`) masih MIT/Apache. Jika kebijakan lisensi berubah di masa depan, ADR baru wajib dibuat (§13).

## 4. Arsitektur Keamanan & Infrastruktur Ketahanan

### 4.1 Otorisasi Granular (Permission-based)

* **Bukan Role-based.** Validasi akses menggunakan *Permission string* spesifik: `products.create`, `products.read`, dll.
* **Mekanisme:** *Endpoint Filter* kustom `RequirePermissionFilter` yang membaca *claims* JWT (`permissions`).
* **Sintaks wajib di Endpoint:**
  ```csharp
  app.MapPost("/products", CreateProductEndpoint.HandleAsync)
     .RequirePermission("products.create");
  ```
* **Sumber permission:** disimpan di tabel `Permissions` dan dipasangkan ke `Users` via `UserPermissions` (many-to-many). JWT diisi *permission claims* saat *login* (snapshot, bukan live-lookup).
* **Refresh Token:** wajib didukung. JWT lifetime ≤ 15 menit; Refresh token lifetime ≤ 7 hari, disimpan di tabel `RefreshTokens` (hashed) dengan rotasi.

### 4.2 Cache Engine & Resilience

* Seluruh operasi yang membutuhkan Redis wajib menggunakan abstraksi `ICacheService`. Kontrak minimum:
  ```csharp
  public interface ICacheService
  {
      Task<T?> GetOrSetAsync<T>(
          string key,
          Func<CancellationToken, Task<T>> factory,  // DB fallback
          TimeSpan ttl,
          CancellationToken ct = default);

      Task InvalidateAsync(string key, CancellationToken ct = default);
      Task InvalidateByTagAsync(string tag, CancellationToken ct = default);
  }
  ```
* **Resilience Pipeline (Polly v8):** `Timeout(500ms) → Retry(2x exponential) → CircuitBreaker → Fallback(factory)`.
* **L1/L2/L3:** L1 = `IMemoryCache` (per-instance), L2 = Redis (shared), L3 = `factory` delegate (DB). L1 di-bypass jika L2 sehat.

### 4.3 Strategi Invalidasi Cache

* **Tag-based invalidation** wajib dipakai untuk *list queries*. Contoh: `products:list:*` di-invalidate via tag `products`.
* **Setelah Command sukses**, handler *Command* wajib memanggil `InvalidateByTagAsync("{aggregate}")` sebelum `return`. Ini ditegakkan oleh *MediatR pipeline behavior* `CacheInvalidationBehavior` yang membaca atribut `[InvalidatesCache("products")]` di Command.
* **Read-your-writes:** Query handler dilarang membaca cache jika Command pada aggregate yang sama baru saja dieksekusi dalam *request scope* yang sama. Ditangani via `IRequestContext.RecentlyMutatedAggregates`.

### 4.4 Error Format

* Semua eror sistem dan validasi kegagalan (FluentValidation) dikembalikan dalam format standar **RFC 7807 ProblemDetails**.
* `Common/Responses/ApiResponse<T>` **hanya** membungkus *success path*. Error path selalu ProblemDetails — tidak ada *double-wrapping*.

## 5. Peta Direktori Vertikal (Directory Map)

Struktur ini wajib dipatuhi secara ketat untuk menjaga kohesi fitur.

```text
📦 <repo-root>
 ┣ 📜 docker-compose.yml         # Local orchestration (Postgres, Redis, OTel collector)
 ┣ 📜 Directory.Packages.props   # Central Package Management (versi terkunci)
 ┣ 📜 Directory.Build.props      # Compiler settings global
 ┣ 📜 global.json                # SDK pin (10.0.x)
 ┣ 📂 src
 ┃ ┗ 📂 Api
 ┃   ┣ 📂 Common                 # Shared kernels
 ┃   ┃ ┣ 📂 Exceptions           # Global Exception Handler (RFC 7807)
 ┃   ┃ ┣ 📂 Responses            # ApiResponse<T> (success only)
 ┃   ┃ ┣ 📂 Behaviors            # MediatR: Validation, Logging, CacheInvalidation
 ┃   ┃ ┣ 📂 Endpoints            # IEndpoint marker + auto-registration
 ┃   ┃ ┗ 📂 Context              # IRequestContext (user, tenant, recentlyMutated)
 ┃   ┣ 📂 Infrastructure         # System configuration & engines
 ┃   ┃ ┣ 📂 Caching              # ICacheService, RedisCacheService, ResiliencePipelineFactory
 ┃   ┃ ┣ 📂 Data                 # AppDbContext, IDbConnectionFactory, Migrations/
 ┃   ┃ ┣ 📂 Security             # JWT setup, RequirePermissionFilter, RefreshToken store
 ┃   ┃ ┣ 📂 Observability        # OTel + Serilog wiring
 ┃   ┃ ┗ 📂 RateLimiting         # Default policies
 ┃   ┣ 📂 Features               # Business Domains (The Slices)
 ┃   ┃ ┗ 📂 Products
 ┃   ┃   ┣ 📂 CreateProduct      # 1 Slice = 1 Feature (lihat §7)
 ┃   ┃   ┃ ┣ 📜 Endpoint.cs
 ┃   ┃   ┃ ┣ 📜 Command.cs
 ┃   ┃   ┃ ┣ 📜 Validator.cs
 ┃   ┃   ┃ ┗ 📜 Handler.cs
 ┃   ┃   ┗ 📂 GetProductById
 ┃   ┃     ┣ 📜 Endpoint.cs
 ┃   ┃     ┣ 📜 Query.cs
 ┃   ┃     ┗ 📜 Handler.cs
 ┃   ┣ 📜 Program.cs             # Bootstrapper (Clean DI)
 ┃   ┗ 📜 appsettings.json
 ┗ 📂 tests
   ┗ 📂 Api.IntegrationTests
     ┣ 📂 Infrastructure         # IntegrationTestWebAppFactory, BaseIntegrationTest
     ┗ 📂 Features               # 1:1 mirror dari src/Api/Features
       ┗ 📂 Products
         ┣ 📂 CreateProduct
         ┃ ┗ 📜 CreateProductTests.cs
         ┗ 📂 GetProductById
           ┗ 📜 GetProductByIdTests.cs
```

## 6. Aturan Orkestrasi Agen AI (System Directives)

Bagian ini berisi batasan ( *constraints*) teknis untuk agen AI yang akan menulis dan mengembangkan basis kode ini.

1. **VSA Strict Compliance:** Dilarang membuat direktori `Controllers`, `Services`, atau `Repositories`. Semua logika fitur harus terisolasi di dalam `src/Api/Features/{Domain}/{FeatureName}/`.
2. **No MVC Controllers:** Wajib menggunakan Minimal API (`app.MapPost`, `app.MapGet`) yang diregistrasikan melalui method statis dengan signature **eksak**:
   ```csharp
   public static void MapEndpoint(IEndpointRouteBuilder app);
   ```
   Auto-discovery dilakukan via `IEndpoint` marker interface (lihat §7.1).
3. **Strict Data Access Split:** Agen AI **default**: EF Core untuk `IRequestHandler<TCommand>`, Dapper untuk `IRequestHandler<TQuery, TResult>`. *Escape hatch* (§10) diperbolehkan dengan komentar `// ESCAPE: <alasan>`.
4. **No Testing Mocks:** Dilarang menggunakan `Moq` atau `NSubstitute` untuk dependensi internal (DbContext, ICacheService, handler). Mocking diperbolehkan **hanya** untuk *external HTTP services* via `WireMock.Net`.
5. **Fail-Safe Injection:** Agen dilarang menulis *try-catch* Redis di level Handler. Wajib menginjeksi `ICacheService` dan melempar fungsi *database fallback* sebagai parameter delegasi (§4.2).
6. **No Manual Wrapping:** Endpoint wajib `return TypedResults.*`. Wrapping `ApiResponse<T>` dilakukan oleh *result filter* global, bukan oleh handler.
7. **One Slice, One PR:** Setiap *slice* adalah unit perubahan atomik. Agen AI tidak boleh memodifikasi >1 slice per PR kecuali untuk *cross-cutting refactor* yang disetujui via ADR.
8. **Definition of Done Wajib (§8).** Slice tidak dianggap selesai sampai semua butir DoD tercentang.

## 7. Canonical Slice Example: `CreateProduct`

Bagian ini adalah **template sumber-kebenaran**. AI agent menggunakan ini sebagai pola *copy-and-modify* untuk semua *slice* baru.

### 7.1 `Common/Endpoints/IEndpoint.cs`

```csharp
namespace Api.Common.Endpoints;

public interface IEndpoint
{
    static abstract void MapEndpoint(IEndpointRouteBuilder app);
}
```

Auto-registrasi di `Program.cs`:

```csharp
var endpointTypes = typeof(Program).Assembly
    .GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(IEndpoint).IsAssignableFrom(t));

foreach (var t in endpointTypes)
{
    var map = t.GetMethod(nameof(IEndpoint.MapEndpoint),
        BindingFlags.Public | BindingFlags.Static);
    map?.Invoke(null, [app]);
}
```

> Catatan AOT: jika AOT diaktifkan (Milestone 6), refleksi di atas diganti dengan source generator yang men-emit registrasi statis.

### 7.2 `Features/Products/CreateProduct/Command.cs`

```csharp
namespace Api.Features.Products.CreateProduct;

[InvalidatesCache("products")]
public sealed record CreateProductCommand(
    string Name,
    string Sku,
    decimal Price) : IRequest<Guid>;
```

### 7.3 `Features/Products/CreateProduct/Validator.cs`

```csharp
namespace Api.Features.Products.CreateProduct;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sku).NotEmpty().Matches("^[A-Z0-9-]{3,32}$");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}
```

### 7.4 `Features/Products/CreateProduct/Handler.cs`

```csharp
namespace Api.Features.Products.CreateProduct;

internal sealed class CreateProductHandler(AppDbContext db)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Name = cmd.Name,
            Sku = cmd.Sku,
            Price = cmd.Price,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }
}
```

### 7.5 `Features/Products/CreateProduct/Endpoint.cs`

```csharp
namespace Api.Features.Products.CreateProduct;

public sealed class CreateProductEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products", HandleAsync)
           .RequirePermission("products.create")
           .RequireIdempotency()                 // §9.2
           .WithName("CreateProduct")
           .WithTags("Products")
           .Produces<Guid>(StatusCodes.Status201Created)
           .ProducesValidationProblem();
    }

    private static async Task<Created<Guid>> HandleAsync(
        CreateProductCommand cmd,
        ISender sender,
        CancellationToken ct)
    {
        var id = await sender.Send(cmd, ct);
        return TypedResults.Created($"/api/v1/products/{id}", id);
    }
}
```

### 7.6 `tests/.../CreateProductTests.cs`

```csharp
public sealed class CreateProductTests(IntegrationTestWebAppFactory f)
    : BaseIntegrationTest(f)
{
    [Fact]
    public async Task Should_Create_Product_When_Input_Is_Valid()
    {
        var cmd = new CreateProductCommand("Coffee", "SKU-001", 25_000m);
        var id = await Sender.Send(cmd);

        var saved = await DbContext.Products.FindAsync(id);
        saved.ShouldNotBeNull();
        saved!.Sku.ShouldBe("SKU-001");
    }

    [Fact]
    public async Task Should_Fail_Validation_When_Sku_Is_Invalid()
    {
        var cmd = new CreateProductCommand("Coffee", "bad sku!", 1m);
        await Should.ThrowAsync<ValidationException>(() => Sender.Send(cmd));
    }
}
```

## 8. Definition of Done (DoD) per Slice

Sebuah *slice* dianggap **selesai** hanya jika seluruh butir berikut terpenuhi. Checklist ini wajib dipasang di deskripsi PR.

- [ ] Direktori `Features/{Domain}/{FeatureName}/` dibuat sesuai §5.
- [ ] `Endpoint.cs` mengimplementasi `IEndpoint` dengan `MapEndpoint` statis.
- [ ] Routing menyertakan `.RequirePermission("...")` atau `.AllowAnonymous()` (eksplisit).
- [ ] `Command.cs` / `Query.cs` adalah `record` immutable; Command memiliki `[InvalidatesCache]` jika me-mutasi state yang ter-cache.
- [ ] `Validator.cs` ada untuk setiap input non-trivial (Command **wajib**, Query **bila ada parameter eksternal**).
- [ ] `Handler.cs` mengikuti aturan EF/Dapper split (§6 directive #3) atau memiliki komentar `// ESCAPE:`.
- [ ] Min. 2 *integration tests*: 1 happy path + 1 negative path (validasi/permission).
- [ ] Permission baru (jika ada) ditambahkan ke seed migration `Permissions`.
- [ ] OpenAPI metadata: `.WithName`, `.WithTags`, `.Produces<>`, `.ProducesValidationProblem()`.
- [ ] Logging terstruktur: handler memakai `ILogger<T>` dengan `LogInformation` minimal pada *entry* dan *outcome*.
- [ ] Tidak ada package baru di luar §3.1 tanpa ADR.
- [ ] `dotnet build` & `dotnet test` hijau di CI.

## 9. Cross-Cutting Concerns

### 9.1 API Versioning
* Skema **URL-segment**: `/api/v{n}/...`. Default `v1`.
* Tidak menggunakan header versioning untuk menyederhanakan caching & dokumentasi OpenAPI.

### 9.2 Idempotency (Command)
* Endpoint mutasi wajib mendukung header `Idempotency-Key` via filter `RequireIdempotency()`.
* Penyimpanan: tabel `IdempotencyKeys (Key, RequestHash, Response, ExpiresAt)`. TTL 24 jam.
* Konflik (key sama, hash berbeda) → `409 Conflict`.

### 9.3 Pagination, Sorting, Filtering (Query)
* Query list wajib menerima `?page=1&pageSize=20&sort=name:asc&filter=...`.
* `pageSize` di-clamp ke `[1, 100]`.
* Response: `PagedResult<T> { Items, Page, PageSize, TotalCount }`.

### 9.4 Rate Limiting
* Default global: 100 req/menit per IP (fixed window).
* Endpoint sensitif (login, refresh token): 10 req/menit per IP.
* Override per-endpoint via `.RequireRateLimiting("policy-name")`.

### 9.5 Health Checks
* `/health/live` → liveness (proses hidup; tanpa cek dependensi).
* `/health/ready` → readiness (cek Postgres & Redis; jika Redis mati, status `Degraded` bukan `Unhealthy` — sesuai filosofi *graceful degradation*).
* `/health/startup` → opsional untuk migrasi (§9.7).

### 9.6 Audit & Soft Delete
* Entity dasar `AuditableEntity { CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy }`.
* Entity yang mendukung soft-delete mewarisi `SoftDeletableEntity { IsDeleted, DeletedAtUtc, DeletedBy }`.
* `AppDbContext` menerapkan global query filter `!IsDeleted` dan auto-stamp pada `SaveChangesAsync` melalui `IRequestContext`.

### 9.7 Migrasi Skema
* **Tidak otomatis** saat startup di environment `Production`.
* Disediakan endpoint admin internal `/admin/migrate` (terbatas permission `system.migrate`) **atau** *job* terpisah `Api.Migrator` yang dipanggil di pipeline deploy.
* Di `Development` & `Testing` (Testcontainers): migrasi dijalankan otomatis pada startup.

### 9.8 Secret Management
* `Development`: .NET User Secrets.
* `Staging`/`Production`: variabel lingkungan + provider eksternal (AWS Secrets Manager / Azure Key Vault / Vault) via `IConfiguration` provider.
* Dilarang men-*commit* string koneksi/private key. Pre-commit hook `gitleaks` direkomendasikan.

### 9.9 Multi-Tenancy
* **v2.1 ini single-tenant.** Hooks (`IRequestContext.TenantId`) sudah disiapkan tetapi tidak diaktifkan. Multi-tenant adalah scope v3.x dan memerlukan ADR baru.

### 9.10 Transaksi & Konsistensi Read-Write
* Default: setiap Command berjalan dalam transaksi EF implisit (`SaveChangesAsync`).
* Jika handler memerlukan transaksi eksplisit lintas operasi, gunakan `await using var tx = await db.Database.BeginTransactionAsync(ct);`.
* `IDbConnectionFactory` (Dapper) **tidak** berbagi koneksi dengan `AppDbContext`. Untuk skenario *read-after-write* yang kritis, query handler menerima sinyal dari `IRequestContext.RecentlyMutatedAggregates` dan **bypass cache** (§4.3).

## 10. Escape Hatches Terkontrol

Penyimpangan dari directives diperbolehkan **hanya** dengan ketiga syarat:

1. Komentar `// ESCAPE: <alasan singkat, 1 baris>` tepat di atas baris yang menyimpang.
2. Justifikasi diulang di deskripsi PR.
3. Tidak menyentuh aturan keamanan (§4.1) atau testing (§6 directive #4).

Contoh sah:
```csharp
// ESCAPE: optimistic concurrency check butuh raw SQL, EF tracking tidak cukup
var rows = await connection.ExecuteAsync(sql, p);
```

## 11. Rencana Pelaksanaan (Milestones)

* **Milestone 1: Foundation & Bootstrapping**
  * Inisialisasi `Program.cs` Minimal APIs.
  * `Directory.Packages.props`, `global.json`, `Directory.Build.props`.
  * Global Exception Handler (RFC 7807) & `ApiResponse<T>` (success-only).
  * `IEndpoint` + auto-registrasi.

* **Milestone 2: CQRS & Data Layer**
  * MediatR + `ValidationBehavior` + `LoggingBehavior`.
  * EF Core 10 + migrasi awal (Users, Permissions, RefreshTokens, IdempotencyKeys).
  * `IDbConnectionFactory` (Dapper).
  * Canonical slice `CreateProduct` + `GetProductById` (referensi §7).

* **Milestone 3: Resilience & Security**
  * `ICacheService` + Polly v8 pipeline + tag invalidation + `CacheInvalidationBehavior`.
  * JWT + Refresh Token rotation + `RequirePermissionFilter`.
  * Rate limiting policies default.

* **Milestone 4: Pure Integration Testing**
  * `IntegrationTestWebAppFactory` (Testcontainers PostgreSQL + Redis).
  * `BaseIntegrationTest` + helper untuk seed permission/user.
  * CI: `dotnet test` di GitHub Actions / Azure Pipelines.

* **Milestone 5: Observability & DevOps**
  * OpenTelemetry (OTLP) + Serilog JSON.
  * Health checks (live/ready/startup).
  * `Dockerfile` multi-stage (Alpine atau Chiseled Ubuntu).
  * `docker-compose.yml` (Postgres, Redis, OTel collector).

* **Milestone 6 (Stretch): AOT Profile**
  * Source-generator-based mediator pengganti MediatR.
  * Source-generated endpoint registration (gantikan refleksi §7.1).
  * `<PublishAot>true</PublishAot>` di profil `Release-AOT`.

## 12. Non-Goals (Eksplisit Tidak Termasuk)

Untuk mencegah *scope creep*, hal-hal berikut **bukan** bagian dari boilerplate v2.1:

* GraphQL / gRPC (REST only).
* Frontend / UI / BFF.
* Multi-tenancy aktif (lihat §9.9).
* Event sourcing / Domain events publishing keluar proses (in-process MediatR notifications boleh).
* Background job scheduler (Hangfire/Quartz) — bisa ditambahkan sebagai *opt-in* via ADR.
* SignalR / WebSocket.
* Distributed transactions / Saga.

## 13. Architecture Decision Records (ADR)

Setiap deviasi material dari PRD dicatat sebagai ADR di `docs/adr/NNNN-title.md` dengan template:

```text
# NNNN. <Judul Keputusan>
Status: Proposed | Accepted | Superseded by NNNN
Date: YYYY-MM-DD
Context: <kondisi & masalah>
Decision: <apa yang diputuskan>
Consequences: <trade-off positif & negatif>
```

ADR yang men-*supersede* bagian PRD wajib disertai bump versi PRD (mis. `v2.2`).

## 14. Glosarium Singkat

| Istilah | Definisi |
| --- | --- |
| **Slice** | Direktori `Features/{Domain}/{FeatureName}/` berisi Endpoint+Command/Query+Validator+Handler. |
| **Aggregate** | Akar entitas konsisten (mis. `Product`). Cache tag biasanya = nama aggregate plural lowercase. |
| **L1/L2/L3** | MemoryCache / Redis / DB-fallback. |
| **DoD** | Definition of Done (§8). |
| **Escape Hatch** | Penyimpangan terdokumentasi dari directive (§10). |
| **ADR** | Architecture Decision Record (§13). |
