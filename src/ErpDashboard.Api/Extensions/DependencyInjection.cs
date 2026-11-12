using ErpDashboard.Api.Features.Inventory.Materials;

namespace ErpDashboard.Api.Extensions;

public static class DependencyInjection
{
    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers(options => { options.ReturnHttpNotAcceptable = true; })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.AllowInputFormatterExceptionMessages = false;
            });

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
        return builder;
    }

    public static WebApplicationBuilder AddAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<User>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        var tokenStamp = principal?.FindFirstValue("security_stamp");

                        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                            || string.IsNullOrWhiteSpace(tokenStamp))
                        {
                            context.Fail("Invalid or missing security stamp.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var user = await db.Users
                            .AsNoTracking()
                            .Where(u => u.Id == userId)
                            .Select(u => new { u.SecurityStamp,u.ShowroomId , u.Role.Permissions })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                        if (user is null || !string.Equals(tokenStamp, user.SecurityStamp, StringComparison.Ordinal))
                        {
                            context.Fail("Access token has been revoked.");
                            return;
                        }

                        context.HttpContext.Items[PermissionHandler.ItemsKey] =
                            new HashSet<string>(user.Permissions, StringComparer.Ordinal);
                        
                        if (user.ShowroomId is { } showroomId)
                            context.HttpContext.Items[CurrentShowroom.ItemsKey] = showroomId;
                    }
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return builder;
    }

    public static WebApplicationBuilder AddApiDocs(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddServer(new OpenApiServer
            {
                Url = builder.Configuration["ApiSettings:BaseUrl"]
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token."
            });

            options.OperationFilter<AuthorizationOperationFilter>();
        });

        return builder;
    }

    public static WebApplicationBuilder AddLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter()));
        return builder;
    }

    public static WebApplicationBuilder AddErrorHandling(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                var pd = ctx.ProblemDetails;

                // pd.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                pd.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier;
                if (pd is HttpValidationProblemDetails validation)
                {
                    validation.Errors = validation.Errors
                        .GroupBy(e => ToCamelCaseKey(e.Key))
                        .ToDictionary(g => g.Key, g => g.SelectMany(e => e.Value).ToArray());
                }

                pd.Detail ??= pd.Status switch
                {
                    400 => "One or more validation errors occurred.",
                    401 => "Authentication is required.",
                    403 => "You do not have permission to perform this action.",
                    404 => "The requested resource was not found.",
                    _ => pd.Title
                };
            };
        });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        return builder;
    }

    public static WebApplicationBuilder AddFeatureServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
        builder.Services.Configure<FinanceSettings>(builder.Configuration.GetSection(FinanceSettings.SectionName));
        builder.Services.Configure<CompanySettings>(builder.Configuration.GetSection(CompanySettings.SectionName));
        builder.Services.Configure<SalesSettings>(builder.Configuration.GetSection(SalesSettings.SectionName));
        builder.Services.AddSingleton<IInvoicePdfGenerator, InvoicePdfGenerator>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IRoleService, RoleService>();
        builder.Services.AddScoped<IShowroomService, ShowroomService>();
        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IMaterialService, MaterialService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<IEmployeeService, EmployeeService>();
        builder.Services.AddScoped<IDepartmentService, DepartmentService>();
        builder.Services.AddScoped<ISupplierService, SupplierService>();
        builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddScoped<IProfileService, ProfileService>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserPermissions, CurrentUserPermissions>();
        builder.Services.AddScoped<IStockService, StockService>();
        builder.Services.AddScoped<IWarehouseService, WarehouseService>();
        builder.Services.AddScoped<IWarehouseScope, WarehouseScope>();
        builder.Services.AddScoped<ICurrentShowroom, CurrentShowroom>();
        builder.Services.AddScoped<IStockReconciliationService, StockReconciliationService>();
        builder.Services.AddScoped<IStockTransferService, StockTransferService>();

        return builder;
    }

    public static WebApplicationBuilder AddCors(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("ERPPolicy", policy =>
            {
                var origins = builder.Configuration
                    .GetSection("Cors:AllowedOrigins")
                    .Get<string[]>() ?? [];

                policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
            });
        });
        return builder;
    }

    public static WebApplicationBuilder AddHealth(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                "database",
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(3));

        return builder;
    }

    private static string ToCamelCaseKey(string key) =>
        string.Join('.', key.Split('.').Select(part => JsonNamingPolicy.CamelCase.ConvertName(part)));
}