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

    public partial class MainWindow : Window
    {
        private Point _lastMousePosition;
        private bool _isPanning;
        private List<GraphEdge> _edges = new List<GraphEdge>();

        // Переменные для рисования связей
        private bool _isDrawingEdge = false;
        private GraphNode _sourceNodeForEdge = null;
        private Line _tempLine = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region 1. Логика панорамирования (ПКМ)

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Блокируем панорамирование, если мы сейчас тянем связь
            if (_isDrawingEdge) return;

            var border = sender as Border;
            if (border != null)
            {
                _lastMousePosition = e.GetPosition(border);
                _isPanning = true;
                border.CaptureMouse(); // Захват мыши для Border
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
                    border.ReleaseMouseCapture(); // Сброс захвата
                }
                Cursor = Cursors.Arrow;
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            // A. Панорамирование (если зажата ПКМ)
            if (_isPanning)
            {
                var border = sender as IInputElement;
                var currentPosition = e.GetPosition(border);
                var delta = currentPosition - _lastMousePosition;
                CanvasTranslate.X += delta.X;
                CanvasTranslate.Y += delta.Y;
                _lastMousePosition = currentPosition;
            }

            // B. Обновление линии связи (если зажата ЛКМ и режим создания связи)
            if (_isDrawingEdge && _tempLine != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                _tempLine.X2 = pos.X;
                _tempLine.Y2 = pos.Y;
            }
        }

        #endregion

        #region 2. Логика Связей (С ИСПОЛЬЗОВАНИЕМ HIT TEST)

        // Начало рисования (Клик по узлу)
        private void Node_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var thumb = sender as Thumb;
            if (thumb == null) return;
            var node = thumb.DataContext as GraphNode;
            // --- ДОБАВЛЕНО: Логика удаления узла ---
            if (RbRemoveNode.IsChecked == true)
            {
                DeleteNode(node);
                e.Handled = true; // Прерываем событие, чтобы не сработали другие клики
                return;
            }
            // Проверяем режим интерфейса
            if (RbAddEdge.IsChecked == true)
            {
                if (thumb != null)
                {
                    StartDrawingEdge(thumb.DataContext as GraphNode, GetThumbCenter(thumb));
                    e.Handled = true; // Останавливаем всплытие, чтобы не сработал клик по Canvas или Drag
                }
            }
        }

        // ОТПУСКАНИЕ МЫШИ (ГЛОБАЛЬНОЕ)
        // Здесь решается судьба связи: создаться или удалиться
        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingEdge)
            {
                // 1. Где мы отпустили мышь?
                Point point = e.GetPosition(GraphCanvas);

                // 2. Ищем, есть ли под курсором узел
                GraphNode targetNode = FindNodeUnderMouse(point);

                // 3. Логика создания
                if (targetNode != null && 
                    targetNode != _sourceNodeForEdge)
                {
                    // Успех: соединяем
                    CreateEdge(_sourceNodeForEdge, targetNode);
                }

                // 4. В любом случае завершаем рисование (удаляем пунктир)
                StopDrawingEdge();

                // Сбрасываем захват мыши с Canvas (критично!)
                GraphCanvas.ReleaseMouseCapture();
            }
        }

        private void StartDrawingEdge(GraphNode sourceNode, Point startPoint)
        {
            _isDrawingEdge = true;
            _sourceNodeForEdge = sourceNode;

            // Создаем временную линию
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

            // ЗАХВАТ МЫШИ: Все события теперь идут в Canvas, даже если курсор над узлом
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

        // Ручной поиск узла под мышкой (Hit Testing)
        private GraphNode FindNodeUnderMouse(Point point)
        {
            GraphNode result = null;

            // HitTest ищет самый верхний визуальный элемент под точкой
            VisualTreeHelper.HitTest(GraphCanvas, null,
                new HitTestResultCallback(hit =>
                {
                    // Идем вверх по дереву от точки попадания, пока не найдем Thumb
                    var visual = hit.VisualHit;
                    while (visual != null)
                    {
                        if (visual is Thumb thumb && thumb.DataContext is GraphNode node)
                        {
                            result = node;
                            return HitTestResultBehavior.Stop; // Нашли! Останавливаемся.
                        }
                        visual = VisualTreeHelper.GetParent(visual);
                    }
                    return HitTestResultBehavior.Continue; // Ищем дальше (если попали в линию или фон)
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
        }

        private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var line = sender as Line;
            var edge = _edges.FirstOrDefault(x => x.VisualLine == line);
            if (edge == null) return;

            // Удаление связи
            if (RbRemoveEdge.IsChecked == true)
            {
                RemoveEdge(edge);
                return;
            }

            // Перетаскивание конца (Редактирование)
            if (RbAddEdge.IsChecked == true || RbSelect.IsChecked == true)
            {
                Point clickPos = e.GetPosition(GraphCanvas);
                Point posA = new Point(line.X1, line.Y1);
                Point posB = new Point(line.X2, line.Y2);

                RemoveEdge(edge); // Удаляем старую

                // Определяем, за какой конец взялись
                if ((clickPos - posA).Length < (clickPos - posB).Length)
                {
                    // Ближе к А -> тянем от B
                    StartDrawingEdge(edge.NodeB, posB);
                }
                else
                {
                    // Ближе к B -> тянем от A
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
        }
        private void DeleteNode(GraphNode node)
        {
            // 1. Находим все связи, подключенные к этому узлу
            // Важно: делаем .ToList(), чтобы создать копию списка, так как мы будем удалять элементы из _edges внутри цикла
            var edgesToRemove = _edges.Where(e => e.NodeA == node || e.NodeB == node).ToList();

            // 2. Удаляем каждую связь
            foreach (var edge in edgesToRemove)
            {
                // Удаляем визуальную линию с холста
                if (edge.VisualLine != null)
                {
                    GraphCanvas.Children.Remove(edge.VisualLine);
                }
                // Удаляем из списка данных
                _edges.Remove(edge);
            }

            // 3. Удаляем визуальное представление самого узла
            var thumb = GetThumbByNode(node);
            if (thumb != null)
            {
                GraphCanvas.Children.Remove(thumb);
            }
        }

        #endregion

        #region 3. Управление Узлами

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Создание узла только в режиме "Добавить узел"
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

            // Только старт связи (конец теперь обрабатывается через HitTest)
            newNode.PreviewMouseLeftButtonDown += Node_PreviewMouseLeftButtonDown;

            Canvas.SetLeft(newNode, position.X - 30);
            Canvas.SetTop(newNode, position.Y - 30);

            Panel.SetZIndex(newNode, 10);
            GraphCanvas.Children.Add(newNode);

            bool showName = RbShowName.IsChecked == true;
            nodeData.SetDisplayMode(showName);
        }

        private void Node_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // Перетаскивание узлов только в режиме "Курсор"
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
        #endregion
    }
}