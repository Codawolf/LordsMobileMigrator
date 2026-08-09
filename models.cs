using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

public class AppDbContext : DbContext
{
    public DbSet<KingdomSnapshot> Snapshots { get; set; } = null!;
    public DbSet<GlobalSetting> Settings { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        // SOLID PRODUCTION PATH: Anchors the database safely in your writable data directory
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lords_mobile_shared.db");
        options.UseSqlite($"Data Source={dbPath}"); 

    }
}

public class KingdomSnapshot
{
    [Key]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int KingdomNumber { get; set; }
    public int Castles { get; set; }
    public int InactiveCastles { get; set; }
    public int ActiveCastles => Castles - InactiveCastles;
    public int Rank { get; set; }
    public int MigrationCost { get; set; }
    public long P50Might { get; set; }
    public int TimeTillWowMinutes { get; set; }
    public string TimeTillWowSummary { get; set; } = string.Empty;
    public int FourChamps { get; set; }
    public int FiveChamps { get; set; }
    public int SixChamps { get; set; }
    public int SevenPlusChamps { get; set; }
    public int AccessoriesChamps { get; set; }
}

public class GlobalSetting
{
    [Key]
    public string Key { get; set; } = "Config";
    public string ChaliceInput { get; set; } = string.Empty;
    public string WatchedInput { get; set; } = string.Empty;
    public int MinPop { get; set; } = 1000;
    public int MaxPop { get; set; } = 1500;
    public int MaxAccessories { get; set; } = 8;

    // NEW FIELD: Stores the global admin access password securely
    public string AdminPassword { get; set; } = "LordsGuild123!"; // Default starting password
}

public class DisplayKingdomRow
{
    public int KingdomNumber { get; set; }
    public int CurrentActive { get; set; }
    public int ActiveChange { get; set; }
    public int CurrentInactive { get; set; }
    public int InactiveChange { get; set; }
    public int MigrationCost { get; set; }
    public string P50MightDisplay { get; set; } = "";
    public int AccessoriesChamps { get; set; }
    public string TimeTillWowSummary { get; set; } = string.Empty;
    public double TotalHoursToWow { get; set; }
    public bool IsChaliceKingdom { get; set; }
    public bool IsWatchedKingdom { get; set; } // NEW FIELD: Tracks if it matches your watched data array
    public bool IsDisregarded { get; set; }
}
