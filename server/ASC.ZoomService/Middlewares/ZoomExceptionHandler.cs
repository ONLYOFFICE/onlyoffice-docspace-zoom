using Microsoft.AspNetCore.Diagnostics;

namespace ASC.ZoomService.Middlewares
{
    public class ZoomExceptionHandler : IExceptionHandler
    {
        private ILogger<ZoomExceptionHandler> log;

        public ZoomExceptionHandler(ILogger<ZoomExceptionHandler> log)
        {
            this.log = log;
        }

        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            log.LogError(exception, $"Got an error while processing {httpContext.Request.Method} {httpContext.Request.Method}");
            return ValueTask.FromResult(false);
        }
    }
}
