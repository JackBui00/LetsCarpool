using LetsCarpool.Server.Data;
using LetsCarpool.Server.Carpooling;
using LetsCarpool.Server.Trips;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options => TripJson.Configure(options.JsonSerializerOptions));
builder.Services.AddSingleton<IDistanceProvider, HaversineDistanceProvider>();
builder.Services.AddSingleton<CarpoolPlanner>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
var tripStoreProvider = TripStoreProviderSelection.Parse(builder.Configuration["CarpoolStore:Provider"]);
if (tripStoreProvider == TripStoreProvider.SqlServer)
    SqlTripStore.ValidateConfiguration(builder.Configuration);
builder.Services.AddSingleton<ITripStore>(services => tripStoreProvider switch
{
    TripStoreProvider.Json => ActivatorUtilities.CreateInstance<JsonTripStore>(services),
    TripStoreProvider.SqlServer => ActivatorUtilities.CreateInstance<SqlTripStore>(services),
    _ => throw new InvalidOperationException("The configured trip store provider is not supported.")
});
builder.Services.AddSingleton(services => new TripService(
    services.GetRequiredService<ITripStore>(), services.GetRequiredService<CarpoolPlanner>(),
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<TimeProvider>()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("trip-create", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { AutoReplenishment = true, PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("trip-join", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { AutoReplenishment = true, PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("carpool-plan", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { AutoReplenishment = true, PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register CarpoolDbContext with the connection string from appsettings.json
builder.Services.AddDbContext<CarpoolDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseMiddleware<TripApiExceptionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapFallback("/api/{**path}", context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

app.MapFallbackToFile("/index.html");

app.Run();
