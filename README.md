# Tutorial: Integration Test ASP.NET Core dengan `WebApplicationFactory` + `HttpClient`

## 1. Pengenalan Singkat

Bayangkan kita punya API yang sudah jalan lewat `dotnet run`, lalu kita ingin mengujinya lewat HTTP tanpa harus menyalakan terminal kedua dan mengetik `curl` satu per satu. Di sinilah **integration test** membantu.

Dalam tutorial ini kita membangun:

- **SUT** (*System Under Test*): API Todo ASP.NET Core 10 dengan 5 endpoint dan database **SQLite asli**.
- **Test project**: NUnit yang menyalakan API tersebut **di dalam proses test** memakai `WebApplicationFactory<Program>`.
- **`HttpClient`**: "curl otomatis" yang mengirim request ke API yang sudah dinyalakan factory itu.

Analogi singkatnya:

| Kegiatan manual | Di dalam test |
|---|---|
| `dotnet run --project TodoApi` | `new WebApplicationFactory<Program>()` |
| Buka browser / ketik `curl` | `factory.CreateClient()` |
| Siapkan sebelum mencoba | `[SetUp]` |
| Bersihkan setelah selesai | `[TearDown]` |

Pendekatan yang dipakai sengaja yang paling sederhana: **tanpa `ConfigureWebHost`, tanpa `CustomWebApplicationFactory`, tanpa `ApiTestBase`**. Semua test ditaruh dalam **satu file**, dan factory + client dibuat di `SetUp` lalu dibersihkan di `TearDown`.

Hasil akhirnya: API dengan 5 endpoint, database SQLite persisten, dan **18 test case** yang semuanya hijau. Panduan resminya bisa dibaca di [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests).

## 2. Persiapan Lingkungan

Yang dibutuhkan:

- **.NET 10 SDK**. Cek dengan:

  ```bash
  dotnet --version
  ```

  Harus menampilkan versi `10.x.x`.

- Editor/IDE apa pun: VS Code, Visual Studio, Rider, atau hanya terminal.
- Pengetahuan dasar C# (class, method, `async/await`) dan HTTP (GET, POST, PUT, DELETE, status code).

Yang **tidak** dibutuhkan:

- MySQL, PostgreSQL, atau SQL Server. SQLite sudah termasuk lewat package NuGet.
- Docker.
- Server yang dijalankan manual saat menjalankan test.

## 3. Instalasi

### 3.1 Membuat solution dan proyek API

Jalankan dari folder kosong:

```bash
mkdir PPL-05-WebApplicationFactory-HttpClient
cd PPL-05-WebApplicationFactory-HttpClient

dotnet new sln -n PPL05
dotnet new web -n TodoApi -o TodoApi -f net10.0
dotnet sln PPL05.slnx add TodoApi/TodoApi.csproj
dotnet add TodoApi/TodoApi.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.12
```

Struktur aplikasi yang akan dibuat:

```text
TodoApi/
  Models/
    Todo.cs
  Data/
    TodoDbContext.cs
  Endpoints/
    TodoEndpoints.cs
  appsettings.json
  Program.cs
  TodoApi.csproj
.gitignore
```

### 3.2 Source code aplikasi

Buat folder `Models`, `Data`, dan `Endpoints` di dalam `TodoApi`, lalu isi file-file berikut.

**`TodoApi/TodoApi.csproj`**

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

**`TodoApi/appsettings.json`**

```json
{
  "ConnectionStrings": {
    "TodoDb": "Data Source=todo.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

Connection string ini dipakai aplikasi saat dijalankan normal. Nanti test akan menimpanya lewat environment variable, jadi `todo.db` asli tidak akan berubah.

**`TodoApi/Models/Todo.cs`**

```csharp
namespace TodoApi.Models;

public class Todo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
```

**`TodoApi/Data/TodoDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
}
```

**`TodoApi/Endpoints/TodoEndpoints.cs`**

Kelima endpoint dikumpulkan di satu file agar rapi. `MapTodoEndpoints()` dipanggil dari `Program.cs`.

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

Aturan validasinya: `title` wajib diisi dan minimal 3 karakter. Kalau tidak valid, responsnya `400` dengan `Content-Type: application/problem+json` dan key error `title`.

**`TodoApi/Program.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TodoDb")));

var app = builder.Build();

