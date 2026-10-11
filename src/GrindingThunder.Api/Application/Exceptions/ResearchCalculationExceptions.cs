namespace GrindingThunder.Api.Application.Exceptions;

public sealed class ResearchTreeVersionNotFoundException(Guid researchTreeVersionId)
    : Exception($"Research tree version '{researchTreeVersionId}' was not found.")
{
}

public sealed class ResearchCalculationInputException(string message)
    : Exception(message)
{
}
