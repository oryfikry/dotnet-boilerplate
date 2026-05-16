using FluentValidation;
using MediatR;

namespace Api.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs all <see cref="IValidator{T}"/> registered
/// for the incoming request before dispatching to its handler.
///
/// Per PRD v2.1 §6 directive #3, validation lives in dedicated
/// <c>FluentValidation</c> validators per slice, not inside handlers.
/// On failure this behavior throws <see cref="ValidationException"/>, which is
/// then translated to RFC 7807 ProblemDetails by
/// <see cref="Common.Exceptions.GlobalExceptionHandler"/> (see also §4.4).
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
