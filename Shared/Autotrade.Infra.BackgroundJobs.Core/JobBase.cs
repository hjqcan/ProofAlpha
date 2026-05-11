using Hangfire;
using Microsoft.Extensions.Logging;

namespace Autotrade.Infra.BackgroundJobs.Core;

/// <summary>
/// 后台任务基类，提供统一的日志记录和异常处理
/// </summary>
/// <typeparam name="T">任务类型（用于日志记录）</typeparam>
public abstract class JobBase<T> where T : class
{
    protected readonly ILogger<T> Logger;

    protected JobBase(ILogger<T> logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// 执行任务（带标准日志和异常处理）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var jobName = GetJobName();
        Logger.LogInformation("===== 开始执行{JobName}任务 =====", jobName);

        try
        {
            await ExecuteJobAsync(cancellationToken);
            Logger.LogInformation("{JobName}任务执行完成", jobName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{JobName}任务执行失败", jobName);
            throw; // 让 Hangfire 记录失败并可能重试
        }
        finally
        {
            Logger.LogInformation("===== {JobName}任务结束 =====", jobName);
        }
    }

    /// <summary>
    /// 子类实现具体的任务逻辑
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected abstract Task ExecuteJobAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取任务名称（用于日志记录）
    /// </summary>
    /// <returns>任务名称</returns>
    protected virtual string GetJobName()
    {
        return typeof(T).Name.Replace("Job", "");
    }
}
