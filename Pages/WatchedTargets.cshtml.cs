using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;

public class WatchedTargetsModel : PageModel
{
    public List<DisplayKingdomRow> WatchedKingdoms { get; set; } = new();
    public string LastUploadDisplay { get; set; } = "No Data Uploaded";

    public void OnGet()
    {
        using var db = new AppDbContext();
        db.Database.EnsureCreated();

        var config = db.Settings.FirstOrDefault(s => s.Key == "Config") ?? new GlobalSetting();
        string? authCookie = Request.Cookies["LordsTrackerAuth"];
        if (authCookie != config.AdminPassword)
        {
            Response.Redirect("/");
            return;
        }

        var latestTimestamp = db.Snapshots
            .OrderByDescending(s => s.Timestamp)
            .Select(s => s.Timestamp)
            .FirstOrDefault();

        if (latestTimestamp == default) return;

        LastUploadDisplay = latestTimestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var priorTimestamp = db.Snapshots
            .Where(s => s.Timestamp < latestTimestamp)
            .OrderByDescending(s => s.Timestamp)
            .Select(s => s.Timestamp)
            .FirstOrDefault();

        var currentSnapshots = db.Snapshots.Where(s => s.Timestamp == latestTimestamp).ToList();
        var historicalData = priorTimestamp != default
            ? db.Snapshots.Where(s => s.Timestamp == priorTimestamp).ToDictionary(s => s.KingdomNumber)
            : new Dictionary<int, KingdomSnapshot>();

        // FETCH NOTES INDEX: Same high-performance look-up mapping block
        var notesLookup = db.Notes.ToDictionary(n => n.KingdomNumber, n => n.Text);

        var watchedSet = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(config.WatchedInput))
        {
            foreach (Match m in Regex.Matches(config.WatchedInput, @"\d+"))
                if (int.TryParse(m.Value, out int id)) watchedSet.Add(id);
        }

        WatchedKingdoms = currentSnapshots
            .Select(curr => {
                historicalData.TryGetValue(curr.KingdomNumber, out var prev);

                bool isWatched = watchedSet.Contains(curr.KingdomNumber);

                // 1. Evaluate your strategy constraint validation checks accurately
                bool failsFilters = curr.Castles < config.MinPop ||
                                    curr.Castles > config.MaxPop ||
                                    curr.AccessoriesChamps > config.MaxAccessories;

                notesLookup.TryGetValue(curr.KingdomNumber, out var noteText);

                return new DisplayKingdomRow
                {
                    KingdomNumber = curr.KingdomNumber,
                    CurrentActive = curr.Castles,
                    CurrentInactive = curr.InactiveCastles,
                    ActiveChange = prev != null ? (curr.Castles - prev.Castles) : 0,
                    InactiveChange = prev != null ? (curr.InactiveCastles - prev.InactiveCastles) : 0,
                    MigrationCost = curr.MigrationCost,
                    P50MightDisplay = (curr.P50Might / 1000000000.0).ToString("0.0") + "B",
                    AccessoriesChamps = curr.AccessoriesChamps,
                    TimeTillWowSummary = curr.TimeTillWowSummary,
                    TotalHoursToWow = curr.TimeTillWowMinutes / 60.0,
                    IsChaliceKingdom = false,
                    IsWatchedKingdom = isWatched,
                    IsDisregarded = failsFilters, // Flags the row status cell property accurately
                    TimeTillWowMinutes = curr.TimeTillWowMinutes,
                    NotesDisplay = noteText ?? string.Empty
                };
            })
            // 2. FIXED: Filter strictly by membership. Do not drop rows that fail parameters!
            .Where(k => k.IsWatchedKingdom)

            // 3. FIXED: Group Prime Targets cleanly at the top and push Warnings to the bottom
            .OrderBy(k => k.IsDisregarded)
            .ThenBy(k => k.KingdomNumber)
            .ToList();
    }
}
