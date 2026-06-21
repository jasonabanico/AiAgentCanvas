using AiAgentCanvas.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HelloWorldAgent;

public static class HelloWorldServiceExtensions
{
    public static IServiceCollection AddHelloWorldAgent(this IServiceCollection services)
    {
        services.AddSingleton<IPersonaSeed>(new PersonaSeed(
            name: "financial-analyst",
            description: "A financial analyst that uses market data tools to research stocks and companies",
            instructions: """
                You are a financial analyst assistant.

                You have access to these tools:
                - stock_quote: Get current price and daily change for a ticker from Yahoo Finance
                - stock_history: Get historical price data with configurable range (5d, 1mo, 3mo, 6mo, 1y, 5y)
                - edgar_company_facts: Fetch SEC EDGAR financial data including revenue, net income, assets, and EPS

                When analyzing a stock:
                1. Start with stock_quote for the current price and daily movement
                2. Use stock_history to show recent trends
                3. Use edgar_company_facts for fundamental data (revenue, earnings, balance sheet)
                4. Synthesize the data into a clear summary with specific numbers

                Always cite the data source (Yahoo Finance or SEC EDGAR) and note that this is informational, not investment advice.
                If the user asks about multiple stocks, compare them side by side.
                If a tool call fails, tell the user what happened and try an alternative approach.
                """));

        services.AddSingleton<IGuardrailSeed>(new GuardrailSeed(
            name: "investment-disclaimer",
            severity: "high",
            enabled: true,
            rule: """
                Never provide specific buy, sell, or hold recommendations.
                Always include a disclaimer that the analysis is informational only and not investment advice.
                Do not predict future stock prices or guarantee returns.
                If the user asks for a recommendation, explain that you can provide data and analysis but not financial advice.
                """));

        services.AddSingleton<IWorkflowSeed>(new WorkflowSeed(
            name: "full-stock-analysis",
            description: "Run a comprehensive stock analysis: current quote, price history, and SEC fundamentals",
            tags: "finance,analysis",
            content: """
                ## Full Stock Analysis

                Follow these steps for the given ticker symbol:

                ### Step 1: Current Quote
                Use `stock_quote` to get the current price, daily change, and volume.
                Present the price with the daily change percentage.

                ### Step 2: Price History
                Use `stock_history` with range "1mo" to get the last month of prices.
                Identify the trend (up, down, sideways) and any notable price movements.

                ### Step 3: Fundamentals
                Use `edgar_company_facts` to pull SEC EDGAR data.
                Focus on: revenue, net income, total assets, and EPS.
                Note year-over-year changes if available.

                ### Step 4: Summary
                Synthesize all data into a concise summary:
                - Current price and recent movement
                - One-month trend direction
                - Key financial metrics
                - Notable observations

                End with the investment disclaimer.
                """));

        services.AddSingleton<IContextSeed>(new ContextSeed(
            topic: "financial-analysis-methodology",
            tags: "finance,methodology",
            content: """
                ## Financial Analysis Reference

                ### Key Metrics
                - **P/E Ratio**: Price / Earnings per Share. Lower may indicate undervaluation; compare within sector.
                - **Revenue Growth**: Year-over-year revenue change. Consistent growth signals business health.
                - **Net Income Margin**: Net Income / Revenue. Higher margins indicate operational efficiency.
                - **EPS**: Earnings per Share. Primary driver of stock valuation.

                ### Data Sources
                - **Yahoo Finance**: Real-time and delayed quotes, historical prices, volume data.
                - **SEC EDGAR**: Official filings (10-K, 10-Q). Revenue, income, assets, and EPS from company facts API.

                ### Analysis Framework
                1. Start with price action (current quote + history) for market sentiment
                2. Layer in fundamentals (EDGAR) for intrinsic value context
                3. Compare metrics to sector averages when possible
                4. Note any divergence between price trend and fundamental performance
                """));

        services.AddSingleton<IEntitySeed>(new EntitySeed(
            name: "stock-analysis-report",
            type: "report",
            tags: "finance,output",
            content: """
                ## Stock Analysis Report Schema

                A stock analysis report should contain:

                ### Header
                - **Ticker**: The stock symbol (e.g., AAPL)
                - **Company Name**: Full company name
                - **Date**: Analysis date

                ### Current Price
                - **Price**: Current trading price
                - **Daily Change**: Dollar and percentage change
                - **Volume**: Trading volume

                ### Price Trend
                - **Period**: Time range analyzed
                - **Direction**: Up / Down / Sideways
                - **High/Low**: Period high and low prices
                - **Notable Events**: Significant price movements

                ### Fundamentals
                - **Revenue**: Most recent annual revenue
                - **Net Income**: Most recent annual net income
                - **EPS**: Earnings per share
                - **Total Assets**: Balance sheet total

                ### Summary
                - Key observations combining price and fundamental data
                - Investment disclaimer
                """));

        services.AddSingleton<ISkillSeed>(new SkillSeed(
            name: "compare-stocks",
            description: "Compare two or more stocks side by side with price and fundamental data",
            promptTemplate: """
                Compare the following stocks: {{tickers}}

                For each stock:
                1. Get the current quote using stock_quote
                2. Get 1-month price history using stock_history
                3. Get fundamental data using edgar_company_facts

                Present the comparison in a table format with columns for each ticker.
                Include: current price, daily change %, 1-month change %, revenue, net income, and EPS.
                Highlight which stock is performing better in each category.

                End with a brief summary of key differences and the investment disclaimer.
                """));

        return services;
    }
}
