using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EventImageServer.Filters;

public sealed class EventOwnerFilter : IAsyncActionFilter
{
    public static readonly object OwnerItemKey = new();
    private const string RoleErrorMessage = "Only EventOwners have a seat order.";
    private readonly EventOwnerResolver _ownerResolver;

    public EventOwnerFilter(EventOwnerResolver ownerResolver)
    {
        _ownerResolver = ownerResolver;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var resolution = await _ownerResolver.ResolveAsync(context.HttpContext.User, RoleErrorMessage);
        if (resolution.Owner == null)
        {
            context.Result = new ObjectResult(new { message = resolution.ErrorMessage })
            {
                StatusCode = resolution.ErrorStatusCode
            };
            return;
        }

        var method = context.HttpContext.Request.Method;
        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            context.Result = new ObjectResult(new { message = "Viewers have read-only access." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        context.HttpContext.Items[OwnerItemKey] = resolution.Owner;
        await next();
    }
}
