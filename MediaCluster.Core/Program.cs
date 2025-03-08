using MediaCluster.Core;
using Serilog;
using System.Text.Json;
using MediaCluster.Common.Models.Configuration;

namespace MediaCluster.Host
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                // Setup configuration
                var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
                
                if (!File.Exists(configPath))
                {
                    // Create default configuration file
                    var defaultConfig = new AppConfig();
                    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, jsonOptions));
                    
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Default configuration file created at: {configPath}");
                    Console.WriteLine("Please update it with your RealDebrid API key before continuing.");
                    Console.ResetColor();
                    return 1;
                }
                
                Console.WriteLine("MediaCluster starting...");
                
                // Configure and build the host
                var builder = WebApplication.CreateBuilder(args);
                
                // Add configuration from config.json
                builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
                
                // Configure Serilog
                var logConfig = new LoggerConfiguration()
                    .ReadFrom.Configuration(builder.Configuration)
                    .MinimumLevel.Verbose()
                    .Enrich.FromLogContext()
                    .WriteTo.Async(a => a.Console());
                
                // Create log directory if it doesn't exist
                var logPath = builder.Configuration["Logging:LogPath"] ?? "logs";
                Directory.CreateDirectory(logPath);
                
                // Add file logging
                logConfig.WriteTo.Async(a => a.File(
                    Path.Combine(logPath, "mediacluster-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31));
                
                Log.Logger = logConfig.CreateLogger();
                builder.Host.UseSerilog();
                
                // Add services
                builder.Services.AddMediaCluster(builder.Configuration);
                
                // Add controllers for QBittorrent API
                builder.Services.AddControllers();
                
                // Configure endpoints
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
                
                // Build the application
                var app = builder.Build();
                
                // Configure the middleware pipeline
                app.UseSwagger();
                app.UseSwaggerUI();
                
                app.UseSerilogRequestLogging();
                app.UseHttpsRedirection();
                app.UseAuthorization();
                app.MapControllers();
                app.MapHealthChecks("/health");
                
                // Run the application
                await app.RunAsync();
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Application terminated unexpectedly: {ex.Message}");
                Console.ResetColor();
                
                Log.Fatal(ex, "Application terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
