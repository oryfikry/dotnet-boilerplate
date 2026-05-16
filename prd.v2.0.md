Berikut adalah dokumen **Product Requirements Document (PRD) Final** yang merangkum seluruh arsitektur, filosofi, dan spesifikasi teknis yang telah kita diskusikan. Dokumen ini dirancang dengan struktur yang sangat deterministik, menjadikannya spesifikasi teknis yang sempurna untuk dieksekusi langsung oleh ekosistem *multi-agent AI* maupun tim *engineer* manusia, memastikan sinkronisasi kode yang presisi dengan *overhead* serendah mungkin.

---

# PRODUCT REQUIREMENTS DOCUMENT (PRD)

**Project Name:** .NET 10 Ultimate Boilerplate (VSA Edition)
**Target Framework:** .NET 10.0 (LTS) & C# 14
**Document Version:** 2.0 (Final)
**Target Audience:** AI Agent Orchestrators, Backend Developers, System Architects

## 1. Ringkasan Eksekutif (Executive Summary)

Proyek ini membangun *starter template backend* "batteries-included" berbasis .NET 10 yang berfokus pada kinerja tinggi dan jejak memori yang sangat rendah. *Boilerplate* ini meninggalkan abstraksi tradisional berlapis (*Layered Architecture*) demi mengadopsi **Vertical Slice Architecture (VSA)**. Dilengkapi dengan ketahanan infrastruktur bawaan (*graceful degradation*) dan strategi pengujian integrasi murni, sistem ini dirancang agar siap untuk *deployment cloud-native*, kompilasi *Ahead-of-Time* (AOT), dan otomasi generasi kode oleh agen AI.

## 2. Prinsip & Filosofi Desain (Core Principles)

* **Locality of Behavior (VSA):** Seluruh siklus hidup sebuah fitur (HTTP *Routing*, Validasi, Logika Bisnis, Akses Data) dienkapsulasi dalam satu direktori mandiri.
* **Zero-Overhead Abstraction:** Menghilangkan *pattern* `IRepository` global. Menggunakan *tools* spesifik untuk tugas spesifik (EF Core murni untuk *Write*, Dapper murni untuk *Read*).
* **Graceful Degradation:** Kegagalan dependensi pihak ketiga (seperti Redis) ditangani secara siluman melalui *resilience pipeline* dan *fallback* ke memori lokal/database, menjamin *uptime* 100% pada logika inti.
* **Deterministic Testing:** Nol toleransi terhadap *mocking* objek *database*. Semua pengujian wajib menggunakan *Testcontainers* untuk memastikan validitas perilaku di lingkungan nyata.

## 3. Spesifikasi Teknis & Stack Teknologi

| Kategori | Teknologi Utama | Deskripsi Implementasi |
| --- | --- | --- |
| **Framework Inti** | .NET 10 Minimal APIs | API yang ramping tanpa `ControllerBase`. |
| **Pola Komunikasi** | MediatR | Pemisahan CQRS dan *pipeline pre-processor* (Validasi). |
| **Akses Data (Command)** | Entity Framework Core 10 | Operasi *Write* (Insert/Update/Delete) dan migrasi skema. |
| **Akses Data (Query)** | Dapper | Operasi *Read* super cepat dengan *raw* SQL. |
| **Database Utama** | PostgreSQL 16+ | Relasional DB utama. |
| **Keamanan & Auth** | JWT + Custom Filters | Autentikasi token JWT dengan otorisasi berbasis *Permission* granular. |
| **Caching & Fallback** | Redis + MemoryCache | Lapis pertama Redis, lapis kedua RAM, lapis ketiga DB (via Polly v8). |
| **Testing** | xUnit + Testcontainers | Pengujian integrasi murni (*Pure Integration Testing*). |
| **Observability** | OpenTelemetry & Serilog | *Tracing, Metrics*, dan *Structured Logging*. |

## 4. Arsitektur Keamanan & Infrastruktur Ketahanan

* **Granular RBAC:** Validasi akses tidak menggunakan *Role* statis, melainkan *Permission* spesifik (contoh: `.RequirePermission("products.create")`).
* **ICacheService Engine:** Seluruh operasi yang membutuhkan Redis wajib menggunakan abstraksi `ICacheService`. *Service* ini otomatis mengeksekusi `ResiliencePipeline` (Polly) yang mencegat *timeout* atau *disconnect* Redis, dan secara otomatis mengeksekusi fungsi *fallback* ke *database*.
* **Problem Details:** Semua eror sistem dan validasi kegagalan (FluentValidation) dikembalikan dalam format standar **RFC 7807**.

