namespace FactChecker.Web.Models;

#pragma warning disable CA1812 // Instantiated by System.Text.Json during model binding
internal sealed class AnalyseRequest
{
    public Uri? Url { get; set; }
}
#pragma warning restore CA1812
