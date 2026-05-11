using Microsoft.Extensions.Configuration;

namespace Autotrade.Infra.BackgroundJobs.Core;

/// <summary>
/// 定时任务配置器接口
/// 每个限界上下文实现此接口以注册自己的定时任务
/// </summary>
public interface IRecurringJobConfigurator
{
    /// <summary>
    /// 配置该模块的所有定时任务
    /// </summary>
    /// <param name="configuration">配置对象</param>
    void ConfigureJobs(IConfiguration configuration);
}
