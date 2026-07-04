using application.services.Logging;
using application.services.Controllers.DFe.NFSe.Providers;
using application.services.Controllers.DFe.NFSe.Providers.ISSNET;
using application.services.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

//builder.Services
//    .AddControllers();
//.AddXmlSerializerFormatters();

builder.Services.AddControllers(options =>
{
    // Remove qualquer formatter XML padrão
    //options.InputFormatters.RemoveType<XmlSerializerInputFormatter>();
    //options.InputFormatters.RemoveType<XmlDataContractSerializerInputFormatter>();

    // Adiciona formatter XML customizado
    options.InputFormatters.Add(new XmlSerializerInputFormatter(options)
    {
        SupportedMediaTypes =
        {
            "application/xml",
        }
    });
});

builder.Services.AddSingleton<SefazSoapService>();
builder.Services.AddSingleton<ApiErrorFileLogger>();
builder.Services.AddSingleton<INFSeProvider, IssNetGoianiaProvider>();
builder.Services.AddSingleton<NFSeProviderResolver>();

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Serviços", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;
        if (ex != null)
        {
            var logger = context.RequestServices.GetRequiredService<ApiErrorFileLogger>();
            logger.Write(ex, context, "UnhandledException");
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { message = "Erro interno no servidor." });
    });
});

if (app.Environment.IsProduction())
{
    app.UsePathBase("/application/services");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/openapi/v1.json", "Development");
        c.RoutePrefix = "";
    });
}
else
{
    app.MapOpenApi("/openapi/{documentName}.json");

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/application/services/openapi/v1.json", "Production");
        c.RoutePrefix = "";
    });
}

app.UseAuthorization();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors("AllowAll");

app.MapControllers();

app.Run();

