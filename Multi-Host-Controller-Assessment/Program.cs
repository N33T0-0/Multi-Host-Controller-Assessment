using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Configuration;
using Multi_Host_Controller_Assessment.Dashboard;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Services;
using Multi_Host_Controller_Assessment.Deployments;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Configure Razor Pages for the dashboard
builder.Services.AddRazorPages(options =>
    options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute()));

// Bind and validate dashboard configuration
builder.Services.Configure<DashboardOptions>(
    builder.Configuration.GetSection(DashboardOptions.SectionName));

// Validate DashboardOptions at startup
var dashboardOptions = builder.Configuration
    .GetSection(DashboardOptions.SectionName)
    .Get<DashboardOptions>() ?? new DashboardOptions();
dashboardOptions.Validate();

// Configure persistence
var connectionString = builder.Configuration.GetConnectionString("LocalDatabase");
builder.Services.AddDbContext<MasterHostDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString))
    {
        options.UseSqlServer(connectionString);
    }
});

// Configure application services
builder.Services.AddScoped<IClientHostService, ClientHostService>();
builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddScoped<IDeploymentService, DeploymentService>();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

// Partial declaration for WebApplicationFactory in integration tests
public partial class Program { }
