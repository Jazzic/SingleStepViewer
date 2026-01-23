using System.Text.Json;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;

    public ConfigurationService(ILogger<ConfigurationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configFilePath = "appsettings.json";
    }

    public async Task<T?> GetConfigurationAsync<T>(string sectionName) where T : class
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogWarning("Configuration file not found: {FilePath}", _configFilePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(_configFilePath);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(sectionName, out var section))
            {
                var sectionJson = section.GetRawText();
                return JsonSerializer.Deserialize<T>(sectionJson);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading configuration section {SectionName}", sectionName);
            return null;
        }
    }

    public async Task SaveConfigurationAsync<T>(string sectionName, T configuration) where T : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath);
            var doc = JsonDocument.Parse(json);
            var root = new Dictionary<string, object?>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name == sectionName)
                {
                    root[property.Name] = configuration;
                }
                else
                {
                    root[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var newJson = JsonSerializer.Serialize(root, options);
            await File.WriteAllTextAsync(_configFilePath, newJson);

            _logger.LogInformation("Saved configuration section {SectionName}", sectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration section {SectionName}", sectionName);
            throw;
        }
    }
}
