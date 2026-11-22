using Microsoft.AspNetCore.Mvc.Testing;

namespace TodoApi.Tests.Infrastructure;

public abstract class ApiTestBase
{
    protected CustomWebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public void SetUp()
    {
        // Factory baru tiap test => database SQLite baru => isolasi antar test.
        Factory = new CustomWebApplicationFactory<Program>();
        Factory.SeedDatabase();

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [TearDown]
    public void TearDown()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
