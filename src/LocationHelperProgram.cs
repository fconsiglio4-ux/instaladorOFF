using System;
using System.Device.Location;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Maquim.OfficeInstaller
{
    internal sealed class LocationHelperContext : ApplicationContext
    {
        readonly NotifyIcon icon;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 300000 };
        bool busy;
        const string Key = @"Software\Maquim\ComputerLocation";
        bool Enabled { get { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && Convert.ToInt32(k.GetValue("Enabled", 0)) == 1; } }
        public LocationHelperContext() {
            icon = new NotifyIcon { Icon = SystemIcons.Information, Text = "Maquim — Localização do computador", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Configurar localização", null, (s,e) => Configure());
            menu.Items.Add("Configurações de localização do Windows", null, (s,e) => { try { Process.Start("ms-settings:privacy-location"); } catch { } });
            menu.Items.Add("Atualizar agora", null, async (s,e) => await Capture(true));
            menu.Items.Add("Sair do auxiliar de localização", null, (s,e) => ExitThread());
            icon.ContextMenuStrip = menu; icon.DoubleClick += (s,e) => Configure();
            timer.Tick += async (s,e) => await Capture(true); timer.Start();
            var first = new System.Windows.Forms.Timer { Interval = 1000 };
            first.Tick += async (s,e) => { first.Stop(); first.Dispose(); using (var k = Registry.CurrentUser.OpenSubKey(Key)) { if (k == null) { Configure(); return; } } await Capture(true); };
            first.Start();
        }
        void Configure() {
            using (var dialog = new Form { Text = "Maquim — Localização do computador", StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(700, 350), Font = new Font("Segoe UI", 10), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) {
                var message = new Label { Left = 20, Top = 20, Width = 660, Height = 250, Text = "O agente Maquim envia o status deste computador ao Roteiro Maquim a cada 12 horas e em mudanças de IP.\n\nVocê pode permitir consultar a localização do Windows (latitude, longitude, precisão e horário). A precisão depende dos recursos e das permissões do Windows.\n\nSem uma posição válida, o serviço usará a localização aproximada pelo IP público. O auxiliar permanece visível na área de notificação." };
                var yes = new Button { Text = "Usar localização do Windows", Left = 20, Top = 285, Width = 310, Height = 36, DialogResult = DialogResult.Yes };
                var no = new Button { Text = "Somente IP público", Left = 365, Top = 285, Width = 310, Height = 36, DialogResult = DialogResult.No };
                dialog.Controls.AddRange(new Control[] { message, yes, no });
                var result = dialog.ShowDialog(); if (result != DialogResult.Yes && result != DialogResult.No) return;
                using (var key = Registry.CurrentUser.CreateSubKey(Key)) key.SetValue("Enabled", result == DialogResult.Yes ? 1 : 0, RegistryValueKind.DWord);
                Capture(result != DialogResult.Yes).ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        async Task Capture(bool suppressPermissionPrompt) {
            if (busy) return; busy = true;
            var value = LocationSnapshot.Unavailable("unavailable");
            try {
                if (Enabled) using (var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.High)) {
                    watcher.Start(suppressPermissionPrompt);
                    var end = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < end) {
                        if (watcher.Permission == GeoPositionPermission.Denied) { value = LocationSnapshot.Unavailable("permission_denied"); break; }
                        var position = watcher.Position; var c = position.Location;
                        if (!c.IsUnknown) {
                            var candidate = new LocationSnapshot { source = "windows", latitude = c.Latitude, longitude = c.Longitude, accuracy_meters = c.HorizontalAccuracy, captured_at = position.Timestamp.ToString("o"), windows_status = "available" };
                            if (LocationSnapshot.Fresh(candidate, DateTimeOffset.Now)) { value = candidate; break; }
                        }
                        if (watcher.Status == GeoPositionStatus.Disabled) break;
                        await Task.Delay(500);
                    }
                    watcher.Stop();
                }
                bool delivered = await Task.Run(() => LocationPipeHost.Publish(value));
                icon.Text = delivered ? (value.source == "windows" ? "Maquim — Localização Windows disponível" : "Maquim — Usando alternativa por IP público") : "Maquim — Serviço de monitoramento indisponível";
            } catch { icon.Text = "Maquim — Localização Windows indisponível"; try { LocationPipeHost.Publish(LocationSnapshot.Unavailable("error")); } catch { } }
            finally { busy = false; }
        }
        protected override void ExitThreadCore() { timer.Stop(); timer.Dispose(); icon.Visible = false; icon.Dispose(); base.ExitThreadCore(); }
    }
    internal static class LocationHelperProgram
    {
        [STAThread]
        static void Main() {
            bool fresh;
            // Local: uma instancia por sessao, sem privilegios de administrador.
            using (var mutex = new Mutex(true, @"Local\MaquimComputerLocation", out fresh)) {
                if (!fresh) return;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var context = new LocationHelperContext()) Application.Run(context);
            }
        }
    }
}
