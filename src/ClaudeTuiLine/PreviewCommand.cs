using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

// SPEC-V2-FRAMEWORK.md §9.6/§9.3.4: rows[].text is deliberately plain (no markup) so a caller
// parsing rows needn't strip it themselves. rows[].width always describes rows[].text — a field
// that can disagree with the text next to it is worse than a missing one. A content row's
// pre-border width (what the layout measured before a panel was wrapped around it) is reported
// separately as contentWidth and is absent — not null, omitted — on border lines, which have no
// such number.
public sealed record PreviewRowJson(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("contentWidth"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ContentWidth = null);

// §9.8: the same notes a dropped pane or a truncated segment would otherwise only surface on
// stderr in the bare form.
public sealed record PreviewNoteJson(
    [property: JsonPropertyName("message")] string Message);

// §9.6: columns is the resolved requested width; usableColumns is columns minus chromeReserve —
// the same subtraction SurfaceLayout.ComputeWidth performs for the real render path.
public sealed record PreviewResultJson(
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("usableColumns")] int UsableColumns,
    [property: JsonPropertyName("rows")] IReadOnlyList<PreviewRowJson> Rows,
    [property: JsonPropertyName("notes")] IReadOnlyList<PreviewNoteJson> Notes);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(PreviewResultJson))]
public partial class PreviewJsonContext : JsonSerializerContext
{
}
