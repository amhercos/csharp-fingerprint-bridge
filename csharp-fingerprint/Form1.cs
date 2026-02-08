using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using libzkfpcsharp;
using fingerprint_bridge.Models;
using fingerprint_bridge.Services;

namespace fingerprint_bridge
{
    public partial class Form1 : Form
    {
        private AppSettings _settings;
        private DatabaseService _dbService;
        private AttendanceSyncService _syncService;

        private IntPtr mDevHandle = IntPtr.Zero;
        private IntPtr mDBHandle = IntPtr.Zero;
        private IntPtr mFormHandle = IntPtr.Zero;
        private bool bIsTimeToDie = false;
        private bool IsRegister = false;
        private int RegisterCount = 0;
        private byte[][] RegTmps = new byte[3][];
        private byte[] RegTmp = new byte[2048];
        private byte[] CapTmp = new byte[2048];
        private byte[] FPBuffer;
        private int mfpWidth, mfpHeight, iFid = 1;
        private Thread captureThread = null;
        private const int MESSAGE_CAPTURED_OK = 0x0400 + 6;
        private bool _isInitializing = false;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();
            SetupApp();
        }

        private void SetupApp()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            _settings = File.Exists(configPath) ? JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(configPath)) : new AppSettings();
            _dbService = new DatabaseService(_settings);
            _syncService = new AttendanceSyncService();

            zkfp2.Init();

            var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
            watchdog.Tick += async (s, e) => await CheckHardwareStatus();
            watchdog.Start();

            var retryTimer = new System.Windows.Forms.Timer { Interval = 20000 };
            retryTimer.Tick += async (s, e) => await ProcessOfflineQueueAsync();
            retryTimer.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            mFormHandle = this.Handle;
            for (int i = 0; i < 3; i++) RegTmps[i] = new byte[2048];
            this.FormClosing += (s, args) => {
                bIsTimeToDie = true;
                _syncService.PersistQueue();
                CleanupDevice();
                zkfp2.Terminate();
            };
        }

        private async Task CheckHardwareStatus()
        {
            if (mDevHandle != IntPtr.Zero || _isInitializing) return;
            if (zkfp2.GetDeviceCount() > 0)
            {
                _isInitializing = true;
                InitializeScannerHardware();
            }
        }

        private void InitializeScannerHardware()
        {
            try
            {
                mDevHandle = zkfp2.OpenDevice(0);
                if (mDevHandle != IntPtr.Zero)
                {
                    byte[] p = new byte[4]; int s2 = 4;
                    zkfp2.GetParameters(mDevHandle, 1, p, ref s2); zkfp2.ByteArray2Int(p, ref mfpWidth);
                    zkfp2.GetParameters(mDevHandle, 2, p, ref s2); zkfp2.ByteArray2Int(p, ref mfpHeight);
                    FPBuffer = new byte[mfpWidth * mfpHeight];
                    if (mDBHandle == IntPtr.Zero) mDBHandle = zkfp2.DBInit();

                    bIsTimeToDie = false;
                    captureThread = new Thread(DoCapture) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                    captureThread.Start();

                    UpdateStatus("Scanner Ready.", Color.Green);
                    Task.Run(() => SyncTemplatesBackground());
                }
            }
            catch { mDevHandle = IntPtr.Zero; }
            finally { _isInitializing = false; }
        }

        private async Task SyncTemplatesBackground()
        {
            try
            {
                var list = await _dbService.GetAllTemplatesAsync();
                zkfp2.DBClear(mDBHandle);
                foreach (var item in list) zkfp2.DBAdd(mDBHandle, item.UserId, item.Template);
                UpdateStatus($"Sync: Loaded {list.Count} templates.", Color.DimGray);
            }
            catch { }
        }

        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                if (mDevHandle == IntPtr.Zero) break;
                int cb = 2048;
                int ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cb);
                if (ret == 0) SendMessage(mFormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                else if (ret == -7) { UpdateStatus("Hardware Disconnected.", Color.Red); break; }
                Thread.Sleep(100);
            }
            CleanupDevice();
        }

        private async Task IdentifyFingerOnUI()
        {
            int fid = 0, score = 0;
            int ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);

            if (ret == zkfp.ZKFP_ERR_OK)
            {
                var req = new AttendanceRequest { FingerprintId = fid, TerminalId = _settings.TerminalId, Timestamp = DateTime.UtcNow };
                UpdateStatus($"Verified User {fid}. Syncing...", Color.Blue);

                var result = await _syncService.SendAttendanceAsync(req);

                if (result.IsSuccess)
                {
                    UpdateStatus($"[Cloud] Success: User {fid} logged.", Color.Green);
                    await TriggerFeedback(true);
                }
                else if (result.IsNetworkError)
                {
                    _syncService.AddToQueue(req);
                    UpdateStatus($"[Offline] WiFi Down. User {fid} queued locally.", Color.Orange);
                    await TriggerFeedback(true);
                }
                else
                {
                    UpdateStatus($"[API Error] Rejected User {fid}: {result.Message}", Color.Red);
                    await TriggerFeedback(false);
                }
            }
            else { UpdateStatus("Verify Failed: No match.", Color.DarkRed); await TriggerFeedback(false); }
        }

        private async Task ProcessOfflineQueueAsync()
        {
            var queue = _syncService.GetQueue();
            if (queue.Count == 0) return;

            int successCount = 0;
            foreach (var req in queue)
            {
                var res = await _syncService.SendAttendanceAsync(req);
                if (res.IsSuccess) { _syncService.RemoveFromQueue(req); successCount++; }
                else if (res.IsNetworkError) break;
                else _syncService.RemoveFromQueue(req);
            }

            if (successCount > 0) UpdateStatus($"[Sync] Backlog: Uploaded {successCount} records.", Color.Teal);
        }

        private void CleanupDevice()
        {
            if (mDevHandle != IntPtr.Zero)
            {
                IntPtr h = mDevHandle;
                mDevHandle = IntPtr.Zero;
                zkfp2.CloseDevice(h);
            }
        }

        private void UpdateStatus(string t, Color c)
        {
            if (this.IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => UpdateStatus(t, c))); return; }
            textRes.SelectionStart = textRes.TextLength;
            textRes.SelectionColor = c;
            textRes.AppendText($"[{DateTime.Now:HH:mm:ss}] {t}\n");
            textRes.ScrollToCaret();
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == MESSAGE_CAPTURED_OK && !this.IsDisposed)
            {
                this.BeginInvoke((MethodInvoker)async delegate {
                    MemoryStream ms = new MemoryStream();
                    try
                    {
                        BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                        if (picFPImg.Image != null) picFPImg.Image.Dispose();
                        picFPImg.Image = new Bitmap(ms);
                        if (IsRegister) HandleEnroll(); else await IdentifyFingerOnUI();
                    }
                    catch { }
                    finally { ms.Close(); ms.Dispose(); }
                });
            }
            else base.DefWndProc(ref m);
        }

        private void HandleEnroll()
        {
            Array.Copy(CapTmp, RegTmps[RegisterCount++], 2048);
            if (RegisterCount >= 3)
            {
                int cb = 2048;
                if (zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cb) == zkfp.ZKFP_ERR_OK)
                {
                    zkfp2.DBAdd(mDBHandle, iFid, RegTmp);
                    _dbService.SaveTemplate(iFid, RegTmp);
                    UpdateStatus("Enroll Success!", Color.Green);
                    IsRegister = false; RegisterCount = 0;
                }
                else { UpdateStatus("Enroll Failed.", Color.Red); IsRegister = false; RegisterCount = 0; }
            }
        }

        private async Task TriggerFeedback(bool success)
        {
            if (mDevHandle == IntPtr.Zero) return;
            byte[] p = new byte[4];
            zkfp2.Int2ByteArray(success ? 1 : 0, p); zkfp2.SetParameters(mDevHandle, 101, p, 4);
            zkfp2.Int2ByteArray(!success ? 1 : 0, p); zkfp2.SetParameters(mDevHandle, 102, p, 4);
            zkfp2.Int2ByteArray(1, p); zkfp2.SetParameters(mDevHandle, 103, p, 4);
            await Task.Delay(500);
            zkfp2.Int2ByteArray(0, p);
            zkfp2.SetParameters(mDevHandle, 101, p, 4); zkfp2.SetParameters(mDevHandle, 102, p, 4); zkfp2.SetParameters(mDevHandle, 103, p, 4);
        }

        // Button events remain the same...
        public void bnInit_Click(object sender, EventArgs e) => _ = CheckHardwareStatus();
        public void bnOpen_Click(object sender, EventArgs e) => _ = CheckHardwareStatus();
        public void btnClearLogs_Click(object sender, EventArgs e) => textRes.Clear();
        public void bnEnroll_Click(object sender, EventArgs e) { if (int.TryParse(txtUserId.Text, out int id)) { iFid = id; IsRegister = true; RegisterCount = 0; } }
        public void bnVerify_Click(object sender, EventArgs e) => IsRegister = false;
        public void bnClose_Click(object sender, EventArgs e) { bIsTimeToDie = true; CleanupDevice(); }
    }
}