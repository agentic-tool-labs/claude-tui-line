using ClaudeTuiLineMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// SPEC-V2-FRAMEWORK.md §12.6: stateless-by-design stdio MCP server. It holds nothing between
// calls — every call re-resolves the config path and re-reads the file from disk.
var builder = Host.CreateApplicationBuilder(args);

// SPEC-12.6-mcp-tools.md §9.7: the ONLY place the real ~/.claude paths reach the running server.
builder.Services.AddSingleton(BackupLedgerFactory.CreateDefault());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ConfigTools>();

// stdout is the JSON-RPC transport; all logging must go to stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
