namespace ProtoTest.OpenApi.Internal;

using ProtoTest.Core;
using ProtoTest.OpenApi.Internal.Model;

/// <summary>
/// Builds the hierarchical coverage items from the loaded spec and the ledger's hit counts: one
/// endpoint row per spec operation, one response row per documented status, and one row per schema
/// property, so the report enumerates the contract even when nothing was hit.
/// </summary>
internal static class OpenApiReportBuilder
{
    public static IReadOnlyList<ProtoReportItem> Build(
        OpenApiSpec document,
        OpenApiCoverageLedger ledger,
        string targetName,
        string category)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ledger);
        var reportItems = new List<ProtoReportItem>();
        foreach (var (pathKey, pathItem) in document.Paths)
        {
            foreach (var (method, operation) in pathItem.Operations)
            {
                var totalEndpointHits = ledger.EndpointHits(method, pathKey);
                var childItems = new List<ProtoReportItem>();

                foreach (var (responseKey, response) in operation.Responses)
                {
                    var responseHits = ledger.ResponseHits(method, pathKey, responseKey);
                    var propertyItems = OpenApiSchemaExtractor
                        .ExtractResponseProperties(response)
                        .Select(propertyPath =>
                        {
                            var propertyHits = ledger.PropertyHits(method, pathKey, responseKey, propertyPath);
                            return new ProtoReportItem(
                                TargetName: targetName,
                                Category: "OpenAPI Property",
                                Identifier: propertyPath,
                                Kind: ProtoReportItemKinds.Coverage,
                                Status: propertyHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                                Count: propertyHits,
                                IsCovered: propertyHits > 0,
                                DisplayName: FormatPropertyPath(propertyPath),
                                DisplayGroup: "Property");
                        })
                        .ToArray();

                    childItems.Add(new ProtoReportItem(
                        TargetName: targetName,
                        Category: "OpenAPI Response",
                        Identifier: responseKey,
                        Kind: ProtoReportItemKinds.Coverage,
                        Status: responseHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                        Count: responseHits,
                        IsCovered: responseHits > 0,
                        Children: propertyItems,
                        DisplayName: string.Equals(responseKey, "default", StringComparison.OrdinalIgnoreCase)
                            ? "Default response"
                            : $"{responseKey} response",
                        DisplayGroup: "Response"));
                }

                reportItems.Add(new ProtoReportItem(
                    TargetName: targetName,
                    Category: category,
                    Identifier: $"{method} {pathKey}",
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: totalEndpointHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                    Count: totalEndpointHits,
                    IsCovered: totalEndpointHits > 0,
                    Children: childItems));
            }
        }

        return reportItems;
    }

    private static string FormatPropertyPath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed == "$") return "Response body";
        if (trimmed.StartsWith("$.", StringComparison.Ordinal)) trimmed = trimmed[2..];
        trimmed = trimmed.Replace("[]", ".item", StringComparison.Ordinal);
        return string.Join(" › ", trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries));
    }
}
