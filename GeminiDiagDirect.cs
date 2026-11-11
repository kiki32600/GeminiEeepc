using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;

namespace GeminiDiagDirect
{
    [ComVisible(true)]
    public class DiagScriptBridge
    {
        private MainForm form;
        public DiagScriptBridge(MainForm f) { this.form = f; }

        public void QueryDtc(string dtcCode)
        {
            form.RunQuickQuery("Analyse en détail le code défaut " + dtcCode + " : conditions exactes de détection, causes racines fréquentes (capteur, faisceau, mécanique/actionneur) et procédure de contrôle pas-à-pas au multimètre ou à l'oscilloscope.");
        }

        public void CopyBlock(string text)
        {
            Clipboard.SetText(text);
        }

        public void ExportReport()
        {
            form.ExportCurrentSessionReport();
        }
    }

    public class MainForm : Form
    {
        private TextBox txtInput;
        private WebBrowser webChat;
        private ComboBox cmbModel;
        private Label lblAttachment;
        private Button btnRemoveAttachment;
        private Button btnSend;
        private Button btnAttach;
        private Button btnNewSession;
        private Button btnLoadHistory;
        private Button btnExportReport;
        private Button btnQuickMenu;
        private Button btnFontPlus;
        private Button btnFontMinus;
        private PictureBox picLogo;
        private Panel topBar;
        private Panel bottomBar;

        private string apiKey = ""; // clé retirée du code
        private string attachedFilePath = null;
        private Bitmap attachedImageBitmap = null;
        private string currentSessionFile = null;
        private string chatHtmlPath = @"C:\Diag\chat_view.html";
        private string logoPath = @"C:\Diag\logo.png";
        private string iconPath = @"C:\Diag\app.ico";

        private int currentFontSize = 14;
        private List<Dictionary<string, object>> chatContents = new List<Dictionary<string, object>>();
        private StringBuilder messagesHtml = new StringBuilder();

        private volatile HttpWebRequest activeWebRequest = null;
        private volatile bool isAnalyzing = false;
        private volatile bool isCancelled = false;

        [STAThread]
        public static void Main()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

            try
            {
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    rk.SetValue(AppDomain.CurrentDomain.FriendlyName, 11001, RegistryValueKind.DWord);
                }
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public MainForm()
        {
            this.Text = "GEMINI DIAG DIRECT - Atelier Diag Automobile (Mode Agent Actif)";
            this.Size = new Size(950, 750);
            this.MinimumSize = new Size(720, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AllowDrop = true;
            this.KeyPreview = true;
            this.BackColor = Color.FromArgb(30, 30, 30);

            if (File.Exists(iconPath))
            {
                try { this.Icon = new Icon(iconPath); } catch { }
            }

            Directory.CreateDirectory(@"C:\Diag\Gemini_History");
            Directory.CreateDirectory(@"C:\Diag\Logs_Input");

            InitUI();
            InitNewSession();

            this.DragEnter += MainForm_DragEnter;
            this.DragDrop += MainForm_DragDrop;
            this.KeyDown += MainForm_KeyDown;
            this.FormClosing += (s, e) => {
                isCancelled = true;
                if (activeWebRequest != null) { try { activeWebRequest.Abort(); } catch { } }
            };

            LoadAvailableModelsAsync();
        }

        private void InitUI()
        {
            topBar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(5), BackColor = Color.FromArgb(38, 38, 38) };

            picLogo = new PictureBox {
                Left = 8, Top = 6, Width = 34, Height = 34,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (File.Exists(logoPath))
            {
                try {
                    byte[] bytes = File.ReadAllBytes(logoPath);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        picLogo.Image = new Bitmap(ms);
                    }
                } catch { }
            }

            btnAttach = CreateStyledButton("📎 Joindre", 48, 9, 90, Color.FromArgb(45, 85, 125));
            btnAttach.Click += (s, e) => SelectAnyFile();

            btnQuickMenu = CreateStyledButton("⚡ Préréglages Diag ▼", 143, 9, 140, Color.FromArgb(50, 70, 95));
            btnQuickMenu.Click += (s, e) => ShowQuickMenu();

            btnNewSession = CreateStyledButton("🔄 Nouveau", 288, 9, 95, Color.FromArgb(55, 55, 55));
            btnNewSession.Click += (s, e) => InitNewSession();

            btnLoadHistory = CreateStyledButton("📂 Historique", 388, 9, 95, Color.FromArgb(55, 55, 55));
            btnLoadHistory.Click += (s, e) => LoadSession();

            btnExportReport = CreateStyledButton("📄 Exporter", 488, 9, 85, Color.FromArgb(45, 75, 55));
            btnExportReport.Click += (s, e) => ExportCurrentSessionReport();

            btnFontMinus = CreateStyledButton("A-", 578, 9, 32, Color.FromArgb(60, 60, 60));
            btnFontMinus.Click += (s, e) => AdjustFontSize(-2);

            btnFontPlus = CreateStyledButton("A+", 614, 9, 32, Color.FromArgb(60, 60, 60));
            btnFontPlus.Click += (s, e) => AdjustFontSize(+2);

            Label lblMod = new Label { Text = "Modèle :", Left = 655, Top = 14, Width = 55, ForeColor = Color.LightGray, Font = new Font("Segoe UI", 9f) };

            cmbModel = new ComboBox {
                Left = 715, Top = 11, Width = 210,
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f)
            };
            cmbModel.Items.AddRange(new string[] {
                "gemini-3.5-flash-lite",
                "gemini-3.8-flash",
                "gemini-3.6-flash",
                "gemini-3.5-flash",
                "gemini-4-argon",
                "gemini-3.1-pro-preview"
            });
            cmbModel.SelectedIndex = 0;

            topBar.Controls.AddRange(new Control[] { 
                picLogo, btnAttach, btnQuickMenu, btnNewSession, btnLoadHistory, 
                btnExportReport, btnFontMinus, btnFontPlus, lblMod, cmbModel 
            });

            bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 105, Padding = new Padding(8), BackColor = Color.FromArgb(35, 35, 35) };

