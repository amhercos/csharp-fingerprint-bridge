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

namespace Demo
{
    public partial class Form1 : Form
    {
        // Database Connection String
        private string connString = "Host=localhost;Port=5432;Username=postgres;Password=password1;Database=fingerprint";

        // Static HttpClient to reuse connections
        private static readonly HttpClient httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

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
        }

        private void bnInit_Click(object sender, EventArgs e)
        {
            cmbIdx.Items.Clear();
            int ret = zkfperrdef.ZKFP_ERR_OK;
            if ((ret = zkfp2.Init()) == zkfperrdef.ZKFP_ERR_OK)
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
                    MessageBox.Show("No device connected!");
                }
            }
            else
            {
                MessageBox.Show("Initialize fail, ret=" + ret + " !");
            }
        }

        private void bnOpen_Click(object sender, EventArgs e)
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

            LoadTemplatesFromLocalDB();
            SetNextAvailableFID();

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

            textRes.Text = $"Device Opened. Users Loaded. Next ID: {iFid}";
        }

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
                Thread.Sleep(200);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MESSAGE_CAPTURED_OK:
                    {
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
                                textRes.Text = "This finger is already registered with ID: " + fid;
                                IsRegister = false;
                                RegisterCount = 0;
                                return;
                            }

                            if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                            {
                                textRes.Text = "Please press the SAME finger.";
                                return;
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

                                    textRes.Text = "Enrollment Success! User ID: " + iFid;
                                    // Optionally update iFid for the next auto-suggestion
                                    SetNextAvailableFID();
                                }
                                else
                                {
                                    textRes.Text = "Merge failed, error: " + ret;
                                }
                                IsRegister = false;
                                RegisterCount = 0;
                            }
                            else
                            {
                                textRes.Text = "Remaining presses: " + (REGISTER_FINGER_COUNT - RegisterCount);
                            }
                        }
                        else
                        {
                            IdentifyFinger();
                        }
                    }
                    break;
                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        // --- NEW METHOD: Checks if ID exists in Postgres ---
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
                        long count = (long)cmd.ExecuteScalar();
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error checking database: " + ex.Message);
                return true; // Stop enrollment if DB is unreachable
            }
        }

        private void SaveToDatabase(int userId, byte[] template)
        {
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    // We use ON CONFLICT as a safety net, but our button check prevents this usually
                    string sql = "INSERT INTO user_fingerprints (user_id, template) VALUES (@id, @tmp) " +
                                 "ON CONFLICT (user_id) DO UPDATE SET template = @tmp";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", userId);
                        cmd.Parameters.AddWithValue("tmp", template);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database Save Failed: " + ex.Message);
            }
        }

        private void IdentifyFinger()
        {
            int fid = 0, score = 0;
            int ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);

            if (zkfp.ZKFP_ERR_OK == ret)
            {
                // 1. Update the UI immediately so you know the hardware worked
                textRes.Text = "Matched ID: " + fid + " (Score: " + score + ")";

                // 2. Trigger the Cloud Call
                NotifyWebAPI(fid);
            }
            else
            {
                textRes.Text = "No match found.";
            }
        }

    private async void NotifyWebAPI(int identifiedFid)
        {
            try
            {
                var payload = new
                {
                    FingerprintId = identifiedFid,
                    TerminalId = 5,
                    Timestamp = DateTime.UtcNow
                };

                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // DOUBLE CHECK THIS URL - If it's wrong, the code jumps to the 'catch' block
                string apiUrl = "https://master-api.amsentry.dev/api/fingerprint/log-attendance";

                var response = await httpClient.PostAsync(apiUrl, content);

                this.BeginInvoke((MethodInvoker)delegate {
                    if (response.IsSuccessStatusCode)
                    {
                        textRes.Text += " - Cloud Updated!";
                        textRes.ForeColor = Color.Green;
                    }
                    else
                    {
                        // This will tell us if it's 401 (Unauthorized) or 404 (Not Found)
                        textRes.Text += $" - API Error: {(int)response.StatusCode}";
                        textRes.ForeColor = Color.Orange;
                    }
                });
            }
            catch (Exception ex)
            {
                this.BeginInvoke((MethodInvoker)delegate {
                    textRes.Text += " - Connection Failed!";
                    // This shows the actual error (e.g., "No such host is known")
                    MessageBox.Show("Cloud Error: " + ex.Message);
                });
            }
        }

        private void LoadTemplatesFromLocalDB()
        {
            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
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
            }
            catch (Exception ex) { MessageBox.Show("Loader Error: " + ex.Message); }
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
                        // Suggest the next ID in the textbox automatically
                        txtUserId.Text = iFid.ToString();
                    }
                }
            }
            catch { iFid = 1; }
        }

        // --- UPDATED ENROLL BUTTON WITH VALIDATION ---
        private void bnEnroll_Click(object sender, EventArgs e)
        {
            if (mDevHandle == IntPtr.Zero)
            {
                MessageBox.Show("Please open device first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtUserId.Text))
            {
                MessageBox.Show("Please type a User ID.");
                return;
            }

            if (int.TryParse(txtUserId.Text, out int customId))
            {
                // Check if ID is already in the database
                if (DoesUserExist(customId))
                {
                    MessageBox.Show($"User ID {customId} already exists! Choose a different ID.");
                    return;
                }

                iFid = customId;
                IsRegister = true;
                RegisterCount = 0;
                textRes.Text = $"Enrollment Started for ID {iFid}: Press finger 3 times.";
            }
            else
            {
                MessageBox.Show("Invalid ID. Please enter numbers only.");
            }
        }

        private void bnClose_Click(object sender, EventArgs e)
        {
            bIsTimeToDie = true;
            Thread.Sleep(500);
            zkfp2.CloseDevice(mDevHandle);
            mDevHandle = IntPtr.Zero;
            bnOpen.Enabled = true;
            bnClose.Enabled = false;
            textRes.Text = "Device Closed.";
        }

        private void bnFree_Click(object sender, EventArgs e)
        {
            zkfp2.Terminate();
            bnInit.Enabled = true;
            bnFree.Enabled = false;
        }

        private void bnVerify_Click(object sender, EventArgs e) { bIdentify = false; IsRegister = false; }
        private void bnIdentify_Click(object sender, EventArgs e) { bIdentify = true; IsRegister = false; }
        private void txtUserId_Enter(object sender, EventArgs e)
        {
            // Clear the status label
            textRes.Text = "Waiting for ID...";
            textRes.ForeColor = Color.Black;

            // Optional: Select all text so you can just start typing over the old ID
            txtUserId.SelectAll();
        }
    }
}