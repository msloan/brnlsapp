using StackExchange.Redis;
using System.Text.Json; // Added for JsonSerializer in EventsController

var builder = WebApplication.CreateBuilder(args);

// Configure Redis Connection
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379")
);

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Optional: Add Swagger/OpenAPI if needed
    // app.UseSwagger();
    // app.UseSwaggerUI();
}

// app.UseHttpsRedirection(); // Comment out if running locally without HTTPS certificate setup

app.UseAuthorization();

// Enable serving static files from wwwroot (for index.html)
app.UseDefaultFiles(); // Enables default file mapping (like index.html)
app.UseStaticFiles(); // Serves files from wwwroot

app.MapControllers();

app.Run();
