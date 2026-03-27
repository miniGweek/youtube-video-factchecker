using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace FactChecker.Web.Services;

#pragma warning disable CA1812 // Instantiated via DI
/// <summary>
/// Renders Razor partial views to HTML strings.
/// Used by the SSE stream endpoint to serve rendered fragments.
/// </summary>
internal sealed class ViewRenderer(
    ICompositeViewEngine viewEngine,
    ITempDataProvider tempDataProvider)
{
    internal async Task<string> RenderPartialAsync<TModel>(
        HttpContext httpContext, string viewName, TModel model)
    {
        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData(),
            new ActionDescriptor());

        // Try by name first (searches Pages/Shared/ etc.)
        var viewResult = viewEngine.FindView(actionContext, viewName, isMainPage: false);

        // Fallback to explicit path if not found by name
        if (!viewResult.Success)
        {
            viewResult = viewEngine.GetView(
                executingFilePath: null,
                viewPath: $"~/Pages/Shared/{viewName}.cshtml",
                isMainPage: false);
        }

        if (!viewResult.Success)
            return $"<!-- partial '{viewName}' not found -->";

        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary()) { Model = model };

        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, viewResult.View, viewData, tempData, writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext).ConfigureAwait(false);
        return writer.ToString();
    }
}
#pragma warning restore CA1812
