using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace LostAndFoundApp.Models
{
    /// <summary>
    /// Validation attribute that ensures a DateTime value is not in the future.
    /// Used on date fields where a future date is logically invalid (e.g., DateFound).
    /// Implements IClientModelValidator so jQuery Validate can enforce this rule
    /// client-side without a round-trip to the server.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class NotFutureDateAttribute : ValidationAttribute, IClientModelValidator
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is DateTime dateValue)
            {
                if (dateValue.Date > DateTime.Today)
                {
                    return new ValidationResult(
                        ErrorMessage ?? $"{validationContext.DisplayName} cannot be in the future.");
                }
            }

            return ValidationResult.Success;
        }

        /// <summary>
        /// Adds data-val-* attributes to the rendered HTML input so jQuery Validate
        /// can enforce the "not in the future" rule on the client side.
        /// </summary>
        public void AddValidation(ClientModelValidationContext context)
        {
            if (!context.Attributes.ContainsKey("data-val"))
                context.Attributes["data-val"] = "true";

            var errorMessage = ErrorMessage ?? $"{context.ModelMetadata.GetDisplayName()} cannot be in the future.";
            context.Attributes["data-val-notfuturedate"] = errorMessage;
        }
    }
}
