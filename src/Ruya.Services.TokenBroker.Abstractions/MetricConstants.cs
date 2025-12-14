namespace Ruya.Services.TokenBroker;

public static class MetricConstants
{
    public const string MeterName = "Ruya.TokenBroker";
    public const string ClientMeterName = "Ruya.TokenBroker.Client";

    // Service Counters
    public const string TokensCreated = "token_service_tokens_created_total";
    public const string TokensExchanged = "token_service_tokens_exchanged_total";
    public const string TokenValidations = "token_service_token_validations_total";
    public const string TokenValidationFailures = "token_service_token_validation_failures_total";
    public const string ApiKeyValidations = "token_service_api_key_validations_total";
    public const string ApiKeyValidationFailures = "token_service_api_key_validation_failures_total";
    public const string ServiceRegistrations = "token_service_service_registrations_total";
    public const string ServiceRemovals = "token_service_service_removals_total";

    // Client Counters
    public const string ClientRequests = "token_client_requests_total";
    public const string ClientRequestFailures = "token_client_request_failures_total";
    public const string ClientCacheHits = "token_client_cache_hits_total";
    public const string ClientExchanges = "token_client_exchanges_total";
    public const string ClientExchangeFailures = "token_client_exchange_failures_total";

    // Histograms
    public const string TokenCreationDuration = "token_service_token_creation_duration_seconds";
    public const string ClientRequestDuration = "token_client_request_duration_seconds";
}
