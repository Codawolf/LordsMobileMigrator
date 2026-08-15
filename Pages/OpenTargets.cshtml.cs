using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;

public class OpenTargetsModel : PageModel
{
    public List<DisplayKingdomRow> OpenKingdoms { get; set; } = new();
    public string LastUploadDisplay { get; set; } = "No Data Uploaded";

    public void OnGet()
    {
        using var db = new AppDbContext();
        db.Database.EnsureCreated();

        // Security check verification pass
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

        // FETCH NOTES INDEX: Builds a fast look-up map to prevent multiple database queries
        var notesLookup = db.Notes.ToDictionary(n => n.KingdomNumber, n => n.Text);

        var chaliceSet = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(config.ChaliceInput))
        {
            foreach (Match m in Regex.Matches(config.ChaliceInput, @"\d+"))
                if (int.TryParse(m.Value, out int id)) chaliceSet.Add(id);
        }

        OpenKingdoms = currentSnapshots
            .Select(curr => {
                historicalData.TryGetValue(curr.KingdomNumber, out var prev);

                // Read from memory check dictionary
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
                    IsChaliceKingdom = chaliceSet.Contains(curr.KingdomNumber),
                    TimeTillWowMinutes = curr.TimeTillWowMinutes,

                    // FIXED: Assigns notes text properties dynamically inside loop tracking structures
                    NotesDisplay = noteText ?? string.Empty
                };
            })
            .Where(k => !k.IsChaliceKingdom && k.CurrentActive >= config.MinPop && k.CurrentActive <= config.MaxPop && k.AccessoriesChamps <= config.MaxAccessories)
            .OrderBy(k => k.MigrationCost)
            .ThenBy(k => k.KingdomNumber)
            .ThenBy(k => k.TotalHoursToWow)
            .ToList();
    }

}
