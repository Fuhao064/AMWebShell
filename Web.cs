using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace AMWebShell;

/// <summary>主窗口与弹窗（Apple ID 登录等）共用的 WebView2 配置。</summary>
internal static class Web
{
    // 注：music.apple.com.cn 域名并不存在（DNS NXDOMAIN），中国区“新发现”页面的实际地址如下
    public const string StartUrl = "https://music.apple.com/cn/new";

    private static readonly string[] AllowedHostSuffixes =
    {
        "apple.com", "apple.com.cn", "icloud.com", "icloud.com.cn", "mzstatic.com", "itunes.com", "cdn-apple.com",
    };

    private static readonly int RasterThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    // --no-proxy-server：不走系统代理，直连国内 CDN
    // 保持 DRM（Widevine/PlayReady）与音频子进程完整可用，同时开启 GPU 光栅化、零拷贝纹理与多线程光栅化
    public static readonly string BrowserArgs =
        "--no-proxy-server " +
        "--enable-gpu-rasterization " +
        "--enable-zero-copy " +
        "--enable-smooth-scrolling " +
        $"--num-raster-threads={RasterThreads} " +
        "--enable-features=CanvasOopRasterization,ParallelDownloading " +
        "--disable-features=msSmartScreenProtection,msEdgeShopping,msWebOOUI,msPdfOOUI," +
        "SpareRendererForSitePerProcess,Translate,AutofillServerCommunication,MediaRouter," +
        "OptimizationHints,InterestFeedContentSuggestions " +
        "--disable-site-isolation-trials " +
        "--process-per-site " +
        "--renderer-process-limit=2 " +
        "--disable-renderer-backgrounding " +
        "--autoplay-policy=no-user-gesture-required " +
        "--disable-sync --no-first-run --disable-extensions " +
        "--js-flags=--max-old-space-size=384";

    private static Task<CoreWebView2Environment>? _env;

