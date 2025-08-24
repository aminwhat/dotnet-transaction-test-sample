using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

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

        Console.WriteLine("=== DUPLICATE INSERT STRESS TEST ===");

        var sw = Stopwatch.StartNew();
        int requestCount = 2000;   // simulate many requests
        int concurrency = 50;      // parallel threads

        var tasks = new List<Task>();

        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scopedDb = new AppDbContext();

                for (int j = 0; j < requestCount / concurrency; j++)
                {
                    // simulate a request inserting the SAME USERNAME
                    using var tx = await scopedDb.Database.BeginTransactionAsync();
                    try
                    {
                        var user = new UserModel
                        {
                            Username = "duplicate_test",
                            CreatedAt = DateTime.UtcNow
                        };

                        scopedDb.Users.Add(user);
                        await scopedDb.SaveChangesAsync();

                        // Random: sometimes "simulate retry" by inserting again
                        if (Random.Shared.Next(1000) < 5) // ~0.5% retry chance
                        {
                            scopedDb.Users.Add(new UserModel
                            {
                                Username = "duplicate_test",
                                CreatedAt = DateTime.UtcNow
                            });
                            await scopedDb.SaveChangesAsync();
                        }

                        await tx.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] {ex.Message}");
                        await tx.RollbackAsync();
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // --- Verification ---
        Console.WriteLine("\n=== VERIFICATION ===");

        var grouped = await db.Users
            .GroupBy(u => u.Username)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var g in grouped)
        {
            Console.WriteLine($"{g.Key} → {g.Count} records");
        }

        var duplicates = grouped.Where(g => g.Count > 1).ToList();

        Console.WriteLine($"\nTotal Users: {await db.Users.CountAsync()}");
        Console.WriteLine($"Duplicates Found: {duplicates.Count}");

        if (duplicates.Any())
        {
            Console.WriteLine("❌ Problem Reproduced: Duplicate rows detected.");
        }
        else
        {
            Console.WriteLine("✅ No duplicates — transactions consistent.");
        }

        Console.WriteLine($"Execution Time: {sw.Elapsed.TotalSeconds:F2}s");
    }
}
