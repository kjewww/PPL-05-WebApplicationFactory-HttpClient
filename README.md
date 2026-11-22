# Tutorial: API & Service Integration Testing dengan WebApplicationFactory + HttpClient

Tutorial ini menunjukkan cara menguji API ASP.NET Core 10 secara end-to-end tanpa menjalankan server di port nyata. Kita memakai **WebApplicationFactory&lt;Program&gt;** untuk menyalakan aplikasi di dalam *in-memory TestServer*, lalu **HttpClient** untuk mengirim request seperti client asli.

Contoh aplikasinya adalah API **Todo** dengan 5 endpoint. Fokus pengujiannya ada lima hal:

1. **Routing** — apakah URL dan method HTTP sudah petakan dengan benar.
2. **Validation** — apakah payload salah menghasilkan 400 yang informatif.
3. **Status codes** — 200 / 201 / 204 / 400 / 404 / 405.
4. **Response body** — `Content-Type` dan isi JSON yang dikembalikan.
5. **Persistence** — apakah data benar-benar tersimpan di database.

Referensi utama: [Integration tests in ASP.NET Core (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests).

---

## 1. Pengenalan singkat

| Konsep | Penjelasan |
|---|---|
| **Unit test** | Menguji satu method/class, dependency di-mock. Cepat, tapi tidak membuktikan pipeline HTTP bekerja. |
| **Integration test** | Menguji beberapa komponen sekaligus: routing, model binding, DI, middleware, service, dan database. |
| **SUT** | *System Under Test*, yaitu aplikasi `TodoApi` yang kita uji. |
| **WebApplicationFactory&lt;TEntryPoint&gt;** | Kelas dari `Microsoft.AspNetCore.Mvc.Testing` yang membuat `TestServer` dari `Program.cs`. `TEntryPoint`-nya biasanya `Program`. |
| **HttpClient** | Client yang dikirimi request ke `TestServer`. Tidak butuh `http://localhost:5000`. |

Alur setiap test:

```text
Arrange  -> siapkan factory, ganti DB dengan SQLite test, seed data, buat HttpClient
Act      -> Client.GetAsync / PostAsJsonAsync / DeleteAsync
Assert   -> cek StatusCode, Header, Body JSON, dan isi database
```

Aplikasi dan test berada di **proyek terpisah**. Test project mereferensi SUT.

---

## 2. Persiapan lingkungan

Yang dibutuhkan:

- **.NET 10 SDK** (tutorial ini memakai `net10.0`). Cek dengan:

  ```bash
  dotnet --version
  ```

  Contoh hasil: `10.0.401`.

- Editor/IDE: Visual Studio 2022 (17.10+), VS Code + C# Dev Kit, atau Rider.
- Pemahaman dasar **NUnit** (`[Test]`, `[TestCase]`, `[SetUp]`, `Assert.That`).
- Tidak perlu menginstal SQL Server/MySQL. Kita memakai **SQLite in-memory**, yang merupakan pilihan yang direkomendasikan Microsoft untuk test EF Core dibandingkan EF Core InMemory provider, karena perilakunya lebih dekat ke database relasional.

---

## 3. Instalasi / membuat proyek dari awal

Jika kamu ingin membuat ulang dari folder kosong, jalankan:

```bash
dotnet new sln -n PPL05
dotnet new web -n TodoApi -o TodoApi -f net10.0
dotnet new nunit -n TodoApi.Tests -o TodoApi.Tests -f net10.0

dotnet sln PPL05.slnx add TodoApi/TodoApi.csproj TodoApi.Tests/TodoApi.Tests.csproj
dotnet add TodoApi.Tests/TodoApi.Tests.csproj reference TodoApi/TodoApi.csproj

dotnet add TodoApi/TodoApi.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.12
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.12
```

> Catatan: SDK .NET 10 membuat file solusi berekstensi **`.slnx`**. Pada SDK lama bisa jadi `.sln`; sesuaikan nama file pada perintah `dotnet sln`.

Ada **dua penyesuaian wajib** setelah template dibuat:

### 3.1. Test project harus memakai Web SDK

Ubah baris pertama `TodoApi.Tests/TodoApi.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
```

Ini membuat `.deps` aplikasi disalin ke folder test dan content root diarahkan ke proyek SUT.

### 3.2. `Program` harus terlihat oleh test project

Tambahkan di akhir `TodoApi/Program.cs`:

```csharp
public partial class Program { }
```

Alternatif lain: tambahkan `<InternalsVisibleTo Include="TodoApi.Tests" />` di `TodoApi.csproj`.

### 3.3. File proyek akhir

`TodoApi/TodoApi.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.12" />
  </ItemGroup>

</Project>
```

`TodoApi.Tests/TodoApi.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit.Analyzers" Version="4.7.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="NUnit.Framework" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TodoApi\TodoApi.csproj" />
  </ItemGroup>

</Project>
```

Karena ada `<Using Include="NUnit.Framework" />`, test tidak perlu menulis `using NUnit.Framework;` di setiap file.

### 3.4. Struktur folder yang dihasilkan

```text
TodoApi/
  Models/Todo.cs
  Data/TodoDbContext.cs
  Endpoints/TodoEndpoints.cs
  Program.cs
  TodoApi.csproj

TodoApi.Tests/
  Infrastructure/CustomWebApplicationFactory.cs
  Infrastructure/ApiTestBase.cs
  RoutingTests.cs
  ValidationTests.cs
  StatusCodeTests.cs
  ResponseBodyTests.cs
  PersistenceTests.cs
  TodoApi.Tests.csproj

PPL05.slnx
```

---

## 4. Langkah implementasi pengujian

### Langkah 1 — Model dan DbContext

`TodoApi/Models/Todo.cs`

```csharp
namespace TodoApi.Models;

public class Todo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
```

`TodoApi/Data/TodoDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
}
```

Tidak ada repository layer atau service layer tambahan. Ini sengaja supaya demo tetap fokus ke pipeline HTTP.

---

### Langkah 2 — Lima endpoint aplikasi

`TodoApi/Endpoints/TodoEndpoints.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Endpoints;

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/todos", async (TodoDbContext db) =>
            await db.Todos.OrderBy(t => t.Id).ToListAsync());

        endpoints.MapGet("/todos/{id:int}", async (TodoDbContext db, int id) =>
            await db.Todos.FindAsync(id) is { } todo
                ? Results.Ok(todo)
                : Results.NotFound());

        endpoints.MapPost("/todos", async (TodoDbContext db, Todo input) =>
        {
            if (HasInvalidTitle(input))
            {
                return InvalidTitle();
            }

            var todo = new Todo { Title = input.Title.Trim(), IsDone = input.IsDone };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.Created($"/todos/{todo.Id}", todo);
        });

        endpoints.MapPut("/todos/{id:int}", async (TodoDbContext db, int id, Todo input) =>
        {
            if (HasInvalidTitle(input))
            {
                return InvalidTitle();
            }

            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            todo.Title = input.Title.Trim();
            todo.IsDone = input.IsDone;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        endpoints.MapDelete("/todos/{id:int}", async (TodoDbContext db, int id) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool HasInvalidTitle(Todo input) =>
        string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length < 3;

    private static IResult InvalidTitle() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["Title wajib diisi minimal 3 karakter."]
        });
}
```

Ringkasan endpoint:

| Method | Route | Ekspektasi sukses | Ekspektasi gagal |
|---|---|---|---|
| GET | `/todos` | 200 + array JSON | – |
| GET | `/todos/{id:int}` | 200 | 404 |
| POST | `/todos` | 201 + header `Location` | 400 |
| PUT | `/todos/{id:int}` | 204 | 400 / 404 |
| DELETE | `/todos/{id:int}` | 204 | 404 |

`Results.ValidationProblem(...)` menghasilkan status 400 dengan `Content-Type: application/problem+json` dan body bertipe `ValidationProblemDetails`.

---

### Langkah 3 — Program.cs

`TodoApi/Program.cs`

```csharp
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

// SQLite in-memory dipakai juga oleh aplikasi normal.
// Koneksi disimpan sebagai singleton supaya tidak ditutup oleh EF.
builder.Services.AddSingleton<DbConnection>(_ =>
{
    var connection = new SqliteConnection("DataSource=:memory:");
    connection.Open();
    return connection;
});
builder.Services.AddDbContext<TodoDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<DbConnection>()));

var app = builder.Build();

// Seed hanya saat dijalankan normal. Saat di-test, factory yang melakukan seed.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();
    if (!db.Todos.Any())
    {
        db.Todos.Add(new Todo { Title = "Contoh todo awal" });
        db.SaveChanges();
    }
}

app.MapTodoEndpoints();

app.Run();

// Dibutuhkan agar WebApplicationFactory<Program> bisa dipakai dari test project.
public partial class Program { }
```

Poin penting:

- `SqliteConnection("DataSource=:memory:")` harus dipertahankan terbuka. Itulah sebabnya didaftarkan sebagai singleton.
- Environment `Testing` akan diset oleh factory, sehingga seed di `Program.cs` dilewati saat test.

---

### Langkah 4 — Custom WebApplicationFactory (intinya)

`TodoApi.Tests/Infrastructure/CustomWebApplicationFactory.cs`

```csharp
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests.Infrastructure;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 1. Hapus pendaftaran DB milik aplikasi (callback ini jalan setelah Program.cs).
            var dbOptions = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDbContextOptionsConfiguration<TodoDbContext>));
            if (dbOptions is not null)
            {
                services.Remove(dbOptions);
            }

            var connection = services.SingleOrDefault(d => d.ServiceType == typeof(DbConnection));
            if (connection is not null)
            {
                services.Remove(connection);
            }

            // 2. SQLite in-memory: koneksi harus dibuka dan disimpan sebagai singleton,
            //    kalau tidak EF akan menutupnya dan data hilang.
            services.AddSingleton<DbConnection>(_ =>
            {
                var sqlite = new SqliteConnection("DataSource=:memory:");
                sqlite.Open();
                return sqlite;
            });

            services.AddDbContext<TodoDbContext>((sp, options) =>
                options.UseSqlite(sp.GetRequiredService<DbConnection>()));
        });
    }

    public void SeedDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        db.Database.EnsureCreated();

        if (!db.Todos.Any())
        {
            db.Todos.Add(new Todo { Id = 1, Title = "Todo awal", IsDone = false });
            db.SaveChanges();
        }
    }
}
```

Yang terjadi di sini:

1. `ConfigureWebHost` dipanggil oleh `WebApplicationFactory` sebelum host SUT dibangun.
2. Callback `ConfigureServices` berjalan **setelah** `Program.cs`, jadi pendaftaran DB aplikasi harus dihapus dulu.
3. Dua descriptor dihapus: konfigurasi `DbContext` **dan** `DbConnection`.
4. Koneksi SQLite test didaftarkan ulang sebagai singleton yang sudah `Open()`.
5. `SeedDatabase()` dipanggil dari test sebelum request pertama.

---

### Langkah 5 — Base test: membuat HttpClient

`TodoApi.Tests/Infrastructure/ApiTestBase.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace TodoApi.Tests.Infrastructure;

public abstract class ApiTestBase
{
    protected CustomWebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public void SetUp()
    {
        // Factory baru tiap test => database SQLite baru => isolasi antar test.
        Factory = new CustomWebApplicationFactory<Program>();
        Factory.SeedDatabase();

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [TearDown]
    public void TearDown()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
```

Kenapa opsi client ini penting:

- `AllowAutoRedirect = false` → kita melihat status asli (misalnya 302/307), bukan hasil redirect yang sudah diikuti.
- `BaseAddress = https://localhost` → menghindari peringatan HTTPS redirection di log.
- Factory dibuat di `[SetUp]`, bukan `[OneTimeSetUp]`, supaya tiap test dapat database bersih.

Semua file test berikutnya tinggal mewarisi `ApiTestBase`.

---

### Langkah 6 — Test Routing

`TodoApi.Tests/RoutingTests.cs`

```csharp
using System.Net;
using System.Text;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class RoutingTests : ApiTestBase
{
    [TestCase("/todos", HttpStatusCode.OK)]
    [TestCase("/todos/1", HttpStatusCode.OK)]
    [TestCase("/todos/999", HttpStatusCode.NotFound)]
    [TestCase("/todos/abc", HttpStatusCode.NotFound)]
    [TestCase("/todo", HttpStatusCode.NotFound)]
    public async Task Routing_ResolvesAsExpected(string url, HttpStatusCode expected)
    {
        var response = await Client.GetAsync(url);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task Post_ToGetOnlyRoute_Returns405()
    {
        var response = await Client.PostAsync("/todos/1",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }
}
```

Yang dibuktikan:

- `/todos` dan `/todos/1` terpetakan.
- `/todos/999` tidak ada datanya → 404.
- `/todos/abc` ditolak constraint `{id:int}` → 404.
- `/todo` salah path → 404.
- POST ke route yang hanya punya GET → 405.

---

### Langkah 7 — Test Validation

`TodoApi.Tests/ValidationTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class ValidationTests : ApiTestBase
{
    [Test]
    public async Task Post_InvalidTitle_Returns400WithFieldErrors()
    {
        var response = await Client.PostAsJsonAsync("/todos",
            new { title = "", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Status, Is.EqualTo(400));
        Assert.That(problem.Errors, Does.ContainKey("title"));
    }
}
```

`PostAsJsonAsync` otomatis mengirim `Content-Type: application/json`. Response-nya bukan string biasa, melainkan `ValidationProblemDetails` yang bisa di-deserialize.

---

### Langkah 8 — Test Status codes dan header

`TodoApi.Tests/StatusCodeTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class StatusCodeTests : ApiTestBase
{
    [Test]
    public async Task Post_ValidTodo_Returns201WithLocation()
    {
        var response = await Client.PostAsJsonAsync("/todos",
            new { title = "Belajar integration test", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(response.Headers.Location, Is.Not.Null);
        Assert.That(response.Headers.Location!.OriginalString, Does.StartWith("/todos/"));
    }

    [Test]
    public async Task Delete_ExistingTodo_Returns204()
    {
        var response = await Client.DeleteAsync("/todos/1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
```

`EnsureSuccessStatusCode()` hanya cocok untuk happy path. Untuk test integrasi, lebih baik asserted status spesifik: 201 bukan sekadar 2xx, 204 bukan sekadar sukses.

---

### Langkah 9 — Test Response body

`TodoApi.Tests/ResponseBodyTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using TodoApi.Models;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class ResponseBodyTests : ApiTestBase
{
    [Test]
    public async Task Get_Todos_ReturnsJsonBodyWithSeededData()
    {
        var response = await Client.GetAsync("/todos");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));

        var todos = await response.Content.ReadFromJsonAsync<List<Todo>>();
        Assert.That(todos, Is.Not.Null);
        Assert.That(todos!, Has.Count.EqualTo(1));
        Assert.That(todos[0].Title, Is.EqualTo("Todo awal"));
    }
}
```

Yang dicek bukan hanya "ada teks Todo awal", tetapi tipe konten dan struktur JSON yang bisa di-bind ke `List<Todo>`.

---

### Langkah 10 — Test Persistence

`TodoApi.Tests/PersistenceTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApi.Data;
using TodoApi.Models;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class PersistenceTests : ApiTestBase
{
    [Test]
    public async Task CreatedTodo_IsPersistedAndReadable()
    {
        var create = await Client.PostAsJsonAsync("/todos",
            new { title = "Tersimpan di database", isDone = false });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await create.Content.ReadFromJsonAsync<Todo>();
        Assert.That(created, Is.Not.Null);

        var get = await Client.GetAsync($"/todos/{created!.Id}");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var fetched = await get.Content.ReadFromJsonAsync<Todo>();
        Assert.That(fetched!.Title, Is.EqualTo("Tersimpan di database"));

        // Verifikasi kedua: baca langsung dari DbContext milik factory.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        Assert.That(await db.Todos.AnyAsync(t => t.Title == "Tersimpan di database"), Is.True);
    }
}
```

Ini adalah inti *service integration testing*: data hasil POST harus bisa dibaca lagi lewat GET **dan** benar-benar ada di tabel SQLite. Jadi test ini membuktikan pipeline HTTP, service, EF Core, dan database bekerja bersama.

---

## 5. Cara menjalankan

Jalankan seluruh test dari root solusi:

```bash
dotnet test PPL05.slnx
```

Jalankan hanya satu fokus (berguna saat presentasi):

```bash
dotnet test PPL05.slnx --filter FullyQualifiedName~RoutingTests
dotnet test PPL05.slnx --filter FullyQualifiedName~ValidationTests
dotnet test PPL05.slnx --filter FullyQualifiedName~StatusCodeTests
dotnet test PPL05.slnx --filter FullyQualifiedName~ResponseBodyTests
dotnet test PPL05.slnx --filter FullyQualifiedName~PersistenceTests
```

Melihat daftar test tanpa menjalankannya:

```bash
dotnet test PPL05.slnx --list-tests
```

Menjalankan aplikasi API secara normal (opsional):

```bash
dotnet run --project TodoApi
```

Contoh request manual dengan curl:

```bash
curl http://localhost:5000/todos
curl -X POST http://localhost:5000/todos \
  -H "Content-Type: application/json" \
  -d "{\"title\":\"Belajar WebApplicationFactory\",\"isDone\":false}"
```

---

## 6. Hasil yang diharapkan

Ketika `dotnet test PPL05.slnx` dijalankan, ringkasan yang harus muncul:

```text
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 3-8 s - TodoApi.Tests.dll (net10.0)
```

Atau dengan verbosity normal:

```text
Test Run Successful.
Total tests: 11
     Passed: 11
 Total time: ~3 s
```

Rincian 11 test case:

| File test | Jumlah | Yang diverifikasi |
|---|---:|---|
| `RoutingTests.cs` | 6 | 200, 404 (data tidak ada), 404 (constraint `id`), 404 (path salah), 405 |
| `ValidationTests.cs` | 1 | 400, `application/problem+json`, error key `title` |
| `StatusCodeTests.cs` | 2 | 201 + `Location`, 204 |
| `ResponseBodyTests.cs` | 1 | `application/json`, array `Todo`, judul sesuai seed |
| `PersistenceTests.cs` | 1 | POST → GET → query langsung ke `TodoDbContext` |

Contoh respons yang diharapkan:

```http
GET /todos
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

[{"id":1,"title":"Todo awal","isDone":false}]
```

```http
POST /todos
HTTP/1.1 201 Created
Location: /todos/2
Content-Type: application/json; charset=utf-8

{"id":2,"title":"Belajar integration test","isDone":false}
```

```http
POST /todos  (title kosong)
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"...","title":"One or more validation errors occurred.","status":400,
 "errors":{"title":["Title wajib diisi minimal 3 karakter."]}}
```

---

## 7. Jebakan yang sering terjadi

1. **Lupa `public partial class Program { }`** → `WebApplicationFactory<Program>` tidak kompilasi.
2. **Test project masih `Microsoft.NET.Sdk`** → ganti ke `Microsoft.NET.Sdk.Web`.
3. **Hanya menghapus `DbContext`, lupa `DbConnection`** → test masih memakai koneksi aplikasi.
4. **`SqliteConnection` tidak di-`Open()` atau tidak singleton** → data hilang/ tabel tidak ditemukan.
5. **Nama DB dibuat di dalam lambda `AddDbContext`** → tiap scope mendapat DB berbeda. Buat nama/lambda koneksi sekali saat konfigurasi.
6. **`AllowAutoRedirect = true`** → status awal tertutup oleh redirect.
7. **Factory dipakai bareng `[OneTimeSetUp]`** → test saling mengotori data. Pakai factory baru per test.
8. **Assertion hanya `EnsureSuccessStatusCode()`** → bug status code tidak terdeteksi.

---

## 8. Referensi

- [Integration tests in ASP.NET Core — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests)
- [WebApplicationFactory&lt;TEntryPoint&gt; — API docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1)
- [WebApplicationFactoryClientOptions — API docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactoryclientoptions)
- [Choose a testing strategy with EF Core — Microsoft Learn](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)
