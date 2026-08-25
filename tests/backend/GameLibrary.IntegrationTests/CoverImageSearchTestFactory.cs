using GameLibrary.Core.CoverImages;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GameLibrary.IntegrationTests;

public sealed class CoverImageSearchTestFactory : WebApplicationFactory<Program>
{
    private readonly bool _replaceProvider;

    public CoverImageSearchTestFactory() : this(replaceProvider: true)
    {
    }

    private CoverImageSearchTestFactory(bool replaceProvider)
    {
        _replaceProvider = replaceProvider;
        Provider = new FakeCoverImageSearchClient();
    }

    public static CoverImageSearchTestFactory MissingProviderConfiguration() => new(replaceProvider: false);

    public FakeCoverImageSearchClient Provider { get; }

    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Test")
        ?? throw new InvalidOperationException(
            "ConnectionStrings__Test must be configured to a real PostgreSQL database to run these integration tests.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);

        if (!_replaceProvider)
        {
            builder.UseSetting("CoverImageSearch:Provider", string.Empty);
        }

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Configuration = null!;
                    options.MetadataAddress = null!;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestTokens.Issuer,
                        ValidateAudience = true,
                        ValidAudience = TestTokens.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(TestTokens.SigningKey),
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    };
                });

            if (_replaceProvider)
            {
                services.RemoveAll<ICoverImageSearchClient>();
                services.AddSingleton<ICoverImageSearchClient>(Provider);
            }
        });
    }
}

public sealed class FakeCoverImageSearchClient : ICoverImageSearchClient
{
    private IReadOnlyList<CoverImageCandidate> _candidates = [];
    private CoverImageSearchUnavailableException? _exception;

    public List<CoverImageSearchQuery> Queries { get; } = [];

    public void Reset()
    {
        _candidates = [];
        _exception = null;
        Queries.Clear();
    }

    public void Return(IReadOnlyList<CoverImageCandidate> candidates) => _candidates = candidates;

    public void Throw(CoverImageSearchUnavailableException exception) => _exception = exception;

    public Task<IReadOnlyList<CoverImageCandidate>> SearchAsync(CoverImageSearchQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        if (_exception is not null)
        {
            return Task.FromException<IReadOnlyList<CoverImageCandidate>>(_exception);
        }

        return Task.FromResult(_candidates);
    }
}
