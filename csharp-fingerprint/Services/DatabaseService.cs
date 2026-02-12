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
            _connString = $"Host={settings.ServerIp};" +
                $"Port={settings.DatabasePort};" +
                $"Username={settings.DatabaseUser};" +
                $"Password={settings.DatabasePass};" +
                $"Database={settings.DatabaseName};" +
                $"Timeout=15;" +
                $"Command Timeout=30;" +
                $"Keepalive=15;";
        }

        public async Task<List<FingerprintTemplate>> GetAllTemplatesAsync()
        {
            var list = new List<FingerprintTemplate>();
            using (var conn = new NpgsqlConnection(_connString))
            {
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand("SELECT user_id, template, first_name, last_name FROM user_fingerprints", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new FingerprintTemplate
                        {
                            UserId = reader.GetInt32(0),
                            Template = (byte[])reader[1],
                            FirstName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            LastName = reader.IsDBNull(3) ? "" : reader.GetString(3)
                        });
                    }
                }
            }
            return list;
        }

        public void SaveTemplate(int id, string firstName, string lastName, byte[] template)
        {
            using (var conn = new NpgsqlConnection(_connString))
            {
                conn.Open();
                var cmd = new NpgsqlCommand(
                    @"INSERT INTO user_fingerprints (user_id, first_name, last_name, template) 
                      VALUES (@id, @fname, @lname, @tmp) 
                      ON CONFLICT (user_id) DO UPDATE SET 
                      first_name = EXCLUDED.first_name, 
                      last_name = EXCLUDED.last_name, 
                      template = EXCLUDED.template", conn);

                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("fname", firstName ?? "");
                cmd.Parameters.AddWithValue("lname", lastName ?? "");
                cmd.Parameters.AddWithValue("tmp", template);
                cmd.ExecuteNonQuery();
            }
        }
    }
}