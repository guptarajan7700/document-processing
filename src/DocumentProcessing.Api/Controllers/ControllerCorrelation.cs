using DocumentProcessing.Api.Hosting;

namespace DocumentProcessing.Api.Controllers;

internal static class ControllerCorrelation
{
    public static Guid Read(HttpContext context) =>
        context.Items.TryGetValue(CorrelationMiddleware.ItemKey, out var value) && value is Guid id
            ? id
            : Guid.NewGuid();
}
