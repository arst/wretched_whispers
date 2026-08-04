using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Deployment;

public class StaticUiTests : IClassFixture<StaticUiTests.StaticUiFactory>
{
    private readonly HttpClient _client;

    public StaticUiTests(StaticUiFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/", "home-ui")]
    [InlineData("/sessions", "sessions-ui")]
    public async Task UiRoutesServeTheirPackagedPage(string path, string expected)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiRoutesNeverFallBackToTheUi()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/missing")).StatusCode);
    }

    public sealed class StaticUiFactory : WebApplicationFactory<Program>
    {
        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"ww-ui-{Guid.NewGuid():N}");
        private SqliteConnection? _connection;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_webRoot);
            Directory.CreateDirectory(Path.Combine(_webRoot, "sessions"));
            File.WriteAllText(Path.Combine(_webRoot, "index.html"), "home-ui");
            File.WriteAllText(Path.Combine(_webRoot, "sessions", "index.html"), "sessions-ui");

            builder.UseEnvironment("Development");
            builder.ConfigureServices((context, services) =>
            {
                context.HostingEnvironment.WebRootPath = _webRoot;
                context.HostingEnvironment.WebRootFileProvider = new PhysicalFileProvider(_webRoot);
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<WretchedWhispersDbContext>)
                             || d.ServiceType == typeof(WretchedWhispersDbContext))
                    .ToList())
                    services.Remove(descriptor);

                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();
                services.AddDbContext<WretchedWhispersDbContext>(options => options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection?.Dispose();
            if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
        }
    }
}
