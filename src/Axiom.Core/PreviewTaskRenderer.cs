using System.Globalization;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace Axiom.Core;

internal sealed class PreviewTaskRenderer(DocumentStructure document, string token) : HtmlObjectRenderer<TaskList>
{
    private readonly IReadOnlyDictionary<int, PreviewTaskMarker> _markers = PreviewTaskToggle.Read(document).ToDictionary(marker => marker.Offset);
    protected override void Write(HtmlRenderer renderer, TaskList task)
    {
        if (!_markers.TryGetValue(task.Span.Start + 1, out var marker)) { new HtmlTaskListRenderer().Write(renderer, task); return; }
        var state = marker.Checked ? "true" : "false";
        var title = marker.Checked ? "Mark incomplete" : "Mark complete";
        renderer.Write("<a class=\"task-toggle\" role=\"checkbox\" aria-checked=\"").Write(state)
            .Write("\" aria-label=\"").Write(title).Write("\" title=\"").Write(title)
            .Write("\" data-task-offset=\"").Write(marker.Offset.ToString(CultureInfo.InvariantCulture))
            .Write("\" href=\"axiom-task://toggle/").Write(System.Net.WebUtility.HtmlEncode(token)).Write("/")
            .Write(marker.Offset.ToString(CultureInfo.InvariantCulture)).Write("/").Write(marker.Checked ? "1" : "0")
            .Write("\">").Write(marker.Checked ? "&#10003;" : "&#160;").Write("</a>");
    }
}
