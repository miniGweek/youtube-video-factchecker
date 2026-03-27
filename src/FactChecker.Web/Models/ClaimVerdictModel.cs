using FactChecker.Core.Models;

namespace FactChecker.Web.Models;

#pragma warning disable CA1812 // Instantiated via ViewRenderer / Razor model binding
internal sealed record ClaimVerdictModel(Claim Claim, FactCheck FactCheck);
#pragma warning restore CA1812
