using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---- Config ----
var devJwtSecret = builder.Configuration["JWT_DEV_SECRET"] ?? "hydronom-dev-secret-please-change";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(devJwtSecret))
        };
    });

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

builder.Services.AddRateLimiter(_ => _.AddFixedWindowLimiter("fixed", options =>
{
    options.Window = TimeSpan.FromSeconds(1);
    options.PermitLimit = 50;
    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    options.QueueLimit = 100;
}));

builder.Services.AddSingleton<IStateStore, InMemoryStateStore>();
builder.Services.AddSingleton<IEventLogger, JsonlEventLogger>();
builder.Services.AddSingleton<ICommandBus, CommandBus>();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---- Hubs ----
app.MapHub<TelemetryHub>("/ws/telemetry");

// ---- Dev Token ----
app.MapGet("/auth/dev-token", (string? user) =>
{
    var handler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(devJwtSecret);
    var descriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user ?? "operator") }),
        Expires = DateTime.UtcNow.AddHours(12),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = handler.CreateToken(descriptor);
    return Results.Ok(new { token = handler.WriteToken(token) });
});

// ---- DTOs ----
app.MapPost("/api/telemetry", async (TelemetryDto telemetry, IStateStore store, IEventLogger logger, IHubContext<TelemetryHub> hub) =>
{
    if (telemetry.Vehicle is null || string.IsNullOrWhiteSpace(telemetry.Vehicle.Id))
        return Results.BadRequest("vehicle.id required");

    await store.UpsertAsync(telemetry.Vehicle.Id, telemetry);
    await logger.AppendAsync("telemetry", telemetry.Vehicle.Id, telemetry);
    await hub.Clients.Group(telemetry.Vehicle.Id).SendAsync("telemetry", telemetry);

    return Results.Accepted();
}).RequireRateLimiting("fixed");

app.MapGet("/api/state/{vehicleId}", (string vehicleId, IStateStore store) =>
{
    var t = store.Get(vehicleId);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/api/commands", async (CommandDto cmd, ICommandBus bus, IEventLogger logger, IHubContext<TelemetryHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(cmd.VehicleId) || string.IsNullOrWhiteSpace(cmd.Command))
        return Results.BadRequest("vehicle_id and command required");

    var result = await bus.EnqueueAsync(cmd);

    await logger.AppendAsync("command", cmd.VehicleId, cmd);
    await hub.Clients.Group(cmd.VehicleId).SendAsync("command", cmd);

    return Results.Ok(result);
}).RequireAuthorization().RequireRateLimiting("fixed");

app.MapPost("/api/missions", async (MissionDto mission, IStateStore store, IEventLogger logger) =>
{
    await store.UpsertMissionAsync(mission);
    await logger.AppendAsync("mission", mission.VehicleId, mission);
    return Results.Created($"/api/missions/{mission.TaskId}", mission);
}).RequireAuthorization();

app.MapGet("/api/missions/{taskId}", (string taskId, IStateStore store) =>
{
    var m = store.GetMission(taskId);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

app.MapPost("/api/missions/{taskId}/{action}", async (string taskId, string action, IStateStore store, IEventLogger logger, IHubContext<TelemetryHub> hub) =>
{
    var m = store.GetMission(taskId);
    if (m is null) return Results.NotFound();

    var evt = new { taskId, action = action.ToUpperInvariant(), at = DateTime.UtcNow };
    await logger.AppendAsync("mission-event", m.VehicleId, evt);
    await hub.Clients.Group(m.VehicleId).SendAsync("mission-event", evt);
    return Results.Ok(evt);
}).RequireAuthorization();

app.Run();

// -------- Models & Services ----------
public record VehicleInfo(string Id, string Type);
public record Pose(double Lat, double Lon, double HeadingDeg, double SpeedMps);
public record Imu(double RollDeg, double PitchDeg, double YawDeg);
public record Thrusters(int LeftPwm, int RightPwm);
public record Ballast(int LevelPct);
public record Battery(double Voltage, double SocPct);
public record MissionInfo(string Mode, string? TaskId, int? WaypointIndex);

public record TelemetryDto(
    DateTime Timestamp,
    VehicleInfo Vehicle,
    Pose Pose,
    double DepthM,
    Imu Imu,
    Thrusters Thrusters,
    double RudderDeg,
    Ballast Ballast,
    Battery Battery,
    bool Leak,
    double TempC,
    MissionInfo Mission
);

public record CommandDto(
    DateTime Timestamp,
    [property: JsonPropertyName("vehicle_id")] string VehicleId,
    string Command,
    object? Payload
);

public record Waypoint(double Lat, double Lon, double HoldS);
public record MissionConstraints(double MaxSpeedMps, double KeepDepthM);
public record MissionDto(
    [property: JsonPropertyName("task_id")] string TaskId,
    string Name,
    [property: JsonPropertyName("vehicle_id")] string VehicleId,
    string Mode,
    List<Waypoint> Waypoints,
    MissionConstraints Constraints,
    [property: JsonPropertyName("created_by")] string CreatedBy
);

public interface IStateStore
{
    Task UpsertAsync(string vehicleId, TelemetryDto telemetry);
    TelemetryDto? Get(string vehicleId);

    Task UpsertMissionAsync(MissionDto mission);
    MissionDto? GetMission(string taskId);
}

public class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, TelemetryDto> _state = new();
    private readonly Dictionary<string, MissionDto> _missions = new();

    public Task UpsertAsync(string vehicleId, TelemetryDto telemetry)
    {
        _state[vehicleId] = telemetry;
        return Task.CompletedTask;
    }

    public TelemetryDto? Get(string vehicleId) => _state.TryGetValue(vehicleId, out var t) ? t : null;

    public Task UpsertMissionAsync(MissionDto mission)
    {
        _missions[mission.TaskId] = mission;
        return Task.CompletedTask;
    }
    public MissionDto? GetMission(string taskId) => _missions.TryGetValue(taskId, out var m) ? m : null;
}

public interface IEventLogger
{
    Task AppendAsync(string kind, string vehicleId, object data);
}

public class JsonlEventLogger : IEventLogger
{
    private readonly string _baseDir;
    private readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public JsonlEventLogger(IHostEnvironment env)
    {
        _baseDir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(_baseDir);
    }

    public async Task AppendAsync(string kind, string vehicleId, object data)
    {
        var file = Path.Combine(_baseDir, $"{DateTime.UtcNow:yyyyMMdd}-{kind}.jsonl");
        var line = JsonSerializer.Serialize(new { at = DateTime.UtcNow, vehicleId, data }, _jsonOpts);
        await File.AppendAllTextAsync(file, line + Environment.NewLine);
    }
}

public interface ICommandBus
{
    Task<object> EnqueueAsync(CommandDto cmd);
}

public class CommandBus : ICommandBus
{
    public Task<object> EnqueueAsync(CommandDto cmd)
    {
        // TODO: validation & routing; MVP echoes back
        return Task.FromResult<object>(new { accepted = true, cmd.Command, cmd.VehicleId });
    }
}

// ---- SignalR Hub ----
public class TelemetryHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var vehicleId = http?.Request.Query["vehicleId"].ToString();
        if (!string.IsNullOrWhiteSpace(vehicleId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, vehicleId!);
        }
        await base.OnConnectedAsync();
    }
}
