namespace SingleStepViewer.Configuration;

public class AdminUserOptions
{
    public const string SectionName = "AdminUser";

    public string Username { get; set; } = "admin";
    public string Email { get; set; } = "admin@singlestep.local";
    public string Password { get; set; } = "Admin123!";
}
