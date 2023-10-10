using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;
using NAudio.Wave;
using System.IO;
using YamlDotNet.Serialization;

namespace CenturnkTITS
{
    public class AudioRecorder
    {
        private UdpClient listener;
        private System.Timers.Timer inactivityTimer;
        private WaveFileWriter waveFileWriter;
      //  private string basePath = "./";
        private Config config;
        private bool isCallInProgress = false;
        private int srcIdCounter = 0;

        public AudioRecorder(string yamlFilePath)
        {
            config = LoadConfigFromYaml(yamlFilePath);
            Initialize();
        }

        private Config LoadConfigFromYaml(string yamlFilePath)
        {
            var deserializer = new DeserializerBuilder().Build();
            var yaml = File.ReadAllText(yamlFilePath);
            return deserializer.Deserialize<Config>(yaml);
        }

        private void Initialize()
        {
            listener = new UdpClient(config.ReceiveUdp.Port);

            inactivityTimer = new System.Timers.Timer(1000);
            inactivityTimer.Elapsed += InactivityTimer_Elapsed;
            inactivityTimer.AutoReset = false;

            Task.Run(() => ListenForPackets());
        }

        private void ListenForPackets()
        {
            while (true)
            {
                try
                {
                    var endPoint = new IPEndPoint(IPAddress.Parse(config.ReceiveUdp.Address), config.ReceiveUdp.Port);
                    byte[] receivedBytes = listener.Receive(ref endPoint);

                    if (receivedBytes.Length < 320)
                    {
                        Console.WriteLine($"Received unexpected data {receivedBytes.Length}");
                        continue;
                    }

                    int srcId = (receivedBytes[0] << 24) |
                                (receivedBytes[1] << 16) |
                                (receivedBytes[2] << 8) |
                                receivedBytes[3];

                    int audioDataStartIndex = 8;
                    int audioDataLength = 320;

                    int dstId = (receivedBytes[4] << 24) |
                                (receivedBytes[5] << 16) |
                                (receivedBytes[6] << 8) |
                                receivedBytes[7];

                    Console.WriteLine($"Received network call: srcId: {srcId}, dstId: {dstId}");

                    inactivityTimer.Stop();
                    inactivityTimer.Start();

                    byte[] audioData = new byte[audioDataLength];
                    Buffer.BlockCopy(receivedBytes, audioDataStartIndex, audioData, 0, audioDataLength);

                    if (!isCallInProgress)
                    {
                        StartNewCall(srcId, dstId);
                    }

                    if (waveFileWriter != null)
                    {
                        waveFileWriter.Write(audioData, 0, audioData.Length);
                        waveFileWriter.Flush();
                    }
                }
                catch (SocketException)
                {
                    break;
                }
            }

            FinalizeCurrentWriter();
        }

        private void StartNewCall(int srcId, int dstId)
        {
            isCallInProgress = true;

            srcIdCounter++;

            string currentDate = DateTime.Now.ToString("yyyyMMdd");
            string currentTime = DateTime.Now.ToString("HHmm");

            string logsPath = Path.Combine(config.LogPath, "logs");
            if (!Directory.Exists(logsPath)) Directory.CreateDirectory(logsPath);

            string datePath = Path.Combine(logsPath, currentDate);
            if (!Directory.Exists(datePath)) Directory.CreateDirectory(datePath);

            string timePath = Path.Combine(datePath, currentTime);
            if (!Directory.Exists(timePath)) Directory.CreateDirectory(timePath);

            string dstIdPath = Path.Combine(timePath, dstId.ToString());
            if (!Directory.Exists(dstIdPath)) Directory.CreateDirectory(dstIdPath);

            string wavFileName = $"{srcId}_{srcIdCounter}.wav";
            string fullPath = Path.Combine(dstIdPath, wavFileName);

            waveFileWriter = new WaveFileWriter(fullPath, new WaveFormat(8000, 16, 1));
        }


        private void InactivityTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            EndCurrentCall();
        }

        private void EndCurrentCall()
        {
            isCallInProgress = false;
            FinalizeCurrentWriter();
        }
        private void FinalizeCurrentWriter()
        {
            if (waveFileWriter != null)
            {
                waveFileWriter.Flush();
                waveFileWriter.Dispose();
                waveFileWriter = null;
            }
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            FinalizeCurrentWriter();
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("Starting CentrunkTITS application...");

            var recorder = new AudioRecorder("config.yml");

            AppDomain.CurrentDomain.ProcessExit += recorder.CurrentDomain_ProcessExit;


            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    public class Config
    {
        public ReceiveUdpConfig ReceiveUdp { get; set; }
        public string LogPath { get; set; }

    }

    public class ReceiveUdpConfig
    {
        public string Address { get; set; }
        public int Port { get; set; }
    }
}
