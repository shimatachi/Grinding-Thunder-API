using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Api.Application.Models;
using GrindingThunder.Api.Domain.Services;

namespace GrindingThunder.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResearchController(IResearchCalculatorService calculatorService) : ControllerBase
{
    private readonly IResearchCalculatorService _calculatorService = calculatorService;

    [HttpPost("calculate")]
    public async Task<IActionResult> CalculateResearch(
        [FromBody] ResearchCalculationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AverageRpPerMatch <= 0)
        {
            return BadRequest("Average RP per match must be greater than 0.");
        }

        var result = await _calculatorService.CalculateResearchAsync(request, cancellationToken);
        return Ok(result);
    }
}
