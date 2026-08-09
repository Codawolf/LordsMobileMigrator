using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

public class IndexModel : PageModel
{
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? ChaliceInput { get; set; }
    [BindProperty] public string? WatchedInput { get; set; }
    [BindProperty] public int MinPop { get; set; }
    [BindProperty] public int MaxPop { get; set; }
    [BindProperty] public int MaxAccessories { get; set; }

    [BindProperty] public string? EnteredPassword { get; set; }
    [BindProperty] public string? NewPasswordSetting { get; set; }

    public List<DisplayKingdomRow> ProcessedKingdoms { get; set; } = new();
    public string LastUploadDisplay { get; set; } = "No Data Uploaded";
    public bool IsAuthenticated { get; set; } = false;
    public string? LoginErrorMessage { get; set; }

    public void OnGet()
    {
        using var db = new AppDbContext();
        db.Database.Migrate();
        RunManualSchemaPatch(db);

        var config = GetOrInitSettings(db);

        string? authCookie = Request.Cookies["LordsTrackerAuth"];
        IsAuthenticated = (authCookie == config.AdminPassword);

        AssignPropertiesFromConfig(config);

        if (IsAuthenticated)
        {
            LoadDataFromDatabase(config);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var db = new AppDbContext();
        var config = GetOrInitSettings(db);

        // Security check verification pass
        string? authCookie = Request.Cookies["LordsTrackerAuth"];
        if (authCookie != config.AdminPassword) return RedirectToPage();

        // 1. If values were typed on this form execution pass, save them
        if (!string.IsNullOrWhiteSpace(ChaliceInput) || !string.IsNullOrWhiteSpace(WatchedInput))
        {
            UpdateConfigFromProperties(config);
            db.SaveChanges();
        }

        // 2. Process and save the incoming excel matrix data rows
        if (Upload != null && Upload.Length > 0)
        {
            await ProcessUploadedExcelFile(Upload, db);
        }

        // 3. MASTER INLINE RESOLUTION FIX: Re-pull the verified database 
        // properties row right before loading data tables to prevent blank text drops!
        var freshConfig = GetOrInitSettings(db);
        AssignPropertiesFromConfig(freshConfig);

        IsAuthenticated = true;
        LoadDataFromDatabase(freshConfig);
        return Page();
    }

    public IActionResult OnPostLogin()
    {
        using var db = new AppDbContext();
        var config = GetOrInitSettings(db);

        if (EnteredPassword == config.AdminPassword)
        {
            var cookieOptions = new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(7),
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            };
            Response.Cookies.Append("LordsTrackerAuth", config.AdminPassword, cookieOptions);
            return RedirectToPage();
        }

        LoginErrorMessage = "❌ Invalid Guild Access Password. Please try again.";
        AssignPropertiesFromConfig(config);
        return Page();
    }
    public IActionResult OnPostUpdateFilters()
    {
        using var db = new AppDbContext();

        var config = db.Settings.FirstOrDefault(s => s.Key == "Config");
        if (config == null)
        {
            config = new GlobalSetting { Key = "Config" };
            db.Settings.Add(config);
        }

        // Read values directly out of the incoming form body safely
        config.ChaliceInput = Request.Form["ChaliceInput"].ToString() ?? string.Empty;
        config.WatchedInput = Request.Form["WatchedInput"].ToString() ?? string.Empty;

        if (int.TryParse(Request.Form["MinPop"], out int minPop)) config.MinPop = minPop;
        if (int.TryParse(Request.Form["MaxPop"], out int maxPop)) config.MaxPop = maxPop;
        if (int.TryParse(Request.Form["MaxAccessories"], out int maxAcc)) config.MaxAccessories = maxAcc;

        db.SaveChanges();

        // MASTER RESOLUTION FIX: Return a clean status code instead of a full page redirect loop.
        // This prevents the browser from running OnGet() and overwriting your typed text!
        return new JsonResult(new { success = true });
    }
    public IActionResult OnPostUpdatePassword()
    {
        using var db = new AppDbContext();

        // 1. Fetch your master configuration tracking row from the Settings table
        var config = db.Settings.FirstOrDefault(s => s.Key == "Config") ?? GetOrInitSettings(db);

        // 2. Validate current tracking browser cookie permissions match
        string? authCookie = Request.Cookies["LordsTrackerAuth"];
        if (authCookie != config.AdminPassword) return RedirectToPage();

        // 3. If a valid new password is typed, permanently update the row text field
        if (!string.IsNullOrWhiteSpace(NewPasswordSetting) && NewPasswordSetting.Length >= 4)
        {
            config.AdminPassword = NewPasswordSetting;
            db.SaveChanges();

            // RESOLUTION FIX: Instantly issue a brand-new browser tracking cookie 
            // matching your new credentials before reloading the canvas!
            var cookieOptions = new CookieOptions
            {
                Expires = DateTime.UtcNow.AddDays(7),
                HttpOnly = true,
                Secure = true, // Safe enforcement for Let's Encrypt MonsterASP live links
                SameSite = SameSiteMode.Strict
            };

            Response.Cookies.Append("LordsTrackerAuth", NewPasswordSetting, cookieOptions);
        }

        return RedirectToPage();
    }
    public IActionResult OnGetKingdomHistory(int kingdomNumber)
    {
        using var db = new AppDbContext();

        // Security check: Block data lookups if user isn't authenticated
        var config = db.Settings.FirstOrDefault(s => s.Key == "Config") ?? GetOrInitSettings(db);
        string? authCookie = Request.Cookies["LordsTrackerAuth"];
        if (authCookie != config.AdminPassword) return new StatusCodeResult(403);

        // Fetch all historical runs for this specific kingdom, ordered oldest to newest
        var history = db.Snapshots
            .Where(s => s.KingdomNumber == kingdomNumber)
            .OrderBy(s => s.Timestamp)
            .Select(s => new
            {
                // Formats the timestamp nicely for the chart timeline axis label
                Date = s.Timestamp.ToLocalTime().ToString("MM/dd HH:mm"),
                Active = s.ActiveCastles
            })
            .ToList();

        return new JsonResult(history);
    }
    public IActionResult OnPostLogout()
    {
        // 1. Instantly delete the secure authentication token from the browser cookie cache
        Response.Cookies.Delete("LordsTrackerAuth");

        // 2. Perform a clean application redirect back to the home portal login wall
        return RedirectToPage();
    }
    public IActionResult OnPostResetData()
    {
        using var db = new AppDbContext();
        db.Database.OpenConnection();

        // FIXED: Only clear the historical snapshot data rows.
        // Leaving the Settings table intact prevents NullReference pipeline crashes!
        using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = @"DELETE FROM ""Snapshots"";";
            command.ExecuteNonQuery();

            command.CommandText = "VACUUM;";
            command.ExecuteNonQuery();
        }

