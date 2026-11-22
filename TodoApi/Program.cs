using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

// SQLite in-memory dipakai juga oleh aplikasi normal.
// Koneksi disimpan sebagai singleton supaya tidak ditutup oleh EF.
builder.Services.AddSingleton<DbConnection>(_ =>
{
    var connection = new SqliteConnection("DataSource=:memory:");
    connection.Open();
    return connection;
});
builder.Services.AddDbContext<TodoDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<DbConnection>()));

var app = builder.Build();

// Seed hanya saat dijalankan normal. Saat di-test, factory yang melakukan seed.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();
    if (!db.Todos.Any())
    {
        db.Todos.Add(new Todo { Title = "Contoh todo awal" });
        db.SaveChanges();
    }
}

app.MapTodoEndpoints();

app.Run();

// Dibutuhkan agar WebApplicationFactory<Program> bisa dipakai dari test project.
public partial class Program { }
