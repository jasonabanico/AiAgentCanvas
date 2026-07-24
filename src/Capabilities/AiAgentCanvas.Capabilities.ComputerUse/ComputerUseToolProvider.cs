#pragma warning disable MEAI001

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiAgentCanvas.Capabilities.ComputerUse;

public static class ComputerUseToolProvider
{
    public static IReadOnlyList<AITool> CreateTools(BrowserSession session)
    {
        return
        [
            AIFunctionFactory.Create(
                [Description("Navigate the browser to a URL")]
                async (string url) => await session.NavigateAsync(url),
                "browser_navigate"),

            AIFunctionFactory.Create(
                [Description("Click at specific coordinates (x, y) on the page")]
                async (int x, int y) => await session.ClickAsync(x, y),
                "browser_click"),

            AIFunctionFactory.Create(
                [Description("Click an element matching a CSS selector")]
                async (string selector) => await session.ClickSelectorAsync(selector),
                "browser_click_element"),

            AIFunctionFactory.Create(
                [Description("Type text into an input element matching a CSS selector")]
                async (string selector, string text) => await session.TypeTextAsync(selector, text),
                "browser_type"),

            AIFunctionFactory.Create(
                [Description("Take a screenshot of the current page and return the file path")]
                async (string? label) => await session.TakeScreenshotAsync(label),
                "browser_screenshot"),

            AIFunctionFactory.Create(
                [Description("Get the visible text content of the current page")]
                async () => await session.GetPageTextAsync(),
                "browser_get_text"),

            AIFunctionFactory.Create(
                [Description("Get the current page URL and title")]
                async () =>
                {
                    var url = await session.GetCurrentUrlAsync();
                    var title = await session.GetTitleAsync();
                    return JsonSerializer.Serialize(new { url, title });
                },
                "browser_get_info"),
        ];
    }
}
