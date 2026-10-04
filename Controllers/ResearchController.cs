using Microsoft.AspNetCore.Mvc;
using GrindingThunder.Api.Application.Exceptions;
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
        if (request.ResearchTreeVersionId == Guid.Empty)
        {
            return BadRequest("ResearchTreeVersionId is required.");
        }

        if (request.TargetVehicleId == Guid.Empty)
        {
            return BadRequest("TargetVehicleId is required.");
        }

        if (request.AverageRpPerMatch <= 0)
        {
            return BadRequest("Average RP per match must be greater than 0.");
        }

        try
        {
            var result = await _calculatorService.CalculateResearchAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ResearchTreeVersionNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ResearchCalculationInputException exception)
        {
            return BadRequest(exception.Message);
        }
    }
}
