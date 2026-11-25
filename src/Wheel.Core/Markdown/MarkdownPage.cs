using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wheel.Core.Markdown;

/// <summary>The reading-page libraries shipped in engines/markdown/ (marked, DOMPurify, highlight.js, GitHub's CSS).</summary>
public sealed record MarkdownAssets(
    string MarkedJs, string PurifyJs, string HighlightJs,
    string GithubCss, string HighlightLightCss, string HighlightDarkCss)
{
    public static string? FindFolder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "engines", "markdown");
        return File.Exists(Path.Combine(folder, "marked.js")) ? folder : null;
    }

    public static MarkdownAssets Load(string folder)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(folder, name));
        return new MarkdownAssets(
            Read("marked.js"), Read("purify.js"), Read("highlight.js"),
            Read("github-markdown.css"), Read("highlight-light.css"), Read("highlight-dark.css"));
    }

    private static MarkdownAssets? _shipped;

    /// <summary>The copy beside the app, loaded once. Null when it's missing.</summary>
    public static MarkdownAssets? Shipped()
    {
        if (_shipped is not null) return _shipped;
        var folder = FindFolder();
        return folder is null ? null : _shipped = Load(folder);
    }
}

/// <summary>
/// Builds one self-contained HTML page that shows a Markdown file the way GitHub does: GitHub's typography,
/// light and dark mode, contents sidebar, code colouring with copy buttons, tables, task lists, alerts,
/// and right-to-left paragraphs for Arabic. Works offline; the rendered HTML is sanitised before display.
/// </summary>
public static class MarkdownPage
{
    /// <summary>Larger files are refused: they aren't documents anyone reads in one page.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public static string ReadSource(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
            throw new InvalidDataException($"{info.Name} is over 5 MB, too large to show as a page.");
        return File.ReadAllText(path); // detects UTF-8/UTF-16 byte-order marks; UTF-8 otherwise
    }

    /// <param name="markdown">The file's text.</param>
    /// <param name="fileName">Shown in the title and the page header.</param>
    /// <param name="baseFolder">Folder that relative links and images resolve against; null for a page saved beside the file.</param>
    public static string Build(string markdown, string fileName, string? baseFolder, MarkdownAssets assets)
    {
        var title = WebUtility.HtmlEncode(fileName);
        // JSON with <, >, & and quotes escaped as \uXXXX: the text can't close the script element it sits in.
        var source = JsonSerializer.Serialize(markdown);
        var baseTag = baseFolder is null ? "" : $"<base href=\"{WebUtility.HtmlEncode(FolderUri(baseFolder))}\">";

        var sb = new StringBuilder(markdown.Length * 2 + 300_000);
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        // No network requests except images (README badges); no plug-ins, no frames, no form posts.
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src * data: file: blob:; media-src * file:; ")
          .Append("style-src 'unsafe-inline'; script-src 'unsafe-inline'; font-src data:; form-action 'none'\">\n");
        sb.Append(baseTag).Append('\n');
        sb.Append("<title>").Append(title).Append("</title>\n");
        sb.Append("<style>\n").Append(assets.GithubCss).Append("\n</style>\n");
        sb.Append("<style media=\"(prefers-color-scheme: light)\">\n").Append(assets.HighlightLightCss).Append("\n</style>\n");
        sb.Append("<style media=\"(prefers-color-scheme: dark)\">\n").Append(assets.HighlightDarkCss).Append("\n</style>\n");
        sb.Append("<style>\n").Append(PageCss).Append("\n</style>\n");
        sb.Append("</head>\n<body>\n");
        sb.Append(Shell.Replace("{{TITLE}}", title));
        sb.Append("<script id=\"wheel-source\" type=\"application/json\">").Append(source).Append("</script>\n");
        foreach (var js in new[] { assets.MarkedJs, assets.PurifyJs, assets.HighlightJs, PageJs })
            sb.Append("<script>\n").Append(SafeInline(js)).Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>"Convert → HTML": a formatted page saved beside the Markdown file.</summary>
    public static JobResult SaveHtml(string input, MarkdownAssets assets)
    {
        var output = OutputPaths.Beside(input, ".html");
        var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(input))!;
        var outputFolder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        // Beside the file, relative images just work; saved elsewhere (a chosen output folder), point back at the file's folder.
        var baseFolder = string.Equals(sourceFolder, outputFolder, StringComparison.Ordinal) ? null : sourceFolder;
        File.WriteAllText(output, Build(ReadSource(input), Path.GetFileName(input), baseFolder, assets));
        return JobResult.Ok(output);
    }

    /// <summary>"Read": the page for the reader, written to a temporary file (the same one each time per source).</summary>
    public static string WriteReadingCopy(string input, string tempFolder, MarkdownAssets assets)
    {
        var full = Path.GetFullPath(input);
        Directory.CreateDirectory(tempFolder);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8].ToLowerInvariant();
        var output = Path.Combine(tempFolder, $"{OutputPaths.StemOf(full)}-{hash}.html");
        File.WriteAllText(output, Build(ReadSource(full), Path.GetFileName(full), Path.GetDirectoryName(full), assets));
        return output;
    }

    /// <summary>"file:///Users/me/My%20Notes/" — with the trailing slash, so it works as a base.</summary>
    public static string FolderUri(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!full.EndsWith(Path.DirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;
        return new Uri(full).AbsoluteUri;
    }

    private static string SafeInline(string js) =>
        Regex.Replace(js, "</(script)", "<\\/$1", RegexOptions.IgnoreCase);

    private const string Shell = """
        <header class="bar">
          <button class="icon-button" id="toc-button" title="Contents" aria-label="Contents" hidden>☰</button>
          <div class="bar-title"><span class="file-icon">📄</span><span>{{TITLE}}</span></div>
          <div class="bar-meta" id="meta"></div>
          <button class="text-button" onclick="window.print()" title="Print, or save as PDF">Print / PDF</button>
        </header>
        <div class="layout">
          <nav class="toc" id="toc" aria-label="Contents" hidden></nav>
          <main class="card">
            <div class="card-head"><span class="file-icon">📄</span><span>{{TITLE}}</span></div>
            <article class="markdown-body" id="content"></article>
          </main>
        </div>
        <footer class="foot">Shown by Wheel · offline</footer>

        """;

    private const string PageCss = """
        :root { color-scheme: light dark; --page: #f6f8fa; --card: #ffffff; --line: #d1d9e0; --muted: #59636e;
                --hover: #eef1f4; --accent: #e8622c; --current: #0969da; }
        @media (prefers-color-scheme: dark) {
          :root { --page: #010409; --card: #0d1117; --line: #3d444d; --muted: #9198a1; --hover: #151b23; --current: #4493f8; }
        }
        * { box-sizing: border-box; }
        html { scroll-padding-top: 72px; }
        body { margin: 0; background: var(--page); color: var(--muted);
               font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Helvetica, Arial, sans-serif; }
        .bar { position: sticky; top: 0; z-index: 10; display: flex; align-items: center; gap: 12px; padding: 10px 20px;
               background: color-mix(in srgb, var(--page) 88%, transparent); backdrop-filter: blur(10px);
               -webkit-backdrop-filter: blur(10px); border-bottom: 1px solid var(--line); }
        .bar-title { display: flex; align-items: center; gap: 8px; font-weight: 600; color: CanvasText; min-width: 0; }
        .bar-title span:last-child { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .bar-meta { margin-left: auto; white-space: nowrap; }
        .text-button, .icon-button { font: inherit; color: CanvasText; background: var(--card); border: 1px solid var(--line);
               border-radius: 6px; padding: 4px 10px; cursor: pointer; }
        .text-button:hover, .icon-button:hover { background: var(--hover); }
        .layout { display: flex; justify-content: center; gap: 24px; padding: 24px 20px 40px; }
        .card { flex: 0 1 980px; min-width: 0; background: var(--card); border: 1px solid var(--line); border-radius: 8px; }
        .card-head { display: flex; align-items: center; gap: 8px; padding: 10px 16px; border-bottom: 1px solid var(--line);
               font-weight: 600; color: CanvasText; }
        .markdown-body { padding: 32px 44px 44px; background: transparent; }
        .toc { flex: 0 0 250px; position: sticky; top: 76px; align-self: flex-start; max-height: calc(100vh - 96px);
               overflow: auto; padding: 4px 0; font-size: 13px; }
        .toc-title { font-weight: 600; color: CanvasText; padding: 4px 10px 8px; }
        .toc a { display: block; padding: 4px 10px; color: var(--muted); text-decoration: none; border-radius: 6px;
               border-left: 2px solid transparent; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .toc a:hover { background: var(--hover); color: CanvasText; }
        .toc a.current { color: var(--current); border-left-color: var(--accent); background: var(--hover); }
        .toc a.l2 { padding-left: 22px; } .toc a.l3 { padding-left: 34px; font-size: 12.5px; }
        .markdown-body h1, .markdown-body h2, .markdown-body h3, .markdown-body h4 { position: relative; }
        .heading-link { margin-inline-start: 8px; color: var(--muted); text-decoration: none; opacity: 0; font-weight: 400; }
        .markdown-body :is(h1, h2, h3, h4, h5, h6):hover .heading-link { opacity: 1; }
        .markdown-body pre { position: relative; }
        .markdown-body pre code.hljs { padding: 0; background: transparent; } /* the pre already has GitHub's padding */
        .copy-code { position: absolute; top: 8px; right: 8px; font: 12px/1 -apple-system, "Segoe UI", sans-serif;
               padding: 6px 8px; color: CanvasText; background: var(--card); border: 1px solid var(--line);
               border-radius: 6px; cursor: pointer; opacity: 0; transition: opacity .15s; }
        .markdown-body pre:hover .copy-code, .copy-code:focus { opacity: 1; }
        .front-matter { font-size: 13px; margin-bottom: 24px; }
        .foot { text-align: center; padding: 0 0 32px; font-size: 12px; }
        [dir="rtl"] .heading-link { margin-inline-start: 8px; }
        @media (max-width: 1100px) {
          .toc { position: fixed; top: 56px; left: 12px; z-index: 9; width: 280px; max-height: 70vh; padding: 8px;
                 background: var(--card); border: 1px solid var(--line); border-radius: 8px;
                 box-shadow: 0 8px 24px rgba(0,0,0,.2); display: none; }
          .toc.open { display: block; }
        }
        @media (min-width: 1101px) { #toc-button { display: none; } }
        @media (max-width: 760px) {
          .layout { padding: 12px 0 24px; } .card { border-radius: 0; border-left: 0; border-right: 0; }
          .markdown-body { padding: 20px 16px 32px; } .bar-meta { display: none; }
        }
        @media print {
          .bar, .toc, .foot, .card-head, .copy-code, .heading-link { display: none !important; }
          body, .card { background: #fff; border: 0; } .layout { padding: 0; } .markdown-body { padding: 0; }
        }
        """;

    private const string PageJs = """
        (function () {
          'use strict';
          var source = JSON.parse(document.getElementById('wheel-source').textContent);
          var content = document.getElementById('content');

          // Front matter (--- key: value ---) is shown as a small table, as GitHub does.
          var front = null;
          var fm = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(source);
          if (fm) { front = fm[1]; source = source.slice(fm[0].length); }

          var html = marked.parse(source, { gfm: true, breaks: false });
          content.innerHTML = DOMPurify.sanitize(html, { ADD_ATTR: ['target'] });

          if (front) {
            var table = document.createElement('table');
            table.className = 'front-matter';
            front.split(/\r?\n/).forEach(function (line) {
              var m = /^([^:#][^:]*):\s*(.*)$/.exec(line);
              if (!m) return;
              var tr = table.insertRow(), k = tr.insertCell(), v = tr.insertCell();
              k.textContent = m[1].trim(); v.textContent = m[2].trim();
            });
            if (table.rows.length) content.insertBefore(table, content.firstChild);
          }

          // GitHub alerts: > [!NOTE], [!TIP], [!IMPORTANT], [!WARNING], [!CAUTION]
          content.querySelectorAll('blockquote').forEach(function (q) {
            var p = q.firstElementChild;
            if (!p || p.tagName !== 'P') return;
            var m = /^\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*/i.exec(p.innerHTML);
            if (!m) return;
            var kind = m[1].toLowerCase();
            p.innerHTML = p.innerHTML.slice(m[0].length).replace(/^<br>\s*/, '');
            if (!p.textContent.trim() && !p.querySelector('img')) p.remove();
            var box = document.createElement('div');
            box.className = 'markdown-alert markdown-alert-' + kind;
            var title = document.createElement('p');
            title.className = 'markdown-alert-title';
            title.textContent = kind.charAt(0).toUpperCase() + kind.slice(1);
            box.appendChild(title);
            while (q.firstChild) box.appendChild(q.firstChild);
            q.replaceWith(box);
          });

          // Arabic and other right-to-left text reads right to left, paragraph by paragraph.
          content.querySelectorAll('p, li, ul, ol, h1, h2, h3, h4, h5, h6, blockquote, td, th, dt, dd')
            .forEach(function (el) { el.setAttribute('dir', 'auto'); });

          // Heading ids as GitHub makes them, so #links work, plus the contents list.
          var used = {}, headings = [];
          content.querySelectorAll('h1, h2, h3, h4, h5, h6').forEach(function (h) {
            var base = h.textContent.trim().toLowerCase()
              .replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-') || 'section';
            var id = base, n = 0;
            while (used[id]) id = base + '-' + (++n);
            used[id] = true;
            h.id = id;
            var a = document.createElement('a');
            a.className = 'heading-link'; a.href = '#' + id; a.textContent = '#'; a.setAttribute('aria-hidden', 'true');
            h.appendChild(a);
            if (/^H[1-3]$/.test(h.tagName)) headings.push(h);
          });

          var toc = document.getElementById('toc'), tocButton = document.getElementById('toc-button');
          if (headings.length >= 3) {
            var top = Math.min.apply(null, headings.map(function (h) { return +h.tagName[1]; }));
            var t = document.createElement('div'); t.className = 'toc-title'; t.textContent = 'Contents'; toc.appendChild(t);
            headings.forEach(function (h) {
              var a = document.createElement('a');
              a.href = '#' + h.id;
              a.textContent = h.firstChild ? h.firstChild.textContent.trim() || h.textContent : h.textContent;
              a.className = 'l' + (+h.tagName[1] - top + 1);
              a.setAttribute('dir', 'auto');
              a.addEventListener('click', function () { toc.classList.remove('open'); });
              toc.appendChild(a);
            });
            toc.hidden = false; tocButton.hidden = false;
            tocButton.addEventListener('click', function () { toc.classList.toggle('open'); });
            var links = toc.querySelectorAll('a');
            var spy = new IntersectionObserver(function (entries) {
              entries.forEach(function (e) {
                if (!e.isIntersecting) return;
                links.forEach(function (l) { l.classList.toggle('current', l.getAttribute('href') === '#' + e.target.id); });
              });
            }, { rootMargin: '-70px 0px -70% 0px' });
            headings.forEach(function (h) { spy.observe(h); });
          }

          // Code: colours for the named language, and a Copy button.
          content.querySelectorAll('pre > code').forEach(function (code) {
            var m = /language-([\w+#.-]+)/.exec(code.className);
            if (m && hljs.getLanguage(m[1])) { try { hljs.highlightElement(code); } catch (e) {} }
            var b = document.createElement('button');
            b.className = 'copy-code'; b.type = 'button'; b.textContent = 'Copy';
            b.addEventListener('click', function () {
              var done = function () { b.textContent = 'Copied'; setTimeout(function () { b.textContent = 'Copy'; }, 1500); };
              if (navigator.clipboard) navigator.clipboard.writeText(code.textContent).then(done, function () {});
            });
            code.parentNode.appendChild(b);
          });

          // Web links open in a new tab; links within the page stay.
          content.querySelectorAll('a[href]').forEach(function (a) {
            if (/^https?:/i.test(a.getAttribute('href'))) { a.target = '_blank'; a.rel = 'noopener noreferrer'; }
          });

          var words = (content.textContent.match(/\S+/g) || []).length;
          document.getElementById('meta').textContent =
            words.toLocaleString() + ' words · ' + Math.max(1, Math.round(words / 220)) + ' min read';

          if (location.hash) { var target = document.getElementById(decodeURIComponent(location.hash.slice(1))); if (target) target.scrollIntoView(); }
        })();
        """;
}
