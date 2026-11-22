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
