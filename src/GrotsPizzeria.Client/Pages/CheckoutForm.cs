using System.ComponentModel.DataAnnotations;

namespace GrotsPizzeria.Client.Pages;

public sealed class CheckoutForm
{
    [Required(ErrorMessage = "Where should we send it?")]
    [StringLength(200)]
    public string Address { get; set; } = "";

    public string Method { get; set; } = "card";

    [RegularExpression(@"^\d{4}$", ErrorMessage = "Enter exactly 4 digits.")]
    public string? CardLast4 { get; set; } = "4242";
}
