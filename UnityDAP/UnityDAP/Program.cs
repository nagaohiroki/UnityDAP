using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
namespace UnityDAP
{
	internal class Program
	{
		static void Main(string[] args)
		{
			var processes = new ProcessList();
			processes.Create();
		}
	}
	public class ProcessList
	{
		readonly List<UnityProcess> processes = [];
		readonly List<int> ports = [];
		public override string ToString() => string.Join("\n", processes);
		public void Create()
		{
			ScanProcess();
			var task = Task.Run(CheckPorts);
			task.Wait();
			ShowPorts();
			Console.WriteLine(ToString());
		}
		void ScanProcess()
		{
			var processes = Process.GetProcesses();
			foreach(var process in processes)
			{
				Add(process);
			}
		}
		void ShowPorts()
		{
			foreach(var port in ports)
			{
				Console.WriteLine($"Port: {port}");
			}

		}
		async Task CheckPorts()
		{
			var hostName = Dns.GetHostName();
			var address = Dns.GetHostEntry(hostName);
			IPAddress? interNetwork = null;
			foreach(var ip in address.AddressList)
			{
				if(ip.AddressFamily == AddressFamily.InterNetwork)
				{
					interNetwork = ip;
				}
			}
			if(interNetwork == null) { return; }
			Console.WriteLine($"IP Address: {interNetwork}");
			await ScanPorts(interNetwork.MapToIPv4().ToString(), 56000, 56999);
		}
		void Add(Process process)
		{
			var runtime = UnityProcess.Runtime.None;
			if(IsWindowsApp(process))
			{
				runtime = UnityProcess.Runtime.Player;
			}
			if(IsEditor(process))
			{
				runtime = UnityProcess.Runtime.Editor;
			}
			if(runtime == UnityProcess.Runtime.None) { return; }
			processes.Add(new UnityProcess(process));
		}
		static bool IsEditor(Process process)
		{
			if(process.MainWindowHandle == IntPtr.Zero) { return false; }
			return process.ProcessName == "Unity";
		}
		static bool IsWindowsApp(Process process)
		{
			if(process.MainWindowHandle == IntPtr.Zero) { return false; }
			if(process.MainModule == null) { return false; }
			var fileName = process.MainModule.FileName;
			var directory = Path.GetDirectoryName(fileName);
			if(directory == null) { return false; }
			if(!File.Exists(Path.Combine(directory, "UnityPlayer.dll"))) { return false; }
			return true;
		}
		async Task ScanPorts(string ipAddress, int startPort, int endPort)
		{
			var ip = IPAddress.Parse(ipAddress);
			var tasks = new List<Task>();

			for(int port = startPort; port <= endPort; port++)
			{
				tasks.Add(CheckPortAsync(ip, port));
			}
			await Task.WhenAll(tasks);
		}
		async Task CheckPortAsync(IPAddress ip, int port)
		{
			try
			{
				// TcpClientを使用して接続を試行
				using var client = new TcpClient();
				// タイムアウト設定
				var connectTask = client.ConnectAsync(ip, port);
				var timeoutTask = Task.Delay(500);
				var completedTask = await Task.WhenAny(connectTask, timeoutTask);
				if(completedTask == connectTask)
				{
					ports.Add(port);
				}
			}
			catch(SocketException)
			{
				// 接続に失敗した場合
				Console.WriteLine($"Port {port} is closed.");
			}
			catch(Exception ex)
			{
				Console.WriteLine($"Error checking port {port}: {ex.Message}");
			}
		}
	}
	public class UnityProcess
	{
		public enum Platform
		{
			Windows,
			Linux,
			OSX,
		}
		public enum Runtime
		{
			None,
			Editor,
			Player
		}
		public int Id { get; set; }
		public string address { get; set; } = string.Empty;
		public int debugPort { get; set; }
		public int messagePort { get; set; }
		public string name { get; set; } = string.Empty;
		public override string ToString() => $" {name}:{Id} ,address:{address} ,debug:{debugPort}, message:{messagePort}";
		public UnityProcess(Process process)
		{
			Id = process.Id;
			name = process.ProcessName;
			address = "127.0.0.1";
			debugPort = GetDebugPort();
			messagePort = GetMessagePort();
		}
		int GetDebugPort()
		{
			return 56000 + (Id % 1000);
		}
		int GetMessagePort()
		{
			return GetDebugPort() + 2;
		}
		//	IPEndPoint GetIPEndPoint()
		//	{
		//int num = FindFirstPortInRange(unityProcess, listeners, 55000, 55999);
		//		return null;
		//	}
	}
}
