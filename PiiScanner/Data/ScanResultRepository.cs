using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PiiScanner.Data
{
    public sealed class ScanResultRepository
    {

        private readonly string _connectionString;

        public ScanResultRepository(string? dbPath = null)
        {
            string baseDir = AppContext.BaseDirectory;
            string file = dbPath ?? Path.Combine(baseDir, "scanresults.db");
            _connectionString = $"Data Source={file};Cache=Shared";

            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS ScanResultsQueue (
                Id TEXT PRIMARY KEY,
                ScanId TEXT,
                MachineName TEXT,
                Source INTEGER,
                FilePath TEXT,
                Entity TEXT,
                IsDetected INTEGER,
                Details TEXT,
                CreatedAt TEXT,
                Status INTEGER DEFAULT 0
            );
            ";
            cmd.ExecuteNonQuery();
        }

        public async Task EnqueueAsync(Models.ScanResults result)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
            @"
            INSERT OR IGNORE INTO ScanResultsQueue
            (Id, ScanId, MachineName, Source, FilePath, Entity, IsDetected, Details, CreatedAt)
            VALUES ($id, $scanId, $machineName, $source, $filePath, $entity, $isDetected, $details, $createdAt);
            ";
            cmd.Parameters.AddWithValue("$id", result.Id.ToString());
            cmd.Parameters.AddWithValue("$scanId", result.ScanId ?? string.Empty);
            cmd.Parameters.AddWithValue("$machineName", result.MachineName ?? string.Empty);
            cmd.Parameters.AddWithValue("$source", (int)result.Source);
            cmd.Parameters.AddWithValue("$filePath", result.FilePath ?? string.Empty);
            cmd.Parameters.AddWithValue("$entity", result.Entity ?? string.Empty);
            cmd.Parameters.AddWithValue("$isDetected", result.IsDetected ? 1 : 0);
            cmd.Parameters.AddWithValue("$details", result.Details ?? string.Empty);
            cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<Models.ScanResults>> DequeueBatchAsync(int batchSize)
        {
            var list = new List<Models.ScanResults>();

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Select by rowid (insertion order) up to batchSize
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
            @"
            SELECT Id, ScanId, MachineName, Source, FilePath, Entity, IsDetected, Details
            FROM ScanResultsQueue
            WHERE Status = 0
            ORDER BY rowid
            LIMIT $limit;
            ";
            cmd.Parameters.AddWithValue("$limit", batchSize);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var r = new Models.ScanResults
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    ScanId = reader.IsDBNull(1) ? null! : reader.GetString(1),
                    MachineName = reader.IsDBNull(2) ? null! : reader.GetString(2),
                    Source = (Models.StorageSource)reader.GetInt32(3),
                    FilePath = reader.IsDBNull(4) ? null! : reader.GetString(4),
                    Entity = reader.IsDBNull(5) ? null! : reader.GetString(5),
                    IsDetected = reader.GetInt32(6) != 0,
                    Details = reader.IsDBNull(7) ? null! : reader.GetString(7)
                };
                list.Add(r);
            }

            var ids = list.Select(x => x.Id).ToList();

            if (ids.Count > 0)
            {
                using var tran = conn.BeginTransaction();
                using var cmd2 = conn.CreateCommand();
                cmd2.Transaction = tran;

                var paramNames = new List<string>();
                for (int i = 0; i < ids.Count; i++)
                {
                    string pname = $"$id{i}";
                    cmd2.Parameters.AddWithValue(pname, ids[i].ToString());
                    paramNames.Add(pname);
                }

                cmd2.CommandText = $"UPDATE ScanResultsQueue SET Status = 1 WHERE Id IN ({string.Join(", ", paramNames)});";
                await cmd2.ExecuteNonQueryAsync();
                await tran.CommitAsync();
            }

            return list;
        }

        public async Task DeleteBatchAsync(IEnumerable<Guid> ids)
        {
            // build a single DELETE statement with parameters
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var tran = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            int i = 0;
            var parameters = new List<string>();
            foreach (var id in ids)
            {
                string name = $"$id{i}";
                cmd.Parameters.AddWithValue(name, id.ToString());
                parameters.Add(name);
                i++;
            }

            if (parameters.Count == 0)
            {
                await tran.CommitAsync();
                return;
            }

            cmd.CommandText = $"DELETE FROM ScanResultsQueue WHERE Id IN ({string.Join(",", parameters)});";
            cmd.Transaction = tran;
            await cmd.ExecuteNonQueryAsync();
            await tran.CommitAsync();
        }
    }
}