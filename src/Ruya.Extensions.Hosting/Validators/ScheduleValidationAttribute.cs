using System;
using System.ComponentModel.DataAnnotations;
using Cronos;

namespace Ruya.Extensions.Hosting.Validators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class ScheduleValidationAttribute : ValidationAttribute
{
    public bool AllowEmpty { get; set; } = false;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var expression = value?.ToString();

        if (string.IsNullOrWhiteSpace(expression))
        {
            return AllowEmpty
                ? ValidationResult.Success
                : new ValidationResult("Schedule expression is required unless continuous mode is intended.");
        }

        try
        {
            CronExpression.Parse(expression);
            return ValidationResult.Success;
        }
        catch
        {
            return new ValidationResult(ErrorMessage ?? "Invalid cron expression.");
        }
    }
}
