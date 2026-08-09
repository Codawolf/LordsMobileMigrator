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

        // Pull centralized master configuration values from table row
        var config = db.Settings.FirstOrDefault(s => s.Key == "Config") ?? new GlobalSetting();

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

        var chaliceSet = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(config.ChaliceInput))
        {
            foreach (Match m in Regex.Matches(config.ChaliceInput, @"\d+"))
                if (int.TryParse(m.Value, out int id)) chaliceSet.Add(id);
        }

        OpenKingdoms = currentSnapshots
            .Select(curr => {
                historicalData.TryGetValue(curr.KingdomNumber, out var prev);
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
                    IsChaliceKingdom = chaliceSet.Contains(curr.KingdomNumber)
                };
            })
            // Enforce synchronized parameters from shared db row
            .Where(k => !k.IsChaliceKingdom && k.CurrentActive >= config.MinPop && k.CurrentActive <= config.MaxPop && k.AccessoriesChamps <= config.MaxAccessories)
            .OrderBy(k => k.MigrationCost)
            .ThenBy(k => k.KingdomNumber)
            .ThenBy(k => k.TotalHoursToWow)
            .ToList();
    }
}
