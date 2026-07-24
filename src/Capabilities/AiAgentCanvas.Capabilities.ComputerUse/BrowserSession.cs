using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace AiAgentCanvas.Capabilities.ComputerUse;

public sealed class BrowserSession : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private readonly ILogger<BrowserSession> _logger;
    private readonly string _screenshotDir;

    public BrowserSession(ILogger<BrowserSession> logger, string? screenshotDir = null)
    {
        _logger = logger;
        _screenshotDir = screenshotDir ?? Path.Combine(Directory.GetCurrentDirectory(), "agent-workspace", "screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    public bool IsInitialized => _page is not null;

    public async Task EnsureInitializedAsync()
    {
        if (_page is not null) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        _page = await _browser.NewPageAsync();
        await _page.SetViewportSizeAsync(1280, 720);
        _logger.LogInformation("Browser session initialized (Chromium headless)");
    }

    public async Task<string> NavigateAsync(string url)
    {
        await EnsureInitializedAsync();
        var response = await _page!.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 15000,
        });
        _logger.LogInformation("Navigated to {Url}, status={Status}", url, response?.Status);
        return $"Navigated to {url}. Status: {response?.Status}. Title: {await _page.TitleAsync()}";
    }

    public async Task<string> ClickAsync(int x, int y)
    {
        await EnsureInitializedAsync();
        await _page!.Mouse.ClickAsync(x, y);
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        _logger.LogDebug("Clicked at ({X}, {Y})", x, y);
        return $"Clicked at ({x}, {y}). Current URL: {_page.Url}";
    }

    public async Task<string> ClickSelectorAsync(string selector)
    {
        await EnsureInitializedAsync();
        await _page!.ClickAsync(selector, new PageClickOptions { Timeout = 5000 });
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        _logger.LogDebug("Clicked selector {Selector}", selector);
        return $"Clicked element matching '{selector}'. Current URL: {_page.Url}";
    }

    public async Task<string> TypeTextAsync(string selector, string text)
    {
        await EnsureInitializedAsync();
        await _page!.FillAsync(selector, text, new PageFillOptions { Timeout = 5000 });
        _logger.LogDebug("Typed into {Selector}", selector);
        return $"Typed '{text}' into element matching '{selector}'";
    }

    public async Task<string> TakeScreenshotAsync(string? label = null)
    {
        await EnsureInitializedAsync();
        var filename = $"screenshot_{label ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()}.png";
        var path = Path.Combine(_screenshotDir, filename);
        await _page!.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        _logger.LogInformation("Screenshot saved to {Path}", path);
        return path;
    }

    public async Task<string> GetPageTextAsync()
    {
        await EnsureInitializedAsync();
        var text = await _page!.InnerTextAsync("body");
        var truncated = text.Length > 4000 ? text[..4000] + "..." : text;
        return truncated;
    }

    public async Task<string> GetCurrentUrlAsync()
    {
        await EnsureInitializedAsync();
        return _page!.Url;
    }

    public async Task<string> GetTitleAsync()
    {
        await EnsureInitializedAsync();
        return await _page!.TitleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _page = null;
        _browser = null;
        _playwright = null;
    }
}
