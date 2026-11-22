# Todo API — SUT untuk WebApplicationFactory + HttpClient

Branch: `feat/todo-api-sut`

Repositori ini berisi **API Todo ASP.NET Core 10** yang dibuat dari nol dan berfungsi sebagai *System Under Test* (SUT). Belum ada project test di sini — test `WebApplicationFactory` + `HttpClient` akan dipasang pada langkah berikutnya.

## Ringkasan

- ASP.NET Core 10 **minimal API**
- **SQLite file asli** (`todo.db`) lewat EF Core — database persisten, bukan in-memory
- 5 endpoint REST
- Struktur folder modular: `Models`, `Data`, `Endpoints`
- Sudah disiapkan agar mudah dipasang integration test tanpa mengubah source lagi

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
  Program.cs                 DI, seed, MapTodoEndpoints()
  TodoApi.csproj
.gitignore
```

## Persiapan lingkungan

- .NET 10 SDK — cek dengan `dotnet --version`
- Editor/IDE apa pun (VS Code, Visual Studio, Rider)

Tidak perlu MySQL/PostgreSQL/SQL Server. SQLite sudah termasuk lewat package NuGet.

## Instalasi & menjalankan

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

## Contoh request

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

## Kenapa API ini siap dipasang integration test

Beberapa hal sudah disiapkan khusus supaya `WebApplicationFactory<Program>` bisa dipasang tanpa mengubah source lagi:

1. `WebApplication.CreateBuilder(args)` menerima `args`, sehingga override konfigurasi dari test bekerja.
2. `TodoDbContext` hanya didaftarkan sekali lewat `AddDbContext`, sehingga mudah dihapus dan diganti dengan SQLite in-memory milik test.
3. Connection string dibaca dari `ConnectionStrings:TodoDb`, sehingga bisa di-override lewat `ConfigureAppConfiguration`.
4. Seed aplikasi dilewati saat environment bernilai `Testing`; nanti factory yang melakukan seed.
5. `public partial class Program { }` membuat `Program` terlihat dari test project.
6. Validasi memakai `Results.ValidationProblem(...)`, sehingga test bisa mengassert `ValidationProblemDetails` dan status 400.

Rencana test yang akan dipasang:

- `RoutingTests` → 200 / 404 / 405 / constraint `{id:int}`
- `ValidationTests` → 400 + `application/problem+json`
- `StatusCodeTests` → 201 + `Location`, 204
- `ResponseBodyTests` → `Content-Type` + deserialisasi JSON
- `PersistenceTests` → POST → GET → cek langsung ke `TodoDbContext`

## Referensi

- [Integration tests in ASP.NET Core — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0&pivots=nunit#introduction-to-integration-tests)
- [WebApplicationFactory&lt;TEntryPoint&gt; — API docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1)
