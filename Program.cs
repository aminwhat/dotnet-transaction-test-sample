using Microsoft.EntityFrameworkCore;

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
        => options.UseSqlite("Data Source=todos.db");
}

class Program
{
    static async Task Main()
    {
        using var db = new AppDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        Console.WriteLine("=== Running Transaction Stress Test ===");

        var rnd = new Random();
        int totalTransactions = 100;  // scale this higher for heavy tests
        int committedCount = 0, rolledBackCount = 0;

        for (int i = 0; i < totalTransactions; i++)
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Insert N rows in this transaction
                int inserts = rnd.Next(1, 10);
                var items = new List<TodoModel>();

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

                // Randomly decide to rollback or commit
                if (rnd.Next(2) == 0)
                {
                    Console.WriteLine($"[Tx {i}] Rolling back {inserts} inserts...");
                    await transaction.RollbackAsync();
                    rolledBackCount += inserts;
                }
                else
                {
                    Console.WriteLine($"[Tx {i}] Committing {inserts} inserts...");
                    await transaction.CommitAsync();
                    committedCount += inserts;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tx {i}] Exception: {ex.Message}, rolling back...");
                await transaction.RollbackAsync();
            }
        }

        // --- Verification ---
        Console.WriteLine("\n=== Verification Phase ===");
        var dbCount = await db.Todos.CountAsync();

        Console.WriteLine($"Expected committed rows: {committedCount}");
        Console.WriteLine($"Actual rows in DB:       {dbCount}");

        if (dbCount == committedCount)
            Console.WriteLine("✅ Test Passed: All commits/rollbacks behaved correctly.");
        else
            Console.WriteLine("❌ Test Failed: Data mismatch!");

        Console.WriteLine("\n--- Sample Data in DB ---");
        var sample = await db.Todos.AsNoTracking().Take(20).ToListAsync();
        foreach (var todo in sample)
        {
            Console.WriteLine($"{todo.Id}: {todo.Title} (Done: {todo.IsDone})");
        }
    }
}
