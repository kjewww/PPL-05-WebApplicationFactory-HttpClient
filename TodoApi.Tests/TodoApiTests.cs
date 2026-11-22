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
