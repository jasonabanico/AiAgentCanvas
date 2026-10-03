using Xunit;

// Many test classes create SQLite databases and release them with
// SqliteConnection.ClearAllPools(), which empties the process-wide connection pool.
// Run in parallel, one class clears the pool while another is mid-query and the second
// fails with an ObjectDisposedException. The suite takes seconds, so run it serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
