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

namespace EW_Assistant.Views
{
    /// <summary>
    /// 鏂囨。鐭ヨ瘑瀵煎浘瑙嗗浘锛氭敮鎸佹嫋鎷?docx/pdf锛岃嚜鍔ㄨВ鏋愪负鎬濈淮瀵煎浘銆?    /// </summary>
    public partial class KnowledgeMindMapView : UserControl, INotifyPropertyChanged
    {
        private readonly DocumentMindMapParser _parser = new DocumentMindMapParser();
        private MindMapNode _root;
        private MindMapNode _selectedNode;
        private bool _isBusy;
        private bool _isDragHover;
        private string _statusText = "拖入 .docx / .pdf 开始解析;
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
                StatusText = "浠呮敮鎸?.docx / .pdf 鏂囦欢";
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
                Filter = "鏀寔鐨勬枃妗*.docx;*.pdf",
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
                StatusText = $"姝ｅ湪瑙ｆ瀽锛歿Path.GetFileName(path)}";
                var tree = await _parser.ParseAsync(path);
                _root = tree;
                Walk(_root, n => n.IsExpanded = false);
                _root.IsExpanded = false;
                SelectedNode = tree;
                RebuildScene(_root, true);
                StatusText = $"瑙ｆ瀽瀹屾垚锛坽Path.GetFileName(path)}锛?;
                MainWindow.PostProgramInfo($"鐭ヨ瘑瀵煎浘锛氬凡瑙ｆ瀽 {Path.GetFileName(path)}", "ok");
            }
            catch (Exception ex)
            {
                StatusText = $"瑙ｆ瀽澶辫触锛歿ex.Message}";
                MainWindow.PostProgramInfo($"鐭ヨ瘑瀵煎浘瑙ｆ瀽澶辫触锛歿ex.Message}", "error");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RebuildScene(MindMapNode focusNode = null, bool resetView = false)
        {
            VisualNodes.Clear();
            VisualEdges.Clear();
            _visualLookup.Clear();
            if (_root == null)
            {
                CanvasWidth = 900;
                CanvasHeight = 640;
                OutlineSummary = "尚未载入文档";
                return;
            }

            var layout = MindMapLayoutEngine.Layout(_root);
            foreach (var node in layout.Nodes)
            {
                VisualNodes.Add(node);
                if (!_visualLookup.ContainsKey(node.Node))
                    _visualLookup.Add(node.Node, node);
            }
            foreach (var edge in layout.Edges)
                VisualEdges.Add(edge);

            CanvasWidth = layout.Nodes.Count == 0 ? 900 : layout.Nodes.Max(n => n.X + n.Width + 200);
            CanvasHeight = layout.Nodes.Count == 0 ? 640 : layout.Nodes.Max(n => n.Y + n.Height + 160);
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
            OutlineSummary = $"鑺傜偣 {Math.Max(totalNodes, 0)} 涓?路 鏈€澶у眰绾?{Math.Max(depth, 1)} 路 灞曞紑涓?;

            if (focusNode != null && _visualLookup.TryGetValue(focusNode, out var visual))
                FocusOnVisual(visual);
        }

        private static int CountNodes(MindMapNode node)
        {
            if (node == null) return 0;
            var count = 1;
            foreach (var child in node.Children)
                count += CountNodes(child);
            return count;
        }

        private static int CalcDepth(MindMapNode node)
        {
            if (node == null) return 0;
            if (node.Children.Count == 0) return 1;
            return 1 + node.Children.Max(CalcDepth);
        }

        private static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".docx" || ext == ".pdf";
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
            RebuildScene(_root);
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
            _zoom = targetZoom;

            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;

            var translate = PanTransform;
            translate.X = (translate.X - pointer.X) * scaleFactor + pointer.X;
            translate.Y = (translate.Y - pointer.Y) * scaleFactor + pointer.Y;

            e.Handled = true;
        }

        private void MindMapCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
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

        private void FocusOnVisual(MindMapVisualNode visual)
        {
            if (visual == null) return;
            var viewportWidth = MindMapScroll.ViewportWidth;
            var viewportHeight = MindMapScroll.ViewportHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0) return;
            var targetX = viewportWidth / 2.0 - visual.CenterX * _zoom;
            var targetY = viewportHeight / 2.0 - visual.CenterY * _zoom;
            PanTransform.X = targetX;
            PanTransform.Y = targetY;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
