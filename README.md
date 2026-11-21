# AMWebShell

一个基于 **.NET 10 (WinForms) + Microsoft Edge WebView2** 构建的极简、低内存、无标题栏沉浸式 Apple Music 网页端桌面外壳（Windows x64）。

A lightweight, low-memory, frameless Windows desktop WebView2 shell for `music.apple.com`.

---

## 核心特性 (Features)

- **无标题栏沉浸式窗口 (Frameless Background-Integrated UI)**
  - 通过 Win32 `WM_NCCALCSIZE` 与 Windows 11 DWM API 移除原生顶部标题栏，将网页背景与窗口边框完美融合，同时完整保留 Windows 11 原生圆角（`DWMWCP_ROUND`）、窗口阴影、Aero Snap 贴靠布局以及边缘拖拽缩放。
  - **右上角顶边悬停控制胶囊**：窗口控制按钮（最小化 / 最大化与还原 / 关闭）默认自动隐藏，绝不遮挡页面右上角原有的分享或菜单按钮；鼠标推至窗口右上角顶边时自动浮现毛玻璃胶囊。
  - 顶部空白区域支持左键拖拽移动窗口、双击最大化/还原。
- **高性能与流畅渲染 (High Performance & Smooth Rendering)**
  - **多线程与 GPU 硬件加速**：默认开启 GPU 光栅化（`--enable-gpu-rasterization`）、零拷贝纹理（`--enable-zero-copy`）、Canvas OOP 硬件加速（`CanvasOopRasterization`）以及按 CPU 核心数动态分配的多线程光栅化工作线程（`--num-raster-threads`）。
  - **歌词滚动合成层加速**：为歌词容器与歌词行启用独立 GPU 合成层（`will-change: transform, opacity; transform: translateZ(0)`），消除逐行动态模糊重绘造成的掉帧与卡顿。
  - **异步图片解码**：页面封面图像自动启用 `decoding="async"`，将图像解码卸载至后台工作线程，避免歌单加载与界面刷新阻塞主线程。
  - **完整 DRM 与音频兼容**：原生兼容 Windows Edge WebView2 的 Widevine 与 PlayReady EME 加密音频流播放。
- **低内存占用优化 (Low Memory Footprint)**
  - **自适应工作站 GC**：宿主采用 .NET 10 DATAS 自适应工作站 GC（`GarbageCollectionAdaptationMode=1`）+ 后台并发回收 + 动态 PGO，并在编译期剥离未使用的 WPF 程序集，单文件体积仅约 **1.5 MB**。
  - **智能工作集回收**：精简多余浏览器后台特性与子进程数量；当窗口最小化或隐藏至系统托盘时，自动将 WebView2 内存目标设为 `Low`，触发 V8 空闲 GC 并回收宿主与渲染器冷内存页（`EmptyWorkingSet`），大幅降低后台驻留物理内存且不中断音乐播放。
- **系统托盘与后台播放 (System Tray & Media Controls)**
  - 支持关闭窗口时最小化至系统托盘后台继续播放。
  - 托盘右键菜单实时同步当前播放曲目与艺术家信息，支持一键播放/暂停、上一首、下一首（优先调用 `MusicKit` 实例 API）。
  - 自动跟随网页深浅色主题切换窗口 DWM 边框与托盘菜单配色。

---

## 快捷键与操作 (Shortcuts)

| 操作 | 快捷键 / 方式 |
| :--- | :--- |
| **移动窗口** | 鼠标左键按住窗口顶部空白区域拖动 |
| **最大化 / 还原** | 双击窗口顶部空白区域，或将鼠标移至右上角顶边点击最大化图标 |
| **后退 / 前进** | `Alt + ←` / `Alt + →` 或鼠标侧键（前进/后退键） |
| **刷新当前页** | `Ctrl + R` |
| **回到首页（新发现）** | `Alt + Home` |
| **页面缩放 / 重置** | `Ctrl + +` / `Ctrl + -` / `Ctrl + 0` |

---

## 构建说明 (Build Instructions)

### 环境要求
- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Microsoft Edge WebView2 Runtime（Windows 11 已内置）

### 编译发布单文件可执行程序

```powershell
# 1. （可选）由纯代码矢量脚本重新生成原创多尺寸图标 app.ico
pwsh ./assets/make-icon.ps1

# 2. 发布单文件 Release 产物至 dist 目录
dotnet publish AMWebShell.csproj -c Release -o dist
```

构建完成后，单文件可执行程序位于 `dist/AMWebShell.exe`。

---

## 免责声明 (Disclaimer)

> **Non-Affiliation & Trademark Notice / 非官方关联与商标声明**  
> 1. 本项目（`AMWebShell`）是一个独立的第三方开源 WebView2 浏览器外壳项目，与 Apple Inc. 及其任何关联公司**不存在**任何隶属、授权、背书或官方合作关系。  
>    This project (`AMWebShell`) is an independent, third-party open-source WebView2 browser wrapper and is **not** affiliated with, endorsed by, sponsored by, or officially connected with Apple Inc. or any of its subsidiaries.  
> 2. "Apple" 与 "Apple Music" 是 Apple Inc. 的注册商标。本仓库中的所有图标均由 `assets/make-icon.ps1` 通过 GDI+ 几何矢量代码原创生成，不包含任何官方受版权保护的图像资产。  
>    "Apple" and "Apple Music" are registered trademarks of Apple Inc. All application icons in this repository are procedurally generated vector graphics and contain no proprietary assets.  
> 3. 本软件不提供任何绕过、解密、抓包、下载或修改受 DRM 保护媒体流的功能；音乐播放需用户自行登录合法账号，并完全依赖系统浏览器标准的加密媒体扩展（EME）。  
>    This software does not bypass, decrypt, download, or modify any DRM-protected media streams; playback requires a valid user account and utilizes standard browser Encrypted Media Extensions (EME).

---

## License

Released under the [MIT License](./LICENSE).
