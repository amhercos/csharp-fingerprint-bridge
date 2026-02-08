using libzkfpcsharp;
using Newtonsoft.Json;
using Npgsql;
using Sample;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Demo
{
    public partial class Form1 : Form
    {
        private string connString = "Host=localhost;Port=5432;Username=postgres;Password=password1;Database=fingerprint";
        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        // --- PERSISTENCE SETTINGS ---
        private readonly string _queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_queue.json");
        private List<AttendanceRequest> _offlineQueue = new List<AttendanceRequest>();
        private System.Windows.Forms.Timer _retryTimer;
        private bool _isProcessingQueue = false;

        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;
        bool bIsTimeToDie = false;
        bool IsRegister = false;
        byte[] FPBuffer;
        int RegisterCount = 0;
        const int REGISTER_FINGER_COUNT = 3;
        byte[][] RegTmps = new byte[3][];
        byte[] RegTmp = new byte[2048];
        byte[] CapTmp = new byte[2048];
        int cbCapTmp = 2048;
        int cbRegTmp = 0;
        int iFid = 1;
        Thread captureThread = null;
        private int mfpWidth = 0;
        private int mfpHeight = 0;
        private System.Windows.Forms.Timer refreshTimer;
        const int MESSAGE_CAPTURED_OK = 0x0400 + 6;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();
            LoadOfflineQueueFromDisk(); // 1. Recover scans from previous session
            SetupRetryTimer();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FormHandle = this.Handle;
            for (int i = 0; i < 3; i++) RegTmps[i] = new byte[2048];

            this.FormClosing += (s, args) => SaveOfflineQueueToDisk(); // 2. Save scans on exit

            this.Shown += async (s, args) => {
                UpdateStatus("Initializing SLK20R...", Color.Black);
                await Task.Delay(500);
                bnInit_Click(null, null);
                if (mDevHandle == IntPtr.Zero && bnOpen.Enabled) bnOpen_Click(null, null);
            };
        }

        // --- DISK PERSISTENCE LOGIC ---

        private void SaveOfflineQueueToDisk()
        {
            try
            {
                if (_offlineQueue.Count > 0)
                {
                    string json = JsonConvert.SerializeObject(_offlineQueue, Formatting.Indented);
                    File.WriteAllText(_queueFilePath, json);
                }
                else if (File.Exists(_queueFilePath))
                {
                    File.Delete(_queueFilePath); // Clear file if no pending scans
                }
            }
            catch { /* Ignore errors on shutdown */ }
        }

        private void LoadOfflineQueueFromDisk()
        {
            try
            {
                if (File.Exists(_queueFilePath))
                {
                    string json = File.ReadAllText(_queueFilePath);
                    _offlineQueue = JsonConvert.DeserializeObject<List<AttendanceRequest>>(json) ?? new List<AttendanceRequest>();
                    if (_offlineQueue.Count > 0)
                    {
                        UpdateStatus($"Recovered {_offlineQueue.Count} scans from last session.", Color.Purple);
                    }
                }
            }
            catch { _offlineQueue = new List<AttendanceRequest>(); }
        }

        // --- SYNC LOGIC ---

        private void SetupRetryTimer()
        {
            _retryTimer = new System.Windows.Forms.Timer { Interval = 15000 }; // Sync every 15 seconds
            _retryTimer.Tick += async (s, e) => await ProcessOfflineQueue();
            _retryTimer.Start();
        }

        private async Task ProcessOfflineQueue()
        {
            if (_isProcessingQueue || _offlineQueue.Count == 0) return;
            _isProcessingQueue = true;

            var itemsToRetry = _offlineQueue.ToList();
            bool anySuccess = false;

            foreach (var request in itemsToRetry)
            {
                if (await SendToWebAPI(request))
                {
                    _offlineQueue.Remove(request);
                    anySuccess = true;
                }
                else break; // API still down, stop trying for this cycle
            }

            if (anySuccess) SaveOfflineQueueToDisk(); // Update the local file after successful syncs
            _isProcessingQueue = false;
        }

        private async void IdentifyFinger()
        {
            int fid = 0, score = 0;
            int ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);

            if (ret == zkfp.ZKFP_ERR_OK)
            {
                var request = new AttendanceRequest
                {
                    FingerprintId = fid,
                    TerminalId = 5,
                    Timestamp = DateTime.UtcNow
                };

                UpdateStatus($"Match: ID {fid}. Syncing...", Color.Blue);

                if (await SendToWebAPI(request))
                {
                    UpdateStatus($"SUCCESS: Logged ID {fid}", Color.Green);
                    await TriggerFeedback(true);
                }
                else
                {
                    _offlineQueue.Add(request);
                    SaveOfflineQueueToDisk(); // Save immediately to prevent data loss
                    UpdateStatus($"WIFI DOWN: ID {fid} queued locally.", Color.Purple);
                    await TriggerFeedback(true);
                }
            }
            else
            {
                UpdateStatus("FAILED: No Match Found", Color.Red);
                await TriggerFeedback(false);
            }
        }

        private async Task<bool> SendToWebAPI(AttendanceRequest request)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("https://master-api.amsentry.dev/api/fingerprint/log-attendance", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // --- HARDWARE BOILERPLATE (Minified for clarity) ---

        public void bnInit_Click(object sender, EventArgs e)
        {
            if (zkfp2.Init() == zkfperrdef.ZKFP_ERR_OK && zkfp2.GetDeviceCount() > 0)
            {
                cmbIdx.Items.Clear(); cmbIdx.Items.Add("0"); cmbIdx.SelectedIndex = 0;
                bnInit.Enabled = false; bnOpen.Enabled = true;
            }
        }

        public async void bnOpen_Click(object sender, EventArgs e)
        {
            mDevHandle = zkfp2.OpenDevice(cmbIdx.SelectedIndex);
            if (mDevHandle != IntPtr.Zero)
            {
                mDBHandle = zkfp2.DBInit();
                ControlSensor(101, 0); ControlSensor(102, 0); ControlSensor(103, 0);
                await RefreshScannersAsync();
                SetupSyncTimer(); // Timer to refresh user templates from DB

                byte[] p = new byte[4]; int s = 4;
                zkfp2.GetParameters(mDevHandle, 1, p, ref s); zkfp2.ByteArray2Int(p, ref mfpWidth);
                zkfp2.GetParameters(mDevHandle, 2, p, ref s); zkfp2.ByteArray2Int(p, ref mfpHeight);

                FPBuffer = new byte[mfpWidth * mfpHeight];
                bIsTimeToDie = false;
                captureThread = new Thread(DoCapture) { IsBackground = true };
                captureThread.Start();
                bnOpen.Enabled = false; bnClose.Enabled = true; bnEnroll.Enabled = true;
                UpdateStatus("SYSTEM ONLINE", Color.Green);
            }
        }

        private async Task RefreshScannersAsync()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    await conn.OpenAsync();
                    zkfp2.DBClear(mDBHandle);
                    using (var cmd = new NpgsqlCommand("SELECT user_id, template FROM user_fingerprints", conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                        while (await reader.ReadAsync()) zkfp2.DBAdd(mDBHandle, reader.GetInt32(0), (byte[])reader[1]);
                }
                SetNextAvailableFID();
            }
            catch { UpdateStatus("DB Sync Failed", Color.Red); }
        }

        private void SetupSyncTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer { Interval = 300000 };
            refreshTimer.Tick += async (s, e) => await RefreshScannersAsync();
            refreshTimer.Start();
        }

        private void SetNextAvailableFID()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("SELECT COALESCE(MAX(user_id), 0) + 1 FROM user_fingerprints", conn);
                    iFid = Convert.ToInt32(cmd.ExecuteScalar());
                    this.Invoke((MethodInvoker)(() => txtUserId.Text = iFid.ToString()));
                }
            }
            catch { iFid = 1; }
        }

        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                cbCapTmp = 2048;
                if (zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp) == 0)
                    SendMessage(FormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(200);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == MESSAGE_CAPTURED_OK)
            {
                MemoryStream ms = new MemoryStream();
                BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                picFPImg.Image = new Bitmap(ms);
                if (IsRegister) ProcessEnrollment(); else IdentifyFinger();
            }
            else base.DefWndProc(ref m);
        }

        private void ProcessEnrollment()
        {
            Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
            RegisterCount++;
            ControlSensor(101, 1); Thread.Sleep(100); ControlSensor(101, 0);
            if (RegisterCount >= REGISTER_FINGER_COUNT)
            {
                cbRegTmp = 2048;
                if (zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp) == zkfp.ZKFP_ERR_OK)
                {
                    zkfp2.DBAdd(mDBHandle, iFid, RegTmp);
                    SaveToDatabase(iFid, RegTmp);
                    UpdateStatus("Enroll Success!", Color.Green);
                    IsRegister = false; SetNextAvailableFID();
                }
                else { UpdateStatus("Merge Fail", Color.Red); IsRegister = false; }
            }
        }

        private void SaveToDatabase(int userId, byte[] template)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("INSERT INTO user_fingerprints (user_id, template) VALUES (@id, @tmp) ON CONFLICT (user_id) DO UPDATE SET template = @tmp", conn);
                    cmd.Parameters.AddWithValue("id", userId); cmd.Parameters.AddWithValue("tmp", template);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { UpdateStatus("Central DB Save Error", Color.Red); }
        }

        private void UpdateStatus(string text, Color color)
        {
            if (this.InvokeRequired) { this.Invoke(new MethodInvoker(() => UpdateStatus(text, color))); return; }
            textRes.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            textRes.ForeColor = color;
            textRes.SelectionStart = textRes.TextLength;
            textRes.ScrollToCaret();
        }

        private void ControlSensor(int code, int state)
        {
            if (mDevHandle == IntPtr.Zero) return;
            byte[] param = new byte[4]; zkfp2.Int2ByteArray(state, param);
            zkfp2.SetParameters(mDevHandle, code, param, 4);
        }

        private async Task TriggerFeedback(bool success)
        {
            if (mDevHandle == IntPtr.Zero) return;
            ControlSensor(101, 0); ControlSensor(102, 0); ControlSensor(103, 0);
            if (success) { ControlSensor(101, 1); ControlSensor(103, 1); await Task.Delay(400); }
            else { ControlSensor(102, 1); ControlSensor(103, 1); await Task.Delay(1000); }
            ControlSensor(103, 0); ControlSensor(101, 0); ControlSensor(102, 0); ControlSensor(104, 1);
        }

        public void bnEnroll_Click(object sender, EventArgs e) { if (int.TryParse(txtUserId.Text, out int cId)) { iFid = cId; IsRegister = true; RegisterCount = 0; UpdateStatus($"Enrolling ID {iFid}...", Color.Orange); } }
        public void txtUserId_Enter(object sender, EventArgs e) { if (sender is TextBox tb) tb.SelectAll(); }
        public void bnClose_Click(object sender, EventArgs e) { bIsTimeToDie = true; zkfp2.CloseDevice(mDevHandle); mDevHandle = IntPtr.Zero; }
        public void bnFree_Click(object sender, EventArgs e) { zkfp2.Terminate(); }
        public void bnVerify_Click(object sender, EventArgs e) { IsRegister = false; }
        public void bnIdentify_Click(object sender, EventArgs e) { IsRegister = false; }
    }

    public class AttendanceRequest { public int FingerprintId { get; set; } public int TerminalId { get; set; } public DateTime Timestamp { get; set; } }
}