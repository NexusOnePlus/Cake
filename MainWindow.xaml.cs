using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
namespace Cake;


public enum AppType
{
    Path,
    Aumid
}

public class WindowItem
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public Shape? Slice { get; set; }
}

  
   
public partial class MainWindow : System.Windows.Window
{

    private readonly WindowService _windowService = new();
    private List<WindowItem> _windows = new();
    private readonly List<UIElement> _groups = new();
    private Path? _ringPath = null;

    private double _centerX, _centerY;
    private double _outerRadius = 170;
    private double _innerRadius = 60;
    private bool _isVisible = false;

    private bool _needsRefreshAndDraw = false;
    private int _highlightIndexOnLoad = -1;

    public MainWindow()
    {
        InitializeComponent();
       // Loaded += MainWindow_Loaded;
       // SizeChanged += (_, __) => PositionCenterBorder();

        this.IsVisibleChanged += MainWindow_IsVisibleChanged;
        this.SizeChanged += MainWindow_SizeChanged;
    }

    public void PrepareAndShow(int highlightIndex)
    {
        this._needsRefreshAndDraw = true;
        this._highlightIndexOnLoad = highlightIndex;
        this.Show();
        this.Activate();
    }


    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_needsRefreshAndDraw)
        {
            _needsRefreshAndDraw = false;

            Debug.WriteLine("[WINDOW] ContentRendered: Drawing.");
            PositionCenterBorder();
            RefreshWindowsList();

            if (_windows.Count <= 1)
            {
                Debug.WriteLine("[UI] No windows. Hiding selector.");
                this.Hide();
                return;
            }

            Highlight(_highlightIndexOnLoad);
        }
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool isNowVisible = (bool)e.NewValue;
        KeyboardHook.SetSelectorVisibility(isNowVisible);

        if (isNowVisible)
        {
            if (this.ActualWidth > 0 && _needsRefreshAndDraw)
            {
                Debug.WriteLine("[WINDOW] IsVisibleChanged, Refreshing and Drawing.");
                ExecuteRefreshAndDraw();
            }
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width == 0 || e.NewSize.Height == 0) return;

        PositionCenterBorder();

        if (_needsRefreshAndDraw)
        {
            Debug.WriteLine("[WINDOW] SizeChanged, Refreshing and Drawing.");
            ExecuteRefreshAndDraw();
        }
    }

    private void ExecuteRefreshAndDraw()
    {
        _needsRefreshAndDraw = false;

        RefreshWindowsList();

        if (_windows.Count <= 1)
        {
            Debug.WriteLine("[UI] No windows. Hiding.");
            this.Hide();
            return;
        }

        Highlight(_highlightIndexOnLoad);   
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[WINDOW] MainWindow ready");
        PositionCenterBorder();
        BuildFromCurrentWindows();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        int exStyle = (int)GetWindowLong(hwnd, GWL_EXSTYLE);
        // WS_EX_TOOLWINDOW = 0x00000080
        // WS_EX_APPWINDOW  = 0x00040000
        exStyle |= WS_EX_TOOLWINDOW;
        exStyle &= ~WS_EX_APPWINDOW;

        SetWindowLong(hwnd, GWL_EXSTYLE, (IntPtr)exStyle);

       // Debug.WriteLine("[WINDOW] MainWindow ToolWindow");
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);


    public int WindowsCount => _windows.Count;

    public new void Show()
    {
        try
        {
            base.Show();
            this.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WINDOW] ERROR SHOW WINDOW: {ex.Message}");
        }
    }

    public new void Hide()
    {
        try
        {
            base.Hide();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WINDOW] ERROR HIDE WINDOW: {ex.Message}");
        }
    }


    public void RefreshWindowsList()
    {
        Debug.WriteLine("[REFRESH] Reloading...");
        _windows = _windowService.EnumerateWindowsWithIcons().Where(w => w.Icon != null).ToList();
        if (_windows.Count == 0) _windows.Add(new WindowItem { Title = "No apps" });
        Debug.WriteLine($"[REFRESH] Found {_windows.Count} windows.");
        DrawPie(_windows);
    }


    public void Highlight(int index)
    {
        if (index >= 0 && index < _windows.Count)
        {
            foreach (var w in _windows)
            {
                if (w.Slice != null)
                    w.Slice.Fill = Brushes.Transparent;
            }

            var window = _windows[index];
            CenterText.Text = window.Title;

            if (window.Slice is Shape slice)
            {
                var grad = new RadialGradientBrush();
                grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.5));
                slice.Fill = grad;
            }

            Debug.WriteLine($"[HIGHLIGHT] Focusing window: {window.Title}");
        }
    }

    public void ActivateWindow(int index)
    {
        if (index >= 0 && index < _windows.Count)
        {
            NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);
            NativeMethods.GetWindowPlacement(_windows[index].Hwnd, ref placement);
            if (placement.showCmd == NativeMethods.SW_SHOWMINIMIZED || NativeMethods.IsIconic(_windows[index].Hwnd))
            {
                NativeMethods.ShowWindow(_windows[index].Hwnd, NativeMethods.SW_RESTORE);
            }
            else if (placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED)
            {
                NativeMethods.ShowWindow(_windows[index].Hwnd, NativeMethods.SW_SHOWMAXIMIZED);
            }
            else
            {
                NativeMethods.ShowWindow(_windows[index].Hwnd, NativeMethods.SW_RESTORE);
            }

            NativeMethods.SetForegroundWindow(_windows[index].Hwnd);

        }
    }



    private void PositionCenterBorder()
    {
        _centerX = MainCanvas.ActualWidth / 2;
        _centerY = MainCanvas.ActualHeight / 2;
        Canvas.SetLeft(CenterBorder, _centerX - (CenterBorder.Width / 2));
        Canvas.SetTop(CenterBorder, _centerY - (CenterBorder.Height / 2));
    }

    private void BuildFromCurrentWindows()
    {
        _windows = _windowService.EnumerateWindowsWithIcons().Where(w => w.Icon != null).ToList();
        if (_windows.Count == 0)
            _windows.Add(new WindowItem { Title = "No apps" });

        DrawPie(_windows);
    }


    private void DrawPie(List<WindowItem> items)
    {
        foreach (var g in _groups) MainCanvas.Children.Remove(g);
        _groups.Clear();
        if (_ringPath != null)
        {
            MainCanvas.Children.Remove(_ringPath);
            _ringPath = null;
        }

        double cx = _centerX;
        double cy = _centerY;
        double outer = _outerRadius;
        double inner = _innerRadius;

        {
            var ring = new EllipseGeometry(new Point(cx, cy), outer, outer);
            var hole = new EllipseGeometry(new Point(cx, cy), inner, inner);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, ring, hole);



            _ringPath = new Path
            {
                Data = combined,
                Fill = Brushes.Black,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.6
                }
            };


            MainCanvas.Children.Insert(0, _ringPath);
        }





        int n = Math.Max(1, items.Count);
        double step = 360.0 / n;
        double start = -90;

        for (int i = 0; i < n; i++)
        {
            var item = items[i];
            double a1Deg = start + i * step;
            double a2Deg = start + (i + 1) * step;
            double a1 = a1Deg * Math.PI / 180.0;
            double a2 = a2Deg * Math.PI / 180.0;

            var pOuter1 = new Point(cx + outer * Math.Cos(a1), cy + outer * Math.Sin(a1));
            var pOuter2 = new Point(cx + outer * Math.Cos(a2), cy + outer * Math.Sin(a2));
            var pInner2 = new System.Windows.Point(cx + inner * Math.Cos(a2), cy + inner * Math.Sin(a2));
            var pInner1 = new Point(cx + inner * Math.Cos(a1), cy + inner * Math.Sin(a1));
            bool largeArc = (a2Deg - a1Deg) > 180.0;

            var fig = new PathFigure { StartPoint = pOuter1, IsClosed = true };
            fig.Segments.Add(new ArcSegment(pOuter2, new System.Windows.Size(outer, outer), 0, largeArc, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pInner2, true));
            fig.Segments.Add(new ArcSegment(pInner1, new System.Windows.Size(inner, inner), 0, largeArc, SweepDirection.Counterclockwise, true));

            var geom = new PathGeometry();
            geom.Figures.Add(fig);

            /*var grad = new RadialGradientBrush();
            grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(70, 130, 180), 0.0));
            grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(30, 60, 120), 1.0));
            */
            var slice = new System.Windows.Shapes.Path
            {
                Data = geom,
                Fill = Brushes.Transparent,
            };

            item.Slice = slice;


            double mid = (a1 + a2) / 2.0;
            double iconR = inner + (outer - inner) * 0.5;
            double iconX = cx + iconR * Math.Cos(mid);
            double iconY = cy + iconR * Math.Sin(mid);

            UIElement iconElement;
            if (item.Icon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = item.Icon,
                    Width = 64,
                    Height = 64,
                    Opacity = 0.0,
                    SnapsToDevicePixels = true,
                };
                Canvas.SetLeft(img, iconX - img.Width / 2);
                Canvas.SetTop(img, iconY - img.Height / 2);
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                iconElement = img;
            }
            else
            {
                var tb = new TextBlock
                {
                    Text = item.Title ?? "?",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    Opacity = 0.0
                };
                Canvas.SetLeft(tb, iconX - 20);
                Canvas.SetTop(tb, iconY - 10);
                iconElement = tb;
            }

            var group = new Canvas { Background = Brushes.Transparent };
            group.Children.Add(slice);
            group.Children.Add(iconElement);

            int idx = i;
            group.MouseEnter += (_, __) =>
            {
                var grad = new RadialGradientBrush();
                grad.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(255, 255, 255), 0.5));
                slice.Fill = grad;
                CenterText.Text = items[idx].Title;
            };
            group.MouseLeave += (_, __) =>
            {
                slice.Fill = Brushes.Transparent;
                CenterText.Text = "Which";
            };

            group.MouseLeftButtonUp += (_, __) =>
            {
                if (items[idx].Hwnd != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(items[idx].Hwnd);
            };

            MainCanvas.Children.Add(group);
            _groups.Add(group);

            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 90)
            };
            slice.BeginAnimation(OpacityProperty, fade);
            (iconElement as FrameworkElement)?.BeginAnimation(OpacityProperty, fade);
        }
    }
}
