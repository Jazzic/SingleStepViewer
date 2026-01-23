namespace SingleStepViewer.Services.Interfaces;

public interface IConfigurationService
{
    Task<T?> GetConfigurationAsync<T>(string sectionName) where T : class;
    Task SaveConfigurationAsync<T>(string sectionName, T configuration) where T : class;
}
