using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Endpoints;

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/todos", async (TodoDbContext db) =>
            await db.Todos.OrderBy(t => t.Id).ToListAsync());

        endpoints.MapGet("/todos/{id:int}", async (TodoDbContext db, int id) =>
            await db.Todos.FindAsync(id) is { } todo
                ? Results.Ok(todo)
                : Results.NotFound());

        endpoints.MapPost("/todos", async (TodoDbContext db, Todo input) =>
        {
            if (HasInvalidTitle(input))
            {
                return InvalidTitle();
            }

            var todo = new Todo { Title = input.Title.Trim(), IsDone = input.IsDone };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.Created($"/todos/{todo.Id}", todo);
        });

        endpoints.MapPut("/todos/{id:int}", async (TodoDbContext db, int id, Todo input) =>
        {
            if (HasInvalidTitle(input))
            {
                return InvalidTitle();
            }

            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            todo.Title = input.Title.Trim();
            todo.IsDone = input.IsDone;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        endpoints.MapDelete("/todos/{id:int}", async (TodoDbContext db, int id) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool HasInvalidTitle(Todo input) =>
        string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length < 3;

    private static IResult InvalidTitle() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["Title wajib diisi minimal 3 karakter."]
        });
}
