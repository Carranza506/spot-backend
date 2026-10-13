using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// Keeps MVC from reading a multipart body on its own during model binding (the pattern Microsoft
/// documents for file uploads). Without it, the form value providers read the whole form before
/// the action runs, and a body over the size limit ends up as the generic model-binding 400
/// instead of the 413 <see cref="PhotoUploadForm"/> returns. With it, the form is only ever read
/// by <see cref="PhotoUploadForm.ReadAsync"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
