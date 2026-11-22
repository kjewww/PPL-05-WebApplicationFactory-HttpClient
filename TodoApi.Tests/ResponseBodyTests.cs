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
