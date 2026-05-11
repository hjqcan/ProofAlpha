using System.Text.Json.Serialization;
using Autotrade.Api.Configurations;
using Autotrade.Api.ControlRoom;
using Autotrade.Api.Middleware;
using Autotrade.Api.Readiness;
using Autotrade.Application.Readiness;

var builder = WebApplication.CreateBuilder(args);

builder.AddCorsConfiguration()
    .AddOpenApiConfiguration()
    .AddHealthCheckConfiguration()
    .AddAutotradeModuleConfiguration()
    .AddControlRoomConfiguration();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IReadinessProbeCollector, ApiReadinessProbeCollector>();
builder.Services.AddScoped<IReadinessReportService, ReadinessReportService>();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseAutotradeModuleConfiguration();
app.UseGlobalExceptionHandler();
app.MapOpenApiSetup();
app.UseRouting();
app.UseControlRoomLocalAccess();
app.UseCors(CorsConfig.ElectronCorsPolicy);
app.UseAuthorization();

app.MapControllers();
app.MapHealthCheckEndpoints();

await app.RunAsync();
