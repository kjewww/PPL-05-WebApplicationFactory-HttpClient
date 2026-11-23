# Kontrak API Todo

Dokumen ini menjelaskan kontrak HTTP dari `TodoApi` sesuai implementasi di `TodoApi/Endpoints/TodoEndpoints.cs`. Contoh base URL di bawah memakai:

```text
http://localhost:5080
```

## Informasi umum

| Item | Keterangan |
|---|---|
| Base URL | `http://localhost:5080` |
| Prefix route | `/todos` |
| Format body | `application/json` |
| Autentikasi | Tidak ada |
| Penyimpanan | SQLite lewat connection string `TodoDb` |

Response JSON memakai nama properti camelCase (`id`, `title`, `isDone`) sesuai serialisasi default ASP.NET Core.

## Resource

### `Todo`

| Field | Tipe | Keterangan |
|---|---|---|
| `id` | `int` | Dibuat server dan bertambah otomatis; bersifat read-only |
| `title` | `string` | Wajib diisi, di-trim, minimal 3 karakter setelah di-trim |
| `isDone` | `bool` | Status selesai/tidak selesai |

Contoh representasi:

```json
{
  "id": 1,
  "title": "Todo awal",
  "isDone": false
}
```

Request body untuk `POST` dan `PUT` memakai bentuk yang sama. Field `id` boleh dikirim, tetapi diabaikan server:

- `POST` selalu membuat todo baru dengan id yang dihasilkan database.
- `PUT` memakai `id` dari URL, bukan `id` dari body.

## Endpoint

### `GET /todos`

Mengembalikan seluruh todo, diurutkan berdasarkan `id` secara ascending.

| Aspek | Keterangan |
|---|---|
| Request body | Tidak ada |
| Status sukses | `200 OK` |
| Response body | Array of `Todo` |
| Content-Type | `application/json` |

Contoh response:

```json
[
  { "id": 1, "title": "Todo awal", "isDone": false }
]
```

Array bisa kosong bila database tidak berisi data, misalnya saat aplikasi dijalankan dalam environment `Testing`.

### `GET /todos/{id:int}`

Mengambil satu todo berdasarkan id.

| Aspek | Keterangan |
|---|---|
| Request body | Tidak ada |
| `200 OK` | Todo dengan `id` tersebut ditemukan |
| `404 Not Found` | `id` tidak ditemukan, atau nilai bukan integer sehingga tidak cocok dengan constraint `{id:int}` |
| Response body | Objek `Todo` saat sukses; kosong saat `404` |

Contoh response sukses:

```json
{ "id": 1, "title": "Todo awal", "isDone": false }
```

### `POST /todos`

Membuat todo baru.

Contoh request:

```json
{
  "title": "Belajar WebApplicationFactory",
  "isDone": false
}
```

| Status | Kondisi | Keterangan |
|---|---|---|
| `201 Created` | Valid | Body berisi todo yang baru dibuat, header `Location: /todos/{id}` |
| `400 Bad Request` | `title` tidak valid | Body berisi `ValidationProblemDetails` dengan `application/problem+json` |

Nilai `title` dipangkas spasi awal/akhirnya sebelum disimpan. Misalnya `"  Belajar  "` disimpan sebagai `"Belajar"`.

Contoh response sukses:

```http
HTTP/1.1 201 Created
Location: /todos/2
Content-Type: application/json
```

```json
{ "id": 2, "title": "Belajar WebApplicationFactory", "isDone": false }
```

### `PUT /todos/{id:int}`

Mengganti `title` dan `isDone` dari todo yang sudah ada.

Contoh request:

```json
{
  "title": "Sudah dipelajari",
  "isDone": true
}
```

| Status | Kondisi | Keterangan |
|---|---|---|
| `204 No Content` | Valid dan todo ditemukan | Berhasil, tanpa response body |
| `400 Bad Request` | `title` tidak valid | Validasi dicek sebelum pencarian todo |
| `404 Not Found` | `id` tidak ditemukan | Response body kosong |

Karena validasi dilakukan lebih dulu, request dengan `title` tidak valid dan `id` tidak ada akan menghasilkan `400`, bukan `404`.

### `DELETE /todos/{id:int}`

Menghapus todo berdasarkan id.

| Aspek | Keterangan |
|---|---|
| Request body | Tidak ada |
| `204 No Content` | Todo berhasil dihapus, tanpa response body |
| `404 Not Found` | `id` tidak ditemukan, response body kosong |

## Validasi

Aturan `title` berlaku untuk `POST` dan `PUT`:

- tidak boleh null atau kosong;
- tidak boleh hanya spasi;
- setelah di-trim harus minimal 3 karakter.

Aturan ini berasal dari `HasInvalidTitle(input)` di `TodoEndpoints.cs`:

```csharp
string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length < 3
```

Bila gagal, endpoint mengembalikan `Results.ValidationProblem` dengan status `400`, Content-Type `application/problem+json`, dan error key `title`:

```json
{
  "status": 400,
  "errors": {
    "title": ["Title wajib diisi minimal 3 karakter."]
  }
}
```

## Ringkasan status code

| Status | Arti |
|---|---|
| `200 OK` | `GET` berhasil |
| `201 Created` | `POST` berhasil, disertai header `Location` |
| `204 No Content` | `PUT` atau `DELETE` berhasil, tanpa body |
| `400 Bad Request` | Validasi `title` gagal |
| `404 Not Found` | Id tidak ditemukan atau route tidak cocok |

## Data awal

Saat aplikasi dijalankan di luar environment `Testing`, `Program.cs` memastikan tabel ada dan menambahkan satu todo bila database masih kosong:

```json
{ "id": 1, "title": "Todo awal", "isDone": false }
```

Seed ini dilewati pada environment `Testing`. Integration test memakai database sementara melalui environment variable `ConnectionStrings__TodoDb`, sehingga tidak menyentuh `TodoApi/todo.db`.

## Batasan

- Belum ada autentikasi atau otorisasi.
- Belum ada pagination, filter, atau query parameter pengurutan.
- Urutan `GET /todos` sudah ditetapkan di kode, yaitu berdasarkan `id` ascending.
- Tidak ada endpoint selain kelima endpoint yang dijelaskan di atas.
