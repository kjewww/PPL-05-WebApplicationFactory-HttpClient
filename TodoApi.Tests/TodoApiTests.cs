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