    public static Task<CoreWebView2Environment> GetEnvironmentAsync(string dataDir) =>
        _env ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDir, "WebView2"),
            new CoreWebView2EnvironmentOptions(BrowserArgs) { AreBrowserExtensionsEnabled = false });

    /// <summary>注入页面：隐藏推广、细滚动条、歌词滚动 GPU 加速、异步图片解码、无标题栏拖拽与顶部边缘悬停窗口控制胶囊。</summary>
    public const string InjectedScript = """
        (() => {
          // 注意：登录按钮也位于 [slot="native-cta"] 容器内，只能隐藏“在‘音乐’中打开”按钮本身
          const css = `
            [data-testid="native-cta"], .native-cta,
            .upsell-banner, [data-testid="upsell-banner"], .web-navigation__native-upsell,
            a[href^="itms"], a[href^="music:"], a[href^="musics:"] { display: none !important; }
            ::-webkit-scrollbar { width: 8px; height: 8px; }
            ::-webkit-scrollbar-thumb { background: rgba(128,128,128,.35); border-radius: 4px; }
            ::-webkit-scrollbar-thumb:hover { background: rgba(128,128,128,.55); }
            ::-webkit-scrollbar-track, ::-webkit-scrollbar-corner { background: transparent; }

            /* 歌词滚动 GPU 独立合成层加速：消除非当前行实时模糊重绘卡顿 */
            amp-lyrics,
            .lyrics__container,
            [class*="lyrics-container"],
            [data-testid*="lyrics"] {
              will-change: transform;
              transform: translateZ(0);
              backface-visibility: hidden;
            }
            .lyrics__line,
            [class*="lyrics-line"],
            [class*="lyric-line"],
            [data-testid*="lyrics-line"],
            .amp-lyrics-line {
              filter: none !important;
              -webkit-filter: none !important;
              will-change: transform, opacity;
              transform: translateZ(0);
              backface-visibility: hidden;
            }

            /* 右上角悬浮窗口控制胶囊：使用 div 并强制隔离页面全局样式，默认隐藏不遮挡页面右上角按钮，鼠标移至右上角顶边时浮现 */
            #aml-winbtns, #aml-winbtns * {
              box-sizing: border-box !important;
              margin: 0 !important;
              padding: 0 !important;
              border: 0 !important;
              float: none !important;
              position: static !important;
              transform: none !important;
              line-height: 1 !important;
            }
            #aml-winbtns {
              position: fixed !important;
              top: 0 !important;
              right: 0 !important;
              height: 28px !important;
              display: flex !important;
              flex-direction: row !important;
              align-items: center !important;
              z-index: 2147483647 !important;
              background: var(--aml-bar-bg, rgba(28, 28, 30, 0.94)) !important;
              backdrop-filter: blur(14px) !important;
              -webkit-backdrop-filter: blur(14px) !important;
              border-bottom-left-radius: 8px !important;
              box-shadow: 0 2px 10px rgba(0, 0, 0, 0.35) !important;
              user-select: none !important;
              -webkit-user-select: none !important;
              opacity: 0 !important;
              pointer-events: none !important;
              transition: opacity 0.14s ease !important;
              overflow: hidden !important;
            }
            #aml-winbtns.aml-show {
              opacity: 1 !important;
              pointer-events: auto !important;
            }
            #aml-winbtns .aml-winbtn {
              width: 38px !important;
              height: 28px !important;
              display: flex !important;
              align-items: center !important;
              justify-content: center !important;
              color: var(--aml-fg, rgba(240, 240, 240, 0.88)) !important;
              background: transparent !important;
              cursor: default !important;
            }
            #aml-winbtns .aml-winbtn:hover {
              background: var(--aml-hover, rgba(255, 255, 255, 0.16)) !important;
              color: var(--aml-fg-active, #ffffff) !important;
            }
            #aml-winbtns .aml-winbtn--close:hover {
              background: #e81123 !important;
              color: #ffffff !important;
            }
            #aml-winbtns svg {
              width: 10px !important;
              height: 10px !important;
              display: block !important;
              pointer-events: none !important;
              flex-shrink: 0 !important;
            }`;

          const add = () => {
            if (!document.getElementById('aml-style')) {
              const st = document.createElement('style');
              st.id = 'aml-style';
              st.textContent = css;
              (document.head || document.documentElement).appendChild(st);
            }
          };
          if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add);

          if (window.top !== window || !window.chrome || !chrome.webview) return;
          const post = m => { try { chrome.webview.postMessage(JSON.stringify(m)); } catch {} };
          const ric = window.requestIdleCallback || (cb => setTimeout(cb, 50));

          const SVG_MIN = '<svg viewBox="0 0 10 10"><path d="M1.5 5h7" stroke="currentColor" stroke-width="1.2" fill="none"/></svg>';
          const SVG_MAX = '<svg viewBox="0 0 10 10"><rect x="1.5" y="1.5" width="7" height="7" stroke="currentColor" stroke-width="1.2" fill="none"/></svg>';
          const SVG_RES = '<svg viewBox="0 0 10 10"><path d="M3 1.5h5.5V7H7M1.5 3H7v5.5H1.5z" stroke="currentColor" stroke-width="1.1" fill="none"/></svg>';
          const SVG_CLS = '<svg viewBox="0 0 10 10"><path d="M1.5 1.5l7 7M8.5 1.5l-7 7" stroke="currentColor" stroke-width="1.2" fill="none"/></svg>';

          let barEl = null, maxBtn = null, barShown = false;
          window.__amlSetMax = isMax => { if (maxBtn) maxBtn.innerHTML = isMax ? SVG_RES : SVG_MAX; };

          const mountChrome = () => {
            add();
            if (document.body && !document.getElementById('aml-winbtns')) {
              const bar = document.createElement('div');
              bar.id = 'aml-winbtns';
              bar.innerHTML =
                `<div class="aml-winbtn" data-act="min" title="最小化">${SVG_MIN}</div>` +
                `<div class="aml-winbtn" data-act="max" title="最大化 / 还原">${SVG_MAX}</div>` +
                `<div class="aml-winbtn aml-winbtn--close" data-act="close" title="关闭">${SVG_CLS}</div>`;
              barEl = bar;
              maxBtn = bar.querySelector('[data-act="max"]');
              bar.addEventListener('click', e => {
                const btn = e.target.closest('[data-act]');
                if (btn) { e.stopPropagation(); post({ t: 'winctl', a: btn.getAttribute('data-act') }); }
              });
              document.body.appendChild(bar);
            }
          };
          if (document.body) mountChrome(); else document.addEventListener('DOMContentLoaded', mountChrome);

          // 检查事件路径中是否包含任何可交互控件（兼容 Shadow DOM 内部按钮）
          const isInteractiveEvent = e => {
            const path = e.composedPath ? e.composedPath() : [e.target];
            for (const n of path) {
              if (!n || n.nodeType !== 1) continue;
              const tag = n.tagName;
              if (tag === 'BUTTON' || tag === 'A' || tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || tag === 'LABEL') return true;
              if (n.id === 'aml-winbtns') return true;
              const role = n.getAttribute && n.getAttribute('role');
              if (role === 'button' || role === 'link' || role === 'searchbox' || role === 'tab' || role === 'menuitem') return true;
              const tid = n.getAttribute && n.getAttribute('data-testid');
              if (tid && /button|menu|input|search|cta|action/i.test(tid)) return true;
            }
            return false;
          };

          let dragStart = null, topResizeCursor = false;
          window.addEventListener('mousemove', e => {
            // 右上角顶边触发显示窗口控制胶囊，移出右上角区域自动隐藏，完全不遮挡页面右上角分享/菜单按钮
            if (barEl) {
              const inCorner = e.clientX >= window.innerWidth - 122 && (barShown ? e.clientY <= 32 : e.clientY <= 7);
              if (inCorner !== barShown) {
                barShown = inCorner;
                barEl.classList.toggle('aml-show', barShown);
              }
            }
            if (dragStart) {
              if ((e.buttons & 1) === 0) { dragStart = null; return; }
              if (Math.abs(e.screenX - dragStart.x) + Math.abs(e.screenY - dragStart.y) >= 5) {
                dragStart = null;
                post({ t: 'winctl', a: 'drag' });
              }
              return;
            }
            if (e.clientY < 4 && e.clientX < window.innerWidth - 122) {
              const c = e.clientX < 8 ? 'nwse-resize' : 'ns-resize';
              document.documentElement.style.cursor = c;
              topResizeCursor = true;
            } else if (topResizeCursor) {
              document.documentElement.style.cursor = '';
              topResizeCursor = false;
            }
          }, { passive: true });

          window.addEventListener('mousedown', e => {
            if (e.button !== 0 || isInteractiveEvent(e)) return;
            if (e.clientY < 4 && e.clientX < window.innerWidth - 122) {
              post({ t: 'winctl', a: e.clientX < 8 ? 'resize-tl' : 'resize-t' });
              return;
            }
            if (e.clientY >= 4 && e.clientY < 34 && e.detail === 1) {
              dragStart = { x: e.screenX, y: e.screenY };
            }
          }, { passive: true });

          window.addEventListener('mouseup', () => { dragStart = null; }, { passive: true });

          window.addEventListener('dblclick', e => {
            if (e.button === 0 && e.clientY >= 4 && e.clientY < 34 && !isInteractiveEvent(e)) {
              dragStart = null;
              post({ t: 'winctl', a: 'max' });
            }
          }, { passive: true });

          // 异步多线程解码新增图片，不修改 srcset/sizes 以保证歌单交互与封面渲染完全原生
          const markAsyncImgs = root => {
            if (!root || !root.querySelectorAll) return;
            for (const img of root.querySelectorAll('img')) {
              if (img.decoding !== 'async') img.decoding = 'async';
            }
          };
          document.addEventListener('DOMContentLoaded', () => {
            mountChrome();
            ric(() => markAsyncImgs(document.body));
          });

          let lastBg = '', lastNp = '';
          const bgOf = () => {
            for (const sel of ['.navigation', 'nav', 'body', 'html']) {
              const el = document.querySelector(sel); if (!el) continue;
              const c = getComputedStyle(el).backgroundColor;
              if (c && !/rgba\(.*,\s*0\)$/.test(c) && c !== 'transparent') return c;
            }
            return matchMedia('(prefers-color-scheme: dark)').matches ? 'rgb(31, 31, 31)' : 'rgb(255, 255, 255)';
          };
          const tick = () => {
            if (document.body) {
              mountChrome();
              const bg = bgOf();
              if (bg !== lastBg) {
                lastBg = bg;
                const m = bg.match(/\d+/g);
                const isDark = !m || (m[0] * 299 + m[1] * 587 + m[2] * 114) / 1000 < 128;
                const root = document.documentElement.style;
                root.setProperty('--aml-bar-bg', isDark ? 'rgba(28, 28, 30, 0.94)' : 'rgba(245, 245, 247, 0.94)');
                root.setProperty('--aml-fg', isDark ? 'rgba(240, 240, 240, 0.88)' : 'rgba(28, 28, 28, 0.88)');
                root.setProperty('--aml-fg-active', isDark ? '#ffffff' : '#000000');
                root.setProperty('--aml-hover', isDark ? 'rgba(255, 255, 255, 0.16)' : 'rgba(0, 0, 0, 0.1)');
                post({ t: 'bg', v: bg });
              }
            }
            let title = '', artist = '', playing = false;
            try {
              const mk = window.MusicKit && MusicKit.getInstance && MusicKit.getInstance();
              const it = mk && mk.nowPlayingItem;
              if (it) { title = it.title || it.attributes?.name || ''; artist = it.artistName || it.attributes?.artistName || ''; playing = !!mk.isPlaying; }
            } catch {}
            if (!title && navigator.mediaSession && navigator.mediaSession.metadata) {
              const md = navigator.mediaSession.metadata; title = md.title; artist = md.artist;
              playing = navigator.mediaSession.playbackState === 'playing';
            }
            const np = JSON.stringify({ title, artist, playing });
            if (np !== lastNp) { lastNp = np; post({ t: 'np', title, artist, playing }); }
            ric(() => markAsyncImgs(document.body));
          };
          setInterval(tick, 1500);
          document.addEventListener('DOMContentLoaded', tick);
          matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => setTimeout(tick, 200));
        })();
        """;

    /// <summary>播放控制：优先用 MusicKit API，失败时点击播放栏按钮。</summary>
    public static string ControlScript(string action) => action switch
    {
        "toggle" => """
            (() => { const mk = window.MusicKit?.getInstance?.();
              if (mk && mk.nowPlayingItem) { mk.isPlaying ? mk.pause() : mk.play(); return 'mk'; }
              const b = document.querySelector('[data-testid="playback-controls__playback-btn"]') ||
                [...document.querySelectorAll('button')].find(x => /^(播放|暂停|Play|Pause)$/.test(x.getAttribute('aria-label') || ''));
              if (b) { b.click(); return 'btn'; } return 'none'; })()
            """,
        "next" => """
            (() => { const mk = window.MusicKit?.getInstance?.(); if (mk && mk.nowPlayingItem) { mk.skipToNextItem(); return 'mk'; }
              const b = [...document.querySelectorAll('button')].find(x => /^(下一首|Next)$/.test(x.getAttribute('aria-label') || ''));
              if (b) { b.click(); return 'btn'; } return 'none'; })()
            """,
        "prev" => """
            (() => { const mk = window.MusicKit?.getInstance?.(); if (mk && mk.nowPlayingItem) { mk.skipToPreviousItem(); return 'mk'; }
              const b = [...document.querySelectorAll('button')].find(x => /^(上一首|Previous)$/.test(x.getAttribute('aria-label') || ''));
              if (b) { b.click(); return 'btn'; } return 'none'; })()
            """,
        _ => "null",
    };

    /// <summary>移除一切浏览器控件与无关功能。</summary>
    public static void Configure(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsReputationCheckingRequired = false;

        core.NavigationStarting += (_, e) =>
        {
            if (!IsAllowed(e.Uri)) { e.Cancel = true; OpenExternal(e.Uri); }
        };
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) =>
        {
            e.State = e.PermissionKind switch
            {
                CoreWebView2PermissionKind.Autoplay => CoreWebView2PermissionState.Allow,
                CoreWebView2PermissionKind.ClipboardRead => CoreWebView2PermissionState.Allow,
                _ => CoreWebView2PermissionState.Deny,
            };
        };
    }

    public static bool IsAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return true;
        if (u.Scheme is not ("http" or "https")) return u.Scheme is "about" or "data" or "blob";
        var host = u.Host.ToLowerInvariant();
        return AllowedHostSuffixes.Any(sfx => host == sfx || host.EndsWith("." + sfx));
    }

    public static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "mailto")) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }
}
