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

    // This matches the DTO structure returned by your ASP.NET API
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
            _queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_queue.json");
            LoadQueue();
        }

        public async Task<SyncResult> SendAttendanceAsync(AttendanceRequest req)
        {
            try
            {
                var json = JsonConvert.SerializeObject(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Calling your Cloud/Office API
                var response = await _httpClient.PostAsync("https://master-api.amsentry.dev/api/fingerprint/log-attendance", content);

                // Read the response body to get the "Message" from your AttendanceProcessingService
                var responseBody = await response.Content.ReadAsStringAsync();
                var apiData = JsonConvert.DeserializeObject<ApiResponseDto>(responseBody);

                // Use the message from API if available, otherwise use default reason
                string serverMessage = apiData?.Message ?? response.ReasonPhrase;

                if (response.IsSuccessStatusCode)
                {
                    return new SyncResult
                    {
                        IsSuccess = true,
                        IsNetworkError = false,
                        Message = serverMessage
                    };
                }

                // If it's a 4xx error (Conflict, Not Found, etc.)
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        IsNetworkError = false,
                        Message = serverMessage
                    };
                }

                // Server-side crash (500)
                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Server Error (500)" };
            }
            catch (HttpRequestException)
            {
                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = "Network Down" };
            }
            catch (Exception ex)
            {
                return new SyncResult { IsSuccess = false, IsNetworkError = true, Message = $"Error: {ex.Message}" };
            }
        }

        public void AddToQueue(AttendanceRequest req) { lock (_lock) { _offlineQueue.Add(req); PersistQueue(); } }
        public List<AttendanceRequest> GetQueue() { lock (_lock) return new List<AttendanceRequest>(_offlineQueue); }
        public void RemoveFromQueue(AttendanceRequest req) { lock (_lock) { _offlineQueue.Remove(req); PersistQueue(); } }

        private void LoadQueue()
        {
            if (File.Exists(_queueFilePath)) try
                {
                    var text = File.ReadAllText(_queueFilePath);
                    _offlineQueue = JsonConvert.DeserializeObject<List<AttendanceRequest>>(text) ?? new List<AttendanceRequest>();
                }
                catch { _offlineQueue = new List<AttendanceRequest>(); }
        }

        public void PersistQueue()
        {
            lock (_lock)
            {
                var json = JsonConvert.SerializeObject(_offlineQueue, Formatting.Indented);
                File.WriteAllText(_queueFilePath, json);
            }
        }
    }
}