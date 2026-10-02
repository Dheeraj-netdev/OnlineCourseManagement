using System.Text;
using CourseService.Configuration;
using CourseService.Data;
using CourseService.Documentation;
using CourseService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace CourseService;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(provider =>
            ServiceSettings.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddDbContext<CourseDbContext>(options => options.UseInMemoryDatabase(
            builder.Configuration["Database:Name"] ?? "CourseService"));
        builder.Services.AddSingleton<CourseMutationGate>();
        builder.Services.AddScoped<CourseManagementService>();
        builder.Services.AddHttpClient<IUserDirectoryClient, UserDirectoryClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<ServiceSettings>();
            client.BaseAddress = settings.UserServiceBaseUrl;
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add("X-Service-Key", settings.ApiKey);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ServiceSettings>((options, settings) =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = settings.JwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSigningKey)),
                RequireSignedTokens = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id)
                        || id == Guid.Empty
                        || context.Principal?.FindFirst("role")?.Value is not ("Student" or "Instructor"))
                    {
                        context.Fail("The access token contains invalid identity claims.");
                    }
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Course Service",
                Version = "v1",
                Description = "Courses and enrollments. Course reads are limited to the caller's enrollments."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the access token issued by User Service login."
            });
            options.OperationFilter<EndpointSecurityOperationFilter>();
        });

        var app = builder.Build();
        // Resolve configuration before accepting requests, including in production.
        _ = app.Services.GetRequiredService<ServiceSettings>();
        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "CourseService" })).AllowAnonymous();
        app.Run();
    }
}
