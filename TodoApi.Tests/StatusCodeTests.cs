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
