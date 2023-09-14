using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace Eclipse;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length != 0 && CheckInstancesFromRunningProcesses(out var processes, out var currentProcess))
        {
            var _otherInstance = GetAlreadyRunningInstance(processes, currentProcess);
            SendDataMessage(_otherInstance, e.Args[0]);
            Environment.Exit(0);
        }
        else
        {
            var mainWindow = new MainWindow();
            if (e.Args.Length == 1) mainWindow.Play(e.Args[0]);
            mainWindow.Show();
        }
    }

    public static bool CheckInstancesFromRunningProcesses(out Process[] processes, out Process currentProcess)
    {
        currentProcess = Process.GetCurrentProcess();
        processes = Process.GetProcessesByName(currentProcess.ProcessName).ToArray();

        return processes.Length > 1;
    }
    public static Process GetAlreadyRunningInstance(Process[] processes, Process currentProcess)
    {
        for (var i = 0; i < processes.Length; i++)
            if (processes[i].Id != currentProcess.Id) return processes[i];
        return new Process();
    }

    public static void SendDataMessage(Process targetProcess, string msg)
    {
        var _stringMessageBuffer = Marshal.StringToHGlobalUni(msg);
        var _copyDataBuff = IntPtrAlloc<COPYDATASTRUCT>(new COPYDATASTRUCT
        {
            dwData = IntPtr.Zero,
            lpData = _stringMessageBuffer,
            cbData = msg.Length * 2
        });
        _ = SendMessage(targetProcess.MainWindowHandle, 74, IntPtr.Zero, _copyDataBuff);
        Marshal.FreeHGlobal(_copyDataBuff);
        Marshal.FreeHGlobal(_stringMessageBuffer);
    }

    [LibraryImport("user32", EntryPoint = "SendMessageA")]
    private static partial int SendMessage(IntPtr Hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

    // Token: 0x06000009 RID: 9 RVA: 0x000021DC File Offset: 0x000003DC
    public static IntPtr IntPtrAlloc<T>(T param)
    {
        if (param == null) throw new ArgumentNullException(nameof(param));

        var retval = Marshal.AllocHGlobal(Marshal.SizeOf(param));
        Marshal.StructureToPtr(param, retval, false);
        return retval;
    }
}

internal struct COPYDATASTRUCT
{
    public IntPtr dwData;
    public int cbData;
    public IntPtr lpData;
}
