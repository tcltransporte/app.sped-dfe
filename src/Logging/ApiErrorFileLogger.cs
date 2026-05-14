using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace application.services.Logging;

/// <summary>
/// Grava erros da API em arquivo no diretório <c>Logs</c> (raiz do conteúdo), com stack trace
/// incluindo arquivo e número da linha quando símbolos de depuração (PDB) estão disponíveis.
/// </summary>
public sealed class ApiErrorFileLogger
{
    private readonly string _contentRoot;
    private static readonly object FileLock = new();

    public ApiErrorFileLogger(IWebHostEnvironment environment)
    {
        _contentRoot = environment.ContentRootPath;
    }

    public void Write(Exception exception, HttpContext? httpContext, string? contextLabel)
    {
        var logsDir = Path.Combine(_contentRoot, "Logs");
        Directory.CreateDirectory(logsDir);
        var logFilePath = Path.Combine(logsDir, $"api-errors-{DateTime.UtcNow:yyyy-MM-dd}.log");

        var sb = new StringBuilder(4096);
        sb.AppendLine("============================================================");
        sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
        if (httpContext != null)
        {
            sb.AppendLine($"Request: {httpContext.Request.Method} {httpContext.Request.Path}{httpContext.Request.QueryString}");
            sb.AppendLine($"Remote IP: {httpContext.Connection.RemoteIpAddress}");
        }
        if (!string.IsNullOrEmpty(contextLabel))
            sb.AppendLine($"Action/context: {contextLabel}");

        AppendExceptionChain(sb, exception);
        sb.AppendLine();

        var text = sb.ToString();
        lock (FileLock)
        {
            File.AppendAllText(logFilePath, text, Encoding.UTF8);
        }
    }

    private static void AppendExceptionChain(StringBuilder sb, Exception exception)
    {
        var ex = exception;
        var depth = 0;
        while (ex != null)
        {
            sb.AppendLine();
            sb.AppendLine($"--- Nível {depth}: {ex.GetType().FullName} ---");
            sb.AppendLine($"Mensagem: {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("Quadros de pilha (arquivo:linha quando disponível):");
            var stackTrace = new StackTrace(ex, fNeedFileInfo: true);
            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                if (frame == null)
                    continue;
                var method = frame.GetMethod();
                var fileName = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                var declaringType = method?.DeclaringType?.FullName ?? "?";
                var methodName = method?.Name ?? "?";
                sb.Append($"  #{i} {declaringType}.{methodName}");
                if (!string.IsNullOrEmpty(fileName))
                    sb.Append($" | {fileName}:{lineNumber}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine();
                sb.AppendLine("StackTrace (texto):");
                sb.AppendLine(ex.StackTrace);
            }

            ex = ex.InnerException;
            depth++;
        }
    }
}