            lblAttachment = new Label { Text = "Pièce jointe : Aucune (Entrée = Envoyer, Maj+Entrée = Saut de ligne)", Left = 10, Top = 4, Width = 700, AutoEllipsis = true, ForeColor = Color.Silver, Font = new Font("Segoe UI", 8.5f) };

            btnRemoveAttachment = new Button {
                Text = "✕ Retirer", Left = 720, Top = 2, Width = 70, Height = 20,
                Visible = false, BackColor = Color.FromArgb(80, 30, 30), ForeColor = Color.LightCoral,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7.5f)
            };
            btnRemoveAttachment.FlatAppearance.BorderSize = 0;
            btnRemoveAttachment.Click += (s, e) => ClearAttachment();

            txtInput = new TextBox {
                Left = 10, Top = 26, Width = 770, Height = 70,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(22, 22, 22), ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f)
            };

            txtInput.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter)
                {
                    if (e.Shift)
                    {
                        // Saut de ligne
                    }
                    else
                    {
                        e.SuppressKeyPress = true;
                        SendMessage();
                    }
                }
            };

            btnSend = new Button {
                Text = "ENVOYER\n(Entrée)",
                Left = 790, Top = 26, Width = 115, Height = 70,
                BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += (s, e) => SendMessage();

            bottomBar.Controls.AddRange(new Control[] { lblAttachment, btnRemoveAttachment, txtInput, btnSend });

            webChat = new WebBrowser {
                Dock = DockStyle.Fill,
                IsWebBrowserContextMenuEnabled = true,
                WebBrowserShortcutsEnabled = true,
                ObjectForScripting = new DiagScriptBridge(this)
            };
            webChat.DocumentCompleted += (s, e) => {
                try {
                    if (webChat.Document != null && webChat.Document.Window != null)
                        webChat.Document.Window.ScrollTo(0, 1000000);
                } catch { }
            };

            this.Controls.Add(webChat);
            this.Controls.Add(bottomBar);
            this.Controls.Add(topBar);

            this.Resize += (s, e) => {
                txtInput.Width = this.ClientSize.Width - 150;
                btnSend.Left = this.ClientSize.Width - 130;
                cmbModel.Width = Math.Max(130, this.ClientSize.Width - 730);
            };
        }

        private Button CreateStyledButton(string text, int left, int top, int width, Color backColor)
        {
            Button btn = new Button {
                Text = text, Left = left, Top = top, Width = width, Height = 28,
                BackColor = backColor, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void AdjustFontSize(int delta)
        {
            currentFontSize = Math.Max(11, Math.Min(24, currentFontSize + delta));
            RenderFullChatHtml();
        }

        private void ShowQuickMenu()
        {
            ContextMenu cm = new ContextMenu();
            cm.MenuItems.Add("📁 Scanner et analyser le dernier log récent", (s, e) => {
                txtInput.Text = "Scanne mes logs récents et analyse le dernier relevé enregistré.";
                SendMessage();
            });
            cm.MenuItems.Add("📊 Analyser Live Data (Pression Rail, Turbo & MAF)", (s, e) => {
                txtInput.Text = "Analyse en détail les paramètres d'injection directe, de pression turbo et de débit d'air par rapport à leurs consignes. Identifie tout écart ou saturation.";
                txtInput.Focus();
            });
            cm.MenuItems.Add("🔍 Recherche panne sur Code Défaut (DTC)", (s, e) => {
                txtInput.Text = "Procédure de diagnostic pour le code défaut : [Indiquez le code P0xxx]. Donne les contrôles au multimètre/oscilloscope et les causes fréquentes.";
                txtInput.Focus();
            });
            cm.MenuItems.Add("💨 Bilan Dépollution (FAP, Régénération & EGR)", (s, e) => {
                txtInput.Text = "Évalue l'état du système de dépollution : encrassement FAP, pression différentielle et fonctionnement de la vanne EGR. Affiche une jauge d'état.";
                txtInput.Focus();
            });
            cm.MenuItems.Add("🧪 Corrections débits injecteurs (mg/cp)", (s, e) => {
                txtInput.Text = "Analyse les valeurs de correction de débit des injecteurs. Les tolérances sont-elles dépassées ? Quel injecteur est suspect ?";
                txtInput.Focus();
            });
            cm.MenuItems.Add("🔌 Détecter mon interface OBD / Ports COM", (s, e) => {
                txtInput.Text = "Vérifie les ports COM et détecte mon interface de diagnostic USB branchée sur l'ordinateur.";
                SendMessage();
            });

            cm.Show(btnQuickMenu, new Point(0, btnQuickMenu.Height));
        }

        private void RenderFullChatHtml()
        {
            string html = @"<!DOCTYPE html>
<html>
<head>
<meta http-equiv='X-UA-Compatible' content='IE=edge' />
<meta charset='utf-8' />
<style>
    body {
        background-color: #1a1a1a;
        color: #e4e4e4;
        font-family: 'Segoe UI', Tahoma, Arial, sans-serif;
        font-size: " + currentFontSize + @"px;
        line-height: 1.5;
        margin: 0;
        padding: 15px;
        -webkit-user-select: text;
        -ms-user-select: text;
        user-select: text;
    }
    .banner {
        border-bottom: 1px solid #333;
        padding-bottom: 10px;
        margin-bottom: 15px;
    }
    .banner-title {
        font-size: " + (currentFontSize + 3) + @"px;
        font-weight: bold;
        color: #79c0ff;
        vertical-align: middle;
    }
    .banner-sub {
        font-size: " + (currentFontSize - 2) + @"px;
        color: #888;
        vertical-align: middle;
        margin-left: 6px;
    }
    .msg-box {
        margin-bottom: 16px;
        border-radius: 6px;
        padding: 12px 16px;
        max-width: 96%;
        word-wrap: break-word;
    }
    .msg-user {
        background-color: #1e334a;
        border-left: 4px solid #007acc;
        margin-left: auto;
    }
    .msg-gemini {
        background-color: #212922;
        border-left: 4px solid #28a745;
        margin-right: auto;
    }
    .msg-system {
        background-color: #2a2a2a;
        border-left: 4px solid #888;
        font-size: " + (currentFontSize - 1) + @"px;
        color: #aaa;
    }
    .msg-tool {
        background-color: #222933;
        border-left: 4px solid #6f42c1;
        font-size: " + (currentFontSize - 1) + @"px;
        color: #c9d1d9;
    }
    .msg-error {
        background-color: #442222;
        border-left: 4px solid #dc3545;
        color: #ff9999;
    }
    .header {
        font-weight: bold;
        font-size: 11px;
        margin-bottom: 6px;
        text-transform: uppercase;
        letter-spacing: 0.5px;
    }
    .user-hdr { color: #5dade2; }
    .gemini-hdr { color: #58d68d; }
    .system-hdr { color: #aaa; }
    .tool-hdr { color: #d2a8ff; }
    .error-hdr { color: #e74c3c; }

    /* TABLEAUX DE DIAG */
    table {
        border-collapse: collapse;
        width: 100%;
        margin: 10px 0;
        background-color: #141414;
        border: 1px solid #333;
    }
    th {
        background-color: #2c3e50;
        color: #ecf0f1;
        font-weight: 600;
        padding: 8px 10px;
        border: 1px solid #444;
        text-align: left;
    }
    td {
        padding: 6px 10px;
        border: 1px solid #333;
        font-family: 'Consolas', monospace;
        font-size: " + (currentFontSize - 1) + @"px;
    }
    tr:nth-child(even) { background-color: #1a1a1a; }
    tr:hover { background-color: #252525; }

    /* BADGES INTERACTIFS DTC */
    .dtc-badge {
        display: inline-block;
        padding: 2px 7px;
        border-radius: 4px;
        font-weight: bold;
        font-family: 'Consolas', monospace;
        background: #900c3f;
        color: #ffc4d6;
        border: 1px solid #c70039;
        cursor: pointer;
        margin: 2px;
    }
    .dtc-badge:hover {
        background: #c70039;
        color: #fff;
    }
    .badge-alert { background: #b02a37; color: #fff; padding: 2px 6px; border-radius: 3px; font-weight: bold; }
    .badge-warn  { background: #d97706; color: #000; padding: 2px 6px; border-radius: 3px; font-weight: bold; }
    .badge-ok    { background: #198754; color: #fff; padding: 2px 6px; border-radius: 3px; font-weight: bold; }

    /* COMPOSANTS CARTES ET JAUGES ATELIER (FAP / RCO / PRESSION) */
    .card {
        background: #181c18;
        border: 1px solid #28a745;
        border-radius: 6px;
        padding: 12px 16px;
        margin: 10px 0;
        max-width: 480px;
        box-shadow: 0 4px 8px rgba(0,0,0,0.4);
    }
    .title {
        font-size: 12px;
        font-weight: bold;
        color: #58d68d;
        margin-bottom: 8px;
        text-transform: uppercase;
        letter-spacing: 0.5px;
    }
    .gauge-container {
        background: #252525;
        border-radius: 4px;
        height: 24px;
        width: 100%;
        position: relative;
        overflow: hidden;
        border: 1px solid #444;
    }
    .gauge-fill {
        background: linear-gradient(90deg, #00B95C, #FBBC04 70%, #FC413D 88%);
        height: 100%;
    }
    .gauge-text {
        position: absolute;
        width: 100%;
        height: 100%;
        top: 0;
        left: 0;
        text-align: center;
        line-height: 24px;
        font-size: 11px;
        font-weight: bold;
        color: #ffffff;
        text-shadow: 1px 1px 2px #000;
    }
    .footer-info {
        display: block;
        margin-top: 8px;
        font-size: 11px;
        color: #aaa;
    }

    code {
        font-family: 'Consolas', monospace;
        background-color: #111;
        color: #f39c12;
        padding: 2px 6px;
        border-radius: 3px;
    }
    pre {
        background-color: #0f140f;
        border: 1px solid #28a745;
        padding: 10px;
        border-radius: 4px;
        overflow-x: auto;
        font-family: 'Consolas', monospace;
        color: #28eb6c;
        cursor: pointer;
        position: relative;
    }
    pre:hover { background-color: #151e15; }
    .copy-hint {
        font-size: 10px;
        color: #888;
        font-family: 'Segoe UI', sans-serif;
        display: block;
        margin-bottom: 4px;
        text-align: right;
    }
    h2, h3, h4 { color: #3498db; margin: 12px 0 6px 0; }
    ul, ol { margin: 6px 0; padding-left: 24px; }
    li { margin-bottom: 4px; }
    .badge {
        display: inline-block;
        padding: 2px 6px;
        border-radius: 3px;
        font-size: 11px;
        background: #2b3b4b;
        color: #79c0ff;
        border: 1px solid #388bfd;
        margin-right: 5px;
    }
</style>
<script>
    function copyBlock(el) {
        var text = el.innerText || el.textContent;
        text = text.replace(/\[Cliquer pour copier\]\s*/i, '');
        if (window.external) {
            window.external.CopyBlock(text);
        }
        var oldColor = el.style.borderColor;
        el.style.borderColor = '#00ffcc';
        setTimeout(function() { el.style.borderColor = oldColor; }, 600);
    }
    function queryDtc(code) {
        if (window.external) {
            window.external.QueryDtc(code);
        }
    }
</script>
</head>
<body>
<div class='banner'>
    <img src='file:///C:/Diag/logo.png' width='28' height='28' style='vertical-align:middle; margin-right:8px;' />
    <span class='banner-title'>GEMINI DIAG DIRECT</span>
    <span class='banner-sub'>• Atelier Diag Automobile (Agent Autonome Actif)</span>
</div>
<div id='chat-container'>" + messagesHtml.ToString() + @"</div>
</body>
</html>";

            try
            {
                File.WriteAllText(chatHtmlPath, html, Encoding.UTF8);
                webChat.Navigate(chatHtmlPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur écriture affichage : " + ex.Message);
            }
        }

        private void AppendMessageBlock(string role, string senderTitle, string htmlBody)
        {
            string boxClass = "msg-gemini";
            string hdrClass = "gemini-hdr";
            if (role == "user") { boxClass = "msg-user"; hdrClass = "user-hdr"; }
            else if (role == "system") { boxClass = "msg-system"; hdrClass = "system-hdr"; }
            else if (role == "tool") { boxClass = "msg-tool"; hdrClass = "tool-hdr"; }
            else if (role == "error") { boxClass = "msg-error"; hdrClass = "error-hdr"; }

            string time = DateTime.Now.ToString("HH:mm:ss");
            string div = "<div class='msg-box " + boxClass + "'>" +
                         "<div class='header " + hdrClass + "'>" + senderTitle + " • " + time + "</div>" +
                         "<div class='content'>" + htmlBody + "</div>" +
                         "</div>\n";

            messagesHtml.Append(div);
            RenderFullChatHtml();
        }

        private void InitNewSession()
        {
            chatContents.Clear();
            messagesHtml.Clear();
            ClearAttachment();
            currentSessionFile = Path.Combine(@"C:\Diag\Gemini_History", DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_Diag.json");

            AppendMessageBlock("system", "SYSTÈME ATELIER", 
                "Session prête. <b>Mode Agent Autonome Activé</b>.<br/>" +
                "• <b>Autonomie fichiers :</b> Dites simplement <i>\"Scanne mes logs récents\"</i> ou <i>\"Analyse le dernier relevé\"</i> et Gemini cherchera et lira le fichier sur le disque tout seul !<br/>" +
                "• <b>Terminal :</b> Gemini peut inspecter le réseau ou les ports COM avec votre accord.<br/>" +
                "• <b>Entrée</b> = Envoyer | <b>Maj + Entrée</b> = Saut de ligne.");
        }

        public void RunQuickQuery(string query)
        {
            txtInput.Text = query;
            SendMessage();
        }

        public void ExportCurrentSessionReport()
        {
            try
            {
                string reportPath = Path.Combine(@"C:\Diag\Gemini_History", "Rapport_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".html");
                File.Copy(chatHtmlPath, reportPath, true);
                MessageBox.Show("Rapport complet exporté avec succès :\n" + reportPath, "Export Diag", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur export : " + ex.Message);
            }
        }

        private void SelectAnyFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog { 
                Filter = "Fichiers compatibles (*.csv;*.txt;*.log;*.png;*.jpg;*.jpeg;*.bmp)|*.csv;*.txt;*.log;*.png;*.jpg;*.jpeg;*.bmp|Logs Diag (*.csv;*.txt;*.log)|*.csv;*.txt;*.log|Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers|*.*",
                Title = "Sélectionner un fichier Diag ou une Image"
            })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    SetAttachment(ofd.FileName);
                }
            }
        }

        private void SetAttachment(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
            {
                try
                {
                    attachedImageBitmap = new Bitmap(path);
                    attachedFilePath = null;
                    lblAttachment.Text = "Pièce jointe : [Image] " + Path.GetFileName(path) + " (" + attachedImageBitmap.Width + "x" + attachedImageBitmap.Height + ")";
                    lblAttachment.ForeColor = Color.LightGreen;
                    btnRemoveAttachment.Visible = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Impossible de charger l'image : " + ex.Message);
                }
            }
            else
            {
                attachedFilePath = path;
                attachedImageBitmap = null;
                FileInfo fi = new FileInfo(path);
                lblAttachment.Text = "Pièce jointe : [Log/Fichier] " + fi.Name + " (" + (fi.Length / 1024) + " Ko)";
                lblAttachment.ForeColor = Color.LightGreen;
                btnRemoveAttachment.Visible = true;
            }
        }

        private void ClearAttachment()
        {
            attachedFilePath = null;
            if (attachedImageBitmap != null)
            {
                attachedImageBitmap.Dispose();
                attachedImageBitmap = null;
            }
            lblAttachment.Text = "Pièce jointe : Aucune (Entrée = Envoyer, Maj+Entrée = Saut de ligne)";
            lblAttachment.ForeColor = Color.Silver;
            btnRemoveAttachment.Visible = false;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                if (Clipboard.ContainsImage())
                {
                    Image img = Clipboard.GetImage();
                    if (img != null)
                    {
                        attachedImageBitmap = new Bitmap(img);
                        attachedFilePath = null;
                        lblAttachment.Text = "Pièce jointe : [Capture écran presse-papier] (" + attachedImageBitmap.Width + "x" + attachedImageBitmap.Height + ")";
                        lblAttachment.ForeColor = Color.LightGreen;
                        btnRemoveAttachment.Visible = true;
                        e.Handled = true;
                    }
                }
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                SetAttachment(files[0]);
            }
        }

        private void CancelCurrentAnalysis()
        {
            isCancelled = true;
            if (activeWebRequest != null)
            {
                try { activeWebRequest.Abort(); } catch { }
            }
            if (!this.IsDisposed && this.IsHandleCreated)
            {
                this.Invoke((MethodInvoker)delegate {
                    AppendMessageBlock("system", "SYSTÈME", "⏹ <i>Analyse interrompue par l'utilisateur.</i>");
                    ResetSendButton();
                });
            }
        }

        private void SendMessage()
        {
            if (isAnalyzing)
            {
                CancelCurrentAnalysis();
                return;
            }

            string text = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(text) && attachedFilePath == null && attachedImageBitmap == null)
                return;

            isAnalyzing = true;
            isCancelled = false;

            btnSend.Enabled = true;
            btnSend.Text = "⏹ STOP\n(Annuler)";
            btnSend.BackColor = Color.FromArgb(192, 57, 43);
            txtInput.Enabled = false;

            List<Dictionary<string, object>> parts = new List<Dictionary<string, object>>();
            string badges = "";

            if (attachedImageBitmap != null)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    attachedImageBitmap.Save(ms, ImageFormat.Png);
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    Dictionary<string, object> inlineData = new Dictionary<string, object> {
                        { "mimeType", "image/png" },
                        { "data", base64 }
                    };
                    parts.Add(new Dictionary<string, object> { { "inlineData", inlineData } });
                }
                badges += "<span class='badge'>📷 Image jointe</span> ";
            }

            if (attachedFilePath != null && File.Exists(attachedFilePath))
            {
                string fileContent = File.ReadAllText(attachedFilePath, Encoding.Default);
                string fileBlock = "\n--- FICHIER DIAG JOINT (" + Path.GetFileName(attachedFilePath) + ") ---\n" + fileContent + "\n--- FIN FICHIER ---\n";
                text = (string.IsNullOrEmpty(text) ? "Analyse en détail ce relevé de diagnostic." : text) + "\n" + fileBlock;
                badges += "<span class='badge'>📁 " + Path.GetFileName(attachedFilePath) + "</span> ";
            }

            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(new Dictionary<string, object> { { "text", text } });
            }

            chatContents.Add(new Dictionary<string, object> {
                { "role", "user" },
                { "parts", parts }
            });

            string userDisplay = badges + (string.IsNullOrEmpty(txtInput.Text) ? "<i>(Analyse demandée)</i>" : WebUtility.HtmlEncode(txtInput.Text).Replace("\n", "<br/>"));
            AppendMessageBlock("user", "VOUS", userDisplay);

            ClearAttachment();
            txtInput.Clear();

            string modelName = cmbModel.Text.Trim();
            if (string.IsNullOrEmpty(modelName)) modelName = "gemini-3.5-flash-lite";

            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                ExecuteAgentLoop(modelName);
            });
        }

        // DÉCLARATION DES OUTILS AUTONOMES POUR GEMINI (FUNCTION CALLING)
        private object[] GetToolsDeclaration()
        {
            object[] declarations = new object[] {
                new Dictionary<string, object> {
                    { "name", "lister_fichiers_diag" },
                    { "description", "Scanne les dossiers de l'ordinateur d'atelier pour trouver les logs FORScan ou DAS." },
                    { "parameters", new Dictionary<string, object> {
                        { "type", "OBJECT" },
                        { "properties", new Dictionary<string, object> {
                            { "filtre", new Dictionary<string, string> { { "type", "STRING" }, { "description", "Filtre" } } }
                        }}
                    }}
                },
                new Dictionary<string, object> {
                    { "name", "lire_fichier_diag" },
                    { "description", "Lit un fichier de relevé." },
                    { "parameters", new Dictionary<string, object> {
                        { "type", "OBJECT" },
                        { "properties", new Dictionary<string, object> {
                            { "nom_ou_chemin", new Dictionary<string, string> { { "type", "STRING" }, { "description", "Chemin" } } }
                        }},
                        { "required", new string[] { "nom_ou_chemin" } }
                    }}
                },
                new Dictionary<string, object> {
                    { "name", "executer_commande_terminal" },
                    { "description", "Execute commande." },
                    { "parameters", new Dictionary<string, object> {
                        { "type", "OBJECT" },
                        { "properties", new Dictionary<string, object> {
                            { "commande", new Dictionary<string, string> { { "type", "STRING" }, { "description", "Commande" } } }
                        }},
                        { "required", new string[] { "commande" } }
                    }}
                }
            };

            return new object[] {
                new Dictionary<string, object> {
                    { "functionDeclarations", declarations }
                }
            };
        }

        // BOUCLE D'AGENT AUTONOME (MULTI-TOURS AVEC APPELS D'OUTILS)
        private void ExecuteAgentLoop(string model)
        {
            int maxIterations = 5;
            int iteration = 0;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

                Dictionary<string, object> systemInstruction = new Dictionary<string, object> {
                    { "parts", new object[] {
                        new Dictionary<string, string> {
                            { "text", "Tu es un ingénieur motoriste et expert technique en diagnostic automobile multimarque autonome. Tu disposes d'outils pour scanner les logs sur le disque, lire les fichiers de relevés directement et inspecter le système ou réseau. Si l'utilisateur demande d'analyser un fichier récent, utilise tes outils pour le chercher et le lire tout seul. Mets en forme tes synthèses de valeurs avec des tableaux Markdown (| Col1 | Col2 |). Si tu évalues un taux d'encrassement FAP, un RCO Turbo ou une valeur en pourcentage, intègre directement ce composant visuel HTML dans ta réponse pour un affichage graphique direct : <div class='card'><div class='title'>TITRE DU PARAMETRE</div><div class='gauge-container'><div class='gauge-fill' style='width:XX%;'></div><div class='gauge-text'>XX% - STATUT</div></div><div class='footer-info'><span>Détail 1</span><span>Détail 2</span></div></div>." }
                        }
                    }}
                };

                while (iteration < maxIterations)
                {
                    if (isCancelled) return;
                    iteration++;
                    string url = "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent?key=" + apiKey;

                    Dictionary<string, object> requestBody = new Dictionary<string, object> {
                        { "systemInstruction", systemInstruction },
                        { "tools", GetToolsDeclaration() },
                        { "contents", chatContents }
                    };

                    string jsonPayload = serializer.Serialize(requestBody);
                    byte[] data = Encoding.UTF8.GetBytes(jsonPayload);

                    string responseJson = null;
                    int netAttempts = 0;
                    while (netAttempts < 3)
                    {
                        if (isCancelled) return;
                        try
                        {
                            netAttempts++;
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                            req.Method = "POST";
                            req.ContentType = "application/json";
                            req.ContentLength = data.Length;
                            req.Timeout = 180000;
                            activeWebRequest = req;

                            using (Stream stream = req.GetRequestStream())
                            {
                                stream.Write(data, 0, data.Length);
                            }

                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            {
                                responseJson = reader.ReadToEnd();
                            }
                            break;
                        }
                        catch (WebException wex)
                        {
                            if (isCancelled || wex.Status == WebExceptionStatus.RequestCanceled) return;

                            if (wex.Status == WebExceptionStatus.NameResolutionFailure || wex.Status == WebExceptionStatus.ConnectFailure || wex.Status == WebExceptionStatus.Timeout)
                            {
                                if (netAttempts < 3)
                                {
                                    System.Threading.Thread.Sleep(1500);
                                    continue;
                                }
                            }
                            throw;
                        }
                    }

                    if (isCancelled) return;

                    Dictionary<string, object> respObj = serializer.Deserialize<Dictionary<string, object>>(responseJson);

                    ArrayList candidates = respObj["candidates"] as ArrayList;
                    if (candidates == null || candidates.Count == 0) break;

                    Dictionary<string, object> cand = candidates[0] as Dictionary<string, object>;
                    Dictionary<string, object> content = cand["content"] as Dictionary<string, object>;
                    ArrayList replyParts = content["parts"] as ArrayList;
                    if (replyParts == null || replyParts.Count == 0) break;

                    Dictionary<string, object> firstPart = replyParts[0] as Dictionary<string, object>;

                    // 1. SI GEMINI DEMANDE UN APPEL D'OUTIL (FUNCTION CALL)
                    if (firstPart.ContainsKey("functionCall"))
                    {
                        Dictionary<string, object> funcCall = firstPart["functionCall"] as Dictionary<string, object>;
                        string funcName = (string)funcCall["name"];
                        Dictionary<string, object> args = funcCall.ContainsKey("args") ? funcCall["args"] as Dictionary<string, object> : new Dictionary<string, object>();

                        // Ajouter la réponse du modèle avec son functionCall dans l'historique
                        chatContents.Add(new Dictionary<string, object> {
                            { "role", "model" },
                            { "parts", new object[] { firstPart } }
                        });

                        // Exécution de l'outil en local
                        Dictionary<string, object> toolResult = ExecuteLocalTool(funcName, args);

                        // Ajouter le résultat de l'outil pour Gemini
                        Dictionary<string, object> funcRespPart = new Dictionary<string, object> {
                            { "functionResponse", new Dictionary<string, object> {
                                { "name", funcName },
                                { "response", toolResult }
                            }}
                        };

                        chatContents.Add(new Dictionary<string, object> {
                            { "role", "user" },
                            { "parts", new object[] { funcRespPart } }
                        });

                        // On boucle pour que Gemini analyse le résultat de l'outil !
                        continue;
                    }

                    // 2. SI GEMINI RENVOIE UNE RÉPONSE TEXTE FINALE
                    if (firstPart.ContainsKey("text"))
                    {
                        string reply = (string)firstPart["text"];

                        chatContents.Add(new Dictionary<string, object> {
                            { "role", "model" },
                            { "parts", new object[] { new Dictionary<string, object> { { "text", reply } } } }
                        });

                        SaveSession();

                        string formattedHtml = MarkdownToHtml(reply);

                        if (!this.IsDisposed && this.IsHandleCreated)
                        {
                            this.Invoke((MethodInvoker)delegate {
                                AppendMessageBlock("gemini", "GEMINI (" + model + ")", formattedHtml);
                            });
                        }
                        return;
                    }
                }
            }
            catch (WebException wex)
            {
                if (isCancelled || wex.Status == WebExceptionStatus.RequestCanceled) return;

                string errMsg = wex.Message;
                if (wex.Response != null)
                {
                    using (StreamReader sr = new StreamReader(wex.Response.GetResponseStream()))
                    {
                        errMsg += "<br/><pre>" + WebUtility.HtmlEncode(sr.ReadToEnd()) + "</pre>";
                    }
                }

                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate {
                        AppendMessageBlock("error", "ERREUR SERVEUR (" + model + ")", errMsg);
                    });
                }
            }
            catch (Exception ex)
            {
                if (isCancelled) return;

                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate {
                        AppendMessageBlock("error", "ERREUR", ex.Message);
                    });
                }
            }
            finally
            {
                activeWebRequest = null;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.Invoke((MethodInvoker)delegate {
                        ResetSendButton();
                    });
                }
            }
        }

        // EXÉCUTION RÉELLE DES OUTILS LOCAUX SUR L'ORDINATEUR D'ATELIER
        private Dictionary<string, object> ExecuteLocalTool(string name, Dictionary<string, object> args)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            if (name == "lister_fichiers_diag")
            {
                List<string> foundFiles = new List<string>();
                string[] searchDirs = new string[] {
                    @"C:\Diag\Logs_Input",
                    @"C:\Diag",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FORScan")
                };

                string filter = args.ContainsKey("filtre") ? (string)args["filtre"] : "";

                foreach (string d in searchDirs)
                {
                    if (Directory.Exists(d))
                    {
                        DirectoryInfo di = new DirectoryInfo(d);
                        foreach (FileInfo fi in di.GetFiles("*.*"))
                        {
                            string ext = fi.Extension.ToLower();
                            if (ext == ".csv" || ext == ".txt" || ext == ".log")
                            {
                                if (string.IsNullOrEmpty(filter) || fi.Name.ToLower().Contains(filter.ToLower()))
                                {
                                    foundFiles.Add(fi.Name + " (" + (fi.Length / 1024) + " Ko, modifié le " + fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm") + ") [Chemin: " + fi.FullName + "]");
                                }
                            }
                        }
                    }
                }

                this.Invoke((MethodInvoker)delegate {
                    AppendMessageBlock("tool", "AGENT AUTONOME (Scan Disque)", "🔍 <b>Scan effectué :</b> " + foundFiles.Count + " fichier(s) de diagnostic trouvé(s).");
                });

                result.Add("fichiers_trouves", foundFiles.ToArray());
                return result;
            }
            else if (name == "lire_fichier_diag")
            {
                string path = args.ContainsKey("nom_ou_chemin") ? (string)args["nom_ou_chemin"] : "";
                string resolvedPath = null;

                if (File.Exists(path))
                {
                    resolvedPath = path;
                }
                else
                {
                    string[] searchDirs = new string[] {
                        @"C:\Diag\Logs_Input",
                        @"C:\Diag",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FORScan")
                    };

                    foreach (string d in searchDirs)
                    {
                        string candidate = Path.Combine(d, Path.GetFileName(path));
                        if (File.Exists(candidate))
                        {
                            resolvedPath = candidate;
                            break;
                        }
                    }
                }

                if (resolvedPath != null && File.Exists(resolvedPath))
                {
                    string content = File.ReadAllText(resolvedPath, Encoding.Default);
                    this.Invoke((MethodInvoker)delegate {
                        AppendMessageBlock("tool", "AGENT AUTONOME (Lecture Fichier)", "📖 <b>Fichier lu :</b> " + Path.GetFileName(resolvedPath) + " (" + (content.Length / 1024) + " Ko transmis à Gemini)");
                    });
                    result.Add("statut", "succes");
                    result.Add("chemin", resolvedPath);
                    result.Add("contenu", content);
                }
                else
                {
                    result.Add("statut", "erreur");
                    result.Add("message", "Fichier introuvable sur le disque : " + path);
                }
                return result;
            }
            else if (name == "executer_commande_terminal")
            {
                string cmd = args.ContainsKey("commande") ? (string)args["commande"] : "";

                // Demande d'autorisation de l'utilisateur (Garde-fou humain)
                bool allowed = false;
                this.Invoke((MethodInvoker)delegate {
                    DialogResult dr = MessageBox.Show("Gemini demande l'autorisation d'exécuter cette commande sur le PC d'atelier :\n\n" + cmd + "\n\nAutorisez-vous l'exécution ?", "Autorisation Terminal - Gemini Diag", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    allowed = (dr == DialogResult.Yes);
                });

                if (!allowed)
                {
                    result.Add("statut", "refuse");
                    result.Add("message", "L'utilisateur a refusé l'exécution de cette commande.");
                    return result;
                }

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = "cmd.exe",
                        Arguments = "/c " + cmd,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit(15000);
                        string output = p.StandardOutput.ReadToEnd();
                        string error = p.StandardError.ReadToEnd();
                        string full = string.IsNullOrEmpty(error) ? output : output + "\nERREUR:\n" + error;

                        this.Invoke((MethodInvoker)delegate {
                            AppendMessageBlock("tool", "AGENT AUTONOME (Terminal)", "⚡ <b>Commande exécutée :</b> <code>" + WebUtility.HtmlEncode(cmd) + "</code>");
                        });

                        result.Add("statut", "succes");
                        result.Add("sortie_terminal", full);
                    }
                }
                catch (Exception ex)
                {
                    result.Add("statut", "erreur");
                    result.Add("message", ex.Message);
                }

                return result;
            }

            result.Add("erreur", "Outil non reconnu : " + name);
            return result;
        }

        private void ResetSendButton()
        {
            isAnalyzing = false;
            btnSend.Enabled = true;
            btnSend.Text = "ENVOYER\n(Entrée)";
            btnSend.BackColor = Color.FromArgb(0, 122, 204);
            txtInput.Enabled = true;
            txtInput.Focus();
        }

        private string MarkdownToHtml(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";

            StringBuilder sb = new StringBuilder();
            string[] lines = md.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            bool inTable = false;
            bool inCodeBlock = false;
            int cardDepth = 0;
            StringBuilder codeBuffer = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        string codeText = codeBuffer.ToString();
                        if (codeText.Contains("<div class=\"card\"") || codeText.Contains("<div class='card'") || codeText.Contains("gauge-container"))
                        {
                            sb.Append(codeText);
                            sb.Append("<pre onclick='copyBlock(this)' title='Cliquez pour copier tout le code'>");
                            sb.Append("<span class='copy-hint'>[Cliquer pour copier le code du widget ci-dessus]</span>");
                            sb.Append(WebUtility.HtmlEncode(codeText) + "</pre>");
                        }
                        else
                        {
                            sb.Append("<pre onclick='copyBlock(this)' title='Cliquez pour copier tout le code'>");
                            sb.Append("<span class='copy-hint'>[Cliquer pour copier]</span>");
                            sb.Append(WebUtility.HtmlEncode(codeText) + "</pre>");
                        }

                        codeBuffer.Clear();
                        inCodeBlock = false;
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeBuffer.Clear();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    if (!line.Trim().StartsWith("<!DOCTYPE") && !line.Trim().StartsWith("<html") && !line.Trim().StartsWith("</html") && !line.Trim().StartsWith("<head") && !line.Trim().StartsWith("</head") && !line.Trim().StartsWith("<body") && !line.Trim().StartsWith("</body"))
                    {
                        codeBuffer.AppendLine(line);
                    }
                    continue;
                }

                if (line.Trim().StartsWith("|") && line.Trim().EndsWith("|"))
                {
                    if (Regex.IsMatch(line, @"^\|[\s\-:|]+\|$"))
                        continue;

                    string[] cells = line.Trim().Substring(1, line.Trim().Length - 2).Split('|');
                    if (!inTable)
                    {
                        sb.Append("<table><tr>");
                        foreach (string c in cells) sb.Append("<th>" + FormatInline(c.Trim()) + "</th>");
                        sb.Append("</tr>");
                        inTable = true;
                    }
                    else
                    {
                        sb.Append("<tr>");
                        foreach (string c in cells) sb.Append("<td>" + FormatInline(c.Trim()) + "</td>");
                        sb.Append("</tr>");
                    }
                    continue;
                }
                else if (inTable)
                {
                    sb.Append("</table>");
                    inTable = false;
                }

                string trimmed = line.Trim();
                if (cardDepth == 0 && (line.Contains("<div class='card'") || line.Contains("<div class=\"card\"") || line.Contains("gauge-container")))
                {
                    int opens = Regex.Matches(line, @"<div[\s>]", RegexOptions.IgnoreCase).Count;
                    int closes = Regex.Matches(line, @"</div>", RegexOptions.IgnoreCase).Count;
                    cardDepth = Math.Max(0, opens - closes);
                    sb.Append(line + "\n");
                    continue;
                }
                if (cardDepth > 0)
                {
                    int opens = Regex.Matches(line, @"<div[\s>]", RegexOptions.IgnoreCase).Count;
                    int closes = Regex.Matches(line, @"</div>", RegexOptions.IgnoreCase).Count;
                    cardDepth = Math.Max(0, cardDepth + opens - closes);
                    sb.Append(line + "\n");
                    continue;
                }
                if (trimmed.StartsWith("<div") || trimmed.StartsWith("</div") || trimmed.StartsWith("<span") || trimmed.StartsWith("</span"))
                {
                    sb.Append(line + "\n");
                    continue;
                }

                if (line.StartsWith("### ")) { sb.Append("<h4>" + FormatInline(line.Substring(4)) + "</h4>"); continue; }
                if (line.StartsWith("## ")) { sb.Append("<h3>" + FormatInline(line.Substring(3)) + "</h3>"); continue; }
                if (line.StartsWith("# ")) { sb.Append("<h2>" + FormatInline(line.Substring(2)) + "</h2>"); continue; }

                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                {
                    sb.Append("<li>" + FormatInline(line.TrimStart().Substring(2)) + "</li>");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    sb.Append("<br/>");
                    continue;
                }

                sb.Append("<p style='margin:4px 0;'>" + FormatInline(line) + "</p>");
            }

            if (inTable) sb.Append("</table>");

            return sb.ToString();
        }

        private string FormatInline(string text)
        {
            text = WebUtility.HtmlEncode(text);

            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");

            text = Regex.Replace(text, @"\b([PUB C][0-3][0-9A-Fa-f]{3})\b", "<span class='dtc-badge' onclick=\"queryDtc('$1')\" title='Cliquer pour analyser ce code défaut'>$1 🔍</span>");

            text = Regex.Replace(text, @"\b(ACTIF|DEFAUT|DÉFAUT|CRITIQUE|ERROR)\b", "<span class='badge-alert'>$1</span>", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(MEMORISE|MÉMORISÉ|INTERMITTENT|ATTENTE|WARNING)\b", "<span class='badge-warn'>$1</span>", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(CONFORME|NORMAL|OK|PASSED)\b", "<span class='badge-ok'>$1</span>", RegexOptions.IgnoreCase);

            return text;
        }

        private void LoadAvailableModelsAsync()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                try
                {
                    string url = "https://generativelanguage.googleapis.com/v1beta/models?key=" + apiKey;
                    using (WebClient client = new WebClient())
                    {
                        string json = client.DownloadString(url);
                        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        Dictionary<string, object> obj = serializer.Deserialize<Dictionary<string, object>>(json);
                        ArrayList models = obj["models"] as ArrayList;

                        if (models == null) return;

                        List<string> modelNames = new List<string>();
                        foreach (object m in models)
                        {
                            Dictionary<string, object> mdict = m as Dictionary<string, object>;
                            if (mdict == null) continue;

                            string name = (string)mdict["name"];
                            if (name.StartsWith("models/")) name = name.Substring(7);

                            if (name.Contains("gemini") && !name.Contains("tts") && !name.Contains("embedding") && !name.Contains("image"))
                            {
                                modelNames.Add(name);
                            }
                        }

                        if (modelNames.Count > 0)
                        {
                            this.Invoke((MethodInvoker)delegate {
                                string current = cmbModel.Text;
                                cmbModel.Items.Clear();
                                if (modelNames.Contains("gemini-3.5-flash-lite")) cmbModel.Items.Add("gemini-3.5-flash-lite");
                                if (modelNames.Contains("gemini-3.8-flash")) cmbModel.Items.Add("gemini-3.8-flash");
                                if (modelNames.Contains("gemini-3.6-flash")) cmbModel.Items.Add("gemini-3.6-flash");
                                if (modelNames.Contains("gemini-3.5-flash")) cmbModel.Items.Add("gemini-3.5-flash");
                                if (modelNames.Contains("gemini-4-argon")) cmbModel.Items.Add("gemini-4-argon");
                                if (modelNames.Contains("gemini-3.1-pro-preview")) cmbModel.Items.Add("gemini-3.1-pro-preview");

                                foreach (string m in modelNames) {
                                    if (!cmbModel.Items.Contains(m)) cmbModel.Items.Add(m);
                                }

                                if (cmbModel.Items.Contains(current)) cmbModel.SelectedItem = current;
                                else cmbModel.SelectedIndex = 0;
                            });
                        }
                    }
                }
                catch { }
            });
        }

        private void SaveSession()
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = serializer.Serialize(chatContents);
                File.WriteAllText(currentSessionFile, json, Encoding.UTF8);
            }
            catch { }
        }

        private void LoadSession()
        {
            using (OpenFileDialog ofd = new OpenFileDialog { InitialDirectory = @"C:\Diag\Gemini_History", Filter = "Sessions (*.json)|*.json" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        ArrayList loaded = serializer.Deserialize<ArrayList>(json);

                        if (loaded == null) return;

                        chatContents.Clear();
                        messagesHtml.Clear();
                        currentSessionFile = ofd.FileName;

                        foreach (object item in loaded)
                        {
                            Dictionary<string, object> msg = item as Dictionary<string, object>;
                            if (msg == null) continue;

                            chatContents.Add(msg);
                            string role = (string)msg["role"];
                            ArrayList parts = msg["parts"] as ArrayList;
                            string text = "";
                            if (parts != null)
                            {
                                foreach (object p in parts)
                                {
                                    Dictionary<string, object> partDict = p as Dictionary<string, object>;
                                    if (partDict == null) continue;

                                    if (partDict.ContainsKey("text"))
                                        text += (string)partDict["text"] + "\n";
                                    else if (partDict.ContainsKey("inlineData"))
                                        text += "[Image jointe]\n";
                                }
                            }

                            if (role == "user")
                                AppendMessageBlock("user", "VOUS", WebUtility.HtmlEncode(text.Trim()).Replace("\n", "<br/>"));
                            else if (role == "model" && !string.IsNullOrEmpty(text))
                                AppendMessageBlock("gemini", "GEMINI", MarkdownToHtml(text.Trim()));
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Erreur au chargement : " + ex.Message);
                    }
                }
            }
        }
    }
}
