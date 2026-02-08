namespace fingerprint_bridge
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.bnInit = new System.Windows.Forms.Button();
            this.bnOpen = new System.Windows.Forms.Button();
            this.bnEnroll = new System.Windows.Forms.Button();
            this.bnVerify = new System.Windows.Forms.Button();
            this.bnFree = new System.Windows.Forms.Button();
            this.bnClose = new System.Windows.Forms.Button();
            this.bnIdentify = new System.Windows.Forms.Button();
            this.textRes = new System.Windows.Forms.RichTextBox();
            this.picFPImg = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.cmbIdx = new System.Windows.Forms.ComboBox();
            this.txtUserId = new System.Windows.Forms.TextBox();
            this.btnClearLogs = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.picFPImg)).BeginInit();
            this.SuspendLayout();
            // 
            // bnInit
            // 
            this.bnInit.Location = new System.Drawing.Point(24, 23);
            this.bnInit.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bnInit.Name = "bnInit";
            this.bnInit.Size = new System.Drawing.Size(150, 44);
            this.bnInit.TabIndex = 0;
            this.bnInit.Text = "Initialize";
            this.bnInit.Click += new System.EventHandler(this.bnInit_Click);
            // 
            // bnOpen
            // 
            this.bnOpen.Enabled = false;
            this.bnOpen.Location = new System.Drawing.Point(186, 23);
            this.bnOpen.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bnOpen.Name = "bnOpen";
            this.bnOpen.Size = new System.Drawing.Size(150, 44);
            this.bnOpen.TabIndex = 1;
            this.bnOpen.Text = "Open";
            this.bnOpen.Click += new System.EventHandler(this.bnOpen_Click);
            // 
            // bnEnroll
            // 
            this.bnEnroll.Enabled = false;
            this.bnEnroll.Location = new System.Drawing.Point(24, 79);
            this.bnEnroll.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bnEnroll.Name = "bnEnroll";
            this.bnEnroll.Size = new System.Drawing.Size(150, 44);
            this.bnEnroll.TabIndex = 2;
            this.bnEnroll.Text = "Enroll";
            this.bnEnroll.Click += new System.EventHandler(this.bnEnroll_Click);
            // 
            // bnVerify
            // 
            this.bnVerify.Enabled = false;
            this.bnVerify.Location = new System.Drawing.Point(186, 79);
            this.bnVerify.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bnVerify.Name = "bnVerify";
            this.bnVerify.Size = new System.Drawing.Size(150, 44);
            this.bnVerify.TabIndex = 3;
            this.bnVerify.Text = "Verify";
            this.bnVerify.Click += new System.EventHandler(this.bnVerify_Click);
            // 
            // bnFree
            // 
            this.bnFree.Location = new System.Drawing.Point(0, 0);
            this.bnFree.Name = "bnFree";
            this.bnFree.Size = new System.Drawing.Size(75, 23);
            this.bnFree.TabIndex = 0;
            // 
            // bnClose
            // 
            this.bnClose.Enabled = false;
            this.bnClose.Location = new System.Drawing.Point(24, 135);
            this.bnClose.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bnClose.Name = "bnClose";
            this.bnClose.Size = new System.Drawing.Size(150, 44);
            this.bnClose.TabIndex = 5;
            this.bnClose.Text = "Close";
            this.bnClose.Click += new System.EventHandler(this.bnClose_Click);
            // 
            // bnIdentify
            // 
            this.bnIdentify.Location = new System.Drawing.Point(0, 0);
            this.bnIdentify.Name = "bnIdentify";
            this.bnIdentify.Size = new System.Drawing.Size(75, 23);
            this.bnIdentify.TabIndex = 0;
            // 
            // textRes
            // 
            this.textRes.Location = new System.Drawing.Point(24, 212);
            this.textRes.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.textRes.Name = "textRes";
            this.textRes.ReadOnly = true;
            this.textRes.Size = new System.Drawing.Size(916, 285);
            this.textRes.TabIndex = 7;
            this.textRes.Text = "";
            // 
            // picFPImg
            // 
            this.picFPImg.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picFPImg.Location = new System.Drawing.Point(684, 23);
            this.picFPImg.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.picFPImg.Name = "picFPImg";
            this.picFPImg.Size = new System.Drawing.Size(258, 175);
            this.picFPImg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picFPImg.TabIndex = 8;
            this.picFPImg.TabStop = false;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(0, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(100, 23);
            this.label1.TabIndex = 0;
            // 
            // cmbIdx
            // 
            this.cmbIdx.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbIdx.FormattingEnabled = true;
            this.cmbIdx.Location = new System.Drawing.Point(440, 27);
            this.cmbIdx.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.cmbIdx.Name = "cmbIdx";
            this.cmbIdx.Size = new System.Drawing.Size(76, 33);
            this.cmbIdx.TabIndex = 10;
            // 
            // txtUserId
            // 
            this.txtUserId.Location = new System.Drawing.Point(348, 83);
            this.txtUserId.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.txtUserId.Name = "txtUserId";
            this.txtUserId.Size = new System.Drawing.Size(96, 31);
            this.txtUserId.TabIndex = 11;
            // 
            // btnClearLogs
            // 
            this.btnClearLogs.Location = new System.Drawing.Point(202, 135);
            this.btnClearLogs.Name = "btnClearLogs";
            this.btnClearLogs.Size = new System.Drawing.Size(134, 43);
            this.btnClearLogs.TabIndex = 12;
            this.btnClearLogs.Text = "Clear Logs";
            this.btnClearLogs.UseVisualStyleBackColor = true;
            this.btnClearLogs.Click += new System.EventHandler(this.btnClearLogs_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(968, 523);
            this.Controls.Add(this.btnClearLogs);
            this.Controls.Add(this.txtUserId);
            this.Controls.Add(this.cmbIdx);
            this.Controls.Add(this.picFPImg);
            this.Controls.Add(this.textRes);
            this.Controls.Add(this.bnClose);
            this.Controls.Add(this.bnVerify);
            this.Controls.Add(this.bnEnroll);
            this.Controls.Add(this.bnOpen);
            this.Controls.Add(this.bnInit);
            this.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.Name = "Form1";
            this.Text = "Fingerprint Bridge";
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.picFPImg)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button bnInit;
        private System.Windows.Forms.Button bnOpen;
        private System.Windows.Forms.Button bnEnroll;
        private System.Windows.Forms.Button bnVerify;
        private System.Windows.Forms.Button bnFree;
        private System.Windows.Forms.Button bnClose;
        private System.Windows.Forms.Button bnIdentify;
        private System.Windows.Forms.RichTextBox textRes; // Changed type here
        private System.Windows.Forms.PictureBox picFPImg;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox cmbIdx;
        private System.Windows.Forms.TextBox txtUserId;
        private System.Windows.Forms.Button btnClearLogs;
    }
}