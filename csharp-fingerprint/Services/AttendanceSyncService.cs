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

    public class AttendanceSyncService
    {
        private readonly HttpClient _httpClient;
        private List<AttendanceRequest> _offlineQueue = new List<AttendanceRequest>();
        private readonly string _queueFilePath;
        private readonly object _lock = new object();

        public AttendanceSyncService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_queue.json");
            LoadQueue();
        }

        public async Task<SyncResult> SendAttendanceAsync(AttendanceRequest req)
        {
            try
            {
                var json = JsonConvert.SerializeObject(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://master-api.amsentry.dev/api/fingerprint/log-attendance", content);

                if (response.IsSuccessStatusCode)
                    return new SyncResult { IsSuccess = true, IsNetworkError = false, Message = "Success" };

                // 400-499: Logic errors (User not found, etc.)
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    string errorDetail = await response.Content.ReadAsStringAsync();
                    return new SyncResult { IsSuccess = false, IsNetworkError = false, Message = $"Rejected: {response.StatusCode}" };
                }

                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Server Error" };
            }
            catch (HttpRequestException) { return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Network Down" }; }
            catch (Exception ex) { return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = ex.Message }; }
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

        private void LoadQueue()
        {
            try
            {
                if (File.Exists(_queueFilePath))
                {
                    string json = File.ReadAllText(_queueFilePath);
                    var data = JsonConvert.DeserializeObject<List<AttendanceRequest>>(json);
                    if (data != null) _offlineQueue = data;
                }
            }
            catch { }
        }

        public void PersistQueue()
        {
            try
            {
                lock (_lock)
                {
                    string json = JsonConvert.SerializeObject(_offlineQueue, Formatting.Indented);
                    File.WriteAllText(_queueFilePath, json);
                }
            }
            catch { }
        }
    }
}