// These tests mutate process-wide environment variables (CLAUDE_PLUGIN_DATA, HOME,
// CLAUDE_TUI_LINE_CONFIG) to control CLI discovery and config-path resolution. Serialize the
// whole assembly so no two tests race on that shared state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
