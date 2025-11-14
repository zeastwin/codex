using EW_Assistant.Component.MindMap;
using EW_Assistant.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EW_Assistant.Views
{
    /// <summary>
    /// 文档知识导图视图：支持拖拽 docx/pdf，自动解析为思维导图。
    /// </summary>
    public partial class KnowledgeMindMapView : UserControl, INotifyPropertyChanged
    {
        private readonly DocumentMindMapParser _parser = new DocumentMindMapParser();
        private MindMapNode _root;
        private MindMapNode _selectedNode;
        private bool _isBusy;
        private bool _isDragHover;
        private string _statusText = "拖入 .docx / .pdf 开始解析";
        private string _outlineSummary = "尚未载入文档";
        private double _canvasWidth = 1200;
        private double _canvasHeight = 800;
        private bool _isPanning;
        private Point _panStart;
        private Point _panOrigin;
        private double _zoom = 1.0;
        private const double MinZoom = 0.3;
        private const double MaxZoom = 3.0;
        private readonly Dictionary<MindMapNode, MindMapVisualNode> _visualLookup = new Dictionary<MindMapNode, MindMapVisualNode>();
        private bool _layoutRefreshScheduled;
        private MindMapNode _layoutRefreshFocus;
        private bool _layoutRefreshReset;
        private bool _resetViewAfterMeasure;

        public KnowledgeMindMapView()
        {
            InitializeComponent();
            VisualNodes = new ObservableCollection<MindMapVisualNode>();
            VisualEdges = new ObservableCollection<MindMapEdge>();
            DataContext = this;
        }

        public ObservableCollection<MindMapVisualNode> VisualNodes { get; }
        public ObservableCollection<MindMapEdge> VisualEdges { get; }

        public MindMapNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode == value) return;
                if (_selectedNode != null) _selectedNode.IsSelected = false;
                _selectedNode = value;
                if (_selectedNode != null) _selectedNode.IsSelected = true;
                OnPropertyChanged();
            }
        }

        public bool HasDocument => _root != null;

        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy == value) return; _isBusy = value; OnPropertyChanged(); }
        }

        public bool IsDragHover
        {
            get => _isDragHover;
            set { if (_isDragHover == value) return; _isDragHover = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
        }

        public string OutlineSummary
        {
            get => _outlineSummary;
            set { if (_outlineSummary == value) return; _outlineSummary = value; OnPropertyChanged(); }
        }

        public double CanvasWidth
        {
            get => _canvasWidth;
            set { if (Math.Abs(_canvasWidth - value) < 0.1) return; _canvasWidth = value; OnPropertyChanged(); }
        }

        public double CanvasHeight
        {
            get => _canvasHeight;
            set { if (Math.Abs(_canvasHeight - value) < 0.1) return; _canvasHeight = value; OnPropertyChanged(); }
        }

        private async void DropHost_OnDrop(object sender, DragEventArgs e)
        {
            IsDragHover = false;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            var target = files.FirstOrDefault(IsSupportedFile);
            if (target == null)
            {
                StatusText = "仅支持 .docx / .pdf 文件";
                return;
            }
            await ParseFileAsync(target);
        }

        private void DropHost_OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var allow = files?.Any(IsSupportedFile) == true;
            e.Effects = allow ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void DropHost_OnPreviewDragEnter(object sender, DragEventArgs e)
        {
            IsDragHover = true;
            DropHost_OnPreviewDragOver(sender, e);
        }

        private void DropHost_OnPreviewDragLeave(object sender, DragEventArgs e)
        {
            IsDragHover = false;
        }

        private async void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "支持的文档|*.docx;*.pdf",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                await ParseFileAsync(dialog.FileName);
            }
        }

        private async Task ParseFileAsync(string path)
        {
            try
            {
                IsBusy = true;
                StatusText = $"正在解析：{Path.GetFileName(path)}";
                var tree = await _parser.ParseAsync(path);
                _root = tree;
                OnPropertyChanged(nameof(HasDocument));
                _resetViewAfterMeasure = true;
                Walk(_root, n => n.IsExpanded = false);
                _root.IsExpanded = false;
                SelectedNode = tree;
                RebuildScene(_root, true);
                StatusText = $"解析完成（{Path.GetFileName(path)}）";
                MainWindow.PostProgramInfo($"知识导图：已解析 {Path.GetFileName(path)}", "ok");
            }
            catch (Exception ex)
            {
                StatusText = $"解析失败：{ex.Message}";
                MainWindow.PostProgramInfo($"知识导图解析失败：{ex.Message}", "error");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RebuildScene(MindMapNode focusNode = null, bool resetView = false)
        {
            if (_root == null)
            {
                VisualNodes.Clear();
                VisualEdges.Clear();
                _visualLookup.Clear();
                CanvasWidth = 900;
                CanvasHeight = 640;
                OutlineSummary = "尚未载入文档";
                if (resetView)
                {
                    _zoom = 1.0;
                    AnimateZoomTo(_zoom);
                    AnimatePanTo(0, 0);
                }
                return;
            }

            Dictionary<MindMapNode, double> knownHeights = null;
            if (_visualLookup.Count > 0)
            {
                knownHeights = new Dictionary<MindMapNode, double>(_visualLookup.Count);
                foreach (var kv in _visualLookup)
                    knownHeights[kv.Key] = kv.Value.Height;
            }

            double ResolveHeight(MindMapNode node)
            {
                if (knownHeights != null && knownHeights.TryGetValue(node, out var recorded) && recorded > 0)
                    return recorded;
                return 80;
            }

            var layout = MindMapLayoutEngine.Layout(
                _root,
                verticalGap: 60,
                nodeHeightProvider: ResolveHeight,
                verticalGapProvider: node =>
                {
                    var height = ResolveHeight(node);
                    var dynamicGap = height * 0.22;
                    if (dynamicGap < 28) dynamicGap = 28;
                    if (dynamicGap > 70) dynamicGap = 70;
                    return dynamicGap;
                });
            var ordered = new List<MindMapVisualNode>();
            var lookup = new Dictionary<MindMapNode, MindMapVisualNode>();

            foreach (var layoutNode in layout.Nodes)
            {
                if (_visualLookup.TryGetValue(layoutNode.Node, out var existing))
                {
                    existing.X = layoutNode.X;
                    existing.Y = layoutNode.Y;
                    lookup[layoutNode.Node] = existing;
                    ordered.Add(existing);
                }
                else
                {
                    ordered.Add(layoutNode);
                    lookup[layoutNode.Node] = layoutNode;
                }
            }

            for (int i = VisualNodes.Count - 1; i >= 0; i--)
            {
                if (!ordered.Contains(VisualNodes[i]))
                    VisualNodes.RemoveAt(i);
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                var desired = ordered[i];
                var currentIndex = VisualNodes.IndexOf(desired);
                if (currentIndex == i) continue;
                if (currentIndex >= 0)
                    VisualNodes.Move(currentIndex, i);
                else
                    VisualNodes.Insert(i, desired);
            }

            VisualEdges.Clear();
            foreach (var edge in layout.Edges)
                VisualEdges.Add(edge);
            _visualLookup.Clear();
            foreach (var kv in lookup)
                _visualLookup[kv.Key] = kv.Value;

            CanvasWidth = layout.Nodes.Count == 0 ? 900 : layout.Nodes.Max(n => n.X + n.Width + 220);
            CanvasHeight = layout.Nodes.Count == 0 ? 640 : layout.Nodes.Max(n => n.Y + n.Height + 180);
            if (resetView)
            {
                _zoom = 1.0;
                ZoomTransform.ScaleX = 1;
                ZoomTransform.ScaleY = 1;
                PanTransform.X = 0;
                PanTransform.Y = 0;
            }

            var totalNodes = CountNodes(_root) - 1;
            var depth = CalcDepth(_root) - 1;
            OutlineSummary = $"节点 {Math.Max(totalNodes, 0)} 个 · 最大层级 {Math.Max(depth, 1)} · 展开中";

            MindMapVisualNode focusVisual = null;
            if (focusNode != null)
                _visualLookup.TryGetValue(focusNode, out focusVisual);
            if (focusVisual == null && _visualLookup.TryGetValue(_root, out var rootVisual))
                focusVisual = rootVisual;
            if (focusVisual != null)
                FocusOnVisual(focusVisual, resetView);
        }

        private void NodeBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.DataContext is not MindMapVisualNode visual) return;
            SelectedNode = visual.Node;
        }

        private void NodeBorder_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border border && border.DataContext is MindMapVisualNode visual)
            {
                visual.Width = e.NewSize.Width;
                visual.Height = e.NewSize.Height;
                var reset = _resetViewAfterMeasure;
                if (_resetViewAfterMeasure)
                    _resetViewAfterMeasure = false;
                RequestLayoutRefresh(visual.Node, reset);
            }
        }

        private void ExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is MindMapVisualNode visual)
            {
                var focus = visual.Node;
                var expanded = toggle.IsChecked == true;
                if (expanded && visual.Node.Children.Count > 0)
                    focus = visual.Node.Children[0];
                RebuildScene(focus);
            }
            else
            {
                RebuildScene();
            }
        }

        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            if (_root == null) return;
            Walk(_root, n => n.IsExpanded = true);
            RebuildScene(_root);
        }

        private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            if (_root == null) return;
            foreach (var child in _root.Children)
                Walk(child, n => n.IsExpanded = false);
            _root.IsExpanded = false;
            RebuildScene(_root, true);
        }

        private void Walk(MindMapNode node, Action<MindMapNode> visitor)
        {
            visitor?.Invoke(node);
            foreach (var child in node.Children)
                Walk(child, visitor);
        }

        private void MindMapScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pointer = e.GetPosition(MindMapCanvas);
            var scaleFactor = e.Delta > 0 ? 1.12 : 0.9;
            var targetZoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom * scaleFactor));
            scaleFactor = targetZoom / _zoom;
            if (Math.Abs(targetZoom - _zoom) < 0.001) return;
            _zoom = targetZoom;

            var translate = PanTransform;
            var targetX = (translate.X - pointer.X) * scaleFactor + pointer.X;
            var targetY = (translate.Y - pointer.Y) * scaleFactor + pointer.Y;

            AnimateZoomTo(_zoom);
            AnimatePanTo(targetX, targetY);
            e.Handled = true;
        }

        private void MindMapCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetViewToRoot();
                e.Handled = true;
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed && !IsNodeElement(e.OriginalSource))
            {
                BeginPan(e.GetPosition(MindMapScroll));
                e.Handled = true;
            }
        }

        private void MindMapCanvas_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            var current = e.GetPosition(MindMapScroll);
            var delta = current - _panStart;
            PanTransform.X = _panOrigin.X + delta.X;
            PanTransform.Y = _panOrigin.Y + delta.Y;
            e.Handled = true;
        }

        private void MindMapCanvas_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            EndPan();
            e.Handled = true;
        }

        private void MindMapCanvas_OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            EndPan();
        }

        private void BeginPan(Point startPoint)
        {
            _isPanning = true;
            _panStart = startPoint;
            _panOrigin = new Point(PanTransform.X, PanTransform.Y);
            PanTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanTransform.BeginAnimation(TranslateTransform.YProperty, null);
            MindMapCanvas.CaptureMouse();
            MindMapCanvas.Cursor = Cursors.Hand;
        }

        private void EndPan()
        {
            _isPanning = false;
            MindMapCanvas.ReleaseMouseCapture();
            MindMapCanvas.Cursor = Cursors.Arrow;
        }

        private static bool IsNodeElement(object source)
        {
            var dep = source as DependencyObject;
            while (dep != null)
            {
                if (dep is FrameworkElement fe && fe.DataContext is MindMapVisualNode)
                    return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void MindMapScroll_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetViewToRoot();
                e.Handled = true;
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed && !IsNodeElement(e.OriginalSource))
            {
                BeginPan(e.GetPosition(MindMapScroll));
                e.Handled = true;
            }
        }

        private void ResetViewToRoot()
        {
            if (_root == null) return;
            AnimateZoomTo(1.0);
            if (_visualLookup.TryGetValue(_root, out var visual))
                FocusOnVisual(visual);
        }

        private void FocusOnVisual(MindMapVisualNode visual, bool resetView = false)
        {
            if (visual == null) return;
            var viewportWidth = MindMapScroll.ViewportWidth > 0 ? MindMapScroll.ViewportWidth : ActualWidth;
            var viewportHeight = MindMapScroll.ViewportHeight > 0 ? MindMapScroll.ViewportHeight : ActualHeight;
            var targetX = viewportWidth / 2.0 - visual.CenterX * _zoom;
            var targetY = viewportHeight / 2.0 - visual.CenterY * _zoom;
            if (resetView)
            {
                PanTransform.X = targetX;
                PanTransform.Y = targetY;
            }
            else
            {
                AnimatePanTo(targetX, targetY);
            }
        }

        private static bool IsSupportedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".docx" || ext == ".pdf";
        }

        private static int CountNodes(MindMapNode node)
        {
            if (node == null) return 0;
            int count = 1;
            foreach (var child in node.Children)
                count += CountNodes(child);
            return count;
        }

        private static int CalcDepth(MindMapNode node)
        {
            if (node == null) return 0;
            if (node.Children.Count == 0) return 1;
            int max = 0;
            foreach (var child in node.Children)
                max = Math.Max(max, CalcDepth(child));
            return 1 + max;
        }

        private void RequestLayoutRefresh(MindMapNode focusNode = null, bool resetView = false)
        {
            if (focusNode != null)
                _layoutRefreshFocus = focusNode;
            _layoutRefreshReset = _layoutRefreshReset || resetView;
            if (_layoutRefreshScheduled) return;
            _layoutRefreshScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _layoutRefreshScheduled = false;
                var focus = _layoutRefreshFocus;
                var reset = _layoutRefreshReset;
                _layoutRefreshFocus = null;
                _layoutRefreshReset = false;
                if (_root == null) return;
                RebuildScene(focus ?? _selectedNode ?? _root, reset);
            }), DispatcherPriority.Background);
        }

        private void AnimatePanTo(double targetX, double targetY)
        {
            var duration = TimeSpan.FromMilliseconds(420);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var animX = new DoubleAnimation(targetX, duration) { EasingFunction = easing };
            var animY = new DoubleAnimation(targetY, duration) { EasingFunction = easing };
            PanTransform.BeginAnimation(TranslateTransform.XProperty, animX);
            PanTransform.BeginAnimation(TranslateTransform.YProperty, animY);
        }

        private void AnimateZoomTo(double targetZoom)
        {
            targetZoom = Math.Max(MinZoom, Math.Min(MaxZoom, targetZoom));
            _zoom = targetZoom;
            var duration = TimeSpan.FromMilliseconds(420);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var animX = new DoubleAnimation(targetZoom, duration) { EasingFunction = easing };
            var animY = new DoubleAnimation(targetZoom, duration) { EasingFunction = easing };
            ZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            ZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}



