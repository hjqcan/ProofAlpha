using Autotrade.Strategy.Application.ControlRoom;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Polymarket.Extensions;

namespace Autotrade.Api.ControlRoom;

public static class ControlRoomServiceCollectionExtensions
{
    public static WebApplicationBuilder AddControlRoomConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<ControlRoomOptions>(
            builder.Configuration.GetSection(ControlRoomOptions.SectionName));
        builder.Services.Configure<ExecutionOptions>(
            builder.Configuration.GetSection(ExecutionOptions.SectionName));
        builder.Services.Configure<RiskOptions>(
            builder.Configuration.GetSection(RiskOptions.SectionName));
        builder.Services.AddStrategyControlRoomReadModel(builder.Configuration);

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IControlRoomMarketDataService, ControlRoomMarketDataService>();
        builder.Services.AddScoped<IControlRoomQueryService, ControlRoomQueryService>();
        builder.Services.AddScoped<IControlRoomCommandService, ControlRoomCommandService>();

        if (builder.Configuration.GetValue($"{ControlRoomOptions.SectionName}:EnablePublicMarketData", true))
        {
            builder.Services.AddPolymarketGammaClient(builder.Configuration);
            builder.Services.AddPolymarketClobClient(builder.Configuration);
        }

        return builder;
    }
}
