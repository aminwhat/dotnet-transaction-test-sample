using Microsoft.EntityFrameworkCore;
using System;

public class UserModel
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<UserModel> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite("Data Source=users_test.db");
}

class Program
{
    static async Task Main()
    {
        using var db = new AppDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        Console.WriteLine("=== SINGLE REQUEST DUPLICATE TEST ===");

        using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var username = "test_user";
            var user = new UserModel { Username = username, CreatedAt = DateTime.UtcNow };

            db.Users.Add(user);
            db.SaveChanges();

            // 🔴 simulate accidental duplicate insert
            var duplicate = new UserModel { Username = username, CreatedAt = DateTime.UtcNow };
            db.Users.Add(duplicate);
            await db.SaveChangesAsync();

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("\n=== RESULT IN DB ===");
        foreach (var u in db.Users.AsNoTracking())
        {
            Console.WriteLine($"{u.Id}: {u.Username} at {u.CreatedAt:O}");
        }
    }
}
