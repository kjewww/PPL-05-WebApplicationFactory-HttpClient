using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Endpoints;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TodoDb")));

var app = builder.Build();

// Pastikan tabel ada dan ada 1 data awal saat aplikasi dijalankan.
// Environment "Testing" dilewati kalau nanti mau dipakai custom factory.
// Integration test sekarang memakai DB sementara lewat env var ConnectionStrings__TodoDb.
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
