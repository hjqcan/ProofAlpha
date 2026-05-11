using Autotrade.Application.Readiness;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/readiness")]
public sealed class ReadinessController(IReadinessReportService readinessReportService) : ControllerBase
{
    [HttpGet("contract")]
    public ActionResult<FirstRunWizardContract> GetContract()
    {
        return Ok(FirstRunReadinessContract.Create());
    }

    [HttpGet]
    public async Task<ActionResult<ReadinessReport>> Get(CancellationToken cancellationToken)
    {
        var report = await readinessReportService.GetReportAsync(cancellationToken).ConfigureAwait(false);
        return Ok(report);
    }
}
