using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

public class TodoModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<TodoModel> Todos { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite("Data Source=todos_large.db"); // new DB file for large test
}

class Program
{
    static async Task Main()
    {
        using var db = new AppDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        Console.WriteLine("=== LARGE TRANSACTION STRESS TEST ===");

        var rnd = new Random();
        int totalTransactions = 1000;  // ⬅️ adjust for even larger load (e.g. 5000)
        int committedCount = 0, rolledBackCount = 0;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < totalTransactions; i++)
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Insert between 50 and 200 rows per transaction
                int inserts = rnd.Next(50, 200);
                var items = new List<TodoModel>(inserts);

                for (int j = 0; j < inserts; j++)
                {
                    items.Add(new TodoModel
                    {
                        Title = $"Tx{i}_Item{j}",
                        IsDone = rnd.Next(2) == 0
                    });
                }

                db.Todos.AddRange(items);
                await db.SaveChangesAsync();

                // Randomly rollback or commit
                if (rnd.Next(100) < 40) // 40% rollback chance
                {
                    await transaction.RollbackAsync();
                    rolledBackCount += inserts;
                }
                else
                {
                    await transaction.CommitAsync();
                    committedCount += inserts;
                }

                // Log progress every 100 transactions
                if (i % 100 == 0 && i > 0)
                    Console.WriteLine($"Processed {i}/{totalTransactions} transactions...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tx {i}] Error: {ex.Message} → Rolling back...");
                await transaction.RollbackAsync();
            }
        }

        sw.Stop();

        // --- Verification ---
        Console.WriteLine("\n=== VERIFICATION ===");
        var dbCount = await db.Todos.CountAsync();

        Console.WriteLine($"Committed inserts (expected): {committedCount}");
        Console.WriteLine($"Rolled back inserts (ignored): {rolledBackCount}");
        Console.WriteLine($"Actual rows in DB:            {dbCount}");
        Console.WriteLine($"Execution Time: {sw.Elapsed.TotalSeconds:F2}s");

        if (dbCount == committedCount)
            Console.WriteLine("✅ Test Passed: Database state matches expectations.");
        else
            Console.WriteLine("❌ Test Failed: Data mismatch!");

        Console.WriteLine("\n--- Sample of DB Rows ---");
        var sample = await db.Todos.AsNoTracking().Take(30).ToListAsync();
        foreach (var todo in sample)
            Console.WriteLine($"{todo.Id}: {todo.Title} (Done: {todo.IsDone})");
    }
}
