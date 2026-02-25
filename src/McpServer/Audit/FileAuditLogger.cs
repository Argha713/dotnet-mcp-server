// Argha - 2026-02-25 - Phase 6.2: writes JSONL audit records to rolling daily files with retention cleanup
using McpServer.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Audit;

/// <summary>
/// Appends one JSON line per tool call to a rolling daily file under the configured
/// audit log directory. File names follow the pattern <c>audit-yyyy-MM-dd.jsonl</c>.
///
/// Thread-safety is provided by a <see cref="SemaphoreSlim"/> so that concurrent
/// tool calls never interleave partial lines.
///
/// On the first write of each process lifetime, files older than
/// <see cref="AuditSettings.RetentionDays"/> are deleted (best-effort).
/// </summary>
public sealed class FileAuditLogger : IAuditLogger, IDisposable
{
    private readonly AuditSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;
    // Argha - 2026-02-25 - one-at-a-time writer guard; async-compatible
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Argha - 2026-02-25 - retention cleanup runs once per process lifetime
    private volatile bool _cleanupRan;

    public FileAuditLogger(IOptions<AuditSettings> options)
    {
        _settings = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Argha - 2026-02-25 - skip null fields to keep JSONL lines compact
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <inheritdoc />
    public async Task LogCallAsync(AuditRecord record)
    {
        if (!_settings.Enabled) return;

        // Argha - 2026-02-25 - guard against misconfiguration; PostConfigure should have resolved this
        if (string.IsNullOrWhiteSpace(_settings.LogDirectory)) return;

        // Argha - 2026-02-25 - sanitize arguments before writing; passwords must never reach disk
        var sanitizedRecord = record with
        {
            Arguments = AuditArgumentSanitizer.Sanitize(record.Arguments)
        };

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_cleanupRan)
            {
                RunRetentionCleanup();
                _cleanupRan = true;
            }

            Directory.CreateDirectory(_settings.LogDirectory);

            var fileName = $"audit-{record.Timestamp:yyyy-MM-dd}.jsonl";
            var filePath = Path.Combine(_settings.LogDirectory, fileName);

            var json = JsonSerializer.Serialize(sanitizedRecord, _jsonOptions);

            // Argha - 2026-02-25 - FileShare.ReadWrite allows external tools to tail the log while server runs
            using var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Argha - 2026-02-25 - delete audit files older than RetentionDays; failures are silently skipped
    private void RunRetentionCleanup()
    {
        if (_settings.RetentionDays <= 0) return;
        if (!Directory.Exists(_settings.LogDirectory)) return;

        var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);

        foreach (var file in Directory.GetFiles(_settings.LogDirectory, "audit-*.jsonl"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch
            {
                // Argha - 2026-02-25 - retention cleanup is best-effort; a locked or read-only file is skipped
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }
}