// Pastikan tabel ada dan ada 1 data awal saat aplikasi dijalankan.
// Environment "Testing" dilewati kalau nanti mau dipakai custom factory.
// Integration test sekarang memakai DB sementara lewat env var ConnectionStrings__TodoDb.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();

    if (!db.Todos.Any())
    {
        db.Todos.Add(new Todo { Title = "Todo awal", IsDone = false });
        db.SaveChanges();
    }
}

app.MapTodoEndpoints();

app.Run();

// Dibutuhkan supaya WebApplicationFactory<Program> bisa dipakai dari test project
// tanpa mengubah file ini lagi nanti.
public partial class Program { }
```

Tiga bagian yang penting untuk test:

1. `WebApplication.CreateBuilder(args)` menerima `args`, jadi override konfigurasi bekerja.
2. Connection string dibaca dari `ConnectionStrings:TodoDb`, jadi bisa ditimpa.
3. `public partial class Program { }` membuat class `Program` terlihat dari project test.

**`.gitignore`** di root repository:

```gitignore
# Build output
bin/
obj/

# SQLite database file (dibuat otomatis saat aplikasi dijalankan)
*.db
*.db-shm
*.db-wal

# IDE / editor
.vs/
.idea/
*.user
```

## 4. Langkah Implementasi Pengujian

### 4.1 Membuat proyek test dan dependency

```bash
dotnet new nunit -n TodoApi.Tests -o TodoApi.Tests -f net10.0
dotnet sln PPL05.slnx add TodoApi.Tests/TodoApi.Tests.csproj
dotnet add TodoApi.Tests/TodoApi.Tests.csproj reference TodoApi/TodoApi.csproj
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.12
```

Template NUnit membuat file `UnitTest1.cs` — **hapus file itu**, karena kita akan memakai satu file sendiri.

Lalu ubah `TodoApi.Tests.csproj` menjadi seperti di bawah. Perhatikan bahwa `Sdk` diubah dari `Microsoft.NET.Sdk` menjadi **`Microsoft.NET.Sdk.Web`**; ini membuat `WebApplicationFactory` bisa menemukan *content root* aplikasi.

### 4.2 Source code proyek test

**`TodoApi.Tests/TodoApi.Tests.csproj`**

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
    <ProjectReference Include="..\TodoApi\TodoApi.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="NUnit.Framework" />
  </ItemGroup>

</Project>
```

