var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// 1. ADD THIS FOR MULTI-BROWSER SESSION PERSISTENCE
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60); // Keeps data active for 1 hour
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// THIS ENABLES YOU TO SEE THE REASON FOR THE CRASH LIVE ON THE WEB SITE
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Temporarily forcing the Developer Exception Page so we can read the exact crash reason
    app.UseDeveloperExceptionPage();
}
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// 2. ADD THIS BEFORE MAPPING RAZOR PAGES
app.UseSession();

app.MapRazorPages();

app.Run();
