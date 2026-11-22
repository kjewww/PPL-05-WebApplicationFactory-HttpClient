using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Tests.Infrastructure;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 1. Hapus pendaftaran DB milik aplikasi (callback ini jalan setelah Program.cs).
            var dbOptions = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDbContextOptionsConfiguration<TodoDbContext>));
            if (dbOptions is not null)
            {
                services.Remove(dbOptions);
            }

            var connection = services.SingleOrDefault(d => d.ServiceType == typeof(DbConnection));
            if (connection is not null)
            {
                services.Remove(connection);
            }

            // 2. SQLite in-memory: koneksi harus dibuka dan disimpan sebagai singleton,
            //    kalau tidak EF akan menutupnya dan data hilang.
            services.AddSingleton<DbConnection>(_ =>
            {
                var sqlite = new SqliteConnection("DataSource=:memory:");
                sqlite.Open();
                return sqlite;
            });

            services.AddDbContext<TodoDbContext>((sp, options) =>
                options.UseSqlite(sp.GetRequiredService<DbConnection>()));
        });
    }

    public void SeedDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        db.Database.EnsureCreated();

        if (!db.Todos.Any())
        {
            db.Todos.Add(new Todo { Id = 1, Title = "Todo awal", IsDone = false });
            db.SaveChanges();
        }
    }
}
