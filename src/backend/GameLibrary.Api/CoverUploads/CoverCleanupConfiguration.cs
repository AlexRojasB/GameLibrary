namespace GameLibrary.Api.CoverUploads;

public static class CoverCleanupConfiguration
{
    // Explicit no-managed-covers QA opt-out, including when QA hosts in Production.
    // Never infer this from uploads being disabled, an environment name or a DB host.
    public static bool Register(IServiceCollection services, IConfiguration configuration)
    {
        var value = configuration["PrivateQa:DisableCoverCleanup"];
        if (value is not null && !bool.TryParse(value, out _))
            throw new InvalidOperationException("PrivateQa:DisableCoverCleanup must be true or false.");

        var disabled = value is not null && bool.Parse(value);
        if (disabled)
        {
            if (configuration.GetValue<bool>("CoverUploads:Enabled")
                || !string.IsNullOrEmpty(configuration["CoverUploads:StorageBaseUrl"])
                || !string.IsNullOrEmpty(configuration["CoverUploads:StorageCredential"]))
                throw new InvalidOperationException("Private QA cleanup opt-out requires uploads disabled and no Storage base or credential.");

            // Operator must independently establish zero managed-cover rows/references.
            // Do not open the database or resolve a cleanup scope to make this decision.
            return true;
        }

        services.AddHostedService<CoverCleanupService>();
        return false;
    }
}
