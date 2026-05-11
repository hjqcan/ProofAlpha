using Hangfire;
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;

namespace Autotrade.Infra.BackgroundJobs.Core;

/// <summary>
/// 定时任务配置辅助类
/// </summary>
public static class RecurringJobHelper
{
    /// <summary>
    /// 注册或更新定时任务（从配置中读取参数）
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="configuration">配置对象</param>
    /// <param name="jobId">任务唯一标识</param>
    /// <param name="jobExpression">任务执行表达式</param>
    /// <param name="configSection">配置节点路径（例如：BackgroundJobs:MarketDataSync）</param>
    /// <param name="defaultCronExpression">默认 Cron 表达式（5 段格式：分 时 日 月 周）</param>
    public static void AddOrUpdateJob<TJob>(
        IConfiguration configuration,
        string jobId,
        Expression<Func<TJob, Task>> jobExpression,
        string configSection,
        string defaultCronExpression = "*/5 * * * *")
    {
        var enabled = configuration.GetValue<bool>($"{configSection}:Enabled", true);

        if (enabled)
        {
            var cronExpression = configuration[$"{configSection}:CronExpression"] ?? defaultCronExpression;
            var timeZone = TimeZoneInfo.Local;

            RecurringJob.AddOrUpdate<TJob>(
                jobId,
                jobExpression,
                cronExpression,
                new RecurringJobOptions
                {
                    TimeZone = timeZone
                });
        }
        else
        {
            // 如果配置为禁用，则移除任务
            RecurringJob.RemoveIfExists(jobId);
        }
    }
}
