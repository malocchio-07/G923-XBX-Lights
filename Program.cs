using System;


using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

static class DllFinder {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern IntPtr LoadLibrary(string path);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern bool SetDllDirectory(string lpPathName);

  public static void EnsureEnginesWrapperLoaded() {
    string[] dirs = {
      @"C:\Program Files\LGHUB\dep",
      @"C:\Program Files\LGHUB",
      @"C:\Program Files (x86)\LGHUB\dep",
      @"C:\Program Files (x86)\LGHUB"
    };
    foreach (var d in dirs) {
      var p = Path.Combine(d, "LogitechSteeringWheelEnginesWrapper.dll");
      if (File.Exists(p)) {
        // make sure this dir is on the search path, then load it explicitly
        SetDllDirectory(d);
        var h = LoadLibrary(p);
        if (h != IntPtr.Zero) { Console.WriteLine("Loaded EnginesWrapper: " + p); return; }
      }
    }
    Console.WriteLine("⚠️ Could not pre-load EnginesWrapper; relying on default search path.");
  }
}


class Program {
  static int FindWheel() { for (int i=0;i<4;i++) if (LogitechGSDK.LogiIsConnected(i)) return i; return -1; }

  static void Main(string[] args) {
    int port = args.Length>0 ? int.Parse(args[0]) : 5610;

    // 1) Preload the same DLL the demo uses
    DllFinder.EnsureEnginesWrapperLoaded();

    // 2) Init SDK (G HUB must be running)
    bool init = LogitechGSDK.LogiSteeringInitialize(true);
    Console.WriteLine($"SDK init = {init}");

    // --- Quick 5s LED self-test (comment out after first run) ---
    var t0 = DateTime.UtcNow;
    while ((DateTime.UtcNow - t0).TotalSeconds < 5) {
      LogitechGSDK.LogiUpdate();
      int w = FindWheel();
      if (w >= 0) {
        int idle=2000, max=8000, cur=7600; // force bright LEDs
        bool ok = LogitechGSDK.LogiPlayLeds(w, cur, idle + (int)(0.6*(max-idle)), (int)(0.98*max));
        if (ok) Console.WriteLine($"LED OK on wheel {w}");
      }
    }
    // ------------------------------------------------------------

    // 3) Telemetry loop
    using var udp = new UdpClient(port);
    var ep = new IPEndPoint(IPAddress.Any, port);
    Console.WriteLine($"Listening on UDP :{port} (FH5 Data Out must match)");

    float smooth=0, a=0.35f;
    while (true) {
      var buf = udp.Receive(ref ep);
      if (buf.Length < 20) continue;
      int isOn = BitConverter.ToInt32(buf,0);
      if (isOn==0) continue;

      float max  = BitConverter.ToSingle(buf,8);
      float idle = BitConverter.ToSingle(buf,12);
      float cur  = BitConverter.ToSingle(buf,16);
      if (max < 1000f) max = 8000f;

      smooth = (smooth==0) ? cur : smooth + a*(cur - smooth);

      LogitechGSDK.LogiUpdate();
      int w = FindWheel();
      if (w >= 0) {
        int first = (int)MathF.Max(idle + 0.60f*(max-idle), idle + 500f);
        int red   = (int)(0.98f*max);
        LogitechGSDK.LogiPlayLeds(w, (int)smooth, first, red);
        Console.WriteLine($"wheel={w} rpm={smooth:0}/{max:0}");
      }
    }
  }
}
