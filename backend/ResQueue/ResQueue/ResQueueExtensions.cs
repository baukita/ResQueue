using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using ResQueue.Endpoints;
using ResQueue.Factories;
using ResQueue.Features.Messages.DeleteMessages;
using ResQueue.Features.Messages.GetMessages;
using ResQueue.Features.Messages.GetSingleMessage;
using ResQueue.Features.Messages.PurgeQueue;
using ResQueue.Features.Messages.RequeueMessages;
using ResQueue.Features.Messages.RequeueSpecificMessages;
using ResQueue.Features.Subscriptions.GetSubscriptions;
using ResQueue.Middleware;
using ResQueue.Providers.DbConnectionProvider;

namespace ResQueue;

public static class ResQueueExtensions
{
    public static IServiceCollection AddResQueue(this IServiceCollection services,
        Action<ResQueueOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // register the transformer types declared in the options
        var options = new ResQueueOptions();
        configureOptions(options);
        foreach (var transformerType in options.TransformerTypes)
        {
            services.AddScoped(transformerType);
        }

        services.AddSingleton<IDatabaseConnectionFactory, DatabaseConnectionFactory>();
        services.AddSingleton<IDbConnectionProvider, DbConnectionProvider>();

        services.AddTransient<IRequeueMessagesFeature, RequeueMessagesFeature>();
        services.AddTransient<IRequeueSpecificMessagesFeature, RequeueSpecificMessagesFeature>();
        services.AddTransient<IDeleteMessagesFeature, DeleteMessagesFeature>();
        services.AddTransient<IGetMessagesFeature, GetMessagesFeature>();
        services.AddTransient<IGetSingleMessageFeature, GetSingleMessageFeature>();
        services.AddTransient<IPurgeQueueFeature, PurgeQueueFeature>();
        services.AddTransient<IGetSubscriptionsFeature, GetSubscriptionsFeature>();

        return services;
    }

    public static IApplicationBuilder UseResQueue(this WebApplication app, string prefix = "resqueue",
        Action<RouteGroupBuilder>? configureApi = null, string? pathBase = null)
    {
        app.UseMiddleware<ResQueueExceptionMiddleware>();

        var fullPrefix = string.IsNullOrEmpty(pathBase)
            ? prefix
            : $"{pathBase.Trim('/')}/{prefix}";

        app.MapGet("resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/config.js", () => Results.Content(
            $$"""
              globalThis.resqueueConfig = {
                  prefix: "{{fullPrefix}}"
              }
              """
            , "text/javascript"
        )).ExcludeFromDescription();

        var rewriteOptions = new RewriteOptions()
            .AddRedirect($"^{prefix}$", $"{prefix}/")           // Base: add trailing slash
            .AddRedirect($"^{prefix}/(.+)/$", $"{prefix}/$1");  // Sub-routes: remove trailing slash

        (string Pattern, string Replacement)[] staticRewrites =
        [
            ("/assets/(.*)", "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/assets/$1"),
            ("/config.js", "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/config.js"),
            ("/favicon.ico", "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/favicon.ico"),
            ("/api/(.*)", "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/api/$1"),
        ];

        rewriteOptions = staticRewrites.Aggregate(rewriteOptions, (opts, rewrite) => opts.AddRewrite(
            regex: $"^{prefix}{rewrite.Pattern}$",
            replacement: rewrite.Replacement,
            skipRemainingRules: true
        ));

        string[] frontendRoutes =
        [
            "/",
            "/overview",
            "/topics",
            "/queues",
            "/recurring-jobs",
            "/queues/[^/]+",
        ];

        rewriteOptions = frontendRoutes.Aggregate(rewriteOptions, (options, route) => options.AddRewrite(
            regex: $"^{prefix}{route}$",
            replacement: "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/index.html",
            skipRemainingRules: true
        ));

        app.UseRewriter(rewriteOptions);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider =
                new PhysicalFileProvider(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resqueue-wwwroot")),
            RequestPath = "/resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc"
        });

        var api = app.MapGroup("resqueue-4e8efb80-6aae-496f-b8bf-611b63e725bc/api")
            .ExcludeFromDescription();

        configureApi?.Invoke(api);
        api.MapAuthEndpoints();
        api.MapQueueEndpoints();
        api.MapMessageEndpoints();
        api.MapJobsEndpoints();
        api.MapSubscriptionsEndpoints();

        return app;
    }
}