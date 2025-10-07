using Microsoft.AspNetCore.Http;
using NWebDav.Server.Helpers;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using Serilog;

namespace NzbWebDAV.Middlewares;

public class ExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // If the response has not started, we can write our custom response
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 499; // Non-standard status code for client closed request
                await context.Response.WriteAsync("Client closed request.");
            }
        }
        catch (UsenetArticleNotFoundException e)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 404;
            }

            var filePath = GetRequestFilePath(context);
            Log.Error($"File `{filePath}` has missing articles: {e.Message}");
        }
        catch (SeekPositionNotFoundException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 404;
            }

            var filePath = GetRequestFilePath(context);
            var seekPosition = context.Request.GetRange()?.Start?.ToString() ?? "unknown";
            Log.Error($"File `{filePath}` could not seek to byte position: {seekPosition}");
        }
        catch (Exception e) when (IsDavItemRequest(context))
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 500;
            }

            var filePath = GetRequestFilePath(context);
            Log.Error($"File `{filePath}` could not be read due to unhandled {e.GetType()}: {e.Message}");
        }
    }

    private static string GetRequestFilePath(HttpContext context)
    {
        return context.Items["DavItem"] is DavItem davItem
            ? davItem.Path
            : context.Request.Path;
    }

    private static bool IsDavItemRequest(HttpContext context)
    {
        return context.Items["DavItem"] is DavItem;
    }
}