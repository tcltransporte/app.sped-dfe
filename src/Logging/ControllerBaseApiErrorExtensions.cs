using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace application.services.Logging;

public static class ControllerBaseApiErrorExtensions
{
    /// <summary>
    /// Registra a exceção no arquivo de log da API antes de devolver erro ao cliente.
    /// </summary>
    public static void LogApiError(this ControllerBase controller, Exception ex, [CallerMemberName] string? actionName = null)
    {
        var logger = controller.HttpContext.RequestServices.GetRequiredService<ApiErrorFileLogger>();
        var label = $"{controller.GetType().Name}.{actionName}";
        logger.Write(ex, controller.HttpContext, label);
    }
}
