using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UserService.Authentication;
using UserService.Data;
using UserService.Models;
using UserService.Services;

namespace UserService;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IConfiguration>().GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
            options.Validate();
            return options;
        });
        builder.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IConfiguration>().GetSection("ServiceAuthentication").Get<ServiceAuthenticationOptions>()
                ?? new ServiceAuthenticationOptions();
            options.Validate();
            return options;
        });
        builder.Services.AddDbContext<UserDbContext>(options => options.UseInMemoryDatabase(
            builder.Configuration["Database:Name"] ?? "UserService"));
        builder.Services.AddSingleton<UserWriteLock>();
        builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddScoped<UserManager>();
        builder.Services.AddSingleton<TokenIssuer>();
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Online Course Management — User Service", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Use the accessToken returned by POST /api/auth/login."
            });
            options.AddSecurityDefinition(ServiceApiKeyDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ServiceApiKeyDefaults.HeaderName,
                Description = "Service credential for the internal user directory."
            });
            options.OperationFilter<EndpointSecurityOperationFilter>();
        });
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ServiceApiKeyHandler>(
                ServiceApiKeyDefaults.AuthenticationScheme, _ => { });
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtOptions>((options, jwtOptions) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var subject = context.Principal?.FindFirst("sub")?.Value;
                        if (!Guid.TryParse(subject, out var id) || id == Guid.Empty
                            || context.Principal?.FindFirst("role")?.Value is not (UserRoles.Student or UserRoles.Instructor))
                        {
                            context.Fail("The access token contains invalid identity claims.");
                        }
                        return Task.CompletedTask;
                    }
                };
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        _ = app.Services.GetRequiredService<JwtOptions>();
        _ = app.Services.GetRequiredService<ServiceAuthenticationOptions>();
        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health");
        app.Run();
    }
}
