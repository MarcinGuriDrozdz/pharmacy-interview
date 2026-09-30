using DeliveryTracking.Application;
using DeliveryTracking.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace DeliveryTracking.Api.Infrastructure;

public sealed class TrackingExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            DeliveryNotFoundException => StatusCodes.Status404NotFound,
            CourierNotAssignedException => StatusCodes.Status403Forbidden,
            DeliveryStateException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            BadHttpRequestException bad => bad.StatusCode,
            _ => 0,
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = exception.Message },
        });
    }
}
