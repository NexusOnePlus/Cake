using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
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

    private bool _needsRefreshAndDraw = false;
    private int _highlightIndexOnLoad = -1;

    private int _currentIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        // Loaded += MainWindow_Loaded;
        // SizeChanged += (_, __) => PositionCenterBorder();

       // this.IsVisibleChanged += MainWindow_IsVisibleChanged;
       // this.SizeChanged += MainWindow_SizeChanged;
    }

    /*public void PrepareAndShow(int highlightIndex)
    {
        this._needsRefreshAndDraw = true;
        this._highlightIndexOnLoad = highlightIndex;
        this.Show();
        this.Activate();
    }*/

    public void HandleAltTab(bool isFirstTime)
    {
        if (isFirstTime)
        {
            Debug.WriteLine("[UI] First Tab: Preparing, showing, and stealing focus...");

            RefreshWindowsList();
            if (_windows.Count <= 1)
            {
                Debug.WriteLine("[UI] Not enough windows to switch. Aborting.");
                HandleAltRelease();
                return;
            }

            this.Show();
            this.UpdateLayout();
            PositionCenterBorder();
            DrawPie(_windows);
            this.Activate();

            _currentIndex = 1;
            Highlight(_currentIndex);
        }
        else
        {
            Debug.WriteLine("[UI] Next Tab: Cycling...");
            if (this.IsVisible && _windows.Count > 0)
            {
                _currentIndex = (_currentIndex + 1) % _windows.Count;
                Highlight(_currentIndex);
            }
        }
    }

    public void HandleAltRelease()
    {
        Debug.WriteLine("[UI] Alt Released: Activating selected window and hiding.");
        if (this.IsVisible && _currentIndex != -1)
        {
            ActivateWindow(_currentIndex);
        }

        this.Hide();
        _currentIndex = -1;
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
                if (w.Slice is Path path)
                {
                    path.Fill = Brushes.Transparent;
                }
            }

            var window = _windows[index];
            CenterText.Text = window.Title;
            
            if (window.Slice is Path slice)
            {
                slice.Fill = Brushes.White;
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
        double cornerRadius = 8.0;
        for (int i = 0; i < n; i++)
        {
            var item = items[i];
            double a1Deg = start + i * step;
            double a2Deg = start + (i + 1) * step;

            double startAngleRad = a1Deg * Math.PI / 180.0;
            double endAngleRad = a2Deg * Math.PI / 180.0;

            cornerRadius = Math.Min(cornerRadius, (outer - inner) / 2);
            if (cornerRadius <= 0) cornerRadius = 0.1;

            double outerAngleShift = Math.Asin(cornerRadius / outer);
            double innerAngleShift = Math.Asin(cornerRadius / inner);

            Point pOuterStart = new Point(cx + outer * Math.Cos(startAngleRad + outerAngleShift), cy + outer * Math.Sin(startAngleRad + outerAngleShift));
            Point pOuterEnd = new Point(cx + outer * Math.Cos(endAngleRad - outerAngleShift), cy + outer * Math.Sin(endAngleRad - outerAngleShift));
            Point pInnerEnd = new Point(cx + inner * Math.Cos(endAngleRad - innerAngleShift), cy + inner * Math.Sin(endAngleRad - innerAngleShift));
            Point pInnerStart = new Point(cx + inner * Math.Cos(startAngleRad + innerAngleShift), cy + inner * Math.Sin(startAngleRad + innerAngleShift));

            Point pCornerOuterEnd = new Point(cx + (outer - cornerRadius) * Math.Cos(endAngleRad), cy + (outer - cornerRadius) * Math.Sin(endAngleRad));
            Point pCornerInnerEnd = new Point(cx + (inner + cornerRadius) * Math.Cos(endAngleRad), cy + (inner + cornerRadius) * Math.Sin(endAngleRad));
            Point pCornerInnerStart = new Point(cx + (inner + cornerRadius) * Math.Cos(startAngleRad), cy + (inner + cornerRadius) * Math.Sin(startAngleRad));
            Point pCornerOuterStart = new Point(cx + (outer - cornerRadius) * Math.Cos(startAngleRad), cy + (outer - cornerRadius) * Math.Sin(startAngleRad));

            bool largeArc = (a2Deg - a1Deg) > 180.0;
            var cornerSize = new Size(cornerRadius, cornerRadius);

            var fig = new PathFigure { StartPoint = pOuterStart, IsClosed = true };

            fig.Segments.Add(new ArcSegment(pOuterEnd, new Size(outer, outer), 0, largeArc, SweepDirection.Clockwise, true));
            fig.Segments.Add(new ArcSegment(pCornerOuterEnd, cornerSize, 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pCornerInnerEnd, true));
            fig.Segments.Add(new ArcSegment(pInnerEnd, cornerSize, 0, false, SweepDirection.Clockwise, true));

            fig.Segments.Add(new ArcSegment(pInnerStart, new Size(inner, inner), 0, largeArc, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new ArcSegment(pCornerInnerStart, cornerSize, 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pCornerOuterStart, true));
            fig.Segments.Add(new ArcSegment(pOuterStart, cornerSize, 0, false, SweepDirection.Clockwise, true));

            var geom = new PathGeometry();
            geom.Figures.Add(fig);

            var slice = new Path { Data = geom, Fill = Brushes.Transparent, Tag = geom };
            item.Slice = slice;


            double mid = (startAngleRad + endAngleRad) / 2.0;
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
                slice.Fill = Brushes.White;
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

            /*var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(10))
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 90)
            };
            slice.BeginAnimation(OpacityProperty, fade);
            (iconElement as FrameworkElement)?.BeginAnimation(OpacityProperty, fade);*/
            slice.Opacity = 1.0;
            if (iconElement is FrameworkElement fe)
                fe.Opacity = 1.0;
        }
    }
}
