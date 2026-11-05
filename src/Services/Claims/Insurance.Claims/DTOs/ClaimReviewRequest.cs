using System.ComponentModel.DataAnnotations;
using Insurance.Claims.Contracts;

namespace Insurance.Claims.DTOs;

public class ClaimReviewRequest
{
    [Required]
    public ClaimStatus? Status { get; set; }        // the status to move TO

    [StringLength(1000)]
    public string? AdminNotes { get; set; }
}