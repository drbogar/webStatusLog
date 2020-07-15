using LiteDB;
using Microsoft.Win32;
using NativeWifi;
using Newtonsoft.Json;
using SpeedTest;
using SpeedTest.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace WslService
{
    public partial class WslService : ServiceBase
    {
        private Guid GUID;
        private HttpClient httpClient;
        private SpeedTestClient speedTestClient;
        private Settings speedTestSettings;
        private Timer timer;

        public class Connection
        {
            public string Type { get; set; }
            public string ConnectedToMac { get; set; }
        }

        public class Speeds
        {
            public double Download { get; set; }
            public double Upload { get; set; }
        }

        public class WebStatusLog
        {
            public DateTime Timestamp { get; set; }
            public Guid Guid { get; set; }
            public Connection Connection { get; set; }
            public Speeds Speeds { get; set; }
        }

        public WslService()
        {
            InitializeComponent();
            Prepares();
        }

        private void Prepares()
        {
            GUID = GetGuid();
            httpClient = new HttpClient();
            speedTestClient = new SpeedTestClient();
            // TODO: logging with NLog
            timer = new Timer()
            {
                Interval = 60000,
                AutoReset = true,
                Enabled = false,
            };
            timer.Elapsed += Timer_Elapsed;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            WebStatusLog webStatusLog;
            Speeds speedsObject;
            PhysicalAddress wlanMac = CheckWlanConnection();
            bool connectionIsAvailable = CheckForInternetConnection();
            speedsObject = new Speeds()
            {
                Download = 0,
                Upload = 0
            };
            if (connectionIsAvailable)
            {
                speedsObject = CheckSpeed();
            }
            webStatusLog = new WebStatusLog
            {
                Timestamp = DateTime.UtcNow,
                Guid = GUID,
                Connection = new Connection()
                {
                    ConnectedToMac = wlanMac != null ? wlanMac.ToString() : "",
                    Type = wlanMac != null ? "Wlan" : "Ethernet",
                },
                Speeds = speedsObject,
            };
            Task saveTask = SaveDataAsync(webStatusLog);

            if (connectionIsAvailable)
            {
                Task sendTask = Task.Run(async () =>
                {
                    List<WebStatusLog> notSendedsArray = await GetNotSendedsAsync(GUID);
                    foreach (WebStatusLog wslObject in notSendedsArray)
                    {
                        HttpResponseMessage response = await SendDataAsync(wslObject);
                        if (!response.IsSuccessStatusCode)
                        {
                            break;
                        }
                    }
                });
            }
        }

        protected override void OnStart(string[] args)
        {
            timer.Start();
        }

        protected override void OnStop()
        {
            timer.Stop();
        }

        private Guid GetGuid()
        {
            string registrySubKeyPath = "SOFTWARE\\Microsoft\\Cryptography";
            using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(registrySubKeyPath))
            {
                Guid guid = new Guid(registryKey.GetValue("MachineGuid").ToString());
                return guid;
            }
        }

        private object GetConfig(string name)
        {
            string registrySubKeyPath = "SYSTEM\\CurrentControlSet\\Services\\Parameters";
            using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(registrySubKeyPath))
            {
                return registryKey.GetValue(name);
            }
        }

        private PhysicalAddress CheckWlanConnection()
        {
            WlanClient wlanClient = new WlanClient();
            try
            {
                PhysicalAddress physicalAddress = wlanClient.Interfaces
                .FirstOrDefault(wli => wli.InterfaceState == Wlan.WlanInterfaceState.Connected)
                .CurrentConnection.wlanAssociationAttributes.Dot11Bssid;
                return physicalAddress;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool CheckForInternetConnection()
        {
            try
            {
                using (WebClient client = new WebClient())
                using (client.OpenRead("http://google.com/generate_204"))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private Speeds CheckSpeed()
        {
            // TODO: own speedtest client
            speedTestSettings = speedTestClient.GetSettings();

            var servers = SelectServers();
            var bestServer = SelectBestServer(servers);

            var downloadSpeed = speedTestClient.TestDownloadSpeed(bestServer, speedTestSettings.Download.ThreadsPerUrl);
            var uploadSpeed = speedTestClient.TestUploadSpeed(bestServer, speedTestSettings.Upload.ThreadsPerUrl);
            return new Speeds()
            {
                Download = downloadSpeed,
                Upload = uploadSpeed
            };
        }

        private async Task SaveDataAsync(WebStatusLog wslObject)
        {
            // TODO: remove the unnecessary layer from the SaveDataAsync
            Task saveDataTask = Task.Factory.StartNew(() => {
                using (LiteDatabase liteDatabase = new LiteDatabase("Filename=" + GetConfig("databaseFilePath") + "; Connection=shared"))
                {
                    ILiteCollection<WebStatusLog> liteCollection = liteDatabase.GetCollection<WebStatusLog>("logs");
                    liteCollection.Insert(wslObject);
                }
            });

            await saveDataTask;
        }

        private async Task<HttpResponseMessage> SendDataAsync(WebStatusLog wslObject)
        {
            string json = JsonConvert.SerializeObject(wslObject);
            StringContent httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage httpResponse = await httpClient.PostAsync(new Uri(GetConfig("apiUri").ToString() + "/logs"), httpContent);
            return httpResponse;
        }

        private async Task<List<WebStatusLog>> GetNotSendedsAsync(Guid guid)
        {
            // TODO: authentication for API requests
            List<WebStatusLog> notSendedsList = new List<WebStatusLog>();
            HttpResponseMessage httpResponse;
            try
            {
                // TODO: get last item from the API instead of all
                httpResponse = await httpClient.GetAsync(new Uri(GetConfig("apiUri").ToString() + "/logs?guid=" + guid));
            }
            catch (Exception)
            {
                return notSendedsList;
            }
            string responseJson = await httpResponse.Content.ReadAsStringAsync();
            List<WebStatusLog> responseObject = JsonConvert.DeserializeObject<List<WebStatusLog>>(responseJson);
            using (LiteDatabase liteDatabase = new LiteDatabase("Filename=" + GetConfig("databaseFilePath") + "; Connection=shared"))
            {
                ILiteCollection<WebStatusLog> liteCollection = liteDatabase.GetCollection<WebStatusLog>("logs");
                List<WebStatusLog> allSavedArray = liteCollection.Query().ToList();

                foreach (WebStatusLog dbWebStatusLog in allSavedArray)
                {
                    bool isNotSended = true;
                    foreach (WebStatusLog apiWebStatusLog in responseObject)
                    {
                        if (dbWebStatusLog.Timestamp.ToUniversalTime() == apiWebStatusLog.Timestamp.ToUniversalTime())
                        {
                            isNotSended = false;
                            break;
                        }
                    }
                    if (isNotSended)
                    {
                        notSendedsList.Add(dbWebStatusLog);
                    }
                }
            }
            return notSendedsList;
        }

        #region SpeedTestRegion
        private IEnumerable<Server> SelectServers()
        {
            var servers = speedTestSettings.Servers.Take(10).ToList();

            foreach (var server in servers)
            {
                server.Latency = speedTestClient.TestServerLatency(server);
            }
            return servers;
        }

        private Server SelectBestServer(IEnumerable<Server> servers)
        {
            var bestServer = servers.OrderBy(x => x.Latency).First();
            return bestServer;
        }
        #endregion
    }
}
