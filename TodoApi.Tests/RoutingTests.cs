using System.Net;
using System.Text;
using TodoApi.Tests.Infrastructure;

namespace TodoApi.Tests;

public class RoutingTests : ApiTestBase
{
    [TestCase("/todos", HttpStatusCode.OK)]
    [TestCase("/todos/1", HttpStatusCode.OK)]
    [TestCase("/todos/999", HttpStatusCode.NotFound)]
    [TestCase("/todos/abc", HttpStatusCode.NotFound)]
    [TestCase("/todo", HttpStatusCode.NotFound)]
    public async Task Routing_ResolvesAsExpected(string url, HttpStatusCode expected)
    {
        var response = await Client.GetAsync(url);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task Post_ToGetOnlyRoute_Returns405()
    {
        var response = await Client.PostAsync("/todos/1",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }
}
