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

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    return new SyncResult { IsSuccess = false, IsNetworkError = false, Message = $"Rejected: {response.StatusCode}" };

                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Server Error" };
            }
            catch { return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Network Down" }; }
        }

        public void AddToQueue(AttendanceRequest req) { lock (_lock) { _offlineQueue.Add(req); PersistQueue(); } }
        public List<AttendanceRequest> GetQueue() { lock (_lock) return new List<AttendanceRequest>(_offlineQueue); }
        public void RemoveFromQueue(AttendanceRequest req) { lock (_lock) { _offlineQueue.Remove(req); PersistQueue(); } }

        private void LoadQueue()
        {
            if (File.Exists(_queueFilePath)) try
                {
                    _offlineQueue = JsonConvert.DeserializeObject<List<AttendanceRequest>>(File.ReadAllText(_queueFilePath)) ?? new List<AttendanceRequest>();
                }
                catch { }
        }

        public void PersistQueue()
        {
            lock (_lock) File.WriteAllText(_queueFilePath, JsonConvert.SerializeObject(_offlineQueue, Formatting.Indented));
        }
    }
}