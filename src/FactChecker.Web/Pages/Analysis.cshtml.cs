using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FactChecker.Web.Pages;

#pragma warning disable CA1812 // Instantiated by Razor Pages framework
internal sealed class AnalysisModel : PageModel
{
    public string AnalysisId { get; private set; } = string.Empty;

    public IActionResult OnGet(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        AnalysisId = id;
        return Page();
    }
}
#pragma warning restore CA1812
