namespace GameLibrary.Core.Steam;

public class SteamIntegrationException : Exception
{
    public SteamIntegrationException(string message) : base(message)
    {
    }
}

public sealed class SteamIntegrationNotConfiguredException : SteamIntegrationException
{
    public SteamIntegrationNotConfiguredException() : base("Steam integration is not configured.")
    {
    }
}

public sealed class SteamImportNotConfiguredException : SteamIntegrationException
{
    public SteamImportNotConfiguredException() : base("Steam library import is unavailable because Steam Web API access is not configured.")
    {
    }
}

public sealed class SteamAccountNotLinkedException : SteamIntegrationException
{
    public SteamAccountNotLinkedException() : base("Link a Steam account before importing Steam games.")
    {
    }
}

public sealed class SteamAccountAlreadyLinkedException : SteamIntegrationException
{
    public SteamAccountAlreadyLinkedException() : base("Unlink the current Steam account before linking another account.")
    {
    }
}

public sealed class InvalidSteamImportRequestException : SteamIntegrationException
{
    public InvalidSteamImportRequestException(string message) : base(message)
    {
    }
}

public sealed class InvalidSteamOpenIdAssertionException : SteamIntegrationException
{
    public InvalidSteamOpenIdAssertionException() : base("Steam account could not be verified.")
    {
    }
}

public class SteamProviderException : SteamIntegrationException
{
    public SteamProviderException() : base("Unable to read your Steam library right now.")
    {
    }
}

public sealed class SteamProviderTimeoutException : SteamProviderException
{
    public SteamProviderTimeoutException() : base()
    {
    }
}