**`TodoApi.Tests/TodoApiTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoApi.Models;

namespace TodoApi.Tests;

public class TodoApiTests
{
    private const string ConnectionStringKey = "ConnectionStrings__TodoDb";

    // Database test ditaruh di folder output test, bukan di folder TodoApi.
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, "test-todo.db");

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        DeleteDatabase();

        // Timpa connection string aplikasi supaya test tidak menyentuh todo.db asli.
        // Pooling=False supaya file langsung bisa dihapus di TearDown.
        Environment.SetEnvironmentVariable(
            ConnectionStringKey, $"Data Source={_databasePath};Pooling=False");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();

        Environment.SetEnvironmentVariable(ConnectionStringKey, null);
        DeleteDatabase();
    }

    private void DeleteDatabase()
    {
        foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // --- Routing & response dasar -------------------------------------------------

    [Test]
    public async Task Get_Todos_Returns200_WithJsonContentType()
    {
        var response = await _client.GetAsync("/todos");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
    }

    [Test]
    public async Task Get_Todos_ReturnsSeededTodo()
    {
        var todos = await _client.GetFromJsonAsync<List<Todo>>("/todos");

        Assert.That(todos, Is.Not.Null);
        Assert.That(todos!.Count, Is.EqualTo(1));
        Assert.That(todos[0].Title, Is.EqualTo("Todo awal"));
    }

    [Test]
    public async Task Get_TodoById_ReturnsSeededTodo()
    {
        var todo = await _client.GetFromJsonAsync<Todo>("/todos/1");

        Assert.That(todo, Is.Not.Null);
        Assert.That(todo!.Id, Is.EqualTo(1));
        Assert.That(todo.Title, Is.EqualTo("Todo awal"));
        Assert.That(todo.IsDone, Is.False);
    }

    [Test]
    public async Task Get_TodoById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/todos/999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Get_TodoById_NonIntegerId_Returns404()
    {
        var response = await _client.GetAsync("/todos/abc");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST ---------------------------------------------------------------------

    [Test]
    public async Task Post_ValidTodo_Returns201_WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/todos",
            new { title = "Belajar factory", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/todos/2"));
    }

    [Test]
    public async Task Post_ValidTodo_ReturnsCreatedTodo()
    {
        var response = await _client.PostAsJsonAsync("/todos",
            new { title = "  Belajar factory  ", isDone = true });
        var todo = await response.Content.ReadFromJsonAsync<Todo>();

        Assert.That(todo, Is.Not.Null);
        Assert.That(todo!.Title, Is.EqualTo("Belajar factory"));
        Assert.That(todo.IsDone, Is.True);
    }

    [Test]
    public async Task Post_EmptyTitle_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/todos",
            new { title = "", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Post_ShortTitle_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/todos",
            new { title = "ab", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Post_InvalidTitle_ReturnsProblemJson_WithTitleError()
    {
        var response = await _client.PostAsJsonAsync("/todos",
            new { title = "x", isDone = false });

        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Errors, Does.ContainKey("title"));
    }

    [Test]
    public async Task Post_EmptyBody_Returns400()
    {
        var response = await _client.PostAsync("/todos",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- PUT ----------------------------------------------------------------------

    [Test]
    public async Task Put_ExistingTodo_Returns204()
    {
        var response = await _client.PutAsJsonAsync("/todos/1",
            new { title = "Todo selesai", isDone = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Put_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/todos/999",
            new { title = "Todo selesai", isDone = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Put_InvalidTitle_Returns400()
    {
        var response = await _client.PutAsJsonAsync("/todos/1",
            new { title = "", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // --- DELETE --------------------------------------------------------------------

    [Test]
    public async Task Delete_ExistingTodo_Returns204()
    {
        var response = await _client.DeleteAsync("/todos/1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync("/todos/999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Perubahan terlihat di request berikutnya ---------------------------------

    [Test]
    public async Task Post_ThenGetById_ReturnsCreatedTodo()
    {
        await _client.PostAsJsonAsync("/todos", new { title = "Dari test", isDone = false });

        var todo = await _client.GetFromJsonAsync<Todo>("/todos/2");

        Assert.That(todo, Is.Not.Null);
        Assert.That(todo!.Title, Is.EqualTo("Dari test"));
    }

    [Test]
    public async Task Delete_ThenGetById_Returns404()
    {
        await _client.DeleteAsync("/todos/1");

        var response = await _client.GetAsync("/todos/1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

### 4.3 Memahami `SetUp` dan `TearDown`

Empat hal yang terjadi di `SetUp`, berurutan:

1. `DeleteDatabase()` — menghapus sisa database dari test sebelumnya kalau ada.
2. `Environment.SetEnvironmentVariable("ConnectionStrings__TodoDb", ...)` — menimpa connection string aplikasi. Tanda `__` adalah cara .NET menulis key bertingkat `ConnectionStrings:TodoDb` pada environment variable.
3. `new WebApplicationFactory<Program>()` — menyalakan aplikasi di dalam proses test.
4. `_factory.CreateClient()` — membuat `HttpClient` yang menembak aplikasi itu.

Nilai `Pooling=False` penting. Tanpa itu, koneksi SQLite masih disimpan *connection pool* sehingga file `test-todo.db` tidak bisa dihapus di `TearDown` dan test akan gagal dengan error `file is being used by another process`.

Di `TearDown`, urutannya:

1. `_client.Dispose()`
2. `_factory.Dispose()`
3. hapus environment variable
4. `DeleteDatabase()`

Karena setiap test membuat factory baru dan database sendiri, tiap test selalu melihat data awal yang sama, yaitu satu todo `"Todo awal"` hasil seed di `Program.cs`. Test tidak perlu menulis kode seed sendiri.

### 4.4 Daftar 18 test case

| # | Nama test | Yang diverifikasi |
|---|---|---|
| 1 | `Get_Todos_Returns200_WithJsonContentType` | `200` + `application/json` |
| 2 | `Get_Todos_ReturnsSeededTodo` | array berisi 1 todo `"Todo awal"` |
| 3 | `Get_TodoById_ReturnsSeededTodo` | `200` + `Id`, `Title`, `IsDone` |
| 4 | `Get_TodoById_UnknownId_Returns404` | id `999` → `404` |
| 5 | `Get_TodoById_NonIntegerId_Returns404` | `/todos/abc` → `404` (constraint `{id:int}`) |
| 6 | `Post_ValidTodo_Returns201_WithLocation` | `201` + `Location: /todos/2` |
| 7 | `Post_ValidTodo_ReturnsCreatedTodo` | body hasil POST, `title` ter-trim |
| 8 | `Post_EmptyTitle_Returns400` | `title` kosong → `400` |
| 9 | `Post_ShortTitle_Returns400` | `title` 2 karakter → `400` |
| 10 | `Post_InvalidTitle_ReturnsProblemJson_WithTitleError` | `problem+json` + key error `title` |
| 11 | `Post_EmptyBody_Returns400` | body kosong → `400` |
| 12 | `Put_ExistingTodo_Returns204` | update id `1` → `204` |
| 13 | `Put_UnknownId_Returns404` | update id `999` → `404` |
| 14 | `Put_InvalidTitle_Returns400` | update dengan `title` kosong → `400` |
| 15 | `Delete_ExistingTodo_Returns204` | hapus id `1` → `204` |
| 16 | `Delete_UnknownId_Returns404` | hapus id `999` → `404` |
| 17 | `Post_ThenGetById_ReturnsCreatedTodo` | POST lalu GET → data baru terbaca |
| 18 | `Delete_ThenGetById_Returns404` | DELETE lalu GET → `404` |

Untuk menambah test, cukup tambah method `[Test]` di dalam class `TodoApiTests`; tidak perlu class atau setup baru:

```csharp
[Test]
public async Task Get_Todos_AfterPost_ReturnsTwoItems()
{
    await _client.PostAsJsonAsync("/todos", new { title = "Todo kedua", isDone = false });

    var todos = await _client.GetFromJsonAsync<List<Todo>>("/todos");

    Assert.That(todos!.Count, Is.EqualTo(2));
}
```

## 5. Cara Menjalankan

Dari root repository:

```bash
# 1. Unduh dependency
dotnet restore PPL05.slnx

