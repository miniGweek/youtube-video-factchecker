using FactChecker.Core.Pipeline;
using FactChecker.Infrastructure.YouTube;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FactChecker.Web.Pages;

#pragma warning disable CA1812 // Instantiated by Razor Pages framework
internal sealed class IndexModel(AnalysisPipeline pipeline) : PageModel
{
    [BindProperty]
    public string VideoUrl { get; set; } = string.Empty;

    public static void OnGet() { }

    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(VideoUrl))
        {
            ModelState.AddModelError(nameof(VideoUrl), "Please enter a YouTube URL.");
            return Page();
        }

        if (!Uri.TryCreate(VideoUrl, UriKind.Absolute, out var videoUri))
        {
            ModelState.AddModelError(nameof(VideoUrl), "That doesn't look like a valid URL.");
            return Page();
        }

        if (!YouTubeUrlValidator.TryParseVideoId(videoUri, out _))
        {
            ModelState.AddModelError(nameof(VideoUrl), "Please enter a YouTube video URL (youtube.com/watch?v=... or youtu.be/...).");
            return Page();
        }

        var analysisId = Guid.NewGuid().ToString("N");
        _ = Task.Run(() => pipeline.RunAsync(analysisId, videoUri));

        return Redirect($"/analysis/{analysisId}");
    }
}
#pragma warning restore CA1812
