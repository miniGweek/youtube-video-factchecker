namespace FactChecker.Web.Models;

#pragma warning disable CA1812 // Instantiated via ViewRenderer / Razor model binding
internal sealed record ClaimsHeaderModel(int Count, bool IsComplete, int VerifiedCount = 0);
#pragma warning restore CA1812
