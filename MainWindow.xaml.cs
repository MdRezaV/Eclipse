using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TagLib;
using WpfScreenHelper;
using Window = System.Windows.Window;

namespace Eclipse;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (GetActiveMonitors() > 1) isPc = true;

        defaultWidth = Width;
        defaultHeight = Height;

        defaultX = isPc ? -20 - defaultWidth : 20;
        defaultY = MON_HEIGHT - defaultHeight - 44 - 20;

        Left = defaultX;
        Top = defaultY;

        // Default Location
        MoveToDefaultLocation(false);

        // Default Theme
        ThemeLightMode();

        // Default Vis
        ToggleFullScreen(false);

        InitializeOutputDevice();

        new Thread(() =>
        {
            InitializeAudioDevice();

            if (audioDevice is not null)
            {
                while (true)
                {
                    AddVisData(audioDevice);

                    RenderVisFrame();
                }
            }
        }).Start();

        if (outputDevice is not null)
            outputDevice.PlaybackStopped += OutputDevice_PlaybackStopped;

        SeekInit();
        UpdaterInit();
    }

    #region Init

    private WaveOutEvent? outputDevice;
    private AudioFileReader? audioFile;
    private MMDevice? audioDevice;

    private void InitializeOutputDevice()
    {
        try
        {
            outputDevice = new WaveOutEvent();
        }
        catch { outputDevice = null; }
    }
    private void InitializeAudioDevice()
    {
        try
        {
            audioDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch { }
    }

    #endregion

    #region Vis

    private int avgCount = 0;
    private double xMult = 0;
    private double amp = 0;
    private float[] visData = new float[ushort.MaxValue + 1];
    private float[] lastnums = Array.Empty<float>();
    private ushort visDataIndex = 0;
    private ushort readIndex = 0;
    private int avgIndex = 0;
    private int lastPoints = -1;
    private object visLocker = new();

    private void AddVisData(MMDevice audioDevice)
    {
        unchecked
        {
            lock (visLocker)
            {
                lastnums[avgIndex] = audioDevice.AudioMeterInformation.MasterPeakValue;
                visData[visDataIndex] = lastnums.Average();

                if (++avgIndex >= avgCount)
                {
                    visDataIndex++;
                    avgIndex = 0;
                }
            }
        }
    }

    private void BeginReadVisData()
    {
        unchecked
        {
            readIndex = visDataIndex;
        }
    }
    private float ReadNextVisData()
    {
        unchecked
        {
            var ind = readIndex - 1;
            if (ind < 0) ind += ushort.MaxValue;
            readIndex = (ushort)ind;
            return visData[ind];
        }
    }

    private void RenderVisFrame()
    {
        var width = 0D;
        var height = amp;

        Dispatcher.Invoke(() =>
        {
            width = MainVisCanvas.ActualWidth;
            MainVisCanvas.Height = height;
        });

        var margin = 40D;
        var dx = xMult / avgCount * avgIndex;

        var xHalfMult = xMult / 2;

        var numPoints = (int)((width - (margin * 2)) / xMult);

        if (numPoints <= 0) return;

        var actualWidth = numPoints * xMult;
        margin = (width - actualWidth) / 2;

        BeginReadVisData();

        var p = new Point[numPoints];

        for (var i = 0; i < p.Length; i++)
        {
            var x = (xMult * i) + margin + dx;
            var y = CustomMap(ReadNextVisData());
            p[i] = new Point(x, y);
        }

        var c = new Point[p.Length][];

        var dxt = xMult * 0.4;

        for (var i = 1; i < p.Length - 1; i++)
        {
            var dy = p[i + 1].Y - p[i - 1].Y;
            var dyt = dy * 0.2;

            var x = p[i].X + dxt;
            var y = p[i].Y + dyt;

            var x2 = p[i].X - dxt;
            var y2 = p[i].Y - dyt;

            c[i] = new Point[2];
            c[i][0] = new Point(x, y);
            c[i][1] = new Point(x2, y2);
        }

        if (lastPoints == numPoints)
        {
            Dispatcher.Invoke(() =>
            {
                var geometry = (PathGeometry)MainVisPath.Data;
                var segments = geometry.Figures[0].Segments;
                geometry.Figures[0].StartPoint = p[0];
                var segstart = (QuadraticBezierSegment)segments[0];
                segstart.Point1 = c[1][1];
                segstart.Point2 = p[1];

                for (var i = 1; i < p.Length - 2; i++)
                {
                    var seg = (BezierSegment)segments[i];
                    seg.Point1 = c[i][0];
                    seg.Point2 = c[i + 1][1];
                    seg.Point3 = p[i + 1];
                }

                var segend = (QuadraticBezierSegment)segments[numPoints - 2];
                segend.Point1 = c[p.Length - 2][0];
                segend.Point2 = p[^1];
            });
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                var segments = new List<PathSegment>
                {
                    new QuadraticBezierSegment(c[1][1], p[1], isStroked: true)
                };

                for (var i = 1; i < p.Length - 2; i++)
                    segments.Add(new BezierSegment(c[i][0], c[i + 1][1], p[i + 1], isStroked: true));

                segments.Add(new QuadraticBezierSegment(c[p.Length - 2][0], p[^1], isStroked: true));

                var figures = new List<PathFigure>
                {
                    new PathFigure(p[0], segments, closed: false)
                };

                MainVisPath.Data = new PathGeometry(figures);

                //using (var file = new StreamWriter(@"C:\Users\Mmdre\Desktop\New Text Document.txt", false))
                //    file.Write($"M {margin} {firstY} {string.Join(' ', segments.Where(x => x is BezierSegment).Cast<BezierSegment>().Select(x => $"C {x.Point1.X} {x.Point1.Y} {x.Point2.X} {x.Point2.Y} {x.Point3.X} {x.Point3.Y}"))}");
            });

            lastPoints = numPoints;
        }

        var sleep = lastnums.Average();
        Thread.Sleep((int)(10 - (sleep * 4)));
    }
    private double CustomMap(double vol)
    {
        return (-amp * vol * vol) + amp;
    }

    #endregion

    #region Visuals

    private int theme = 0;
    private void ThemeLightMode()
    {
        MainBorder.Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)); // WINDOW_BACKGROUND
        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)); // WINDOW_BORDERBRUSH
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[0].Color = Color.FromArgb(255, 255, 255, 255); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[1].Color = Color.FromArgb(255, 0, 0, 0); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[2].Color = Color.FromArgb(255, 0, 0, 0); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[3].Color = Color.FromArgb(255, 255, 255, 255); // VIS_STROKE
        BirdPath.Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)); // BIRD_FILL
        MainVisPath.StrokeThickness = 1;
    }

    private void ThemeDarkMode()
    {
        MainBorder.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); // WINDOW_BACKGROUND
        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)); // WINDOW_BORDERBRUSH
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[0].Color = Color.FromArgb(255, 0, 0, 0); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[1].Color = Color.FromArgb(255, 255, 255, 255); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[2].Color = Color.FromArgb(255, 255, 255, 255); // VIS_STROKE
        ((LinearGradientBrush)MainVisPath.Stroke).GradientStops[3].Color = Color.FromArgb(255, 0, 0, 0); // VIS_STROKE
        BirdPath.Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)); // BIRD_FILL
        MainVisPath.StrokeThickness = 2;
    }

    private bool f12Active = false;
    private void ToggleF12()
    {
        if (f12Active)
        {
            MoveToDefaultLocation(true);
            Width = defaultWidth;
            Height = defaultHeight;
            Topmost = false;
            f12Active = false;
        }
        else
        {
            WindowState = WindowState.Normal;
            Width = MON_WIDTH * 2;
            Height = MON_HEIGHT;
            Left = -MON_WIDTH;
            Top = 0;
            Topmost = true;
            f12Active = true;
        }
        ToggleFullScreen(f12Active);
    }

    private void ToggleF11()
    {
        if (f12Active) ToggleF12();

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            ToggleFullScreen(false);
        }
        else
        {
            WindowState = WindowState.Maximized;
            ToggleFullScreen(true);
        }
    }

    private void ToggleFullScreen(bool full)
    {
        lock (visLocker)
        {
            if (full)
            {
                amp = 200;
                avgCount = 10;
                xMult = 20;
                MainBorder.BorderThickness = new Thickness(0);
                Canvas.SetTop(Bird, amp + 15);
            }
            else
            {
                amp = 26;
                avgCount = 5;
                xMult = 3;
                MainBorder.BorderThickness = new Thickness(1);
                Canvas.SetTop(Bird, amp + 9);
            }
            avgIndex = 0;
            lastnums = new float[avgCount];
        }
    }

    #endregion

    #region Controls

    private double defaultX;
    private double defaultY;
    private const int MON_WIDTH = 1920;
    private const int MON_HEIGHT = 1080;
    private double defaultWidth;
    private double defaultHeight;
    private bool isPc = false;

    private static int GetActiveMonitors()
    {
        return Screen.AllScreens.Count();
    }

    private void MoveToDefaultLocation(bool screenRel)
    {
        if (screenRel)
        {
            var pos = System.Windows.Forms.Cursor.Position;
            var s = Screen.FromPoint(new Point(pos.X, pos.Y));
            var bounds = s.WpfBounds;

            var oneX = bounds.Width / 3 + bounds.X;
            var oneY = bounds.Height / 3 + bounds.Y;

            var twoX = 2 * bounds.Width / 3 + bounds.X;
            var twoY = 2 * bounds.Height / 3 + bounds.Y;

            if (pos.X < oneX) defaultX = bounds.X + 20;
            else if (pos.X < twoX) defaultX = bounds.Width / 2 + bounds.X - defaultWidth / 2;
            else defaultX = -20 - defaultWidth + bounds.X + bounds.Width;

            if (pos.Y < oneY) defaultY = bounds.Y + 20;
            else if (pos.Y < twoY) defaultY = bounds.Height / 2 + bounds.Y - defaultHeight / 2;
            else defaultY = bounds.Height - defaultHeight - 44 - 20;
        }
        else
        {
            defaultX = isPc ? -20 - defaultWidth : 20;
            defaultY = MON_HEIGHT - defaultHeight - 44 - 20;
        }

        Left = defaultX;
        Top = defaultY;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 74)
        {
            var _dataStruct = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            var _strMsg = Marshal.PtrToStringUni(_dataStruct.lpData, _dataStruct.cbData / 2);
            Play(_strMsg);
        }
        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(new HwndSourceHook(WndProc));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (f12Active) goto case Key.F12;
                    else
                    {
                        WindowState = WindowState.Normal;
                        ToggleFullScreen(false);
                    }
                    break;
                case Key.F11:
                    ToggleF11();
                    break;
                case Key.F12:
                    if (isPc)
                        ToggleF12();
                    break;
                case Key.S:
                    switch (++theme)
                    {
                        case 0: ThemeLightMode(); break;
                        case 1: ThemeDarkMode(); break;
                        default: theme = 0; goto case 0;
                    }
                    break;
                case Key.T:
                    Topmost = !Topmost;
                    break;
                case Key.D:
                    if (!f12Active)
                        MoveToDefaultLocation(true);
                    break;
                case Key.Space:
                    PauseResume();
                    break;
                case Key.Right:
                    if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) Seek(15.0);
                    else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) Seek(8.0);
                    else Seek(2.5);
                    break;
                case Key.Left:
                    if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) Seek(-15.0);
                    else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) Seek(-8.0);
                    else Seek(-2.5);
                    break;
            }
        }
        catch { }
    }

    private bool controlPanelMouseToggle = false;

    private void MainBorder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (!f12Active)
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragMove();
                    controlPanelMouseToggle = false;
                }
        }
        catch { }
    }

    private void MainBorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        controlPanelMouseToggle = true;
    }

    private void MainBorder_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (controlPanelMouseToggle == true && e.ChangedButton == MouseButton.Left)
                PauseResume();
            controlPanelMouseToggle = false;
        }
        catch { }
    }

    private void MainBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) Seek((e.Delta > 0) ? -8 : 8);
            else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) Seek((e.Delta > 0) ? -15 : 15);
            else Seek((e.Delta > 0) ? -2.5 : 2.5);
        }
        catch { }
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                Play(files[0]);
            }
        }
        catch { }
    }

    #endregion

    #region Music Stuff

    private bool ignore = false;
    public void Play(string fileName)
    {
        try
        {
            if (outputDevice is null) return;

            audioFile = new AudioFileReader(fileName);
            ignore = true;
            if (outputDevice.PlaybackState > PlaybackState.Stopped) outputDevice.Stop();
            outputDevice.Init(audioFile);

            using (var tag = File.Create(fileName))
            {
                var tagString = string.IsNullOrWhiteSpace(tag.Tag.Title) ? "Empty" : tag.Tag.Title;
                TextTextBlock.TextAlignment = TextAlignment.Center;
                TextTextBlock.FontSize = 19;
                TextTextBlock.Text = tagString;
            }
            outputDevice.Play();
        }
        catch { }
    }

    public void PauseResume()
    {
        try
        {
            if (outputDevice is null) return;
            if (audioFile is null) return;

            if (outputDevice.PlaybackState == PlaybackState.Playing)
            {
                ignore = true;
                outputDevice.Pause();
            }
            else
            {
                ignore = false;
                if ((long)Math.Floor(audioFile.CurrentTime.TotalMilliseconds) == (long)Math.Floor(audioFile.TotalTime.TotalMilliseconds))
                    audioFile.Position = 0L;
                outputDevice.Play();
            }
        }
        catch { }
    }

    private object seekLocker = new();
    private bool seeking = false;
    private ManualResetEvent resetEvent = new(false);
    private TimeSpan seekFirst = TimeSpan.Zero;
    private double amount = 0;
    private int remainingTime = 0;
    private const int maxTime = 4;
    private void Seek(double amn)
    {
        try
        {
            if (audioFile != null)
            {
                lock (seekLocker)
                {
                    if (!seeking)
                    {
                        seeking = true;
                        _ = resetEvent.Set();
                        seekFirst = audioFile.CurrentTime;
                        amount = amn;
                    }
                    else
                    {
                        amount += amn;
                    }
                    remainingTime = maxTime;
                    Canvas.SetLeft(Bird, Math.Min(Width - 50, Math.Max(40.0, Map(seekFirst.TotalMilliseconds + (amount * 1000.0), 0.0, audioFile.TotalTime.TotalMilliseconds, 40.0, Width - 50))));
                }
            }
        }
        catch { }
    }

    private void SeekInit()
    {
        _ = Task.Run(delegate ()
        {
            try
            {
                while (true)
                {
                    _ = resetEvent.WaitOne();
                    while (true)
                    {
                        lock (seekLocker)
                        {
                            if (--remainingTime <= 0)
                            {
                                if (audioFile != null)
                                {
                                    var timeSpan = seekFirst.Add(TimeSpan.FromSeconds(amount));

                                    if (timeSpan.TotalMilliseconds < 0.0) timeSpan = TimeSpan.Zero;
                                    if (timeSpan > audioFile.TotalTime) timeSpan = audioFile.TotalTime;

                                    audioFile.CurrentTime = timeSpan;
                                    Bird.Dispatcher.Invoke(delegate ()
                                    {
                                        Canvas.SetLeft(Bird, Map(audioFile.CurrentTime.TotalMilliseconds, 0.0, audioFile.TotalTime.TotalMilliseconds, 40.0, Width - 50));
                                    });
                                    seeking = false;
                                    _ = resetEvent.Reset();
                                }
                                break;
                            }
                        }
                        Thread.Sleep(100);
                    }
                }
            }
            catch
            {
            }
        });
    }

    private void UpdaterInit()
    {
        _ = Task.Run(delegate ()
        {
            try
            {
                while (true)
                {
                    if (audioFile != null)
                    {
                        bool s;
                        lock (seekLocker)
                        {
                            s = !seeking;
                        }
                        if (s)
                        {
                            Bird.Dispatcher.Invoke(delegate ()
                            {
                                Canvas.SetLeft(Bird, Map(audioFile.CurrentTime.TotalMilliseconds, 0.0, audioFile.TotalTime.TotalMilliseconds, 40.0, Width - 50));
                            });
                        }
                    }
                    Thread.Sleep(100);
                }
            }
            catch { }
        });
    }

    private void OutputDevice_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (audioFile != null && !ignore)
        {
            audioFile.Position = 0L;
            PauseResume();
        }
        if (ignore) ignore = false;
    }

    public static double Map(double value, double fromSource, double toSource, double fromTarget, double toTarget)
    {
        return ((value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget)) + fromTarget;
    }

    #endregion
}
