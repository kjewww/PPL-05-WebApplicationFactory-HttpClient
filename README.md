# Todo API + Integration Test (`WebApplicationFactory` + `HttpClient`)

Branch: `feat/todo-api-sut`

Repositori ini berisi dua hal:

1. **Todo API** ASP.NET Core 10 yang dibuat dari nol → *System Under Test* (SUT).
2. **Integration test** NUnit yang menjalankan API tersebut **di dalam proses test** memakai `WebApplicationFactory<Program>` dan `HttpClient`.

Pendekatan test-nya sengaja dibuat paling sederhana: **tanpa `ConfigureWebHost`, tanpa custom factory, tanpa `ApiTestBase`**. Factory dan client dibuat di `SetUp`, dibersihkan di `TearDown`, dan semua test ada di **satu file**.

## Ringkasan

- ASP.NET Core 10 **minimal API**, 5 endpoint REST
- **SQLite file asli** (`todo.db`) lewat EF Core — database persisten, bukan in-memory
- Struktur API modular: `Models`, `Data`, `Endpoints`
- Test NUnit: **18 test case**, satu file, `SetUp` + `TearDown`
- Test memakai database SQLite sementara, jadi **tidak menyentuh `todo.db` milik aplikasi**
- `dotnet build` → 0 warning, `dotnet test` → 18 passed

## Struktur folder

```text
PPL05.slnx
TodoApi/
  Models/
    Todo.cs                  model Todo: Id, Title, IsDone
  Data/
    TodoDbContext.cs         DbSet<Todo> + EF Core SQLite
  Endpoints/
    TodoEndpoints.cs         kelima endpoint /todos
  appsettings.json           ConnectionStrings:TodoDb = Data Source=todo.db
  appsettings.Development.json
  Program.cs                 DI, seed, MapTodoEndpoints(), public partial class Program
  TodoApi.csproj             EF Core SQLite 10.0.12
TodoApi.Tests/
  TodoApiTests.cs            SetUp + TearDown + 18 test
  TodoApi.Tests.csproj       NUnit + Microsoft.AspNetCore.Mvc.Testing 10.0.12
.gitignore
README.md
```

## Persiapan lingkungan

- .NET 10 SDK — cek dengan `dotnet --version`
- Editor/IDE apa pun (VS Code, Visual Studio, Rider)

Tidak perlu MySQL/PostgreSQL/SQL Server. SQLite sudah termasuk lewat package NuGet.

## Menjalankan API saja

Dari root repositori:

```bash
dotnet restore PPL05.slnx
dotnet build PPL05.slnx
dotnet run --project TodoApi
```

Secara default aplikasi berjalan di URL yang tertera di output, misalnya:

```text
Now listening on: http://localhost:5234
```

Saat pertama kali dijalankan, aplikasi akan:

1. Membuat file `TodoApi/todo.db` otomatis lewat `Database.EnsureCreated()`.
2. Mengisi 1 data awal: `Id = 1`, `Title = "Todo awal"`, `IsDone = false`.

Pada menjalankan berikutnya, data lama tetap ada karena file database tidak dihapus.

Kalau ingin port tertentu:

```bash
dotnet run --project TodoApi --urls http://localhost:5080
```

## Endpoint

| Method | Route | Deskripsi | Sukses | Gagal |
|---|---|---|---|---|
| GET | `/todos` | Ambil semua todo | `200 OK` + array JSON | – |
| GET | `/todos/{id:int}` | Ambil todo by id | `200 OK` | `404 Not Found` |
| POST | `/todos` | Buat todo baru | `201 Created` + header `Location` | `400 Bad Request` |
| PUT | `/todos/{id:int}` | Update todo | `204 No Content` | `400` / `404` |
| DELETE | `/todos/{id:int}` | Hapus todo | `204 No Content` | `404 Not Found` |

Aturan validasi: `title` wajib diisi dan minimal 3 karakter. Jika tidak valid, response berupa `application/problem+json` dengan field error `title`.

## Contoh request Manual

```bash
# 1. List todo
curl http://localhost:5080/todos

# 2. Ambil todo id 1
curl http://localhost:5080/todos/1

# 3. Buat todo
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

Contoh body sukses:

```json
[{"id":1,"title":"Todo awal","isDone":false}]
```

Contoh response `POST` yang valid:

```http
HTTP/1.1 201 Created
Location: /todos/2
Content-Type: application/json; charset=utf-8

{"id":2,"title":"Belajar WebApplicationFactory","isDone":false}
```

Contoh response `POST` yang tidak valid:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"status":400,"errors":{"title":["Title wajib diisi minimal 3 karakter."]}}
```

---

## Integration test

### Menjalankan

```bash
dotnet test PPL05.slnx
```

Hasil yang diharapkan:

```text
Passed!  - Failed:     0, Passed:     18, Skipped:     0, Total:     18
```

Tidak perlu menjalankan `dotnet run` dulu. `WebApplicationFactory` menyalakan aplikasi sendiri di dalam proses test.

### Mental model 30 detik

| Yang biasa dilakukan | Di test |
|---|---|
| `dotnet run --project TodoApi` | `new WebApplicationFactory<Program>()` |
| Buka browser / `curl` | `factory.CreateClient()` |
| Siapkan sebelum tiap test | `[SetUp]` |
| Bersihkan sesudah tiap test | `[TearDown]` |

