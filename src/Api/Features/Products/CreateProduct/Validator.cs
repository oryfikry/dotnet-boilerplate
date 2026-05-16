using FluentValidation;

namespace Api.Features.Products.CreateProduct;

internal sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Sku)
            .NotEmpty()
            .Matches("^[A-Z0-9-]{3,32}$")
            .WithMessage("SKU must be 3-32 chars: uppercase letters, digits, or hyphens.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0);
    }
}