## 5. Peta Direktori Vertikal (Directory Map)

Struktur ini wajib dipatuhi secara ketat untuk menjaga kohesi fitur.

```text
📦 src/Api
 ┣ 📂 Common                 # Shared kernels 
 ┃ ┣ 📂 Exceptions           # Global Exception Handler
 ┃ ┣ 📂 Responses            # JSON Response Wrappers
 ┃ ┗ 📂 Behaviors            # MediatR Validation Pipeline
 ┣ 📂 Infrastructure         # System configuration & engines
 ┃ ┣ 📂 Caching              # ICacheService & ResiliencePipeline
 ┃ ┣ 📂 Data                 # AppDbContext & IDbConnectionFactory
 ┃ ┗ 📂 Security             # JWT Setup & PermissionFilter
 ┣ 📂 Features               # Business Domains (The Slices)
 ┃ ┗ 📂 Products
 ┃   ┣ 📂 CreateProduct      # 1 Slice = 1 Feature
 ┃   ┃ ┣ 📜 Endpoint.cs
 ┃   ┃ ┣ 📜 Command.cs
 ┃   ┃ ┣ 📜 Validator.cs
 ┃   ┃ ┗ 📜 Handler.cs
 ┣ 📜 Program.cs             # Bootstrapper (Clean DI)
 ┗ 📜 docker-compose.yml     # Local orchestration (DB, Redis)

📦 tests/Api.IntegrationTests
 ┣ 📂 Infrastructure         # BaseIntegrationTest & Testcontainers Setup
 ┗ 📂 Features               # 1:1 Mirroring of src/Api/Features
   ┗ 📂 Products
     ┗ 📂 CreateProduct
       ┗ 📜 CreateProductTests.cs

```

## 6. Aturan Orkestrasi Agen AI (System Directives)

Bagian ini berisi batasan ( *constraints*) teknis untuk agen AI yang akan menulis dan mengembangkan basis kode ini.

1. **VSA Strict Compliance:** Dilarang membuat direktori `Controllers`, `Services`, atau `Repositories`. Semua logika fitur harus terisolasi di dalam `src/Api/Features/{Domain}/{FeatureName}/`.
2. **No MVC Controllers:** Wajib menggunakan Minimal API (`app.MapPost`, `app.MapGet`) yang diregistrasikan melalui *static method* pada kelas `Endpoint` di dalam *slice* fitur.
3. **Strict Data Access Split:** Agen AI **hanya** boleh menggunakan EF Core untuk `IRequestHandler<Command>`. Agen AI **wajib** menggunakan Dapper untuk `IRequestHandler<Query>`.
4. **No Testing Mocks:** Dilarang menggunakan pustaka `Moq` atau `NSubstitute`. Semua *test code* harus merupakan keturunan dari `BaseIntegrationTest` dan mengeksekusi `ISender.Send()` langsung terhadap *Testcontainers*.
5. **Fail-Safe Injection:** Agen dilarang menulis *try-catch* Redis di level *Handler*. Wajib menginjeksi `ICacheService` dan melemparkan fungsi *database fallback* sebagai parameter delegasi.

## 7. Rencana Pelaksanaan (Milestones)

* **Milestone 1: Foundation & Bootstrapping**
* Inisialisasi `Program.cs` Minimal APIs.
* Pembuatan Global Exception (RFC 7807) dan Standar JSON Response.
* Setup kerangka dasar untuk registrasi *Endpoint* berbasis VSA.


* **Milestone 2: CQRS & Data Layer**
* Integrasi MediatR dan *Validation Pipeline* (FluentValidation).
* Konfigurasi EF Core 10 dan migrasi skema awal (Users, Permissions).
* Konfigurasi Dapper Connection Factory.


* **Milestone 3: Resilience & Security**
* Setup Redis dan implementasi `ICacheService` dengan *Resilience Pipeline*.
* Setup autentikasi JWT dan pembuatan filter kustom `.RequirePermission()`.


* **Milestone 4: Pure Integration Testing**
* Setup `IntegrationTestWebAppFactory` dengan *Testcontainers* PostgreSQL.
* Pembuatan *Base Class* untuk *testing* otomatisasi CI/CD.


* **Milestone 5: Observability & DevOps**
* Integrasi Native OpenTelemetry dan Serilog.
* Pembuatan `Dockerfile` multi-stage (Alpine) dan konfigurasi *Health Checks*.