Jadi `WebApplicationFactory` itu "server palsu yang jalan di dalam test", dan `HttpClient` itu "curl otomatis" yang menembak server itu.

### Yang terjadi di `SetUp`

```csharp
DeleteDatabase();

Environment.SetEnvironmentVariable(
    "ConnectionStrings__TodoDb",
    $"Data Source={_databasePath};Pooling=False");

_factory = new WebApplicationFactory<Program>();
_client = _factory.CreateClient();
```

- `ConnectionStrings__TodoDb` adalah cara .NET menimpa key `ConnectionStrings:TodoDb` lewat environment variable (tanda `__` = `:`).
- Database test bernama `test-todo.db` dan diletakkan di folder output test (`TodoApi.Tests/bin/Debug/net10.0`), **bukan** di folder `TodoApi`.
- `Pooling=False` diperlukan supaya file SQLite benar-benar dilepas dan bisa dihapus di `TearDown`.
- Karena environment test masih `Development`, seed di `Program.cs` tetap jalan dan membuat `"Todo awal"` otomatis. Test tidak perlu kode seed sendiri.

### Yang terjadi di `TearDown`

```csharp
_client.Dispose();
_factory.Dispose();

Environment.SetEnvironmentVariable("ConnectionStrings__TodoDb", null);
DeleteDatabase();
```

Urutannya penting: client dibuang dulu, lalu factory, baru file database dihapus.

### Kenapa tidak pakai `ConfigureWebHost` / custom factory / `ApiTestBase`

Komponen-komponen itu **bukan basic**. Dipakai kalau ada kebutuhan lanjutan:

- `ConfigureWebHost` + custom factory → kalau mau SQLite **in-memory**, ganti/mock service lewat DI, atau akses `TodoDbContext` langsung.
- `ApiTestBase` → kalau ada banyak file test dan kode `SetUp`/`TearDown` ingin ditulis sekali.

Untuk demo ini, keduanya belum perlu. Yang dibutuhkan hanya: project mereferensi API, pakai `Microsoft.AspNetCore.Mvc.Testing`, SDK test = `Microsoft.NET.Sdk.Web`, `Program` terlihat (`public partial class Program { }`), lalu `SetUp` → request → assert → `TearDown`.

### Daftar 18 test case

Semua ada di `TodoApi.Tests/TodoApiTests.cs`.

| # | Test | Yang dicek |
|---|---|---|
| 1 | `Get_Todos_Returns200_WithJsonContentType` | 200 + `application/json` |
| 2 | `Get_Todos_ReturnsSeededTodo` | array berisi 1 todo `"Todo awal"` |
| 3 | `Get_TodoById_ReturnsSeededTodo` | 200 + `Id`, `Title`, `IsDone` sesuai seed |
| 4 | `Get_TodoById_UnknownId_Returns404` | id `999` → 404 |
| 5 | `Get_TodoById_NonIntegerId_Returns404` | `/todos/abc` → 404 (constraint `{id:int}`) |
| 6 | `Post_ValidTodo_Returns201_WithLocation` | 201 + `Location: /todos/2` |
| 7 | `Post_ValidTodo_ReturnsCreatedTodo` | body hasil POST, title ter-trim |
| 8 | `Post_EmptyTitle_Returns400` | title `""` → 400 |
| 9 | `Post_ShortTitle_Returns400` | title `"ab"` → 400 |
| 10 | `Post_InvalidTitle_ReturnsProblemJson_WithTitleError` | `application/problem+json` + key `title` |
| 11 | `Post_EmptyBody_Returns400` | body kosong → 400 |
| 12 | `Put_ExistingTodo_Returns204` | update todo id 1 → 204 |
| 13 | `Put_UnknownId_Returns404` | update id `999` → 404 |
| 14 | `Put_InvalidTitle_Returns400` | update dengan title kosong → 400 |
| 15 | `Delete_ExistingTodo_Returns204` | hapus todo id 1 → 204 |
| 16 | `Delete_UnknownId_Returns404` | hapus id `999` → 404 |
| 17 | `Post_ThenGetById_ReturnsCreatedTodo` | POST lalu GET → data baru terbaca |
| 18 | `Delete_ThenGetById_Returns404` | DELETE lalu GET → 404 |

Setiap test mendapat database bersih karena `SetUp` membuat factory baru dan `TearDown` menghapus file DB-nya.

### Menambah test baru

Tambahkan method `[Test]` di dalam `TodoApiTests`. Tidak perlu membuat class atau setup baru:

```csharp
[Test]
public async Task Get_Todos_AfterPost_ReturnsTwoItems()
{
    await _client.PostAsJsonAsync("/todos", new { title = "Todo kedua", isDone = false });

    var todos = await _client.GetFromJsonAsync<List<Todo>>("/todos");

    Assert.That(todos!.Count, Is.EqualTo(2));
}
```

Kalau nanti ingin naik level (in-memory DB, mock service, cek tabel langsung), saat itulah `CustomWebApplicationFactory` + `ConfigureWebHost` ditambahkan sebagai file baru — API tidak perlu diubah.

## Referensi

- [Integration tests in ASP.NET Core — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests)
- [WebApplicationFactory&lt;TEntryPoint&gt; — API docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1)
