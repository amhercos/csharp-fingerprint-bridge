using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using fingerprint_bridge.Models;

namespace fingerprint_bridge.Services
{
    public class SyncResult
    {
        public bool IsSuccess { get; set; }
        public bool IsNetworkError { get; set; }
        public string Message { get; set; }
    }

    public class ApiResponseDto
    {
        public string Message { get; set; }
        public string Direction { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Name { get; set; }
    }

    public class AttendanceSyncService
    {
        private readonly HttpClient _httpClient;
        private List<AttendanceRequest> _offlineQueue = new List<AttendanceRequest>();
        private readonly string _queueFilePath;
        private readonly object _lock = new object();

        public AttendanceSyncService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Pointing to the .exe folder
            _queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_queue.json");

            LoadQueue();
        }

        private void LoadQueue()
        {
            lock (_lock)
            {
                if (!File.Exists(_queueFilePath))
                {
                    _offlineQueue = new List<AttendanceRequest>();
                    return;
                }

                try
                {
                    var text = File.ReadAllText(_queueFilePath);
                    // If the file exists but is empty, return new list
                    _offlineQueue = JsonConvert.DeserializeObject<List<AttendanceRequest>>(text) ?? new List<AttendanceRequest>();
                }
                catch (Exception)
                {
                    // If JSON is corrupted, backup the bad file and start fresh
                    try { File.Move(_queueFilePath, _queueFilePath + ".bak"); } catch { }
                    _offlineQueue = new List<AttendanceRequest>();
                }
            }
        }

        public async Task<SyncResult> SendAttendanceAsync(AttendanceRequest req)
        {
            try
            {
                var json = JsonConvert.SerializeObject(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://master-api.amsentry.dev/api/fingerprint/log-attendance", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                ApiResponseDto apiData = null;
                try { apiData = JsonConvert.DeserializeObject<ApiResponseDto>(responseBody); } catch { }

                string serverMessage = apiData?.Message ?? response.ReasonPhrase;

                if (response.IsSuccessStatusCode)
                {
                    return new SyncResult { IsSuccess = true, IsNetworkError = false, Message = serverMessage };
                }

                // Client errors (4xx) - usually means wrong ID or invalid data
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    return new SyncResult { IsSuccess = false, IsNetworkError = false, Message = serverMessage };
                }

                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = $"Server Error ({response.StatusCode})" };
            }
            catch (Exception ex)
            {
                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Connection Failed" };
            }
        }

        public void AddToQueue(AttendanceRequest req)
        {
            lock (_lock)
            {
                _offlineQueue.Add(req);
                PersistQueue();
            }
        }

        public List<AttendanceRequest> GetQueue()
        {
            lock (_lock) return new List<AttendanceRequest>(_offlineQueue);
        }

        public void RemoveFromQueue(AttendanceRequest req)
        {
            lock (_lock)
            {
                _offlineQueue.Remove(req);
                PersistQueue();
            }
        }

        public void PersistQueue()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_offlineQueue, Formatting.Indented);
                    File.WriteAllText(_queueFilePath, json);
                }
                catch (Exception ex)
                {
                    // Log to console/debug if writing fails (e.g. permission issues)
                    System.Diagnostics.Debug.WriteLine($"Failed to save queue: {ex.Message}");
                }
            }
        }
    }
}