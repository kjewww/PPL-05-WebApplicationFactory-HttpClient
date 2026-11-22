using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TodoDb")));

var app = builder.Build();

// Saat dijalankan normal, pastikan tabel ada dan ada 1 data awal.
// Saat nanti dipasang test, environment "Testing" akan diset oleh factory
// sehingga seed di bawah dilewati dan test yang mengatur datanya sendiri.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.EnsureCreated();

    if (!db.Todos.Any())
    {
        db.Todos.Add(new Todo { Title = "Todo awal", IsDone = false });
        db.SaveChanges();
    }
}

app.MapTodoEndpoints();

app.Run();

// Dibutuhkan supaya WebApplicationFactory<Program> bisa dipakai dari test project
// tanpa mengubah file ini lagi nanti.
public partial class Program { }
