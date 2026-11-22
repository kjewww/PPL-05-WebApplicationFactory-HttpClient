using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class ValidationTests : ApiTestBase
{
    [Test]
    public async Task Post_InvalidTitle_Returns400WithFieldErrors()
    {
        var response = await Client.PostAsJsonAsync("/todos",
            new { title = "", isDone = false });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Status, Is.EqualTo(400));
        Assert.That(problem.Errors, Does.ContainKey("title"));
    }
}
