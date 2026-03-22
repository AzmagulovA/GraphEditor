using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace GraphEditor
{

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Point _lastMousePosition;
        private bool _isPanning;
        private List<GraphNode> _nodes = new List<GraphNode>();
        private List<GraphEdge> _edges = new List<GraphEdge>();

        // Переменные для рисования связей
        private bool _isDrawingEdge = false;
        private GraphNode _sourceNodeForEdge = null;
        private Line _tempLine = null;
        private string _recommendationsText = "";
        private List<List<GraphNode>> _pendingGroupsToCollapse = new List<List<GraphNode>>();

        public string RecommendationsText
        {
            get => _recommendationsText;
            set { _recommendationsText = value; OnPropertyChanged(); }
        }

        private double _currentKtc;
        public double CurrentKtc
        {
            get => _currentKtc;
            set { _currentKtc = value; OnPropertyChanged(); }
        }


        private Brush _ktcColor = Brushes.Black;
        public Brush KtcColor
        {
            get => _ktcColor;
            set { _ktcColor = value; OnPropertyChanged(); }
        }

        private string _ktcStatusText = "Ожидание данных...";
        public string KtcStatusText
        {
            get => _ktcStatusText;
            set { _ktcStatusText = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private void UpdateAnalytics()
        {
            CurrentKtc = GraphAnalyzer.CalculateKhd(_nodes, _edges);

            if (_nodes.Count < 3)
            {
                KtcColor = Brushes.Gray;
                KtcStatusText = "Недостаточно узлов для анализа структуры.";
            }
            else if (CurrentKtc <= 0.3)
            {
                KtcColor = Brushes.Green;
                KtcStatusText = "Оптимальный граф. Каждый узел несет уникальную топологическую функцию.";
            }
            else if (CurrentKtc <= 0.6)
            {
                KtcColor = Brushes.DarkOrange;
                KtcStatusText = "Средняя избыточность. Присутствуют транзитные узлы или небольшие дублирования.";
            }
            else
            {
                KtcColor = Brushes.Red;
                KtcStatusText = "Гипердетализация! Обнаружены обширные кластеры или армии клонов. Требуется свертка.";
            }
            var recs = GraphAnalyzer.GetGroupingRecommendations(_nodes, _edges);
            if (recs.Any())
            {
                RecommendationsText = string.Join("\n\n", recs);
            }
            else
            {
                RecommendationsText = "Рекомендаций по группировке пока нет.";
            }
            _pendingGroupsToCollapse = GraphAnalyzer.GetNodesToGroup(_nodes, _edges);

            if (_pendingGroupsToCollapse.Any())
            {
                RecommendationsText = $"Найдено {_pendingGroupsToCollapse.Count} избыточных групп.\nНажмите кнопку для объединения.";
                BtnAutoGroup.Visibility = Visibility.Visible; 
            }
            else
            {
                RecommendationsText = "Рекомендаций по группировке пока нет.";
                BtnAutoGroup.Visibility = Visibility.Collapsed;
            }
        }

        // Обработчик нажатия на кнопку авто-свертки
        private void BtnAutoGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingGroupsToCollapse.Any())
            {
                var groupToCollapse = _pendingGroupsToCollapse.First();
                GroupNodesToMetaNode(groupToCollapse);

            }
        }

        #region 1. Логика панорамирования (ПКМ)

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingEdge) return;

            var border = sender as Border;
            if (border != null)
            {
                _lastMousePosition = e.GetPosition(border);
                _isPanning = true;
                border.CaptureMouse(); 
                Cursor = Cursors.SizeAll;
            }
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                var border = sender as Border;
                _isPanning = false;
                if (border != null)
                {
                    border.ReleaseMouseCapture(); 
                }
                Cursor = Cursors.Arrow;
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var border = sender as IInputElement;
                var currentPosition = e.GetPosition(border);
                var delta = currentPosition - _lastMousePosition;
                CanvasTranslate.X += delta.X;
                CanvasTranslate.Y += delta.Y;
                _lastMousePosition = currentPosition;
            }

            if (_isDrawingEdge && _tempLine != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
            }
        }

        #endregion

        #region 2. Логика Связей (С ИСПОЛЬЗОВАНИЕМ HIT TEST)

        private void Node_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var thumb = sender as Thumb;
            if (thumb == null) return;
            var node = thumb.DataContext as GraphNode;
            if (RbRemoveNode.IsChecked == true)
            {
                DeleteNode(node);
                e.Handled = true; 
                return;
            }
            if (RbAddEdge.IsChecked == true)
            {
                if (thumb != null)
                {
                    StartDrawingEdge(thumb.DataContext as GraphNode, GetThumbCenter(thumb));
                    e.Handled = true;
                }
            }
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingEdge)
            {
                Point point = e.GetPosition(GraphCanvas);

                GraphNode targetNode = FindNodeUnderMouse(point);

                if (targetNode != null && 
                    targetNode != _sourceNodeForEdge)
                {
                    CreateEdge(_sourceNodeForEdge, targetNode);
                }

                StopDrawingEdge();

                GraphCanvas.ReleaseMouseCapture();
            }
        }

        private void StartDrawingEdge(GraphNode sourceNode, Point startPoint)
        {
            _isDrawingEdge = true;
            _sourceNodeForEdge = sourceNode;

            _tempLine = new Line
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                X1 = startPoint.X,
                Y1 = startPoint.Y,
                X2 = startPoint.X,
                Y2 = startPoint.Y,
                IsHitTestVisible = false // Чтобы не мешала HitTest'у
            };

            GraphCanvas.Children.Add(_tempLine);

            GraphCanvas.CaptureMouse();
        }

        private void StopDrawingEdge()
        {
            _isDrawingEdge = false;
            _sourceNodeForEdge = null;

            if (_tempLine != null)
            {
                GraphCanvas.Children.Remove(_tempLine);
                _tempLine = null;
            }
        }

        private GraphNode FindNodeUnderMouse(Point point)
        {
            GraphNode result = null;

            VisualTreeHelper.HitTest(GraphCanvas, null,
                new HitTestResultCallback(hit =>
                {
                    var visual = hit.VisualHit;
                    while (visual != null)
                    {
                        if (visual is Thumb thumb && thumb.DataContext is GraphNode node)
                        {
                            result = node;
                            return HitTestResultBehavior.Stop; 
                        }
                        visual = VisualTreeHelper.GetParent(visual);
                    }
                    return HitTestResultBehavior.Continue; 
                }),
                new PointHitTestParameters(point));

            return result;
        }

        private void CreateEdge(GraphNode nodeA, GraphNode nodeB)
        {
            // Проверка дубликатов
            bool exists = _edges.Any(e =>
                (e.NodeA == nodeA && e.NodeB == nodeB) ||
                (e.NodeA == nodeB && e.NodeB == nodeA));

            if (exists) return;

            var line = new Line
            {
                Stroke = Brushes.Black,
                StrokeThickness = 3,
                Cursor = Cursors.Hand
            };

            line.MouseLeftButtonDown += Edge_MouseLeftButtonDown;

            var edge = new GraphEdge { NodeA = nodeA, NodeB = nodeB, VisualLine = line };
            _edges.Add(edge);

            Panel.SetZIndex(line, 0);
            GraphCanvas.Children.Add(line);

            UpdateLinePosition(edge);

            UpdateAnalytics();
        }

        private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var line = sender as Line;
            var edge = _edges.FirstOrDefault(x => x.VisualLine == line);
            if (edge == null) return;

            if (RbRemoveEdge.IsChecked == true)
            {
                RemoveEdge(edge);
                return;
            }

            if (RbAddEdge.IsChecked == true || RbSelect.IsChecked == true)
            {
                Point clickPos = e.GetPosition(GraphCanvas);
                Point posA = new Point(line.X1, line.Y1);
                Point posB = new Point(line.X2, line.Y2);

                RemoveEdge(edge); 

                if ((clickPos - posA).Length < (clickPos - posB).Length)
                {
                    StartDrawingEdge(edge.NodeB, posB);
                }
                else
                {
                    StartDrawingEdge(edge.NodeA, posA);
                }

                _tempLine.X2 = clickPos.X;
                _tempLine.Y2 = clickPos.Y;
                e.Handled = true;
            }
        }

        private void RemoveEdge(GraphEdge edge)
        {
            GraphCanvas.Children.Remove(edge.VisualLine);
            _edges.Remove(edge);
            UpdateAnalytics();
        }
        private void DeleteNode(GraphNode node)
        {
            var edgesToRemove = _edges.Where(e => e.NodeA == node || e.NodeB == node).ToList();

            foreach (var edge in edgesToRemove)
            {
                if (edge.VisualLine != null)
                {
                    GraphCanvas.Children.Remove(edge.VisualLine);
                }
                _edges.Remove(edge);
            }

            var thumb = GetThumbByNode(node);
            if (thumb != null)
            {
                GraphCanvas.Children.Remove(thumb);
            }
            _nodes.Remove(node);
            UpdateAnalytics();
        }

        #endregion

        #region 3. Управление Узлами

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RbAddNode.IsChecked == true)
            {
                CreateNode(e.GetPosition(GraphCanvas));
            }
        }

        private void CreateNode(Point position)
        {
            var nodeData = new GraphNode();
            nodeData.Name = $"Узел {GraphCanvas.Children.OfType<Thumb>().Count() + 1}";

            if (double.TryParse(TbDefaultWeight.Text.Replace('.', ','), out double weight))
            {
                nodeData.Importance = weight;
            }

            var newNode = new Thumb
            {
                Style = (Style)FindResource("NodeStyle"),
                DataContext = nodeData,
                Width = 60,
                Height = 60
            };

            newNode.MouseDoubleClick += Node_MouseDoubleClick;
            newNode.DragDelta += Node_DragDelta;

            newNode.PreviewMouseLeftButtonDown += Node_PreviewMouseLeftButtonDown;

            Canvas.SetLeft(newNode, position.X - 30);
            Canvas.SetTop(newNode, position.Y - 30);

            Panel.SetZIndex(newNode, 10);
            GraphCanvas.Children.Add(newNode);

            bool showName = RbShowName.IsChecked == true;
            nodeData.SetDisplayMode(showName);
            _nodes.Add(nodeData);
            UpdateAnalytics();
        }

        private void Node_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (RbSelect.IsChecked == true)
            {
                var thumb = sender as Thumb;
                if (thumb == null) return;

                double left = Canvas.GetLeft(thumb) + e.HorizontalChange;
                double top = Canvas.GetTop(thumb) + e.VerticalChange;

                Canvas.SetLeft(thumb, left);
                Canvas.SetTop(thumb, top);

                UpdateConnectedEdges(thumb.DataContext as GraphNode);
            }
        }

        private void UpdateConnectedEdges(GraphNode node)
        {
            var relatedEdges = _edges.Where(edge => edge.NodeA == node || edge.NodeB == node);
            foreach (var edge in relatedEdges)
            {
                UpdateLinePosition(edge);
            }
        }

        private void UpdateLinePosition(GraphEdge edge)
        {
            var thumbA = GetThumbByNode(edge.NodeA);
            var thumbB = GetThumbByNode(edge.NodeB);

            if (thumbA != null && thumbB != null)
            {
                Point centerA = GetThumbCenter(thumbA);
                Point centerB = GetThumbCenter(thumbB);
                edge.VisualLine.X1 = centerA.X;
                edge.VisualLine.Y1 = centerA.Y;
                edge.VisualLine.X2 = centerB.X;
                edge.VisualLine.Y2 = centerB.Y;
            }
        }

        #endregion

        #region 4. Вспомогательные методы
        private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RbSelect.IsChecked == true && 
                sender is Thumb t && 
                t.DataContext is GraphNode nodeData)
            {
                var infoWindow = new NodeInfoWindow(nodeData);
                infoWindow.Owner = this;
                infoWindow.ShowDialog();
            }
        }

        private void OnViewModeChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb &&
                rb.IsChecked == true && 
                GraphCanvas != null)
            {
                bool showName = rb.Content.ToString() == "Название";
                foreach (var child in GraphCanvas.Children)
                {
                    if (child is Thumb thumb && thumb.DataContext is GraphNode node)
                    {
                        node.SetDisplayMode(showName);
                    }
                }
            }
        }

        private Thumb GetThumbByNode(GraphNode node)
        {
            return GraphCanvas.Children.OfType<Thumb>().FirstOrDefault(t => t.DataContext == node);
        }

        private Point GetThumbCenter(Thumb thumb)
        {
            double left = Canvas.GetLeft(thumb);
            double top = Canvas.GetTop(thumb);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            return new Point(left + 30, top + 30);
        }

        /// <summary>
        /// Выполняет свертку списка узлов в единый Мета-узел (Фактор-граф)
        /// </summary>
        private void GroupNodesToMetaNode(List<GraphNode> nodesToGroup)
        {
            if (nodesToGroup == null || nodesToGroup.Count < 2) return;

            var metaNode = new GraphNode();
            metaNode.Name = $"Мета-узел ({nodesToGroup.Count} шт.)";
            metaNode.Importance = nodesToGroup.Sum(n => n.Importance);

            double avgX = 0, avgY = 0;
            int count = 0;
            foreach (var node in nodesToGroup)
            {
                var thumb = GetThumbByNode(node);
                if (thumb != null)
                {
                    avgX += Canvas.GetLeft(thumb);
                    avgY += Canvas.GetTop(thumb);
                    count++;
                }
            }
            if (count > 0) { avgX /= count; avgY /= count; }

            var metaThumb = new Thumb
            {
                Style = (Style)FindResource("NodeStyle"),
                DataContext = metaNode,
                Width = 65,
                Height = 65 // Делаем его чуть крупнее обычных
            };

            metaThumb.MouseDoubleClick += Node_MouseDoubleClick;
            metaThumb.DragDelta += Node_DragDelta;
            metaThumb.PreviewMouseLeftButtonDown += Node_PreviewMouseLeftButtonDown;

            Canvas.SetLeft(metaThumb, avgX);
            Canvas.SetTop(metaThumb, avgY);
            Panel.SetZIndex(metaThumb, 10);

            GraphCanvas.Children.Add(metaThumb);
            _nodes.Add(metaNode);
            metaNode.SetDisplayMode(RbShowName.IsChecked == true);

            var externalNeighbors = new HashSet<GraphNode>();
            var edgesToRemove = _edges.Where(e => nodesToGroup.Contains(e.NodeA) || nodesToGroup.Contains(e.NodeB)).ToList();

            foreach (var edge in edgesToRemove)
            {
                bool isA_Inside = nodesToGroup.Contains(edge.NodeA);
                bool isB_Inside = nodesToGroup.Contains(edge.NodeB);

                if (isA_Inside && !isB_Inside) externalNeighbors.Add(edge.NodeB);
                else if (!isA_Inside && isB_Inside) externalNeighbors.Add(edge.NodeA);

                GraphCanvas.Children.Remove(edge.VisualLine);
                _edges.Remove(edge);
            }

            foreach (var neighbor in externalNeighbors)
            {
                CreateEdge(metaNode, neighbor); 
            }

            foreach (var node in nodesToGroup)
            {
                var thumb = GetThumbByNode(node);
                if (thumb != null) GraphCanvas.Children.Remove(thumb);
                _nodes.Remove(node);
            }

            UpdateAnalytics();
        }


        #endregion
    }
}