using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using libzkfpcsharp;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using Sample;
using Npgsql;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Demo
{
    public partial class Form1 : Form
    {
        // Database Connection String
        private string connString = "Host=localhost;Port=5432;Username=postgres;Password=password1;Database=fingerprint";

        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;
        bool bIsTimeToDie = false;
        bool IsRegister = false;
        bool bIdentify = true;
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
            // Link the Shown event via code to ensure it runs without needing designer changes
            this.Shown += new System.EventHandler(this.Form1_Shown);
        }

        // AUTO-START LOGIC FOR HEADLESS MINI-PC
        private async void Form1_Shown(object sender, EventArgs e)
        {
            textRes.Text = "System starting up...";
            await Task.Delay(2000); // Give USB drivers time to settle

            bnInit_Click(null, null); // Initialize hardware

            if (bnOpen.Enabled)
            {
                bnOpen_Click(null, null); // Open device
                textRes.Text = "SYSTEM READY: Auto-connected.";
            }
        }

        private void bnInit_Click(object sender, EventArgs e)
        {
            cmbIdx.Items.Clear();
            int ret = zkfp2.Init();
            if (ret == zkfperrdef.ZKFP_ERR_OK)
            {
                int nCount = zkfp2.GetDeviceCount();
                if (nCount > 0)
                {
                    for (int i = 0; i < nCount; i++) cmbIdx.Items.Add(i.ToString());
                    cmbIdx.SelectedIndex = 0;
                    bnInit.Enabled = false;
                    bnFree.Enabled = true;
                    bnOpen.Enabled = true;
                }
                else
                {
                    zkfp2.Terminate();
                    textRes.Text = "No device connected!";
                }
            }
            else
            {
                MessageBox.Show("Initialize fail, ret=" + ret + " !");
            }
        }

        private async void bnOpen_Click(object sender, EventArgs e)
        {
            if (IntPtr.Zero == (mDevHandle = zkfp2.OpenDevice(cmbIdx.SelectedIndex)))
            {
                MessageBox.Show("OpenDevice fail");
                return;
            }
            if (IntPtr.Zero == (mDBHandle = zkfp2.DBInit()))
            {
                MessageBox.Show("Init DB fail");
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
                return;
            }

            // Sync templates and start auto-sync timer
            await RefreshScannersAsync();
            SetupRefreshTimer();

            bnOpen.Enabled = false;
            bnClose.Enabled = true;
            bnEnroll.Enabled = true;
            bnVerify.Enabled = true;
            bnIdentify.Enabled = true;

            RegisterCount = 0;
            cbRegTmp = 0;
            for (int i = 0; i < 3; i++) RegTmps[i] = new byte[2048];

            byte[] paramValue = new byte[4];
            int size = 4;
            zkfp2.GetParameters(mDevHandle, 1, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpWidth);
            size = 4;
            zkfp2.GetParameters(mDevHandle, 2, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpHeight);

            FPBuffer = new byte[mfpWidth * mfpHeight];
            bIsTimeToDie = false;
            captureThread = new Thread(new ThreadStart(DoCapture));
            captureThread.IsBackground = true;
            captureThread.Start();
        }

        private void SetupRefreshTimer()
        {
            if (refreshTimer != null) refreshTimer.Stop();
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1800000; // 30 Minutes
            refreshTimer.Tick += async (s, e) => await RefreshScannersAsync();
            refreshTimer.Start();
        }

        private async Task RefreshScannersAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                    {
                        conn.Open();
                        zkfp2.DBClear(mDBHandle);
                        string sql = "SELECT user_id, template FROM user_fingerprints";
                        using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int id = reader.GetInt32(0);
                                byte[] tmp = (byte[])reader[1];
                                zkfp2.DBAdd(mDBHandle, id, tmp);
                            }
                        }
                    }
                });
                SetNextAvailableFID();
            }
            catch { /* Silent fail for auto-sync */ }
        }

        // HARDWARE RECOVERY LOGIC ADDED HERE
        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                cbCapTmp = 2048;
                int ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp);
                if (ret == zkfp.ZKFP_ERR_OK)
                {
                    SendMessage(FormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                }
                else if (ret == -1) // Unplugged or hardware error
                {
                    Thread.Sleep(2000); // Wait and retry
                }
                Thread.Sleep(200);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MESSAGE_CAPTURED_OK:
                    MemoryStream ms = new MemoryStream();
                    BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                    this.picFPImg.Image = new Bitmap(ms);

                    if (IsRegister)
                    {
                        int ret = zkfp.ZKFP_ERR_OK;
                        int fid = 0, score = 0;
                        ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                        if (zkfp.ZKFP_ERR_OK == ret)
                        {
                            textRes.Text = "Finger already registered with ID: " + fid;
                            IsRegister = false; RegisterCount = 0; return;
                        }

                        if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                        {
                            textRes.Text = "Please press the SAME finger."; return;
                        }

                        Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
                        RegisterCount++;

                        if (RegisterCount >= REGISTER_FINGER_COUNT)
                        {
                            cbRegTmp = 2048;
                            ret = zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp);
                            if (zkfp.ZKFP_ERR_OK == ret)
                            {
                                zkfp2.DBAdd(mDBHandle, iFid, RegTmp);
                                SaveToDatabase(iFid, RegTmp);
                                textRes.Text = "Enrollment Success! ID: " + iFid;
                                SetNextAvailableFID();
                            }
                            else { textRes.Text = "Merge failed: " + ret; }
                            IsRegister = false; RegisterCount = 0;
                        }
                        else { textRes.Text = "Remaining presses: " + (REGISTER_FINGER_COUNT - RegisterCount); }
                    }
                    else { IdentifyFinger(); }
                    break;
                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        private void IdentifyFinger()
        {
            int fid = 0, score = 0;
            int ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
            if (zkfp.ZKFP_ERR_OK == ret)
            {
                textRes.Text = "Matched ID: " + fid;
                NotifyWebAPI(fid);
            }
            else { textRes.Text = "No match found."; }
        }

        private async void NotifyWebAPI(int identifiedFid)
        {
            try
            {
                var payload = new { FingerprintId = identifiedFid, TerminalId = 5, Timestamp = DateTime.UtcNow };
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string apiUrl = "https://master-api.amsentry.dev/api/fingerprint/log-attendance";
                var response = await httpClient.PostAsync(apiUrl, content);
                this.BeginInvoke((MethodInvoker)delegate {
                    if (response.IsSuccessStatusCode) { textRes.Text += " - Cloud Updated!"; textRes.ForeColor = Color.Green; }
                    else { textRes.Text += $" - API Error: {(int)response.StatusCode}"; }
                });
            }
            catch (Exception ex)
            {
                this.BeginInvoke((MethodInvoker)delegate { textRes.Text += " - API Error!"; });
            }
        }

        private void SaveToDatabase(int userId, byte[] template)
        {
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string sql = "INSERT INTO user_fingerprints (user_id, template) VALUES (@id, @tmp) ON CONFLICT (user_id) DO UPDATE SET template = @tmp";
                    using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", userId);
                        cmd.Parameters.AddWithValue("tmp", template);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("DB Save Error: " + ex.Message); }
        }

        private void SetNextAvailableFID()
        {
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string sql = "SELECT COALESCE(MAX(user_id), 0) + 1 FROM user_fingerprints";
                    using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                    {
                        iFid = Convert.ToInt32(cmd.ExecuteScalar());
                        this.Invoke((MethodInvoker)delegate { txtUserId.Text = iFid.ToString(); });
                    }
                }
            }
            catch { iFid = 1; }
        }

        private bool DoesUserExist(int userId)
        {
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string sql = "SELECT COUNT(1) FROM user_fingerprints WHERE user_id = @id";
                    using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", userId);
                        return (long)cmd.ExecuteScalar() > 0;
                    }
                }
            }
            catch { return true; }
        }

        private void bnEnroll_Click(object sender, EventArgs e)
        {
            if (mDevHandle == IntPtr.Zero) return;
            if (int.TryParse(txtUserId.Text, out int customId))
            {
                if (DoesUserExist(customId)) { MessageBox.Show("ID exists!"); return; }
                iFid = customId; IsRegister = true; RegisterCount = 0;
                textRes.Text = "Enrollment Started. Press 3 times.";
            }
        }

        private void bnClose_Click(object sender, EventArgs e)
        {
            bIsTimeToDie = true;
            if (refreshTimer != null) refreshTimer.Stop();
            Thread.Sleep(500);
            zkfp2.CloseDevice(mDevHandle);
            mDevHandle = IntPtr.Zero;
            bnOpen.Enabled = true;
            bnClose.Enabled = false;
        }

        private void bnFree_Click(object sender, EventArgs e) { zkfp2.Terminate(); bnInit.Enabled = true; bnFree.Enabled = false; }
        private void bnVerify_Click(object sender, EventArgs e) { bIdentify = false; IsRegister = false; }
        private void bnIdentify_Click(object sender, EventArgs e) { bIdentify = true; IsRegister = false; }
        private void txtUserId_Enter(object sender, EventArgs e) { txtUserId.SelectAll(); }
    }
}