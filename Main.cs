using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Jigglr
{
    // The main application form
    public class MainForm : Form
    {
        // UI Controls for configuring the behaviour
        private CheckBox chkEnableJiggle;
        private CheckBox chkZenJiggle;
        private CheckBox chkDisableTime;
        private CheckBox chkAutoPauseLock;
        private CheckBox chkStartWithWindows;
        private CheckBox chkStartMinimized;
        private DateTimePicker dtpStopTime;
        private Label lblInterval;
        private NumericUpDown numInterval;
        private Label lblStatus;
        private Label lblHotkeyInfo;
        private CheckBox chkHotKeyCtrl;
        private CheckBox chkHotKeyAlt;
        private CheckBox chkHotKeyShift;
        private CheckBox chkHotKeyWin;
        private ComboBox cmbHotKey;
        private Button btnApplyHotKey;
        private System.Windows.Forms.Timer jiggleTimer;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolTip uiTooltip;
        private bool _isLoading = false;

        // Custom icons for visual feedback
        private Icon activeIcon;
        private Icon idleIcon;
        private IntPtr activeIconHandle;
        private IntPtr idleIconHandle;

        private DateTime? targetDisableTime = null;

        private const string APP_VERSION = "1.4";
        private const int MOUSE_JIGGLE_DELTA = 1;
        private const int DEFAULT_JIGGLE_INTERVAL_SEC = 30; 
        private bool wasJigglingBeforeLock = false;

        // Currently deployed hotkey configuration
        private uint deployedHotKeyModifiers = 0;
        private Keys deployedHotKeyKey = Keys.None;

        // Native Windows methods for sending input
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // Native Windows method to cleanly destroy unmanaged icons to prevent GDI leaks
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        // Standard Windows API structures for input simulation
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        const int INPUT_MOUSE = 0;
        const int INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_F15 = 0x7E;

        // Native Windows methods for registering global hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 1;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const int WM_HOTKEY = 0x0312;

        public MainForm()
        {
            InitialiseIcons();
            InitialiseComponent();
            InitialiseTrayIcon();
            
            LoadConfig();
            CheckStartMinimized();

            // Subscribe to Session Switch events to detect when the PC is locked or unlocked
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.BeginInvoke((MethodInvoker)delegate 
            {
                ApplyHotKey(true); // Attempt to register the saved hotkey silently on startup once the form and handle are fully created
                RefreshHotkeyButtonState(); // Sync UI state once loaded
            });
        }

        // Generate our icons once and store their unmanaged handles appropriately
        private void InitialiseIcons()
        {
            activeIcon = CreateGenericIcon(true, out activeIconHandle);
            idleIcon = CreateGenericIcon(false, out idleIconHandle);
        }

        // Programmatically draws a clean, generic icon that doesn't look like a mouse
        private Icon CreateGenericIcon(bool isActive, out IntPtr hIcon)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // Use Teal to signify active/running, and Grey for idle/paused
                Brush bgBrush = isActive ? Brushes.Teal : Brushes.Gray;
                Pen borderPen = isActive ? Pens.DarkSlateGray : Pens.DimGray;
                
                g.FillEllipse(bgBrush, 2, 2, 28, 28);
                g.DrawEllipse(borderPen, 2, 2, 28, 28);
                g.FillEllipse(Brushes.White, 12, 12, 8, 8);
                
                hIcon = bmp.GetHicon();
                // Clone creates an independent copy so the original handle can be correctly tracked
                return (Icon)Icon.FromHandle(hIcon).Clone();
            }
        }

        // Configures the form and all its controls
        private void InitialiseComponent()
        {
            this.Text = string.Format("Jigglr v{0}", APP_VERSION);
            this.Size = new Size(330, 395);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = idleIcon;

            uiTooltip = new ToolTip();
            uiTooltip.AutoPopDelay = 10000; // Show tooltip for up to 10 seconds
            uiTooltip.InitialDelay = 500;
            uiTooltip.ReshowDelay = 500;

            chkEnableJiggle = new CheckBox { Text = "Enable jiggle?", Location = new Point(15, 15), AutoSize = true };
            chkEnableJiggle.CheckedChanged += ChkEnableJiggle_CheckedChanged;
            uiTooltip.SetToolTip(chkEnableJiggle, "Toggles whether Jigglr is actively preventing sleep.\n\nBy default, it slightly wiggles the mouse cursor back and forth.");

            chkZenJiggle = new CheckBox { Text = "Zen jiggle?", Location = new Point(15, 35), AutoSize = true };
            chkZenJiggle.CheckedChanged += (s, e) => { SaveConfig(); UpdateTrayText(); };
            uiTooltip.SetToolTip(chkZenJiggle, "Phantom Mode: Instead of moving the mouse cursor, Jigglr secretly taps F15.\n\nThis is completely invisible to you and doesn't interrupt highlighted text, but safely keeps programs like Microsoft Teams showing you as 'Active'.");

            chkDisableTime = new CheckBox { Text = "Disable at time?", Location = new Point(15, 55), AutoSize = true };
            chkDisableTime.CheckedChanged += (s, e) => { UpdateTargetDisableTime(); UpdateTrayText(); SaveConfig(); };

            dtpStopTime = new DateTimePicker
            {
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Location = new Point(130, 52),
                Width = 95
            };
            dtpStopTime.ValueChanged += (s, e) => { UpdateTargetDisableTime(); UpdateTrayText(); SaveConfig(); };

            chkAutoPauseLock = new CheckBox { Text = "Auto-pause on lock?", Location = new Point(15, 80), AutoSize = true, Checked = true };
            chkAutoPauseLock.CheckedChanged += (s, e) => SaveConfig();

            chkStartWithWindows = new CheckBox { Text = "Start with Windows?", Location = new Point(15, 100), AutoSize = true };
            chkStartWithWindows.CheckedChanged += ChkStartWithWindows_CheckedChanged;

            chkStartMinimized = new CheckBox { Text = "Start minimised?", Location = new Point(15, 120), AutoSize = true };
            chkStartMinimized.CheckedChanged += (s, e) => SaveConfig();

            lblInterval = new Label { Text = "Jiggle Interval (s):", Location = new Point(13, 145), AutoSize = true };
            numInterval = new NumericUpDown { Location = new Point(115, 143), Width = 60, Minimum = 5, Maximum = 600, Value = DEFAULT_JIGGLE_INTERVAL_SEC };
            numInterval.ValueChanged += (s, e) => { 
                if (jiggleTimer != null) jiggleTimer.Interval = (int)numInterval.Value * 1000;
                SaveConfig();
            };

            lblStatus = new Label { Text = "Status: Idle", Location = new Point(15, 175), AutoSize = true, ForeColor = Color.Gray };
            
            // Hotkey UI Configuration layout
            lblHotkeyInfo = new Label { Text = "Global Hotkey Modifiers & Key:", Location = new Point(15, 210), AutoSize = true, ForeColor = Color.Black };
            
            chkHotKeyCtrl = new CheckBox { Text = "Ctrl", Location = new Point(15, 230), AutoSize = true };
            chkHotKeyAlt = new CheckBox { Text = "Alt", Location = new Point(65, 230), AutoSize = true };
            chkHotKeyShift = new CheckBox { Text = "Shift", Location = new Point(115, 230), AutoSize = true };
            chkHotKeyWin = new CheckBox { Text = "Win", Location = new Point(175, 230), AutoSize = true };
            
            chkHotKeyCtrl.CheckedChanged += (s, e) => RefreshHotkeyButtonState();
            chkHotKeyAlt.CheckedChanged += (s, e) => RefreshHotkeyButtonState();
            chkHotKeyShift.CheckedChanged += (s, e) => RefreshHotkeyButtonState();
            chkHotKeyWin.CheckedChanged += (s, e) => RefreshHotkeyButtonState();
            
            cmbHotKey = new ComboBox { Location = new Point(15, 260), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            for(char c = 'A'; c <= 'Z'; c++) { cmbHotKey.Items.Add(c.ToString()); }
            for(int i = 0; i <= 9; i++) { cmbHotKey.Items.Add(i.ToString()); }
            for(int i = 1; i <= 12; i++) { cmbHotKey.Items.Add("F" + i); }
            cmbHotKey.SelectedIndex = 9; // Defaults to 'J'
            cmbHotKey.SelectedIndexChanged += (s, e) => RefreshHotkeyButtonState();
            
            btnApplyHotKey = new Button { Text = "Deploy Hotkey", Location = new Point(115, 259), Width = 145, Height = 25 };
            btnApplyHotKey.Click += BtnApplyHotKey_Click;
            
            Button btnQuit = new Button { Text = "Quit Jigglr", Location = new Point(15, 295), Width = 280, Height = 40, BackColor = Color.DarkRed, ForeColor = Color.White };
            btnQuit.FlatStyle = FlatStyle.Flat;
            btnQuit.FlatAppearance.BorderSize = 0;
            btnQuit.Click += (s, e) => Application.Exit();
            uiTooltip.SetToolTip(btnQuit, "The top-right ✕ button forcefully minimises Jigglr into the system tray to hide it.\n\nUse this button to explicitly terminate and close Jigglr.");

            this.Controls.Add(chkEnableJiggle);
            this.Controls.Add(chkZenJiggle);
            this.Controls.Add(chkDisableTime);
            this.Controls.Add(dtpStopTime);
            this.Controls.Add(chkAutoPauseLock);
            this.Controls.Add(chkStartWithWindows);
            this.Controls.Add(chkStartMinimized);
            this.Controls.Add(lblInterval);
            this.Controls.Add(numInterval);
            this.Controls.Add(lblStatus);
            this.Controls.Add(lblHotkeyInfo);
            
            this.Controls.Add(chkHotKeyCtrl);
            this.Controls.Add(chkHotKeyAlt);
            this.Controls.Add(chkHotKeyShift);
            this.Controls.Add(chkHotKeyWin);
            this.Controls.Add(cmbHotKey);
            this.Controls.Add(btnApplyHotKey);
            this.Controls.Add(btnQuit);

            jiggleTimer = new System.Windows.Forms.Timer { Interval = (int)numInterval.Value * 1000 };
            jiggleTimer.Tick += JiggleTimer_Tick;

            this.FormClosing += MainForm_FormClosing;
            this.Resize += MainForm_Resize;
        }

        // Set up the context menu and tray icon for the System Tray
        private void InitialiseTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Exit", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Text = "Jigglr - No disable set",
                Icon = idleIcon,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        // Intelligently calculates the absolute moment in time we should disable the jiggler
        private void UpdateTargetDisableTime()
        {
            if (chkDisableTime.Checked)
            {
                DateTime now = DateTime.Now;
                DateTime target = DateTime.Today.Add(dtpStopTime.Value.TimeOfDay);
                
                // If the selected time is earlier than the current time, assume the user means tomorrow
                if (target < now)
                {
                    target = target.AddDays(1);
                }
                targetDisableTime = target;
            }
            else
            {
                targetDisableTime = null;
            }
        }

        // Modifies the tooltip text hovering over the tray icon
        private void UpdateTrayText()
        {
            string baseMode = "Jigglr - ";
            if (chkEnableJiggle.Checked)
            {
                baseMode += chkZenJiggle.Checked ? "Active (Zen)" : "Active (Mouse)";
            }
            else
            {
                baseMode += "Idle";
            }

            if (chkDisableTime.Checked && targetDisableTime.HasValue)
            {
                string timeStr = targetDisableTime.Value.ToString("MMM dd HH:mm");
                string newText = string.Format("{0} - Disabling at {1}", baseMode, timeStr);
                trayIcon.Text = newText.Substring(0, Math.Min(newText.Length, 63));
            }
            else
            {
                trayIcon.Text = baseMode;
            }
        }

        // Determines if we should start silently in the tray
        private void CheckStartMinimized()
        {
            if (chkStartMinimized.Checked)
            {
                this.WindowState = FormWindowState.Minimized;
            }
        }

        private bool GetRegistryBool(RegistryKey key, string name, bool defaultValue)
        {
            object value = key.GetValue(name);
            if (value == null) return defaultValue;
            if (value is int) return (int)value == 1;
            if (value is string)
            {
                bool parsedBool;
                if (bool.TryParse((string)value, out parsedBool)) return parsedBool;
                int parsedInt;
                if (int.TryParse((string)value, out parsedInt)) return parsedInt == 1;
            }
            return defaultValue;
        }

        private int GetRegistryInt(RegistryKey key, string name, int defaultValue)
        {
            object value = key.GetValue(name);
            if (value == null) return defaultValue;
            if (value is int) return (int)value;
            if (value is string)
            {
                int parsedInt;
                if (int.TryParse((string)value, out parsedInt)) return parsedInt;
            }
            return defaultValue;
        }

        // Rehydrate settings from the Windows Registry using safe extraction to support legacy string configs
        private void LoadConfig()
        {
            _isLoading = true;
            try
            {
                string appKey = "SOFTWARE\\Jigglr";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(appKey, false))
            {
                // This correctly respects the _isLoading guard now since it occurs within this block
                chkStartWithWindows.Checked = CheckStartupRegistry();

                if (key != null)
                {
                    // Core Controls
                    chkZenJiggle.Checked = GetRegistryBool(key, "ZenJiggle", false);
                    chkDisableTime.Checked = GetRegistryBool(key, "DisableTime", false);
                    chkAutoPauseLock.Checked = GetRegistryBool(key, "AutoPauseLock", true);
                    chkStartMinimized.Checked = GetRegistryBool(key, "StartMinimized", false);
                    
                    int interval = GetRegistryInt(key, "JiggleInterval", DEFAULT_JIGGLE_INTERVAL_SEC);
                    if (interval >= numInterval.Minimum && interval <= numInterval.Maximum)
                    {
                        numInterval.Value = interval;
                    }

                    string savedTimeStr = key.GetValue("StopTime", "").ToString();
                    DateTime parsedTime;
                    if (!string.IsNullOrEmpty(savedTimeStr) && DateTime.TryParse(savedTimeStr, out parsedTime))
                    {
                        dtpStopTime.Value = parsedTime;
                    }

                    // Enable Jiggle sits last so that if checking it fires event logic, the underlying variables are already pre-loaded
                    chkEnableJiggle.Checked = GetRegistryBool(key, "EnableJiggle", false);

                    // Hotkeys
                    chkHotKeyCtrl.Checked = GetRegistryBool(key, "HotKey_Ctrl", true);
                    chkHotKeyAlt.Checked = GetRegistryBool(key, "HotKey_Alt", true);
                    chkHotKeyShift.Checked = GetRegistryBool(key, "HotKey_Shift", true);
                    chkHotKeyWin.Checked = GetRegistryBool(key, "HotKey_Win", false);
                    
                    string savedKey = key.GetValue("HotKey_Key", "J").ToString();
                    if (cmbHotKey.Items.Contains(savedKey)) {
                        cmbHotKey.SelectedItem = savedKey;
                    }
                }
                else
                {
                    // Fallback to reasonable sensible defaults
                    chkHotKeyCtrl.Checked = true;
                    chkHotKeyAlt.Checked = true;
                    chkHotKeyShift.Checked = true;
                    chkHotKeyWin.Checked = false;
                    cmbHotKey.SelectedItem = "J";
                }
            }

            // Sync the derived state
            UpdateTargetDisableTime();
            UpdateTrayText();
            }
            finally
            {
                _isLoading = false;
            }
        }

        // Save selected configuration to persist across reboots using ints for booleans
        private void SaveConfig()
        {
            if (_isLoading) return; // Prevent saving while loading from triggering mid-load corruption

            string appKey = "SOFTWARE\\Jigglr";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(appKey))
            {
                key.SetValue("EnableJiggle", chkEnableJiggle.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ZenJiggle", chkZenJiggle.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DisableTime", chkDisableTime.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AutoPauseLock", chkAutoPauseLock.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("StartMinimized", chkStartMinimized.Checked ? 1 : 0, RegistryValueKind.DWord);
                
                key.SetValue("JiggleInterval", (int)numInterval.Value, RegistryValueKind.DWord);
                key.SetValue("StopTime", dtpStopTime.Value.ToString("HH:mm:ss"));

                key.SetValue("HotKey_Ctrl", chkHotKeyCtrl.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("HotKey_Alt", chkHotKeyAlt.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("HotKey_Shift", chkHotKeyShift.Checked ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("HotKey_Win", chkHotKeyWin.Checked ? 1 : 0, RegistryValueKind.DWord);
                if (cmbHotKey.SelectedItem != null)
                {
                    key.SetValue("HotKey_Key", cmbHotKey.SelectedItem.ToString());
                }
            }
        }
        
        // Gathers currently selected UI modifier flags
        private uint GetSelectedModifiers()
        {
            uint modifiers = 0;
            if (chkHotKeyCtrl.Checked) modifiers |= MOD_CONTROL;
            if (chkHotKeyAlt.Checked) modifiers |= MOD_ALT;
            if (chkHotKeyShift.Checked) modifiers |= MOD_SHIFT;
            if (chkHotKeyWin.Checked) modifiers |= MOD_WIN;
            return modifiers;
        }
        
        // Parses the ComboBox selection into a valid enumerator value representing the Key
        private Keys GetSelectedKey()
        {
            string keyStr = "";
            if (cmbHotKey.SelectedItem != null)
            {
                keyStr = cmbHotKey.SelectedItem.ToString();
            }
            else if (!string.IsNullOrEmpty(cmbHotKey.Text))
            {
                // When invoked early in the Load event cycle, SelectedItem might be null but the Text might be bound
                keyStr = cmbHotKey.Text;
            }
            else
            {
                return Keys.None;
            }
            
            Keys parsedKey;
            
            if (Enum.TryParse<Keys>(keyStr, out parsedKey))
            {
                return parsedKey;
            }
            else if(keyStr.Length == 1 && char.IsDigit(keyStr[0])) // e.g., '1', '2'
            {
                return (Keys)Enum.Parse(typeof(Keys), "D" + keyStr);
            }
            
            return Keys.None;
        }

        // Actively disables or enables the Deploy button based on whether the config is already live
        private void RefreshHotkeyButtonState()
        {
            uint selectedMods = GetSelectedModifiers();
            Keys selectedKey = GetSelectedKey();
            
            if (deployedHotKeyKey != Keys.None && selectedMods == deployedHotKeyModifiers && selectedKey == deployedHotKeyKey)
            {
                btnApplyHotKey.Enabled = false;
                btnApplyHotKey.Text = "Deployed";
            }
            else
            {
                // Unsaved changes detected
                btnApplyHotKey.Enabled = true;
                btnApplyHotKey.Text = "Deploy Hotkey";
                
                // Clear the green text confirmation whenever they tweak inputs
                if (lblHotkeyInfo.ForeColor == Color.Green)
                {
                    lblHotkeyInfo.Text = "Global Hotkey Modifiers & Key:";
                    lblHotkeyInfo.ForeColor = Color.Black;
                }
            }
        }

        private void BtnApplyHotKey_Click(object sender, EventArgs e)
        {
            ApplyHotKey(false);
            SaveConfig();
            RefreshHotkeyButtonState(); // Refresh the button state immediately
        }

        // Checks whether a hotkey combination is simple enough that it could clobber core Windows functionality
        private bool IsSafeHotkey(uint modifiers, Keys key)
        {
            if (modifiers == 0) return false;

            bool hasCtrl = (modifiers & MOD_CONTROL) == MOD_CONTROL;
            bool hasAlt = (modifiers & MOD_ALT) == MOD_ALT;
            bool hasShift = (modifiers & MOD_SHIFT) == MOD_SHIFT;
            bool hasWin = (modifiers & MOD_WIN) == MOD_WIN;

            int modifierCount = (hasCtrl ? 1 : 0) + (hasAlt ? 1 : 0) + (hasShift ? 1 : 0) + (hasWin ? 1 : 0);

            // Function keys with at least one modifier are generally okay
            if ((int)key >= (int)Keys.F1 && (int)key <= (int)Keys.F12 && modifierCount >= 1) return true;

            // Win key combinations are generally safe since Windows preempts kernel-level overrides (like Win+L)
            if (hasWin) return true;

            // For letters and numbers, require at least TWO modifiers to avoid hijacking simple shortcuts like Ctrl+C
            if (modifierCount >= 2) return true;

            return false;
        }

        // Tries to grab the global shortcut. If it fails due to conflict or safety check, falls back cleanly.
        private void ApplyHotKey(bool isStartup = false)
        {
            uint modifiers = GetSelectedModifiers();
            Keys finalKey = GetSelectedKey();

            if (finalKey != Keys.None)
            {
                if (!IsSafeHotkey(modifiers, finalKey))
                {
                    lblHotkeyInfo.Text = "Global Hotkey: Unsafe (Need 2+ mods)";
                    lblHotkeyInfo.ForeColor = Color.Red;
                    deployedHotKeyKey = Keys.None;
                    
                    if (!isStartup) MessageBox.Show("This hotkey configuration is too simple and might override common application shortcuts (like Ctrl+C).\n\nPlease use at least two modifiers (e.g., Ctrl + Alt), the Win key, or an F-key.", "Jigglr - Unsafe Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // If we get here, it's considered safe enough to attempt registration
                UnregisterHotKey(this.Handle, HOTKEY_ID);
                bool success = RegisterHotKey(this.Handle, HOTKEY_ID, modifiers, (uint)finalKey);
                
                if (!success)
                {
                    lblHotkeyInfo.Text = "Global Hotkey: Failed (In Use by Windows)";
                    lblHotkeyInfo.ForeColor = Color.Red;
                    lblStatus.Text = "Status: Hotkey registration failed";
                    lblStatus.ForeColor = Color.OrangeRed;
                    deployedHotKeyKey = Keys.None;
                    
                    if (!isStartup)
                    {
                        MessageBox.Show("Failed to register the hotkey. It is likely already gracefully reserved by Windows or another application.", "Jigglr - Hotkey Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    // Successfully hooked; keep a record so we can evaluate UI state later
                    deployedHotKeyModifiers = modifiers;
                    deployedHotKeyKey = finalKey;
                    
                    lblHotkeyInfo.Text = "Global Hotkey: Saved!";
                    lblHotkeyInfo.ForeColor = Color.Green;
                }
            }
        }

        // Put the form back on the screen and give it focus
        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        // Detect window minimising to gracefully sweep it into the system tray
        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        // Suppress Alt+F4 / cross button to instead push the form to the background
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
            }
        }

        // Toggles whether the application is actively preventing sleep
        private void ChkEnableJiggle_CheckedChanged(object sender, EventArgs e)
        {
            if (chkEnableJiggle.Checked)
            {
                UpdateTargetDisableTime();
                UpdateTrayText();
                
                jiggleTimer.Start();
                if (chkDisableTime.Checked && targetDisableTime.HasValue)
                {
                    lblStatus.Text = string.Format("Status: Active — stopping at {0}", targetDisableTime.Value.ToString("HH:mm"));
                }
                else
                {
                    lblStatus.Text = "Status: Active";
                }
                lblStatus.ForeColor = Color.Green;
                this.Icon = activeIcon;
                trayIcon.Icon = activeIcon;
            }
            else
            {
                jiggleTimer.Stop();
                lblStatus.Text = "Status: Idle";
                lblStatus.ForeColor = Color.Gray;
                this.Icon = idleIcon;
                trayIcon.Icon = idleIcon;
                UpdateTrayText();
            }

            SaveConfig();
        }

        // Routine executed every tick to decide if a jiggle is needed, or if we ran out of time
        private void JiggleTimer_Tick(object sender, EventArgs e)
        {
            if (chkDisableTime.Checked && targetDisableTime.HasValue)
            {
                if (DateTime.Now >= targetDisableTime.Value)
                {
                    chkEnableJiggle.Checked = false;
                    chkDisableTime.Checked = false;
                    targetDisableTime = null;
                    UpdateTrayText();
                    return;
                }
            }

            PerformJiggle();
        }

        // The core work method. Executes the appropriate unmanaged API instructions to synthesise input.
        private void PerformJiggle()
        {
            INPUT[] inputs = new INPUT[2];
            
            if (chkZenJiggle.Checked)
            {
                // Tap F15, a virtually invisible phantom keystroke that Microsoft Teams respects
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = VK_F15;
                
                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = VK_F15;
                inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            }
            else
            {
                // Actually wiggle the pointer pixel by pixel using our constant
                inputs[0].type = INPUT_MOUSE;
                inputs[0].u.mi.dx = MOUSE_JIGGLE_DELTA;
                inputs[0].u.mi.dy = MOUSE_JIGGLE_DELTA;
                inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE;

                inputs[1].type = INPUT_MOUSE;
                inputs[1].u.mi.dx = -MOUSE_JIGGLE_DELTA;
                inputs[1].u.mi.dy = -MOUSE_JIGGLE_DELTA;
                inputs[1].u.mi.dwFlags = MOUSEEVENTF_MOVE;
            }

            // Insert these events directly into the Windows input stream (hardware representation)
            uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (result == 0)
            {
                // This generally happens when a higher privilege level window is active and rejects our input
                lblStatus.Text = "Status: SendInput Blocked (UIPI?)";
                lblStatus.ForeColor = Color.Red;
            }
            else
            {
                // Append the last jiggle timestamp cleanly on success
                if (chkDisableTime.Checked && targetDisableTime.HasValue)
                {
                    lblStatus.Text = string.Format("Status: Active (last jiggle {0}) - stopping at {1}", DateTime.Now.ToString("HH:mm:ss"), targetDisableTime.Value.ToString("HH:mm"));
                }
                else
                {
                    lblStatus.Text = string.Format("Status: Active (last jiggle {0})", DateTime.Now.ToString("HH:mm:ss"));
                }
                lblStatus.ForeColor = Color.Green;
            }
        }

        // Scans the local user registry hive for our autonomous startup value
        private bool CheckStartupRegistry()
        {
            string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, false))
            {
                if (key != null)
                {
                    object value = key.GetValue("Jigglr");
                    return value != null;
                }
            }
            return false;
        }

        // Applies our executable path to the Windows Logon run registry to bootstrap at next boot
        private void ChkStartWithWindows_CheckedChanged(object sender, EventArgs e)
        {
            if (_isLoading) return; // Prevent redundant registry writes while the form initially checks system state

            string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (chkStartWithWindows.Checked)
                {
                    key.SetValue("Jigglr", string.Format("\"{0}\"", Application.ExecutablePath));
                }
                else
                {
                    try { key.DeleteValue("Jigglr", false); } catch { }
                }
            }
        }

        // Bounce UI thread notifications down so we don't crash cross-thread
        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate { HandleSessionSwitch(e.Reason); });
            }
            else
            {
                HandleSessionSwitch(e.Reason);
            }
        }

        // Ensure we stop faking input when the PC is explicitly locked, allowing idle rules to correctly trigger
        private void HandleSessionSwitch(SessionSwitchReason reason)
        {
            if (!chkAutoPauseLock.Checked) return;

            if (reason == SessionSwitchReason.SessionLock)
            {
                wasJigglingBeforeLock = chkEnableJiggle.Checked;
                if (wasJigglingBeforeLock)
                {
                    chkEnableJiggle.Checked = false;
                }
            }
            else if (reason == SessionSwitchReason.SessionUnlock)
            {
                if (wasJigglingBeforeLock)
                {
                    chkEnableJiggle.Checked = true;
                }
            }
        }

        // Low-level message pump interception; needed to catch our registered Global Hotkey signals
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                chkEnableJiggle.Checked = !chkEnableJiggle.Checked;
            }
            base.WndProc(ref m);
        }

        // Tidy away all our unmanaged bits safely
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                UnregisterHotKey(this.Handle, HOTKEY_ID);

                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }

                if (activeIcon != null) activeIcon.Dispose();
                if (idleIcon != null) idleIcon.Dispose();
                
                // Explicitly cleanse GDI memory
                if (activeIconHandle != IntPtr.Zero) DestroyIcon(activeIconHandle);
                if (idleIconHandle != IntPtr.Zero) DestroyIcon(idleIconHandle);
            }
            base.Dispose(disposing);
        }
    }

    // Program entry-point
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // Apply unhandled exception handlers so the app doesn't disappear silently
            Application.ThreadException += (s, e) =>
                MessageBox.Show(string.Format("Unexpected error: {0}", e.Exception.Message), "Jigglr Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show(ex != null ? ex.Message : "An unknown error occurred.", "Jigglr Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            bool createdNew;
            // Establish a cross-process lock immediately so two Jigglrs don't fight over hotkeys and tray space
            using (Mutex mutex = new Mutex(true, "JigglrAppMutex_0a1b2c3d", out createdNew))
            {
                if (createdNew)
                {
                    // Attempt to notify Windows we handle our own DPI scaling to prevent blurry upscaling
                    try { SetProcessDPIAware(); } catch { }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    
                    MainForm form = new MainForm();
                    Application.Run(form);
                }
                else
                {
                    MessageBox.Show("Jigglr is already running. Check your system tray!", "Jigglr", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
