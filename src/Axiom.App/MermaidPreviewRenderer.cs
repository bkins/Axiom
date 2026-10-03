using Avalonia.Platform;
using System.Text;
using System.Text.Json;

namespace Axiom.App;

internal static class MermaidPreviewRenderer
{
    private static readonly Lazy<string> Runtime = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Mermaid/mermaid.js"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    public static string BuildScript(string token, bool dark) =>
        "if (!window.axiomMermaid) { " + Runtime.Value + " }\n" + $$"""
        (() => {
          const token = {{JsonSerializer.Serialize(token)}};
          if (document.documentElement.dataset.linkToken !== token) return false;
          const current = () => document.documentElement.dataset.linkToken === token;
          const mermaid = window.axiomMermaid;
          mermaid.initialize({startOnLoad:false, securityLevel:'strict', suppressErrorRendering:true,
            theme:{{JsonSerializer.Serialize(dark ? "dark" : "default")}}, maxTextSize:20000, maxEdges:300,
            htmlLabels:false, flowchart:{htmlLabels:false},
            secure:['secure','securityLevel','startOnLoad','maxTextSize','maxEdges','suppressErrorRendering','htmlLabels','flowchart','theme','themeCSS']});
          const blocks = [...document.querySelectorAll('pre > code')].filter(code => [...code.classList].some(name => name.toLowerCase() === 'language-mermaid'));
          const anchors = [...document.querySelectorAll('.source-block')];
          const anchor = anchors.find(block => block.getBoundingClientRect().bottom > 12);
          const fraction = anchor ? Math.max(0, Math.min(1, (12 - anchor.getBoundingClientRect().top) / Math.max(1, anchor.offsetHeight))) : 0;
          const input = new AbortController(); let interacted = false;
          for (const name of ['wheel','touchstart','pointerdown','keydown']) window.addEventListener(name, () => { interacted = true; }, {passive:true, signal:input.signal});
          window.axiomMermaidStatus = {token, total:blocks.length, completed:0, errors:0};
          const fail = (pre, message) => {
            const notice = document.createElement('p');
            notice.className = 'mermaid-error'; notice.setAttribute('role','status');
            notice.textContent = 'Diagram unavailable: ' + message + ' Mermaid source is shown below.';
            pre.before(notice);
            window.axiomMermaidStatus.errors++;
          };
          (async () => {
            try {
            for (let index = 0; index < blocks.length; index++) {
              if (!current()) return;
              const code = blocks[index], pre = code.parentElement, source = code.textContent;
              try {
                if (index >= 8) throw Error('Only eight diagrams are rendered per document.');
                if (source.length > 20000) throw Error('Diagram exceeds 20,000 characters.');
                if (/%%\s*\{|^\s*---/m.test(source)) throw Error('Embedded configuration is disabled.');
                const id = 'axiomDiagram' + token + index;
                let timer;
                const result = await Promise.race([mermaid.render(id, source), new Promise((_, reject) => { timer = setTimeout(() => reject(Error('Rendering exceeded ten seconds.')), 10000); })]).finally(() => clearTimeout(timer));
                if (!current() || !pre.isConnected) return;
                const parsed = new DOMParser().parseFromString(result.svg, 'image/svg+xml');
                const svg = parsed.documentElement;
                if (svg.localName !== 'svg' || parsed.querySelector('parsererror,script,foreignObject,image,iframe,object,embed'))
                  throw Error('Diagram contains unsupported embedded content.');
                for (const node of [svg, ...svg.querySelectorAll('*')]) {
                  for (const attribute of [...node.attributes]) {
                    const name = attribute.localName.toLowerCase(), value = attribute.value;
                    if (name.startsWith('on') || ((name === 'href' || name === 'src') && !value.startsWith('#')) ||
                        /url\s*\(\s*['"]?(?!#)[^)]/i.test(value)) node.removeAttributeNode(attribute);
                  }
                  if (node.localName === 'style' && /@import|url\s*\(\s*['"]?(?!#)[^)]/i.test(node.textContent)) node.remove();
                }
                const figure = document.createElement('figure'); figure.className = 'mermaid-diagram';
                figure.setAttribute('aria-label', 'Mermaid diagram');
                figure.append(document.importNode(svg, true));
                const details = document.createElement('details'), summary = document.createElement('summary');
                summary.textContent = 'Diagram source'; details.append(summary);
                pre.replaceWith(figure); details.append(pre); figure.append(details);
              } catch (error) {
                if (!current() || !pre.isConnected) return;
                fail(pre, String(error.message || 'Check diagram syntax.').slice(0,240));
              }
              if (current()) window.axiomMermaidStatus.completed++;
            }
            if (current() && anchor?.isConnected && !interacted) {
              if (window.__axiomReading) window.__axiomReading.ignoreUntil = performance.now() + 350;
              window.scrollTo(0, window.scrollY + anchor.getBoundingClientRect().top + fraction * anchor.offsetHeight - 12);
            }
            } finally { input.abort(); }
          })();
          return true;
        })()
        """;
}