        // Clear out authorization cookie to force a clean, secure synchronization pass
        Response.Cookies.Delete("LordsTrackerAuth");

        return RedirectToPage();
    }

    private void RunManualSchemaPatch(AppDbContext db)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        db.Database.OpenConnection();

        // 1. FIXED: Ensure the core tracking Snapshots table is created if it was missing!
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS ""Snapshots"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Timestamp"" TEXT NOT NULL,
                ""KingdomNumber"" INTEGER NOT NULL,
                ""Castles"" INTEGER NOT NULL,
                ""InactiveCastles"" INTEGER NOT NULL,
                ""Rank"" INTEGER NOT NULL,
                ""MigrationCost"" INTEGER NOT NULL,
                ""P50Might"" INTEGER NOT NULL,
                ""TimeTillWowMinutes"" INTEGER NOT NULL,
                ""TimeTillWowSummary"" TEXT NULL,
                ""FourChamps"" INTEGER NOT NULL,
                ""FiveChamps"" INTEGER NOT NULL,
                ""SixChamps"" INTEGER NOT NULL,
                ""SevenPlusChamps"" INTEGER NOT NULL,
                ""AccessoriesChamps"" INTEGER NOT NULL
            );";
        command.ExecuteNonQuery();

        // 2. Ensure the centralized Settings table is created
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS ""Settings"" (
                ""Key"" TEXT NOT NULL PRIMARY KEY,
                ""ChaliceInput"" TEXT NULL,
                ""WatchedInput"" TEXT NULL,
                ""MinPop"" INTEGER NOT NULL,
                ""MaxPop"" INTEGER NOT NULL,
                ""MaxAccessories"" INTEGER NOT NULL,
                ""AdminPassword"" TEXT NULL
            );";
        command.ExecuteNonQuery();

        // 3. SCHEMA INSPECTION: Verify the operational settings layout
        bool watchedExists = false;
        bool passwordExists = false;

        command.CommandText = @"PRAGMA table_info(""Settings"");";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string columnName = reader.GetString(1);
                if (columnName.Equals("WatchedInput", StringComparison.OrdinalIgnoreCase)) watchedExists = true;
                if (columnName.Equals("AdminPassword", StringComparison.OrdinalIgnoreCase)) passwordExists = true;
            }
        }

        // 4. Safe Alterations: Append missing column properties if needed
        if (!watchedExists)
        {
            command.CommandText = @"ALTER TABLE ""Settings"" ADD COLUMN ""WatchedInput"" TEXT NULL;";
            command.ExecuteNonQuery();
        }

        if (!passwordExists)
        {
            command.CommandText = @"ALTER TABLE ""Settings"" ADD COLUMN ""AdminPassword"" TEXT NULL;";
            command.ExecuteNonQuery();
        }
    }
    private GlobalSetting GetOrInitSettings(AppDbContext db)
    {
        var config = new GlobalSetting
        {
            Key = "Config",
            ChaliceInput = string.Empty,
            WatchedInput = string.Empty,
            MinPop = 1000,
            MaxPop = 1500,
            MaxAccessories = 8,
            AdminPassword = "LordsGuild123!" // Failsafe starting seed value
        };

        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = @"SELECT ""ChaliceInput"", ""WatchedInput"", ""MinPop"", ""MaxPop"", ""MaxAccessories"", ""AdminPassword"" FROM ""Settings"" WHERE ""Key"" = 'Config' LIMIT 1;";
        db.Database.OpenConnection();

        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                config.ChaliceInput = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                config.WatchedInput = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                config.MinPop = reader.IsDBNull(2) ? 1000 : reader.GetInt32(2);
                config.MaxPop = reader.IsDBNull(3) ? 1500 : reader.GetInt32(3);
                config.MaxAccessories = reader.IsDBNull(4) ? 8 : reader.GetInt32(4);
                config.AdminPassword = reader.IsDBNull(5) ? "LordsGuild123!" : reader.GetString(5);
                return config; // Existing record found and safely read without exceptions
            }
        }

        // SEED CONFIGURATION REPAIR: If the row was wiped out by a reset,
        // this routine safely seeds a fresh configuration into the table immediately.
        using var insertCommand = db.Database.GetDbConnection().CreateCommand();
        insertCommand.CommandText = @"
            INSERT INTO ""Settings"" (""Key"", ""ChaliceInput"", ""WatchedInput"", ""MinPop"", ""MaxPop"", ""MaxAccessories"", ""AdminPassword"")
            VALUES ('Config', '', '', 1000, 1500, 8, 'LordsGuild123!');";
        insertCommand.ExecuteNonQuery();

        return config;
    }
    private async Task ProcessUploadedExcelFile(IFormFile file, AppDbContext db)
    {
        // FIX: Extract a single, frozen timestamp BEFORE the loops begin.
        // Stripping milliseconds ensures all rows share the exact same second footprint.
        var now = DateTime.UtcNow;
        var uploadTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

        var newSnapshots = new List<KingdomSnapshot>();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var tempFile = Path.GetTempFileName();
        using (var s = System.IO.File.Create(tempFile)) await file.CopyToAsync(s);

        using (var s = System.IO.File.Open(tempFile, FileMode.Open, FileAccess.Read))
        using (var reader = ExcelReaderFactory.CreateReader(s))
        {
            bool isHeader = true;
            while (reader.Read())
            {
                if (isHeader) { isHeader = false; continue; }
                try
                {
                    // Pass the uniform shared uploadTime variable to the parser row loop
                    newSnapshots.Add(ParseExcelRowToSnapshot(reader, uploadTime));
                }
                catch { }
            }
        }
        System.IO.File.Delete(tempFile);
        if (newSnapshots.Any()) { db.Snapshots.AddRange(newSnapshots); await db.SaveChangesAsync(); }
    }

    private KingdomSnapshot ParseExcelRowToSnapshot(IExcelDataReader reader, DateTime uploadTime)
    {
        string rawMight = reader.GetValue(5)?.ToString() ?? "0";
        long mightVal = 0;

        // FIXED adaptive parsing routine: Handles both raw integers and custom text variations like '3.8B' safely!
        if (double.TryParse(rawMight, out double parsedDouble))
        {
            // If the source cells are unformatted raw integer values (e.g., 1657700485)
            // If it is already a massive raw number over 1 million, map it directly as bytes
            if (parsedDouble > 1000000)
            {
                mightVal = (long)parsedDouble;
            }
            else
            {
                // Fallback if the cell just says '3.8' instead of '3.8B'
                mightVal = (long)(parsedDouble * 1000000000);
            }
        }
        else
        {
            // Fallback: If it contains textual string modifiers (e.g., '3.8B' or '3.8 B')
            try
            {
                string cleanedText = Regex.Replace(rawMight, "[^0-9.]", "");
                if (double.TryParse(cleanedText, out double cleanNum))
                {
                    mightVal = (long)(cleanNum * 1000000000);
                }
            }
            catch { mightVal = 0; }
        }

        return new KingdomSnapshot
        {
            Timestamp = uploadTime,
            KingdomNumber = Convert.ToInt32(reader.GetValue(0)),
            Castles = Convert.ToInt32(reader.GetValue(1)),
            InactiveCastles = Convert.ToInt32(reader.GetValue(2)),
            Rank = Convert.ToInt32(reader.GetValue(3)),
            MigrationCost = Convert.ToInt32(reader.GetValue(4)),
            P50Might = mightVal, // Stored safely as raw byte integers in SQLite
            TimeTillWowMinutes = Convert.ToInt32(reader.GetValue(6)),
            TimeTillWowSummary = reader.GetValue(7)?.ToString() ?? "",
            FourChamps = Convert.ToInt32(reader.GetValue(8)),
            FiveChamps = Convert.ToInt32(reader.GetValue(9)),
            SixChamps = Convert.ToInt32(reader.GetValue(10)),
            SevenPlusChamps = Convert.ToInt32(reader.GetValue(11)),
            AccessoriesChamps = Convert.ToInt32(reader.GetValue(12))
        };
    }
    private void LoadDataFromDatabase(GlobalSetting config)
    {
        using var db = new AppDbContext();
        var uniqueTimestamps = db.Snapshots.Select(s => s.Timestamp).Distinct().OrderByDescending(t => t).Take(2).ToList();
        if (!uniqueTimestamps.Any()) return;

        DateTime latestTimestamp = uniqueTimestamps.First();
        //LastUploadDisplay = latestTimestamp.ToLocalTime().ToString("g");
        LastUploadDisplay = latestTimestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");
        DateTime? lastRunTime = uniqueTimestamps.Count > 1 ? uniqueTimestamps.Skip(1).FirstOrDefault() : null;
        var currentSnapshots = db.Snapshots.Where(s => s.Timestamp == latestTimestamp).ToList();
        var historicalData = lastRunTime.HasValue
            ? db.Snapshots.Where(s => s.Timestamp == lastRunTime.Value).ToDictionary(s => s.KingdomNumber)
            : new Dictionary<int, KingdomSnapshot>();

        var chaliceSet = ParseInputToHashSet(config.ChaliceInput);

        ProcessedKingdoms = currentSnapshots
            .Select(curr => MapToDisplayRow(curr, historicalData, chaliceSet, config))
            .Where(k => k.IsChaliceKingdom)
            .OrderBy(k => k.IsDisregarded).ThenBy(k => k.KingdomNumber).ToList();
    }

    private DisplayKingdomRow MapToDisplayRow(KingdomSnapshot curr, Dictionary<int, KingdomSnapshot> historicalData, HashSet<int> chaliceSet, GlobalSetting config)
    {
        historicalData.TryGetValue(curr.KingdomNumber, out var prev);
        bool isChalice = chaliceSet.Contains(curr.KingdomNumber);
        bool failsFilters = curr.ActiveCastles < config.MinPop || curr.ActiveCastles > config.MaxPop || curr.AccessoriesChamps > config.MaxAccessories;

        return new DisplayKingdomRow
        {
            KingdomNumber = curr.KingdomNumber,
            CurrentActive = curr.ActiveCastles,
            CurrentInactive = curr.InactiveCastles,
            ActiveChange = prev != null ? (curr.ActiveCastles - prev.ActiveCastles) : 0,
            InactiveChange = prev != null ? (curr.InactiveCastles - prev.InactiveCastles) : 0,
            MigrationCost = curr.MigrationCost,
            P50MightDisplay = (curr.P50Might / 1000000000.0).ToString("0.0") + "B",
            AccessoriesChamps = curr.AccessoriesChamps,
            TimeTillWowSummary = curr.TimeTillWowSummary,
            TotalHoursToWow = curr.TimeTillWowMinutes / 60.0,
            IsChaliceKingdom = isChalice,
            IsDisregarded = failsFilters
        };
    }

    private HashSet<int> ParseInputToHashSet(string input)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(input)) return set;
        foreach (Match m in Regex.Matches(input, @"\d+"))
            if (int.TryParse(m.Value, out int id)) set.Add(id);
        return set;
    }

    private void AssignPropertiesFromConfig(GlobalSetting config)
    {
        ChaliceInput = config.ChaliceInput;
        WatchedInput = config.WatchedInput;
        MinPop = config.MinPop;
        MaxPop = config.MaxPop;
        MaxAccessories = config.MaxAccessories;
    }

    private void UpdateConfigFromProperties(GlobalSetting config)
    {
        config.ChaliceInput = ChaliceInput ?? "";
        config.WatchedInput = WatchedInput ?? "";
        config.MinPop = MinPop;
        config.MaxPop = MaxPop;
        config.MaxAccessories = MaxAccessories;
    }
}


