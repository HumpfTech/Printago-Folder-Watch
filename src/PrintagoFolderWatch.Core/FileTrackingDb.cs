using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PrintagoFolderWatch.Core
{
    /// <summary>
    /// Cross-platform SQLite-based tracking database for file-to-Part bindings.
    /// Preserves Part IDs and metadata across file moves/renames and content updates.
    /// Uses Microsoft.Data.Sqlite for cross-platform compatibility.
    /// </summary>
    public class FileTrackingDb : IDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string dbPath;

        public FileTrackingDb(string dbPath)
        {
            this.dbPath = dbPath;

            // Create database file if it doesn't exist
            bool needsInit = !File.Exists(dbPath);

            connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            if (needsInit)
            {
                InitializeDatabase();
            }
        }

        private void InitializeDatabase()
        {
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS file_tracking (
                    file_path TEXT PRIMARY KEY,
                    file_hash TEXT NOT NULL,
                    part_id TEXT NOT NULL,
                    part_name TEXT NOT NULL,
                    folder_path TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_hash ON file_tracking(file_hash);
                CREATE INDEX IF NOT EXISTS idx_part ON file_tracking(part_id);
            ";

            using var command = new SqliteCommand(createTableSql, connection);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get tracked entry by file path
        /// </summary>
        public FileTrackingEntry? GetByPath(string filePath)
        {
            var sql = "SELECT * FROM file_tracking WHERE file_path = @path";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@path", filePath);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new FileTrackingEntry
                {
                    FilePath = reader.GetString(0),
                    FileHash = reader.GetString(1),
                    PartId = reader.GetString(2),
                    PartName = reader.GetString(3),
                    FolderPath = reader.GetString(4),
                    LastSeenAt = DateTime.Parse(reader.GetString(5)),
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        /// <summary>
        /// Get tracked entry by file hash (for detecting file moves)
        /// </summary>
        public FileTrackingEntry? GetByHash(string fileHash)
        {
            var sql = "SELECT * FROM file_tracking WHERE file_hash = @hash LIMIT 1";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@hash", fileHash);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new FileTrackingEntry
                {
                    FilePath = reader.GetString(0),
                    FileHash = reader.GetString(1),
                    PartId = reader.GetString(2),
                    PartName = reader.GetString(3),
                    FolderPath = reader.GetString(4),
                    LastSeenAt = DateTime.Parse(reader.GetString(5)),
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        /// <summary>
        /// Insert or update file tracking entry
        /// </summary>
        public void Upsert(FileTrackingEntry entry)
        {
            var sql = @"
                INSERT OR REPLACE INTO file_tracking
                (file_path, file_hash, part_id, part_name, folder_path, last_seen_at, created_at)
                VALUES (@path, @hash, @partId, @partName, @folderPath, @lastSeen,
                    COALESCE((SELECT created_at FROM file_tracking WHERE file_path = @path), @created))
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@path", entry.FilePath);
            command.Parameters.AddWithValue("@hash", entry.FileHash);
            command.Parameters.AddWithValue("@partId", entry.PartId);
            command.Parameters.AddWithValue("@partName", entry.PartName);
            command.Parameters.AddWithValue("@folderPath", entry.FolderPath ?? "");
            command.Parameters.AddWithValue("@lastSeen", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Update file path (preserves Part ID and metadata) - used when file is moved/renamed
        /// </summary>
        public bool UpdatePath(string oldPath, string newPath)
        {
            var sql = @"
                UPDATE file_tracking
                SET file_path = @newPath, last_seen_at = @lastSeen
                WHERE file_path = @oldPath
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@newPath", newPath);
            command.Parameters.AddWithValue("@oldPath", oldPath);
            command.Parameters.AddWithValue("@lastSeen", DateTime.UtcNow.ToString("o"));

            return command.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Update file hash (preserves Part ID) - used when file content changes
        /// </summary>
        public bool UpdateHash(string filePath, string newHash)
        {
            var sql = @"
                UPDATE file_tracking
                SET file_hash = @hash, last_seen_at = @lastSeen
                WHERE file_path = @path
            ";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@hash", newHash);
            command.Parameters.AddWithValue("@path", filePath);
            command.Parameters.AddWithValue("@lastSeen", DateTime.UtcNow.ToString("o"));

            return command.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Delete tracking entry
        /// </summary>
        public bool Delete(string filePath)
        {
            var sql = "DELETE FROM file_tracking WHERE file_path = @path";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@path", filePath);

            return command.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Get all tracked entries
        /// </summary>
        public List<FileTrackingEntry> GetAll()
        {
            var entries = new List<FileTrackingEntry>();
            var sql = "SELECT * FROM file_tracking";

            using var command = new SqliteCommand(sql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                entries.Add(new FileTrackingEntry
                {
                    FilePath = reader.GetString(0),
                    FileHash = reader.GetString(1),
                    PartId = reader.GetString(2),
                    PartName = reader.GetString(3),
                    FolderPath = reader.GetString(4),
                    LastSeenAt = DateTime.Parse(reader.GetString(5)),
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                });
            }

            return entries;
        }

        /// <summary>
        /// Clean up orphaned entries (files not seen in N days)
        /// </summary>
        public int CleanupOrphaned(int daysOld = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld).ToString("o");
            var sql = "DELETE FROM file_tracking WHERE last_seen_at < @cutoff";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@cutoff", cutoffDate);

            return command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            connection?.Close();
            connection?.Dispose();
        }
    }

    /// <summary>
    /// Represents a file tracking entry in the database
    /// </summary>
    public class FileTrackingEntry
    {
        public string FilePath { get; set; } = "";
        public string FileHash { get; set; } = "";
        public string PartId { get; set; } = "";
        public string PartName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public DateTime LastSeenAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
