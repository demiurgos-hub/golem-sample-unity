using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// One WorldDecorations row from the authored world database.
    /// </summary>
    public readonly struct WorldDecorRow
    {
        public readonly long ObjectId;
        public readonly int DecorTypeId;
        public readonly float PosX;
        public readonly float PosY;
        public readonly float RotZ;
        public readonly float Scale;

        public WorldDecorRow(long objectId, int decorTypeId, float posX, float posY, float rotZ, float scale)
        {
            ObjectId = objectId;
            DecorTypeId = decorTypeId;
            PosX = posX;
            PosY = posY;
            RotZ = rotZ;
            Scale = scale;
        }
    }

    /// <summary>
    /// Read-only SQLite access to StreamingAssets/world.db for client decor streaming.
    /// Mirrors the Go world's mode=ro + query_only discipline.
    /// </summary>
    /// <remarks>
    /// Desktop only: Application.streamingAssetsPath is a plain filesystem path on Editor /
    /// standalone desktop, so SQLite can open world.db directly. On Android and WebGL the
    /// file lives inside a compressed pack / URL and cannot be opened in-place — a real
    /// project would copy it to persistentDataPath first (see Fractured WorldStreamer.ConnectToDatabaseCopy).
    /// This sample does not implement that copy path.
    /// </remarks>
    public sealed class WorldDatabase : IDisposable
    {
        private const string DatabaseFileName = "world.db";

        private readonly SqliteConnection connection;
        private bool disposed;

        private WorldDatabase(SqliteConnection connection)
        {
            this.connection = connection;
        }

        /// <summary>
        /// Opens StreamingAssets/world.db read-only. Throws if the file is missing or unreadable.
        /// </summary>
        public static WorldDatabase OpenDefault()
        {
            var path = Path.Combine(Application.streamingAssetsPath, DatabaseFileName);
            return Open(path);
        }

        /// <summary>
        /// Opens <paramref name="absolutePath"/> read-only with query_only enforced.
        /// </summary>
        public static WorldDatabase Open(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                throw new ArgumentException("World database path is empty.", nameof(absolutePath));
            }

            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException($"World database not found at '{absolutePath}'.", absolutePath);
            }

            SQLitePCL.Batteries_V2.Init();

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = absolutePath,
                Mode = SqliteOpenMode.ReadOnly,
            };

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA query_only = 1;";
                pragma.ExecuteNonQuery();
            }

            return new WorldDatabase(connection);
        }

        /// <summary>
        /// Returns rows inside the inclusive-exclusive axis-aligned bounds
        /// (PosX &gt;= minX AND PosY &gt;= minY AND PosX &lt; maxX AND PosY &lt; maxY).
        /// </summary>
        public List<WorldDecorRow> QueryBounds(float minX, float minY, float maxX, float maxY)
        {
            ThrowIfDisposed();

            const string sql =
                "SELECT ObjectID, DecorTypeId, PosX, PosY, RotZ, Scale " +
                "FROM WorldDecorations " +
                "WHERE PosX >= $minX AND PosY >= $minY AND PosX < $maxX AND PosY < $maxY";

            var results = new List<WorldDecorRow>();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$minX", minX);
            command.Parameters.AddWithValue("$minY", minY);
            command.Parameters.AddWithValue("$maxX", maxX);
            command.Parameters.AddWithValue("$maxY", maxY);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new WorldDecorRow(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.GetFloat(2),
                    reader.GetFloat(3),
                    reader.GetFloat(4),
                    reader.GetFloat(5)));
            }

            return results;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            connection.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WorldDatabase));
            }
        }
    }
}
