using LazyPos.Api.Data.Constants;
using URF.Core.EF.Trackable.Entities.Message;
using LazyPos.Api.Helpers;
using LazyPos.Api.Middlewares;
using LazyPos.Api.Service.Caching;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;
using URF.Core.Abstractions;
using URF.Core.Abstractions.Trackable;
using URF.Core.EF;
using URF.Core.EF.Trackable;
using URF.Core.EF.Trackable.Entities;
using URF.Core.EF.Trackable.Models;
using URF.Core.Helper.Extensions;
using URF.Core.Services.Contract;
using URF.Core.Services.Hubs;
using URF.Core.Services.Implement;

var builder = WebApplication.CreateBuilder(args);

// configuration
ConfigurationManager configuration = builder.Configuration;

// appSettings
var appSettingsSection = configuration.GetSection("AppSettings");
var appSettings = appSettingsSection.Get<AppSettings>();
builder.Services.Configure<AppSettings>(appSettingsSection);

// startup
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
    options.MaxRequestBodySize = long.MaxValue;
});
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
    options.Limits.MaxRequestBodySize = long.MaxValue;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
});
builder.Services
    .AddSignalR(options =>
    {
        options.EnableDetailedErrors = true;
        options.MaximumReceiveMessageSize = 102400000;
    })
    .AddNewtonsoftJsonProtocol(options =>
    {
        options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
        options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    });
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                })
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

// swagger
builder.Services.ConfigureSwaggerGen(options =>
{
    options.CustomSchemaIds(x => x.FullName);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// base
builder.Services.AddScoped<IMemoryCache, MemoryCache>();
builder.Services.AddScoped<ICacheBase, CacheBase>();
builder.Services.AddScoped<INotifyHub, NotifyHub>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// jwt authentication
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.TokenKey));
builder.Services.AddAuthentication()
    .AddCookie(c => c.SlidingExpiration = true)
    .AddJwtBearer(c =>
    {
        c.SaveToken = true;
        c.RequireHttpsMetadata = false;
        c.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            ValidIssuer = JwtConstant.Issuer,
            ValidAudience = JwtConstant.Audience,
        };
    });

// repository
builder.Services.AddScoped<IRepositoryX<User>, RepositoryX<User>>();
builder.Services.AddScoped<IRepositoryX<Role>, RepositoryX<Role>>();
builder.Services.AddScoped<IRepositoryX<Team>, RepositoryX<Team>>();
builder.Services.AddScoped<IRepositoryX<Audit>, RepositoryX<Audit>>();
builder.Services.AddScoped<IRepositoryX<Notify>, RepositoryX<Notify>>();
builder.Services.AddScoped<IRepositoryX<Language>, RepositoryX<Language>>();
builder.Services.AddScoped<IRepositoryX<UserRole>, RepositoryX<UserRole>>();
builder.Services.AddScoped<IRepositoryX<UserTeam>, RepositoryX<UserTeam>>();
builder.Services.AddScoped<IRepositoryX<Permission>, RepositoryX<Permission>>();
builder.Services.AddScoped<IRepositoryX<Department>, RepositoryX<Department>>();
builder.Services.AddScoped<IRepositoryX<LogActivity>, RepositoryX<LogActivity>>();
builder.Services.AddScoped<IRepositoryX<SmtpAccount>, RepositoryX<SmtpAccount>>();
builder.Services.AddScoped<IRepositoryX<UserActivity>, RepositoryX<UserActivity>>();
builder.Services.AddScoped<IRepositoryX<LogException>, RepositoryX<LogException>>();
builder.Services.AddScoped<IRepositoryX<EmailTemplate>, RepositoryX<EmailTemplate>>();
builder.Services.AddScoped<IRepositoryX<RequestFilter>, RepositoryX<RequestFilter>>();
builder.Services.AddScoped<IRepositoryX<LanguageDetail>, RepositoryX<LanguageDetail>>();
builder.Services.AddScoped<IRepositoryX<LinkPermission>, RepositoryX<LinkPermission>>();
builder.Services.AddScoped<IRepositoryX<UserPermission>, RepositoryX<UserPermission>>();
builder.Services.AddScoped<IRepositoryX<RolePermission>, RepositoryX<RolePermission>>();

// builder.Services
builder.Services.AddScoped<ITenantService, TenantService>();

// chat
builder.Services.AddScoped<IRepositoryX<Group>, RepositoryX<Group>>();
builder.Services.AddScoped<IRepositoryX<Message>, RepositoryX<Message>>();
builder.Services.AddScoped<IRepositoryX<UserGroup>, RepositoryX<UserGroup>>();

// config
builder.Services.Configure<TenantSettings>(configuration.GetSection(nameof(TenantSettings)));
StoreHelper.SchemaWebAdmin = appSettings.SchemaWebAdmin;
StoreHelper.SchemaApi = appSettings.SchemaApi;

// sentry
Sentry.SentrySdk.Init(appSettings.SentryDsn);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<LoggerMiddleware>();
app.UseHttpsRedirection();
app.MapControllers();

app.UseRouting();
app.UseCors(x => x
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetIsOriginAllowed(origin => true) // allow any origin
                .AllowCredentials()); // allow credentials
app.UseStaticFiles();
app.UseAuthorization();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = null; // unlimited I guess
    await next.Invoke();
});
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
StaticFileExtensions.UseStaticFiles(app);

app.Run();
