using ASC.ApiSystem.Controllers;

namespace ASC.ZoomService.Middlewares
{
    public static class ZoomExceptionHandlerMiddleware
    {
        public static async Task HandleException(HttpContext context, Func<Task> next)
        {
            var log = context.RequestServices.GetService<ILogger<ZoomController>>();

            try
            {
                await next();
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Got an error while processing {context.Request.Method} {context.Request.Path}");
                throw;
            }
        }
    }
}
