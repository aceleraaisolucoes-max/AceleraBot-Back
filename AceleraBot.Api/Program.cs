using System.Text.Json;
using AceleraBot.Api.Data;
using AceleraBot.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Porta (Render injeta PORT; default 3000 local)
var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// JSON: snake_case global (linhas de banco). [JsonPropertyName] sobrescreve (stats, webhook, cancel).
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
});

// EF Core / Postgres (Supabase) com convenção snake_case
var conn = builder.Configuration.GetConnectionString("Default")
           ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(conn).UseSnakeCaseNamingConvention());

// HttpClients
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("evolution", c =>
{
    var url = builder.Configuration["EVOLUTION_API_URL"];
    if (Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
    {
        c.BaseAddress = baseUri;
        c.DefaultRequestHeaders.Add("apikey", builder.Configuration["EVOLUTION_API_KEY"] ?? "");
        c.Timeout = TimeSpan.FromSeconds(15);
    }
});

// Serviços
builder.Services.AddScoped<IWhatsappService, WhatsappService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<INotifyService, NotifyService>();
builder.Services.AddScoped<IAiService, AiService>();

// Fila + processador do webhook (fire-and-forget)
builder.Services.AddSingleton<WebhookQueue>();
builder.Services.AddHostedService<WebhookProcessor>();

// CORS: origem = DASHBOARD_URL (fallback "*")
var dashboard = builder.Configuration["DASHBOARD_URL"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
     .WithHeaders("Content-Type", "Authorization");
    if (string.IsNullOrEmpty(dashboard) || dashboard == "*") p.AllowAnyOrigin();
    else p.WithOrigins(dashboard);
}));

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.Run();
