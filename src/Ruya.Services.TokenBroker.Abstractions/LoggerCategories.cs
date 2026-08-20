namespace Ruya.Services.TokenBroker;

public static class LoggerCategories
{
    // The concrete Api type lives in the downstream implementation package, so this abstractions package cannot
    // reference that symbol without reversing the dependency. Keep only that external category suffix literal.
    public const string Api =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.TokenBroker)}.Api";
}
