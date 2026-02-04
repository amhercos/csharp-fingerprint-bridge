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
        // --- Configuration ---
        private string connString = "Host=localhost;Port=5432;Username=postgres;Password=password1;Database=fingerprint";
        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        // --- Hardware Handles ---
        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;

        // --- State Management ---
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
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FormHandle = this.Handle;
            for (int i = 0; i < 3; i++) RegTmps[i] = new byte[2048];

            this.Shown += async (s, args) => {
                UpdateStatus("Initializing SLK20R...", Color.Black);
                await Task.Delay(500);
                bnInit_Click(null, null);
                if (mDevHandle == IntPtr.Zero && bnOpen.Enabled)
                {
                    bnOpen_Click(null, null);
                }
            };
        }

        // --- SENSOR HARDWARE CONTROLS ---

        private void ControlSensor(int code, int state)
        {
            if (mDevHandle == IntPtr.Zero) return;
            byte[] param = new byte[4];
            zkfp2.Int2ByteArray(state, param);

            // 101: Green LED, 102: Red LED, 103: Buzzer (Beep), 104: White Backlight
            zkfp2.SetParameters(mDevHandle, code, param, 4);
        }

        private async Task TriggerFeedback(bool success)
        {
            if (mDevHandle == IntPtr.Zero) return;

            // Step 1: Force all LEDs and Beep OFF to reset state
            ControlSensor(101, 0);
            ControlSensor(102, 0);
            ControlSensor(103, 0);
            ControlSensor(104, 0); // Turn off white light so color is vivid

            if (success)
            {
                ControlSensor(101, 1); // Green ON
                ControlSensor(103, 1); // Beep ON
                await Task.Delay(300);
                ControlSensor(103, 0);
                await Task.Delay(300);
                ControlSensor(101, 0);
            }
            else
            {
                // RED + BEEP for Conflict/Error
                ControlSensor(102, 1); // Red ON
                ControlSensor(103, 1); // Beep ON
                await Task.Delay(1000); // 1-second long error signal
                ControlSensor(103, 0);
                ControlSensor(102, 0);
            }

            ControlSensor(104, 1); // White light back ON for next scan
        }

        // --- CORE IDENTIFICATION ---

        private async void IdentifyFinger()
        {
            int fid = 0, score = 0;
            int ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);

            if (ret == zkfp.ZKFP_ERR_OK)
            {
                UpdateStatus($"Local Match ID {fid}. Syncing Server...", Color.Blue);
                bool apiResult = await NotifyWebAPI(fid);

                if (apiResult)
                {
                    UpdateStatus($"SUCCESS: Logged ID {fid}", Color.Green);
                    await TriggerFeedback(true);
                }
                else
                {
                    // SERVER ERROR / CONFLICT
                    UpdateStatus("SERVER ERROR / CONFLICT", Color.DarkRed);
                    await TriggerFeedback(false);
                }
            }
            else
            {
                UpdateStatus("FAILED: No Match Found", Color.Red);
                await TriggerFeedback(false);
            }
        }

        private async Task<bool> NotifyWebAPI(int identifiedFid)
        {
            try
            {
                var payload = new { FingerprintId = identifiedFid, TerminalId = 5, Timestamp = DateTime.UtcNow };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("https://master-api.amsentry.dev/api/fingerprint/log-attendance", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // --- EVENT HANDLERS (FIXES CS1061 ERRORS) ---

        public void bnInit_Click(object sender, EventArgs e)
        {
            int ret = zkfp2.Init();
            if (ret == zkfperrdef.ZKFP_ERR_OK)
            {
                int nCount = zkfp2.GetDeviceCount();
                if (nCount > 0)
                {
                    cmbIdx.Items.Clear();
                    for (int i = 0; i < nCount; i++) cmbIdx.Items.Add(i.ToString());
                    cmbIdx.SelectedIndex = 0;
                    bnInit.Enabled = false; bnOpen.Enabled = true;
                }
            }
        }

        public async void bnOpen_Click(object sender, EventArgs e)
        {
            mDevHandle = zkfp2.OpenDevice(cmbIdx.SelectedIndex);
            if (mDevHandle != IntPtr.Zero)
            {
                mDBHandle = zkfp2.DBInit();

                // DISABLE AUTOMATIC HARDWARE FEEDBACK
                byte[] disable = new byte[4];
                zkfp2.Int2ByteArray(0, disable);
                zkfp2.SetParameters(mDevHandle, 101, disable, 4);
                zkfp2.SetParameters(mDevHandle, 102, disable, 4);
                zkfp2.SetParameters(mDevHandle, 103, disable, 4);

                await RefreshScannersAsync();
                SetupRefreshTimer();

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

        public void bnEnroll_Click(object sender, EventArgs e) // Name matched to error log
        {
            if (int.TryParse(txtUserId.Text, out int customId))
            {
                iFid = customId; IsRegister = true; RegisterCount = 0;
                UpdateStatus($"Enrolling ID {iFid}...", Color.Orange);
            }
        }

        public void txtUserId_Enter(object sender, EventArgs e) // Name matched to error log
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        // --- BACKGROUND LOGIC ---

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
                    {
                        while (await reader.ReadAsync()) zkfp2.DBAdd(mDBHandle, reader.GetInt32(0), (byte[])reader[1]);
                    }
                }
                SetNextAvailableFID();
            }
            catch { }
        }

        private void SetupRefreshTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer { Interval = 1800000 };
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
            using (var conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                var cmd = new NpgsqlCommand("INSERT INTO user_fingerprints (user_id, template) VALUES (@id, @tmp) ON CONFLICT (user_id) DO UPDATE SET template = @tmp", conn);
                cmd.Parameters.AddWithValue("id", userId); cmd.Parameters.AddWithValue("tmp", template);
                cmd.ExecuteNonQuery();
            }
        }

        private void UpdateStatus(string text, Color color)
        {
            if (this.InvokeRequired) { this.Invoke(new MethodInvoker(() => UpdateStatus(text, color))); return; }
            textRes.AppendText(Environment.NewLine + text);
            textRes.ForeColor = color;
            textRes.SelectionStart = textRes.TextLength;
            textRes.ScrollToCaret();
        }

        // DESIGNER PLACEHOLDERS
        public void bnClose_Click(object sender, EventArgs e) { bIsTimeToDie = true; zkfp2.CloseDevice(mDevHandle); mDevHandle = IntPtr.Zero; }
        public void bnFree_Click(object sender, EventArgs e) { zkfp2.Terminate(); }
        public void bnVerify_Click(object sender, EventArgs e) { IsRegister = false; }
        public void bnIdentify_Click(object sender, EventArgs e) { IsRegister = false; }
    }
}