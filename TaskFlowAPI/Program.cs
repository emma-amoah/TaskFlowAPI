using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security;
using System.Text;
using TaskFlowAPI.Contexts;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Middleware;
using TaskFlowAPI.Models;
using TaskFlowAPI.Services;
using TaskFlowAPI.Services.Email;

var builder = WebApplication.CreateBuilder(args);

var localDevelopmentCorsPolicy = "_localDevelopmentCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: localDevelopmentCorsPolicy,
        policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173") // Allow common local frontend development ports
                  .AllowAnyMethod()  // Allow GET, POST, PUT, DELETE, etc.
                  .AllowAnyHeader()  // Allow standard headers like Authorization, Content-Type, etc.
                  .AllowCredentials(); // Crucial if you choose to store refresh tokens in secure HttpOnly cookies later
        });
});

// Register AutoMapper and scan the assembly for all classes inheriting from 'Profile'
builder.Services.AddAutoMapper(cfg => cfg.AddMaps(typeof(Program).Assembly));


// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        var securityScheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Name = "Authorization",
            In = ParameterLocation.Header,
            Scheme = JwtBearerDefaults.AuthenticationScheme, // "Bearer"
            BearerFormat = "JWT",
            Description = "Enter your raw JWT access token here."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = securityScheme;

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = new List<string>()
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(requirement);

        return Task.CompletedTask;
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddHttpContextAccessor();

// REGISTER THE CRYPTOGRAPHIC TOKEN SERVICE ENGINE
builder.Services.AddScoped<TokenService>();

// Registering DbContext class with SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));


builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    // Security rules configuration for user authentication and password policies
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;

    // Native Identity Account Controls
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5; // Lock account after 5 bad attempts
    options.Lockout.AllowedForNewUsers = true;

    options.SignIn.RequireConfirmedEmail = true; // False for development; set to true in production for email verification
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders()
.AddSignInManager<ApplicationSignInManager>();

// Grab configurations from your uncommitted settings files safely
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretString = jwtSettings["Secret"] ?? throw new InvalidOperationException("Critical Runtime Failure: JWT Secret is not configured inside appsettings.");
var secretKey = Encoding.UTF8.GetBytes(secretString);   

builder.Services.AddAuthentication(options =>
{
    // Make JWT Bearer the default authentication protocol across all API endpoints
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in your Production configuration environment
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(secretKey),

        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],

        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],

        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero // Removes the default 5-minute leeway window for strict expiry handling
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine($"JWT FAILED: {ctx.Exception.GetType().Name} - {ctx.Exception.Message}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Define a policy-based rule that demands a user hold either the Admin or Manager role
    options.AddPolicy(AppPolicies.RequireManagementLevel, policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Manager));
});

builder.Services.AddTransient<IEmailSender, MockEmailSender>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TaskFlow API Ecosystem - v1");

        options.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();

app.UseCors(localDevelopmentCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        // Execute the asynchronous seeder engine task synchronously on startup thread configurations
        await DbInitializer.SeedRolesAndAdminAsync(roleManager, userManager, configuration, loggerFactory);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An initialization error occurred while seeding core identity authorization roles.");
    }
}

app.Run();
