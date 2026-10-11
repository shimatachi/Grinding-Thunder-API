using GrindingThunder.Api.Application.Models;

namespace GrindingThunder.Api.Domain.Services;

public interface IResearchCalculatorService
{
    Task<ResearchCalculationResult> CalculateResearchAsync(
        ResearchCalculationRequest request,
        CancellationToken cancellationToken = default);
}
