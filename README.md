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

Hasil akhirnya: API dengan 5 endpoint, database SQLite persisten, dan **10 test case** yang semuanya hijau. Panduan resminya bisa dibaca di [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests).

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

    // --- Fokus 1: Routing ---------------------------------------------------------

    [TestCase("/todos", 200)]
    [TestCase("/todos/1", 200)]
    [TestCase("/todos/999", 404)]
    [TestCase("/todos/abc", 404)] // route constraint {id:int}
    public async Task Routing_Get_ReturnsExpectedStatus(string path, int expectedStatus)
    {
        var response = await _client.GetAsync(path);

        Assert.That((int)response.StatusCode, Is.EqualTo(expectedStatus));
    }

    // --- Fokus 2: Validasi --------------------------------------------------------

    [TestCase("")]
    [TestCase("ab")]
    [TestCase("   ")]
    public async Task Validation_InvalidTitle_Returns400_WithProblemJson(string title)
    {
        var response = await _client.PostAsJsonAsync("/todos", new { title, isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Errors, Does.ContainKey("title"));
    }

    // --- Fokus 3: Status code -----------------------------------------------------

    [Test]
    public async Task StatusCode_PostPutDelete_Returns201_204_204()
    {
        var post = await _client.PostAsJsonAsync("/todos",
            new { title = "Status code", isDone = false });

        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(post.Headers.Location, Is.Not.Null);

        var put = await _client.PutAsJsonAsync("/todos/2",
            new { title = "Diupdate", isDone = true });

        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var delete = await _client.DeleteAsync("/todos/2");

        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // --- Fokus 4: Response body ---------------------------------------------------

    [Test]
    public async Task ResponseBody_Json_CanBeDeserialized()
    {
        var listResponse = await _client.GetAsync("/todos");

        Assert.That(listResponse.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/json"));

        var todos = await listResponse.Content.ReadFromJsonAsync<List<Todo>>();

        Assert.That(todos, Is.Not.Null);
        Assert.That(todos!.Count, Is.EqualTo(1));
        Assert.That(todos[0].Title, Is.EqualTo("Todo awal"));

        var todo = await _client.GetFromJsonAsync<Todo>("/todos/1");

        Assert.That(todo, Is.Not.Null);
        Assert.That(todo!.Id, Is.EqualTo(1));
        Assert.That(todo.IsDone, Is.False);

        var post = await _client.PostAsJsonAsync("/todos",
            new { title = "  Dibaca lagi  ", isDone = true });
        var created = await post.Content.ReadFromJsonAsync<Todo>();

        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Title, Is.EqualTo("Dibaca lagi"));
        Assert.That(created.IsDone, Is.True);
    }

    // --- Fokus 5: Persistence (tanpa akses DbContext) -----------------------------

    [Test]
    public async Task Persistence_PostGetDeleteGet()
    {
        // 1. POST: buat data baru.
        var created = await _client.PostAsJsonAsync("/todos",
            new { title = "Dari test", isDone = false });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        // Pakai Location dari response, jangan hardcode "/todos/2".
        var location = created.Headers.Location!.ToString();

        // 2. GET: data baru harus terbaca dari server.
        var get = await _client.GetAsync(location);

        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var todo = await get.Content.ReadFromJsonAsync<Todo>();

        Assert.That(todo!.Title, Is.EqualTo("Dari test"));

        // 3. DELETE: hapus data itu.
        var delete = await _client.DeleteAsync(location);

        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // 4. GET lagi: data sudah tidak ada.
        var afterDelete = await _client.GetAsync(location);

        Assert.That(afterDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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

### 4.4 Lima fokus pengujian

Test dikelompokkan menjadi **5 method**, sesuai lima fokus utama. Dua di antaranya memakai `[TestCase]` supaya satu method bisa menjalankan beberapa skenario.

| # | Nama method | Fokus | Eksekusi |
|---|---|---|---|
| 1 | `Routing_Get_ReturnsExpectedStatus` | Routing + constraint `{id:int}` | 4 |
| 2 | `Validation_InvalidTitle_Returns400_WithProblemJson` | Validasi `400` + `problem+json` | 3 |
| 3 | `StatusCode_PostPutDelete_Returns201_204_204` | Status code `201` / `204` / `204` | 1 |
| 4 | `ResponseBody_Json_CanBeDeserialized` | `Content-Type` + deserialisasi JSON | 1 |
| 5 | `Persistence_PostGetDeleteGet` | POST → GET → DELETE → GET | 1 |

Total **10 eksekusi test**.

Fokus kelima membuktikan persistence **tanpa mengakses `DbContext`** — semuanya lewat HTTP:

```text
POST /todos  → 201 + Location: /todos/2
GET  /todos/2 → 200 + title "Dari test"
DELETE /todos/2 → 204
GET  /todos/2 → 404
```

Yang perlu diperhatikan: test mengambil alamat `/todos/2` dari header **`Location`** yang dikirim server, bukan menuliskannya sendiri. Dengan begitu test benar-benar mengikuti apa yang terjadi di server.

Untuk menambah skenario, tambahkan `[TestCase(...)]` baru atau method `[Test]` di dalam class `TodoApiTests`; tidak perlu class atau setup baru:

```csharp
[Test]
public async Task Get_Todos_AfterPost_ReturnsTwoItems()
{
    await _client.PostAsJsonAsync("/todos", new { title = "Todo kedua", isDone = false });

    var todos = await _client.GetFromJsonAsync<List<Todo>>("/todos");

    Assert.That(todos!.Count, Is.EqualTo(2));
}
```

### 4.5 Panduan Syntax `HttpClient` yang Umum Dipakai

Semua method di bawah dipanggil lewat field `_client` yang dibuat di `SetUp`. Karena semuanya `Async`, gunakan `await`.

#### Method utama

| Method | Parameter umum | Hasil | Contoh |
|---|---|---|---|
| `GetAsync(string? requestUri)` | URI relatif/absolut | `Task<HttpResponseMessage>` | `_client.GetAsync("/todos/1")` |
| `GetAsync(uri, HttpCompletionOption)` | `ResponseContentRead` / `ResponseHeadersRead` | `Task<HttpResponseMessage>` | lihat contoh di bawah |
| `GetAsync(uri, CancellationToken)` | token pembatal | `Task<HttpResponseMessage>` | `_client.GetAsync("/todos", token)` |
| `GetFromJsonAsync<T>(uri)` | URI, opsional `JsonSerializerOptions`, token | `Task<T?>` | `_client.GetFromJsonAsync<Todo>("/todos/1")` |
| `PostAsync(uri, HttpContent)` | content | `Task<HttpResponseMessage>` | `_client.PostAsync("/todos", content)` |
| `PostAsJsonAsync(uri, value)` | objek → JSON | `Task<HttpResponseMessage>` | `_client.PostAsJsonAsync("/todos", todo)` |
| `PutAsJsonAsync(uri, value)` | objek → JSON | `Task<HttpResponseMessage>` | `_client.PutAsJsonAsync("/todos/1", todo)` |
| `DeleteAsync(uri)` | URI | `Task<HttpResponseMessage>` | `_client.DeleteAsync("/todos/1")` |
| `SendAsync(HttpRequestMessage)` | request custom | `Task<HttpResponseMessage>` | method/header/content bebas |

Masih tersedia `GetStringAsync(uri)`, `GetByteArrayAsync(uri)`, `GetStreamAsync(uri)`, dan `PatchAsync(uri, content)` kalau kebutuhannya khusus.

#### Parameter yang paling sering bikin bingung

- **`requestUri`** bisa berupa `string` atau `Uri`. Karena `CreateClient()` sudah mengisi `BaseAddress`, cukup tulis `"/todos"` tanpa domain. Pakai awalan `/` supaya jelas bahwa alamat dihitung dari root.
- **`HttpCompletionOption`** — default-nya `ResponseContentRead`, artinya method selesai setelah seluruh body dibaca. `ResponseHeadersRead` langsung kembali setelah header diterima, berguna untuk body besar/streaming.
- **`CancellationToken`** — parameter opsional di urutan terakhir; kalau tidak diisi akan memakai `CancellationToken.None`. Untuk test pendek biasanya tidak perlu.
- **`JsonSerializerOptions`** — opsional. Default web sudah memakai `camelCase` dan case-insensitive, makanya JSON `"isDone"` otomatis cocok dengan properti C# `IsDone`.
- **`using System.Net.Http.Json;`** wajib ada supaya `GetFromJsonAsync`, `PostAsJsonAsync`, `PutAsJsonAsync`, dan `ReadFromJsonAsync` dikenal.

#### Membaca hasil response

| Anggota | Contoh | Fungsi |
|---|---|---|
| `StatusCode` | `Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK))` | Cek status |
| `IsSuccessStatusCode` | `Assert.That(response.IsSuccessStatusCode, Is.True)` | Cek status `2xx` |
| `EnsureSuccessStatusCode()` | `response.EnsureSuccessStatusCode()` | Melempar exception kalau bukan `2xx` |
| `Headers.Location` | `response.Headers.Location` | Ambil `Location` dari `201 Created` |
| `Content.Headers.ContentType?.MediaType` | `Is.EqualTo("application/json")` | Cek tipe body |
| `ReadAsStringAsync()` | `await response.Content.ReadAsStringAsync()` | Body sebagai string |
| `ReadFromJsonAsync<T>()` | `await response.Content.ReadFromJsonAsync<Todo>()` | Body sebagai objek |

#### Contoh ringkas

```csharp
// GET biasa
var response = await _client.GetAsync("/todos/1");
Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

// GET langsung jadi objek
var todo = await _client.GetFromJsonAsync<Todo>("/todos/1");

// POST JSON
var created = await _client.PostAsJsonAsync("/todos",
    new { title = "Contoh", isDone = false });

// Header dan body
var location = created.Headers.Location;
var body = await created.Content.ReadAsStringAsync();
var hasil = await created.Content.ReadFromJsonAsync<Todo>();

// Request custom lewat SendAsync
var request = new HttpRequestMessage(HttpMethod.Delete, "/todos/1");
var deleted = await _client.SendAsync(request);
Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

// GET dengan completion option
var streamed = await _client.GetAsync("/todos", HttpCompletionOption.ResponseHeadersRead);
```

#### Kesalahan yang sering terjadi

1. `GetAsync()` mengembalikan `HttpResponseMessage`, **bukan** objek `Todo`. Kalau mau objek, pakai `GetFromJsonAsync<Todo>()` atau `ReadFromJsonAsync<Todo>()`.
2. Lupa `using System.Net.Http.Json;` → method `...AsJsonAsync(...)` tidak dikenal.
3. Lupa `await` → yang didapat adalah `Task<HttpResponseMessage>`, bukan response-nya.
4. Membuat `new HttpClient()` sendiri di test — jangan. Pakai `_client` dari factory agar request benar-benar masuk ke aplikasi yang diuji.

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
Passed!  - Failed:     0, Passed:     10, Skipped:     0, Total:     10
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
