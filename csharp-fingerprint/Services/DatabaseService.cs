using fingerprint_bridge.Models;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace fingerprint_bridge.Services
{
    public class DatabaseService
    {
        private readonly string _connString;

        public DatabaseService(AppSettings settings)
        {
            // Initializes connection string using the AppSettings model
            _connString = $"Host={settings.ServerIp};Port={settings.DatabasePort};Username={settings.DatabaseUser};Password={settings.DatabasePass};Database={settings.DatabaseName}";
        }

        /// <summary>
        /// Fetches all user templates from the PostgreSQL database.
        /// Required by Form1.SyncTemplates().
        /// </summary>
        public async Task<List<FingerprintTemplate>> GetAllTemplatesAsync()
        {
            var list = new List<FingerprintTemplate>();
            using (var conn = new NpgsqlConnection(_connString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand("SELECT user_id, template FROM user_fingerprints", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new FingerprintTemplate
                        {
                            UserId = reader.GetInt32(0),
                            Template = (byte[])reader[1]
                        });
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Calculates the next available ID by finding the current MAX ID + 1.
        /// Required by Form1.SyncTemplates() and Form1.HandleEnroll().
        /// </summary>
        public int GetNextAvailableId()
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("SELECT COALESCE(MAX(user_id), 0) + 1 FROM user_fingerprints", conn);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch
            {
                return 1; // Default to 1 if the table is empty or connection fails
            }
        }

        /// <summary>
        /// Saves or updates a fingerprint template in the database.
        /// Renamed to SaveTemplate to match the call in Form1.HandleEnroll().
        /// </summary>
        public void SaveTemplate(int id, byte[] template)
        {
            using (var conn = new NpgsqlConnection(_connString))
            {
                conn.Open();
                // Uses ON CONFLICT for PostgreSQL to handle updates to existing users
                var cmd = new NpgsqlCommand(
                    "INSERT INTO user_fingerprints (user_id, template) " +
                    "VALUES (@id, @tmp) " +
                    "ON CONFLICT (user_id) DO UPDATE SET template = @tmp", conn);

                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("tmp", template);
                cmd.ExecuteNonQuery();
            }
        }
    }
}