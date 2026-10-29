using System;
using System.ServiceProcess;
using System.Threading;

namespace Maquim.OfficeInstaller
{
    internal sealed class ComputerMonitorService : ServiceBase
    {
        Timer timer;
        int busy;
        readonly MonitorStore store = new MonitorStore();
        MonitorEngine engine;
        LocationResolver location;
        LocationPipeHost bridge;
        public ComputerMonitorService() { ServiceName = "MaquimComputerMonitor"; CanStop = true; CanShutdown = true; AutoLog = true; }
        protected override void OnStart(string[] args) {
            store.Config(); engine = new MonitorEngine(store);
            location = new LocationResolver(store.Root); bridge = new LocationPipeHost(store.Root); bridge.Start();
            timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }
        void Tick(object state) {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            try { engine.Tick(DateTimeOffset.Now, MonitorCollector.IPs, MonitorCollector.Disks, MonitorTransport.Send, (now, ips) => location.Resolve(now, ips, !String.IsNullOrEmpty(store.Token()))); }
            catch { try { store.Note("Falha ao coletar/enviar status. A fila foi preservada; nova tentativa em até 1 minuto."); } catch { } }
            finally { Interlocked.Exchange(ref busy, 0); }
        }
        protected override void OnStop() {
            if (bridge != null) { bridge.Dispose(); bridge = null; }
            var active = Interlocked.Exchange(ref timer, null);
            if (active != null) {
                RequestAdditionalTime(40000);
                using (var stopped = new ManualResetEvent(false)) { if (active.Dispose(stopped)) stopped.WaitOne(35000); }
            }
            // Escritas atomicas protegem a fila mesmo se o SCM finalizar durante uma requisicao.
        }
        protected override void OnShutdown() { OnStop(); base.OnShutdown(); }
        static void Main() { ServiceBase.Run(new ComputerMonitorService()); }
    }
}