# 2. Pastikan semua kode ter-compile
dotnet build PPL05.slnx

# 3. Jalankan seluruh integration test (API tidak perlu di-run manual)
dotnet test PPL05.slnx

# 4. Optional: jalankan API manual untuk mencoba lewat browser/curl
dotnet run --project TodoApi --urls http://localhost:5080
```

Saat API berjalan, kelima endpoint bisa dicoba manual:

```bash
# 1. List todo
curl http://localhost:5080/todos

# 2. Ambil todo id 1
curl http://localhost:5080/todos/1

# 3. Buat todo baru
curl -i -X POST http://localhost:5080/todos \
  -H "Content-Type: application/json" \
  -d "{\"title\":\"Belajar WebApplicationFactory\",\"isDone\":false}"

# 4. Update todo id 2
curl -i -X PUT http://localhost:5080/todos/2 \
  -H "Content-Type: application/json" \
  -d "{\"title\":\"Sudah dipelajari\",\"isDone\":true}"

# 5. Hapus todo id 2
curl -i -X DELETE http://localhost:5080/todos/2
```

## 6. Hasil yang Diharapkan

### Build

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Test

```text
Passed!  - Failed:     0, Passed:     18, Skipped:     0, Total:     18
```

### Respons endpoint saat dijalankan manual

| Request | Status | Body / catatan |
|---|---|---|
| `GET /todos` | `200 OK` | `[{"id":1,"title":"Todo awal","isDone":false}]` |
| `GET /todos/1` | `200 OK` | `{"id":1,"title":"Todo awal","isDone":false}` |
| `GET /todos/999` | `404 Not Found` | – |
| `POST /todos` valid | `201 Created` | `Location: /todos/2`, body todo baru |
| `POST /todos` title kosong | `400 Bad Request` | `application/problem+json`, error key `title` |
| `PUT /todos/2` | `204 No Content` | – |
| `DELETE /todos/2` | `204 No Content` | – |

Contoh response validasi yang gagal:

```json
{"status":400,"errors":{"title":["Title wajib diisi minimal 3 karakter."]}}
```

### Yang harus terjadi setelah test selesai

- File `TodoApi/todo.db` **tidak berubah** — test memakai `test-todo.db` di folder `TodoApi.Tests/bin/Debug/net10.0`.
- File `test-todo.db`, `test-todo.db-shm`, dan `test-todo.db-wal` sudah dihapus oleh `TearDown`.
- Tidak ada proses server yang tertinggal; `WebApplicationFactory` mematikan aplikasi sendiri.

Kalau muncul error `The process cannot access the file ... test-todo.db because it is being used by another process`, pastikan connection string test masih mengandung `Pooling=False`.
