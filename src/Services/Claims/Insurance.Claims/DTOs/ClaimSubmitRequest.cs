using System.ComponentModel.DataAnnotations;

namespace Insurance.Claims.DTOs;

public class ClaimSubmitRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "EnrollmentId is required.")]
    public int EnrollmentId { get; set; }

    [Required]      // nullable so a missing date is a 400, not 0001-01-01
    public DateOnly? IncidentDate { get; set; }

    [Required, StringLength(2000, MinimumLength = 10, ErrorMessage = "Please describe the incident in at least 10 characters.")]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 100_000_000)]
    public decimal ClaimAmount { get; set; }

    /// <summary>
    /// How many documents the caller will upload right after this (the claim form sends its files next). Optional;
    /// it only decides how long the "We received your claim" email waits for them.
    /// </summary>
    [Range(0, Services.ClaimDocumentService.MaxDocumentsPerClaim)]
    public int? DocumentCount { get; set; }
}